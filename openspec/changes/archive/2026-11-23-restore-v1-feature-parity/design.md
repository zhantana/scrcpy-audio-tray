# 设计：补齐 v1 功能缺口

## D1. 纯音频恢复为主线，投屏变为叠加项

v1 的模型是「音频转发恒开，投屏 = 音频 + 画面」。v2 把 `Screen=true` 写死，丢掉了纯音频。恢复方案：

- `AppSettings.Screen` 保留字段（v1 配置兼容），但恢复其真实语义：是否叠加画面。
- 托盘主入口改为「启动音频转发 / 停止」；「显示手机画面」恢复为独立勾选项（勾选 = 以投屏参数重启，取消 = 回纯音频），对齐 v1 与 spec「屏幕镜像」的开关描述。
- `ScrcpyProcess.BuildArgs` 已有 `--no-video --no-window` 分支，直接复用，不需要改参数构造。
- 启动行为：托盘启动后自动开**纯音频**转发（对齐 spec「启动行为」自动开始），而不是自动投屏。上次的投屏勾选状态仍从配置读，但启动阶段先保证音频通、再按需叠加画面——避免解锁失败/嵌入失败把音频一起拖死。

## D2. 音量键叫醒音频通路

对齐 v1 `Wake-AudioPath` 与 spec：转发启动 1.2 秒后发 `input keyevent 24` + `keyevent 25`（音量+再音量−），只用于激活空闲手机的音频输出通路，不改变音量。放在 `ScrcpyProcess` 启动成功回调之后由编排层触发（不需要独立线程，走统一的延迟调度）。

## D3. 统一轮询循环，不再一个功能一个 Timer

v1 是单 1 秒 Timer 干所有周期活。v2 现状是 SleepMonitor 独占一个 DispatcherTimer。若为本变更的每个功能各建 Timer，会有 4-5 个并发轮询、每轮询一条 adb。设计：新建 `Services/DevicePoller`（单 DispatcherTimer，1 秒节拍），把以下周期任务都挂进去，各自带独立的节流计数（对齐 v1 的节拍分配）：

| 任务 | 节拍 | 来源 |
|---|---|---|
| 手机息屏判定（连续 2 次） | 每秒 | 迁自 `SleepMonitor`（该类并入 Poller 或保留为纯判定函数） |
| 横竖屏方向检测 → 重算窗口 | 每 2 秒 | spec「窗口跟随视频流方向」 |
| USB↔WiFi 传输切换 | 每 5 秒 | spec「连接方式」 |
| scrcpy 实例计数 → 接回声音 | 每秒 | spec「多路投屏抢占声音后自动接回」 |
| 自动重连重试 | 失败后每 4 秒 ×2 | spec「启动行为」 |

所有 adb 查询经 `harden-v2-runtime` 的异步基建在后台线程执行，结果封送回 UI。轮询仅在转发运行时开启；`userStopped` 标志（手动停止后不自动复活）在此层维护。

## D4. 设备菜单对齐 v1

`TrayMenuBuilder` 的设备子菜单改为 v1 的三组结构（spec「连接方式」）：

- **USB 有线** / **WiFi 无线** / **蓝牙（不可用，置灰 + tooltip）**三个分组。
- WiFi 组内：在线设备、已记住但不在线的 `WifiTargets`（点击即 connect）、自动发现并连接（`adb mdns services` 解析 `_adb-tls-connect._tcp`）、添加设备（IP 无端口补 `:5555`）、配对新设备（pair + 配对码）。
- 组尾公共项：重命名此设备…（复用 `DeviceService.Rename`，写入 `device-names.json`）、清理无效设备（断 mDNS 重复项与 offline/unauthorized，保留唯一可用连接）、忘记解锁密码（清 PIN）、重新连接设备、刷新列表。
- mDNS 去重：同机型同时有 `IP:port` 与 `*. _adb-tls-connect._tcp` 记录时只保留 IP 项（spec「同一设备两条记录」）。`AdbClient.ListDevices` 增加 `adb mdns services` 结果参与去重。
- 死代码 `DeviceService.Connect/AddWifiTarget/Rename`、`AdbClient.Disconnect` 由本变更接线复活，不从 `harden-v2-runtime` 的死代码清理中删除（两个变更的 tasks 已互相标注）。

## D5. keep-awake（screen_off_timeout）

对齐 v1 `Push/Pop-KeepAwake` 与 spec「投屏期间不自动息屏」：

- 投屏启动前：读 `settings put system screen_off_timeout` 原值 → 存 `device-timeout.json` → 设为 `2147483647`。
- 停止投屏/退出：读备份还原。
- 启动时先清残留：备份文件存在且当前值=2147483647 视为上次异常退出，直接还原原值，避免把「永不息屏」记成用户设置。
- 只用 `screen_off_timeout`，不碰 `svc power stayon`（spec 明令禁止）。

## D6. 实例计数接回声音

对齐 v1：启动转发时记录「除自己以外的 scrcpy 进程数」基线（按进程名计数，排除自己）；Poller 每秒复查，数量变少 → 等 2 拍 → 整路重启 +「声音已恢复」通知；变多不动作。整路重启的闪断是 spec 已接受的代价。

## D7. 顶栏五根菜单与状态条

- 顶栏改为标题 + `设备/音频/视图/帮助/关于` + 窗口按钮（spec「单行顶部栏」）。设备/音频菜单与托盘菜单**共用同一构建器与回调**（消除 `BuildMenuItems` 与 `TrayMenuBuilder` 的两套定义），视图组：置顶/帧率/显示画面/窗口模式/全屏，帮助组：快捷键说明/查看日志，关于组：版本。
- 状态条贴底：运行状态、模式、编码、缓冲、帧率（spec 已定义）。布局按 spec 的显式坐标规则（顶栏置顶、状态条贴底、内容区夹中间，z-order 最低），沿用 `fix-embedded-window-layout` 修正后的嵌入偏移逻辑，顶栏高度变化需同步更新 `EmbedService` 的纵向偏移。
- F11 全屏/Esc 退出：去掉死绑定，改为窗口 code-behind 处理 PreviewKeyDown（全屏保留顶栏，对齐 v1 `Set-FullScreen`）。

## D8. 托盘双击三分支与单实例

- 双击：运行中且窗口可见 → 停止转发；运行中窗口隐藏 → 显示窗口；未运行 → 启动转发（对齐 v1）。
- 单实例：`Global\ScrcpyAudioTrayV2` 命名互斥体，OnStartup 抢不到即退出并提示；ExitApp/OnExit 释放。

## D9. 与 harden-v2-runtime 的实施顺序

1. `harden-v2-runtime` 阶段一（异步 adb 基建 + 全局异常兜底）先行——本变更所有新功能都依赖它，否则设备菜单、轮询、解锁会进一步加剧 UI 冻结。
2. 本变更按 tasks.md 分阶段落地。
3. `harden-v2-runtime` 收尾的死代码清理放在本变更合入之后执行（部分"死代码"会被本变更复活）。

## 不做的事

- 不改 scrcpy 源码、不重造 adb 协议栈。
- 不在本变更里做「多设备同时开窗、截图录屏、音量独立控制」等 v2 proposal 里的扩展功能——那是 parity 之后的增量。
- PC 侧 Windows 休眠监听（`SystemEvents.PowerModeChanged`）v1 也没有、spec 未定义，不纳入本变更；若要做另立变更。
