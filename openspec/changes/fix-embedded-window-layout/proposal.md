# 变更：修复嵌入窗口顶栏被盖与大量黑边

## Why

v2 重构后，程序窗口模式出现两个可见缺陷：

1. **顶部操作栏消失**：`EmbedService.Attach` 把 scrcpy 画面窗口设为整个主窗口的子窗口，但定位在 `(0,0)`。主窗口 `WindowStyle=None`，其客户区原点就是屏幕上的窗口左上角，`(0,0)` 正好落在 40px 顶栏位置，scrcpy 画面把顶栏整条盖住。顶栏元素其实还在，只是被 z-order 压在下面，用户无法看到也无法点击。
2. **画面外大量灰屏/黑边**：`ScreenService` 已有 `GetVideoRatio()` / `DesiredWindowSize()` / `RememberSize()`，但 `App.xaml.cs` 从未调用。主窗口沿用 XAML 写死的 `440×900`，比例与手机视频流不一致，scrcpy 按视频比例等比缩放后四周留下大片黑边；窗口底色 `#121216` 透出形成灰屏。横竖屏切换也不重算尺寸，画面比例长期错误。

## What Changes

- 修正 scrcpy 子窗口的定位原点：从主窗口左上角改为内容区左上角，纵向偏移顶栏实际高度（含 DPI 缩放），顶栏始终可见可点。
- 投屏启动时按视频流宽高比计算并设置主窗口尺寸，使内容区比例贴合视频，黑边降到不可见。
- scrcpy 启动参数带上算好的 `--window-width/--window-height`，避免先用错误尺寸建窗再纠正导致闪动。
- 横竖屏切换、窗口拖动结束后重新应用尺寸，沿用现有记忆逻辑（竖屏/横屏分别记忆）。
- 全程不改 scrcpy 源码，仍用官方发布包。

## Capabilities

### New Capabilities

无（修复既有能力，不新增）。

### Modified Capabilities

- `phone-audio-bridge` 的单行顶部栏「画面嵌入」与「窗口跟随视频流方向」「记忆各方向窗口尺寸」三项行为被修正：顶栏不再被覆盖，窗口尺寸在启动/旋屏/拖动三个时机真正按视频比例应用。

## Impact

- 影响文件：`EmbedService.cs`（定位偏移）、`MainWindow.xaml.cs`（传入内容区偏移与正确尺寸）、`App.xaml.cs`（接线尺寸计算与记忆）、`ScreenService.cs`（接口微调，如需要）。
- 不影响音频转发、解锁、快捷键、置顶、多设备等功能。
- 原生窗口模式不受影响（不走嵌入路径）。
