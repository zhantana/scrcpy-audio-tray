# 任务清单：修复嵌入窗口顶栏被盖与大量黑边

- [x] 1. App 启动最早期调用 `SetProcessDPIAware()`，确保任何窗口创建前声明 DPI 感知。
- [x] 2. `EmbedService.Attach` / `Resize` 增加物理像素坐标偏移参数，`SetWindowPos` 与 `MoveWindow` 用它作为原点；`Detach` 逻辑不受影响。
- [x] 3. `MainWindow.TryEmbed` 计算顶栏偏移（`TopBar.ActualHeight` × DPI 缩放），传给 `EmbedService`；`SizeChanged` / `OnRenderSizeChanged` 缩放时保持偏移一致。
- [x] 4. `App.ToggleScreen`：非原生模式下，启动前用 `_screen.DesiredWindowSize(...)` 算尺寸并设置主窗口 `Width/Height`，再 `_screen.Start(w, h)`，让 scrcpy 参数带 `--window-width/--window-height`。
- [x] 5. 主窗口尺寸变化防抖后调 `_screen.RememberSize(w, h)`，按横竖方向分别记忆。
- [x] 6. 确认 v2 中无横竖屏方向轮询触发点——`ScreenService` 有方向判断但没有定时检测 scrcpy 虚拟屏尺寸的机制，方向切换自动重算记为未验证（见 summary）。
- [x] 7. 编译 `dotnet build` 通过（0 错误 0 警告）；发布版 `--selftest` 通过（设备在线、videoSize 与屏幕比例一致、启动参数含 window-width/height）。真机目视验证待用户执行。
- [x] 8. 归档：真机目视验证通过（用户确认，2026-11-23）后移入 `openspec/changes/archive/`，spec「单行顶部栏」增量已合并进主 spec。
