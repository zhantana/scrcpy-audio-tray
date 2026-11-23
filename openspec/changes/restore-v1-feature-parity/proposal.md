# 变更：补齐 v2 相对 v1 与现行规范的功能缺口

## Why

v2（C# 重构）已完成框架与投屏主链路，但与 v1（3163 行 PowerShell）及现行 `openspec/specs/phone-audio-bridge/spec.md` 逐项对照后，存在一批**规范已定义、v2 未实现**的功能。对照证据如下（均经源码核实）：

| # | 缺口 | 规范依据 | v2 现状 |
|---|---|---|---|
| 1 | **纯音频转发不可达**：核心场景「手机声音→电脑」在 v2 只能以投屏形态使用 | spec「无线音频转发」「启动行为」 | `Settings.Screen` 被硬编码为 true（`App.xaml.cs:87,168`），`ScrcpyProcess` 纯音频分支（`ScrcpyProcess.cs:99-104`）不可达，托盘只有「启动投屏/停止投屏」 |
| 2 | 音量键叫醒音频通路 | spec「启动后音频通路未激活」 | 全库无 `keyevent 24/25` 逻辑 |
| 3 | 设备菜单缺大半：已记住地址重连、mDNS 自动发现、添加设备、配对码配对、蓝牙置灰分组、重命名、清理无效设备、忘记解锁密码、重新连接、刷新 | spec「连接方式」「手动添加与配对设备」「设备别名」「无效连接清理」 | `DeviceService.Connect/AddWifiTarget/Rename`、`AdbClient.Disconnect` 均为死代码；`WifiTargets` 存了从不用于重连 |
| 4 | 同机型的 mDNS 重复记录不去重 | spec「设备发现与选择」 | `ListDevices` 不合并 |
| 5 | USB↔WiFi 自动切换（插线切有线、拔线回落） | spec「连接方式」插线/拔线场景 | 无任何传输切换逻辑 |
| 6 | 自动重连 + 手动停止后不复活 | spec「启动行为」 | 无重试定时器，启动失败即放弃 |
| 7 | 投屏期间 `screen_off_timeout` 保存/恢复 + 异常残留清理 | spec「投屏期间不自动息屏」 | 全库无命中，永不息屏残留无兜底 |
| 8 | scrcpy 实例计数、别的实例退出后接回声音 | spec「多路投屏抢占声音后自动接回」 | 无实例计数 |
| 9 | 停止投屏时补发 POWER 同步电源状态 | v1 行为（`Stop-Audio` L2361-2371） | 无 |
| 10 | 横竖屏方向轮询（每 2 秒重算窗口尺寸） | spec「窗口跟随视频流方向」 | `GetVideoRatio()` 已有但无定时调用（`fix-embedded-window-layout/summary.md` 已标注） |
| 11 | 顶栏五根菜单（设备/音频/视图/帮助/关于）+ 底部状态条 | spec「单行顶部栏」 | v2 只有一个「操作」菜单（`MainWindow.xaml:27`），无状态条 |
| 12 | F11 全屏 / Esc 退出 | v1 行为 | XAML 绑定无 DataContext，死绑定（`MainWindow.xaml:9`） |
| 13 | 托盘双击三分支（运行中窗口可见→停止 / 隐藏→显示 / 未运行→启动） | v1 行为 | v2 双击一律 ToggleScreen（`App.xaml.cs:72`） |
| 14 | 单实例互斥体 | v1 行为（`Global\ScrcpyAudioTrayUI`） | 无，可多开互相抢占设备 |

注：开机自启不缺——`build/installer-v2.iss:40-54` 已含 startup 任务（默认不勾选）。

## What Changes

- 恢复**纯音频转发**为一等模式：「启动音频转发」为主线，投屏是叠加在音频之上的可选项；启动后补音量键叫醒
- 设备菜单对齐 v1：USB/WiFi/蓝牙分组、WiFi 组四入口、重命名、清理无效设备、忘记解锁密码、重新连接、刷新；mDNS 重复记录去重
- 传输自动化：USB↔WiFi 每 5 秒检测切换、启动失败 4 秒重试×2、手动停止后不复活
- 投屏期行为：`screen_off_timeout` 备份/恢复/残留清理、scrcpy 实例计数接回声音、停止时电源同步、方向轮询
- 窗口：顶栏五根菜单 + 状态条、F11 全屏、托盘双击三分支、单实例互斥体

实施顺序依赖 `harden-v2-runtime` 的异步 adb 基建（见 design.md），两个变更并行立项、分阶段落地。

## Capabilities

### New Capabilities

无（本变更全部是对既有能力 `phone-audio-bridge` 的补全与修正）。

### Modified Capabilities

- `phone-audio-bridge`：「无线音频转发」「设备发现与选择」「连接方式」「手动添加与配对设备」「设备别名」「无效连接清理」「启动行为」「投屏期间不自动息屏」「多路投屏抢占声音后自动接回」「窗口跟随视频流方向」「单行顶部栏」共 11 项 Requirements 在 v2 中真正落地。

## Impact

- 影响文件：`App.xaml.cs`（编排层大改）、`TrayMenuBuilder.cs`、`MainWindow.xaml(.cs)`（五根菜单 + 状态条）、`ScreenService.cs`（纯音频分支、keep-awake、方向轮询）、`DeviceService.cs`（分组、mDNS、切换、重连）、`AdbClient.cs`（mDNS 列表、去重）、`ScrcpyProcess.cs`（音频通路叫醒）、新增 `Services/PollLoop`（统一轮询）。
- `fix-wake-unlock` 的三个未勾任务经核实代码已落地（`GetDeviceState` 已合并查询、`PinPromptWindow` 已接线），真机实测通过后可先行归档，与本变更无冲突。
