# 设计：修复嵌入窗口顶栏被盖与大量黑边

## 上下文

主窗口 `MainWindow` 结构（WPF，无系统边框）：

```
Root Grid
  Row0  Height=40        TopBar（操作菜单 / 最小化 / 关闭）
  Row1  Height=*         ContentArea（ Black，嵌 scrcpy 画面）
```

`EmbedService.Attach(scrcpyHwnd, parentHwnd, w, h)` 当前把 scrcpy 设为 `parentHwnd`（即整个主窗口 `WindowInteropHelper.Handle`）的子窗口，`SetWindowPos` 定位在 `(0,0)`，尺寸用 `ContentArea.ActualWidth/Height`。

两个偏差的根因：

- **原点偏差**：子窗口坐标相对父窗口客户区。主窗口 `WindowStyle=None`，客户区原点 = 窗口可见左上角 = 顶栏左上角。把子窗口放 `(0,0)` 就是压在顶栏上。应放 `(0, topBarHeight)`，其中 `topBarHeight = TopBar.ActualHeight`（WPF DIP，需换算成物理像素）。
- **尺寸未接线**：`ScreenService.DesiredWindowSize()` / `RememberSize()` / `GetVideoRatio()` 已实现但 App 未调用，主窗口保持 XAML 的 `440×900`，比例与视频不符 → 大黑边。

## 关键决策

### 决策 1：定位偏移由 MainWindow 负责计算，EmbedService 只接收绝对偏移

- `EmbedService.Attach` 增加一个 `offsetX/offsetY`（物理像素）参数，`SetWindowPos` / `MoveWindow` 用它作为子窗口在父客户区中的原点。
- `MainWindow` 在 `TryEmbed` 里用 `TopBar.ActualHeight` 与 DPI 缩放算出 `offsetY`，传入。
- 理由：`EmbedService` 不应知道 WPF 布局；职责分离，偏移来源单一可查。

### 决策 2：DPI 统一按物理像素

- 项目尚未声明 DPI 感知（`SetProcessDPIAware` 只声明未调用）。scrcpy 自身 DPI 感知，若主进程不声明会出现二次缩放。
- 在 App 启动最早期（任何窗口创建前）调用 `SetProcessDPIAware()`。
- 所有传给 Win32 的坐标/尺寸用物理像素：WPF 的 DIP 乘以 `PresentationSource.FromVisual(this).CompositionTarget.TransformToDevice` 的缩放（或 `GetDpiForSystem()/96`）。
- 理由：避免 DPI 不一致导致的偏移/黑边误差。

### 决策 3：窗口尺寸在三个时机按视频比例计算并应用

1. **启动时**：`ToggleScreen` 里，非原生模式下先 `EnsureMainWindow()`，再调用 `_screen.DesiredWindowSize(...)` 算出 `(w,h)`，设置主窗口 `Width/Height`，然后 `_screen.Start(w, h)`，scrcpy 参数带上 `--window-width/--window-height`。
2. **横竖屏切换**：复用现有尺寸计算入口（若 v2 尚无方向轮询，本变更不新增轮询，仅在已有触发点接入；若无触发点则记为未验证）。
3. **拖动结束**：主窗口 `SizeChanged` 或拖拽结束后调 `_screen.RememberSize(w,h)`，按当前方向分别记忆。

`DesiredWindowSize` 的 `chromeH` 参数传顶栏高度，`inset` 传边框留白（用于拖动缩放的一圈）。

### 决策 4：内容区背景保持黑，窗口底色不再透出

- 消除黑边靠"尺寸贴合视频比例"，不是靠改背景色。内容区 `Background=Black` 保留作为嵌入前的兜底。
- 若有极小的取整残差（几像素），可接受，不为此引入拉伸变形。

### 决策 5：不改 scrcpy，不引入第三方库

- 仅用官方 scrcpy 5.0.1 与 .NET 8 / WPF 现有依赖。

## 风险与降级

- 跨进程嵌入本就非官方：若 `SetParent` 后 `GetParent` 校验失败，`Attach` 返回 false，沿用现有降级（回到 scrcpy 原生窗口 + 托盘提示）。
- DPI 多显示器混合缩放场景未实测，需在 summary 中标注。
- 方向轮询在 v2 中是否仍存在需在实现时确认；若无，则"横竖屏自动切尺寸"标注为未验证/待补。
