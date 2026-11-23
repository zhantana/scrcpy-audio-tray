# 任务：补齐 v1 功能缺口

> 2026-11-23：功能实现全部完成（与 harden-v2-runtime 一起交付），`dotnet build` 0 警告、`--selftest` 通过。第 6 节真机实测未做，通过前不得归档。

前置：`harden-v2-runtime` 阶段一（异步 adb 基建）已合入。✅

## 1. 纯音频主线

- [x] 1.1 恢复 `Screen` 字段语义（是否叠加画面）；`OnStartup` 自动启动**纯音频**转发（启动先保证音频通，上次带投屏退出则音频起播后再叠画面），投屏不再硬编码
- [x] 1.2 托盘菜单改回「启动音频转发 / 停止转发」+ 独立「显示手机画面（投屏）」勾选项；开关投屏 = 音频不中断的整路重启
- [x] 1.3 转发启动 1.2 秒后发音量键 24→25 叫醒音频通路（`WakeAudioPathAsync`）
- [x] 1.4 纯音频模式下投屏相关菜单（置顶/窗口模式/重置尺寸）置灰（`AppMenus` 按 `Running && ScreenOn` 控制）

## 2. 统一轮询（Services/DevicePoller）

- [x] 2.1 新建 `DevicePoller`：单 1 秒节拍，任务后台执行、事件封送 UI；`SleepMonitor` 并入后删除
- [x] 2.2 方向轮询：每 2 秒读虚拟屏尺寸，比例变化（0.02 容差）→ 按该方向重算窗口尺寸（`DesiredWindowSize` 接受轮询传入的比例，横竖尺寸分别记忆）
- [x] 2.3 keep-awake：新建 `KeepAwake`，投屏启动备份并设 `screen_off_timeout=2147483647`（`device-timeout.json`），停止/退出还原，每次启动先清残留
- [x] 2.4 实例计数：记录他人 scrcpy 基线，数量变少连续 2 拍 → 整路重启 +「声音已恢复」通知；变多不动作
- [x] 2.5 停止转发时屏幕亮且锁着则补发 POWER 同步电源状态（`StopCoreAsync`）
- [x] 2.6 `userStopped` 标志：手动停止后 Poller 不自动复活

## 3. 传输自动化

- [x] 3.1 USB↔WiFi：随设备列表每 5 秒检测，WiFi 设备的同机型 USB 出现 → 切有线并通知；当前设备消失 → 回落到存活设备（USB 优先）；手动选过的设备不被抢（`_manualPick`）；切换带 5 秒冷却
- [x] 3.2 自动重连：启动 600ms 静默连接已存地址 + mDNS；失败每 4 秒重试 ×2，全程气泡提示不弹窗

## 4. 设备管理菜单

- [x] 4.1 `AdbClient.ListMdns`（`adb mdns services`，`_adb-tls-connect._tcp`）+ `DeviceService.MergeDuplicates` 同机型 IP/mDNS 去重
- [x] 4.2 设备菜单分三组：USB 有线 / WiFi 无线 / 蓝牙（置灰 + tooltip）；WiFi 组含在线设备 + 已记住地址（点击重连）+ 自动发现 + 添加设备（补 `:5555`）+ 配对码配对（`InputDialog` 双输入框）
- [x] 4.3 接线重命名（留空恢复默认）、清理无效设备（`DeviceService.ClearBad`，先停转发）、忘记解锁密码（`UnlockService.ForgetPin` 删 `device-pin.json`）
- [x] 4.4 「重新连接设备」「刷新列表」菜单项
- [x] 4.5 手动选过的设备优先于「USB 优先」默认规则

## 5. 窗口

- [x] 5.1 顶栏改五根菜单（设备/音频/视图/帮助/关于），`AppMenus` 工厂与托盘共用；`App.BuildMenuItems` 第二套定义已删除
- [x] 5.2 底部状态条：运行状态、模式、编码、缓冲、帧率；`ChromeH=64`（顶栏 40 + 状态条 24），嵌入偏移仍只含顶栏高度（状态条在内容区下方，不影响子窗口原点）
  - **2026-11-23 追记**：状态条、顶栏标题与五根菜单已按用户要求移除/收敛为单根「操作」（见 `simplify-window-topbar`），`ChromeH` 回落 40，画面区延伸到窗口底
- [x] 5.3 F11 全屏 / Esc 退出：code-behind `PreviewKeyDown`，死掉的 `FullScreenCommand` 绑定已删除
- [x] 5.4 托盘双击三分支：运行中窗口可见→停止 / 隐藏→显示窗口 / 原生模式→停止；未运行→启动
- [x] 5.5 单实例互斥体 `Global\ScrcpyAudioTrayV2`（异常时回退 `Local\`），二次启动提示后退出

## 6. 验证

- [x] 6.1 `dotnet build` 0 警告；`--selftest` 通过（含 MergeDuplicates 去重计数、mDNS 列表、两种参数分支、热键多键拒绝）
- [ ] 6.2 真机对照 spec 逐条实测：纯音频启动/音量键叫醒/插拔线切换/重试/keep-awake 备份还原/实例接回/横竖屏/状态条/双击/单实例（无法实测项在 summary 标「未验证」）
- [ ] 6.3 回归：音频不丢样本（单进程约束未破坏）、热键生效、v1 配置字段无丢失
- [ ] 6.4 通过后归档本变更并合并 spec delta
