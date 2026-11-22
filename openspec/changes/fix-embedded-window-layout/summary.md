# 交付说明：修复嵌入窗口顶栏被盖与大量黑边

## 改了什么

| 文件 | 改动 |
|---|---|
| `App.xaml.cs` | 启动最早调用 `SetProcessDPIAware()`；`ToggleScreen` 在嵌入模式下先用视频比例算窗口尺寸、设置主窗口、把内容区物理尺寸传给 scrcpy；拖动窗口防抖后记忆尺寸 |
| `EmbedService.cs` | `Attach`/`Resize` 增加物理像素原点偏移，`MoveWindow`/`SetWindowPos` 按偏移定位，画面不再压住顶栏 |
| `MainWindow.xaml.cs` | 新增 `DpiScale` 与 `ContentPixelSize()`；`TryEmbed` 下移一个顶栏高度；`ApplyWindowSize` 设窗并返回内容区物理尺寸；尺寸变化通知 App 记忆 |
| `ScreenService.cs` | `DesiredWindowSize` 去掉 DPI 二次缩放，统一在 DIP 空间计算，避免非 100% 缩放下尺寸算错 |

## 验证状态

- **通过**：`dotnet build` 0 错误 0 警告；`--selftest` 读到在线设备、videoSize 与屏幕比例一致、启动参数含 `--window-width/--window-height`。
- **未验证（需真机目视）**：顶栏完整可见可点、画面外黑边不可见、拖动后尺寸记忆、横竖屏切换比例正确。这些依赖实际窗口渲染，编译与自检无法覆盖。
- **未实现（标注）**：v2 目前没有横竖屏方向轮询——`ScreenService.GetVideoRatio()` 会读 scrcpy 虚拟屏方向，但没有定时器持续检测并在方向变化时重算窗口尺寸。因此"App 横竖屏切换后窗口自动变形"在 v2 中仍不成立，本次未新增该机制（超出本变更范围）。启动时按当前方向定尺寸是生效的。

## 归档

真机目视确认上面「未验证」项后，再把本变更移入 `archive/` 并合并 spec delta。
