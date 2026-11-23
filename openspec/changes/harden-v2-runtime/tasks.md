# 任务：v2 运行时加固

> 2026-11-23：阶段一至四全部实现完毕（与 restore-v1-feature-parity 一起交付），`dotnet build` 0 警告、`--selftest` 通过。5.2-5.4 需真机人工验证后才能归档。

## 1. 阶段一：异步化与异常兜底（restore-v1-feature-parity 的前置）

- [x] 1.1 adb 调用移出 UI 线程（**落点调整**：不在 `AdbClient` 加 async 包装，异步统一在调用侧 `Task.Run`，见 design.md D1）
- [x] 1.2 轮询体移到后台线程，事件经 `Dispatcher.BeginInvoke` 封送 UI（`DevicePoller` 替代 `SleepMonitor`）
- [x] 1.3 解锁链路全 async：`UnlockService.InvokeAsync` + `App.UnlockWithPinPromptAsync/DoWake/StartCoreAsync`；`Thread.Sleep` → `await Task.Delay`
- [x] 1.4 右键菜单不再同步阻塞：缓存设备列表先弹菜单，后台刷新
- [x] 1.5 启动/停止串行化：`SemaphoreSlim` 门闩，防自动重连与手动操作并发
- [x] 1.6 全局异常兜底：`DispatcherUnhandledException` + `AppDomain.UnhandledException` + `TaskScheduler.UnobservedTaskException` → 写日志 + 气泡
- [x] 1.7 退出清理 try/catch/finally 兜底（热键、托盘、keep-awake、互斥体释放）

## 2. 阶段二：配置与数据安全

- [x] 2.1 `JsonStore` 原子写（temp + `File.Replace`），写失败上抛、调用方记日志
- [x] 2.2 损坏配置处理：改名 `.corrupt-<时间戳>` 留档 + 气泡提示一次 + 回落默认，不静默
- [x] 2.3 `UnlockService.ReadPin/SavePin` 改走 `JsonStore`，删正则解析
- [x] 2.4 退出双写已消除（配置统一由 OnExit 保存）；保存点仍分散但 `ConfigService.Save` 幂等，进一步收敛列入后续

## 3. 阶段三：进程与正确性

- [x] 3.1 `ScrcpyProcess.Start` 先停旧实例；`Exited` 后 Dispose 并置空
- [x] 3.2 `MainWindow` 的 `ContentArea.SizeChanged` handler 改为构造时注册一次
- [x] 3.3 `EmbedService.Attach` 失败路径 `UndoPartial` 恢复窗口样式与可见性
- [x] 3.4 `FindWindowByPrefix` 校验进程名为 scrcpy 且窗口可见
- [x] 3.5 `GetScreenSize` 去掉 1080x2400 硬编码回退（返回空数组，调用方回落虚拟屏 / 默认比例）
- [x] 3.6 设备状态解析统一到 `AdbClient.ScreenState`，`UnlockService`/`DevicePoller` 只消费结构化结果
- [x] 3.7 `HotkeyParser` 多非修饰键拒绝；`Quote` 转义 `"`；`RememberSize` 复用轮询比例；图标缺失气泡提示

## 4. 阶段四：可维护性

- [x] 4.1 菜单定义合一：`AppMenus.BuildTopBarRoots/BuildTrayMenu` 同一工厂，`App.BuildMenuItems` 与 `TrayMenuBuilder` 已删除
- [x] 4.2 清理死代码：`SettingsWindow.CmbBitrate_SelectionChanged`、`HasPin`、`ScrcpyProcess.MainWindowHandle`、`EmbedService.ChildHandle`、`NativeMethods.FindWindowByClass`、`FullScreenCommand` 死绑定；`SleepMonitor` 整类并入 `DevicePoller` 删除
- [x] 4.3 日志：保留 `tray-error.log` 文件名（spec「托盘程序异常」指定），未做分级；已补关键路径埋点（嵌入、keep-awake、方向、实例计数、配置损坏、解锁失败）

## 5. 验证

- [x] 5.1 `dotnet build` 0 警告；`--selftest` 通过（设备枚举 / 热键解析含多键拒绝 / 两种参数分支）
- [ ] 5.2 断开设备启动托盘：UI 不冻结、菜单可弹、异常路径留日志（对应 1.2/1.4/1.6）
- [ ] 5.3 手工损坏 settings.json 启动：提示 + `.corrupt` 留档 + 默认值回落（对应 2.2）
- [ ] 5.4 投屏中快速启停：无孤儿 scrcpy 进程残留（任务管理器核对，对应 3.1）
- [ ] 5.5 无法自动化的项在 summary 标「未验证」
- [ ] 5.6 通过后归档本变更并合并 spec delta
