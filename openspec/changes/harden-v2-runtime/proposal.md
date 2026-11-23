# 变更：v2 运行时加固与代码质量整改

## Why

v2 源码逐文件审查（19 个 .cs 文件，约 2200 行）发现一批**不影响当前演示路径、但会在真实使用中造成 UI 冻结、配置丢失、进程泄漏**的质量问题。功能补全在 `restore-v1-feature-parity` 进行，本变更负责把地基打牢——那边的新功能全部依赖这里的异步基建。

按危害排序的主要问题（均经源码核实，行号为当前版本）：

**UI 线程阻塞（最严重）**
- 所有 adb 调用都在 UI 线程同步执行，默认超时 8 秒（`AdbClient.cs:64-87`）。息屏轮询每秒在 UI 线程跑两条 `adb shell dumpsys`（`SleepMonitor.cs:54` → `AdbClient.cs:170-171`），设备卡顿时 UI 每秒最多冻结 ~16 秒；右键菜单每次先同步 `Refresh()` 再弹（`App.xaml.cs:104`）；解锁流程含 UI 线程硬 Sleep（`App.xaml.cs:315` 400ms、`UnlockService.cs:105,112` 共 ~1.8s）。

**配置与数据安全**
- `JsonStore.Write` 直接 `File.WriteAllText`，崩溃/断电产生半截 JSON；写入失败被空 catch 吞掉（`JsonStore.cs:37-42`）。
- `JsonStore.Read` 静默吞损坏配置返回 null → 全部回落默认，用户配置无感丢失（`JsonStore.cs:25-28`）。
- `UnlockService.ReadPin/SavePin` 绕过 JsonStore，用正则读 JSON（PIN 含 `"` 即失效）+ 非原子直写（`UnlockService.cs:24-43`）。
- 配置保存点分散 9 处，退出时 `ExitApp` 与 `OnExit` 双写（`App.xaml.cs:384,421`）。

**资源与进程泄漏**
- `ScrcpyProcess.Start` 直接覆盖 `_process`，运行中再 Start 旧进程成孤儿且句柄丢失（`ScrcpyProcess.cs:47`）；`Exited` 后不 Dispose（:50-51）。
- `MainWindow.TryEmbed` 每次嵌入向 `ContentArea.SizeChanged` 追加匿名 handler，永不退订（`MainWindow.xaml.cs:102`）。
- `EmbedService.Attach` 失败路径已把窗口改成 `WS_CHILD` 并 SW_HIDE，却不恢复样式直接返回——子窗口可能永久隐藏（`EmbedService.cs:46`）。
- 无 `DispatcherUnhandledException` / `AppDomain.UnhandledException` 兜底，未捕获异常直接崩且不留日志（`App.xaml.cs` 仅 OnExit 有 try/catch）。

**正确性隐患**
- `FindWindowByPrefix` 按标题前缀匹配且不校验进程，任何第三方窗口标题以 `ScrcpyBridge|<serial>` 开头都会被误嵌入（`NativeMethods.cs:83-98`）。
- `GetScreenSize` 解析失败回退硬编码 1080x2400，其他分辨率设备比例计算会错（`AdbClient.cs:126`）。
- 设备状态解析三处字符串集合各不相同（`AdbClient.cs:158-183`、`UnlockService.cs:52-54,97-98`），`interactiveState=AWAKE` 与 `INTERACTIVE_STATE_AWAKE` 靠子串碰巧兼容，易漂移。
- `HotkeyParser` 多个非修饰键时"最后一个键赢"，静默无报错（`HotkeyWindow.cs:63-66`）。
- `ScrcpyProcess.Quote` 不转义 `"`（:114-115）；`RememberSize` 为打日志连查两次 `GetVideoRatio`（每次一条 adb，`ScreenService.cs:115-118`）；托盘图标缺失时 idle/active 都回退同一系统图标且无提示（`TrayIconService.cs:35-36`）。

**可维护性**
- 两套菜单定义重复（`App.BuildMenuItems` vs `TrayMenuBuilder`）；死代码一批：`SettingsWindow.CmbBitrate_SelectionChanged`（空方法）、`HasPin`、`IsAsleep`、`ScrcpyProcess.MainWindowHandle`、`FindWindowByClass` 等（注：`DeviceService.Connect/AddWifiTarget/Rename`、`AdbClient.Disconnect` 由 parity 变更接线复活，不在清理范围）。
- 日志名为 `tray-error.log` 却记普通信息，且仅 6 处埋点，解锁失败、进程退出原因等关键路径无日志。

## What Changes

- **阶段一（parity 的前置依赖）**：adb 调用全面异步化（`async`/`Task.Run` + 结果封送 UI），解锁/轮询中的硬 Sleep 改为异步等待；全局异常兜底 + 兜底写日志
- `JsonStore`：原子写（temp + `File.Replace`）、写失败上报、损坏配置向用户提示而非静默回默认；`UnlockService` 改用 JsonStore；保存点收敛
- `ScrcpyProcess` 生命周期修复（Start 前先停旧实例、Exited 后 Dispose）；`SizeChanged` handler 退订；`EmbedService` 失败路径恢复窗口样式
- `FindWindowByPrefix` 校验目标窗口进程名；`GetScreenSize` 去掉硬编码回退；设备状态解析统一到 `AdbClient` 一处；热键多键报错
- 死代码清理、菜单定义合一、日志改名 `tray.log` 并补关键路径埋点

## Capabilities

### New Capabilities

无（全部是既有能力的实现质量整改，不改变对外行为语义）。

### Modified Capabilities

- `phone-audio-bridge` 的「启动失败可诊断」新增「配置文件损坏」场景：损坏时提示并可恢复，不静默丢失。

## Impact

- 影响文件：`AdbClient.cs`（异步化）、`App.xaml.cs`、`SleepMonitor.cs`、`UnlockService.cs`、`JsonStore.cs`、`ScrcpyProcess.cs`、`EmbedService.cs`、`MainWindow.xaml.cs`、`TrayMenuBuilder.cs`、`Logger.cs`。
- 对外行为除「配置损坏提示」外不变；`restore-v1-feature-parity` 的设备菜单/轮询功能基于本变更的异步基建实现。
