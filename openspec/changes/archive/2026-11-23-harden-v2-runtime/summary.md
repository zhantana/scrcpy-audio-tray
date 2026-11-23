# Summary: harden-v2-runtime

## 交付内容

四个阶段全部落地：

1. **异步化与异常兜底**：adb 调用移出 UI 线程（设计落点：`AdbClient` 保持同步薄封装，异步统一在调用侧 `Task.Run`，见 design D1 修订）；`DevicePoller` 后台轮询 + `Dispatcher.BeginInvoke` 封送；解锁链路全 async；右键菜单用缓存列表即时弹出；`SemaphoreSlim` 启停门闩；三处全局异常兜底（写日志 + 气泡 + 继续运行）；退出清理 try/catch/finally。
2. **配置与数据安全**：`JsonStore` 原子写（temp + `File.Replace`）；损坏配置改名 `.corrupt-<时间戳>` 留档 + 气泡提示一次 + 默认值回落；PIN 读写改走 `JsonStore`（删正则解析）；退出双写消除（配置统一由 OnExit 保存）。
3. **进程与正确性**：`ScrcpyProcess.Start` 先停旧实例、`Exited` 后 Dispose 置空；`SizeChanged` 只订一次；嵌入失败 `UndoPartial` 恢复样式；`FindWindowByPrefix` 校验进程名与可见性；`GetScreenSize` 去硬编码回退；设备状态解析统一到 `AdbClient`；热键多非修饰键拒绝；`Quote` 转义引号。
4. **可维护性**：菜单定义合一（`AppMenus`，`TrayMenuBuilder` 已删）；死代码清理（`CmbBitrate_SelectionChanged`、`HasPin`、`MainWindowHandle`、`ChildHandle`、`FindWindowByClass`、`FullScreenCommand`、`SleepMonitor` 整类并入）；日志保留 `tray-error.log` 文件名（spec 指定，分级暂缓），关键路径补埋点。

## 验证

- `dotnet build` 0 警告；`--selftest` 通过。
- 用户真机验证通过（2026-11-23）：断开设备启动 UI 不冻结；损坏 settings.json 提示 + `.corrupt` 留档 + 默认回落；投屏中快速启停无孤儿进程。
- 未验证项：无。
- spec 增量（启动失败可诊断 → 配置文件损坏场景）已合并进主 spec。
