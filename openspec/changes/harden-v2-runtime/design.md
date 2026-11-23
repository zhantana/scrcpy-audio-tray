# 设计：v2 运行时加固

## D1. adb 异步化的边界

**实现落点（与最初设计的差异）**：`AdbClient` 保持同步薄封装（`Process.WaitForExit`），不在其上加 async 包装；异步统一在**调用侧**做——`UnlockService.InvokeAsync` 整体 `Task.Run`、`DevicePoller.Tick` 内 `Task.Run`、App 各入口 `await Task.Run(...)`。这样 adb 调用全部离开 UI 线程，而 AdbClient 接口不被 async 污染，`--selftest` 等控制台路径保持同步可用。

- `DevicePoller`：轮询体在后台执行，事件经 `Dispatcher.BeginInvoke` 封送回 UI。
- 右键菜单：用缓存的设备列表即时弹出，adb 刷新在后台进行，右键不冻结 UI。
- 解锁流程：`UnlockService.Invoke` 保持同步实现，`InvokeAsync` 包 `Task.Run`；App 的 `DoWake/StartCoreAsync/UnlockWithPinPromptAsync` 全链路 await；轮询节奏（50-140ms 步进）不变。
- 唯一保持同步的调用点：`OnStartup` 里的一次性设备枚举与 `--selftest`。
- 竞态防护：转发启动/停止序列加一把 `_gate`（SemaphoreSlim），防止轮询自动重连与用户点击并发启停。

## D2. 异常兜底

- `OnStartup` 注册 `DispatcherUnhandledException`、`AppDomain.CurrentDomain.UnhandledException`、`TaskScheduler.UnobservedTaskException` 三处兜底：写 `tray.log`（含堆栈）→ 托盘气泡 → 非致命则继续运行。
- 对齐 spec「托盘程序异常」：完整堆栈必须落日志。
- 退出清理（热键注销、托盘释放、keep-awake 还原）包进 try/catch + finally，进程被杀时至少日志留痕。

## D3. JsonStore 加固

- 写：`WriteText` 写同目录 `.tmp` → `File.Replace`/`File.Move(overwrite)`，失败向上抛而非吞。
- 读：捕获 `JsonException` 时把损坏文件改名留档（`settings.json.corrupt-<时间戳>`），向用户气泡提示一次，再回落默认——不静默。
- `UnlockService.ReadPin/SavePin` 改走 `JsonStore`（`device-pin.json` 本就是 JSON，正则解析是多余的脆弱实现）。
- 保存点收敛：各 Service 的 Save 调用收敛为「配置对象被修改 → 统一 `ConfigService.Save()` 幂等调用（内部去抖）」，消除退出双写。

## D4. 进程与句柄

- `ScrcpyProcess.Start`：进入时若 `_process` 仍在运行先走完整 `Stop()`（此约束同时是文档化的使用契约，调用方仍应先停后启）；`Exited` 回调里 Dispose 旧对象并置空。
- `MainWindow.TryEmbed`：handler 提为字段，`Detach` 时退订；重复嵌入先退订旧的。
- `EmbedService.Attach` 失败路径：恢复原窗口样式（`GWL_STYLE` 写回 + `SWP_FRAMECHANGED` + `ShowWindow(SW_SHOW)`）再返回 false，保证降级后 scrcpy 窗口可见可用。

## D5. 正确性小修

- `FindWindowByPrefix`：命中后 `GetWindowThreadProcessId` → 进程名必须为 `scrcpy.exe` 才采用；枚举时跳过不可见窗口。
- `GetScreenSize`：解析失败返回空数组，由调用方回落到 `GetVideoSize`（虚拟屏）或按视频比例处理，不再写死 1080x2400。
- 设备状态解析统一：`ScreenState` 判定（awake/asleep/locked）只存在于 `AdbClient`，`UnlockService` 只消费结构化结果，删掉自己的字符串集合。
- `HotkeyParser`：多个非修饰键时解析失败并提示，不静默取最后一个。
- `ScrcpyProcess.Quote`：参数含 `"` 时转义（当前参数源可控，属防御性补齐）。
- `ScreenService.RememberSize`：复用一次查询结果打日志，省一条 adb。
- `TrayIconService`：图标缺失时气泡提示一次，避免「运行/停止看不出区别」无人知晓。

## D6. 菜单与死代码

- 菜单合一：`App.BuildMenuItems` 删除，窗口顶栏菜单与托盘菜单统一由 `TrayMenuBuilder` 构建（parity 变更的任务 5.1 引用此决策；若 parity 先合入则以那边实现为准，此处只剩验收）。
- 死代码清理（parity 合入后执行，避开将被复活的成员）：`SettingsWindow.CmbBitrate_SelectionChanged`、`UnlockService.HasPin`、`SleepMonitor.IsAsleep`（若未被 Poller 复用）、`ScrcpyProcess.MainWindowHandle`、`NativeMethods.FindWindowByClass`、`FullScreenCommand` 死绑定。
- 日志：**不改名**——spec「托盘程序异常」明确指定堆栈写入 `tray-error.log`，文件名以 spec 为准；分级暂缓（调用量少，前缀已可区分），关键路径补埋点：解锁每步结果、嵌入成败、配置读写失败、keep-awake 备份恢复、方向变化、实例计数触发。

## D7. 实施阶段划分

| 阶段 | 内容 | 依赖 |
|---|---|---|
| 一（先行，parity 前置） | adb 异步化 + 启停互斥 + 异常兜底 | 无 |
| 二 | JsonStore 加固 + PIN 读写走 JsonStore + 保存点收敛 | 无（可与一并行） |
| 三 | 进程/句柄泄漏修复 + 正确性小修 | 一（异步化后部分代码面会变） |
| 四（parity 合入后） | 菜单合一验收 + 死代码清理 + 日志埋点 | parity |
