# Tasks: simplify-window-topbar

- [x] 1.1 `MainWindow.xaml` 移除底部状态条（第三行 RowDefinition + Border），布局改为顶栏 40 / 内容区两行
- [x] 1.2 `MainWindow.xaml` 移除顶栏标题 TextBlock，顶栏只留「操作」菜单 + 最小化/关闭
- [x] 1.3 `MainWindow.xaml` 加 `WindowChrome`（`GlassFrameThickness=0`、`CaptionHeight=0`、`ResizeBorderThickness=6`）去掉顶部白边；拖动/双击最大化逻辑不变
- [x] 1.4 顶栏 `MenuItem` 深色样式（隐式样式仅限主窗口资源，托盘菜单不受影响）
- [x] 1.5 `AppMenus` 托盘与顶栏共用 `BuildFlatItems`；顶栏 `BuildTopBarRoots` 返回单根「操作」；删 `BuildViewMenu`、`IMenuHost.DeviceTitle`；帮助/关于/全屏并入共用序列
- [x] 1.6 `MainWindow.xaml.cs` 删 `SetStatus`、`SetDeviceTitle`；`App.xaml.cs` 删 `DeviceTitle` 属性、`ChromeH` 64→40→28
- [x] 1.7 窗口宽度严格随视频比例：`DesiredWindowSize` 高度可用记忆值，宽度一律由 `(h - chrome) × r` 推出——记忆宽高比与视频流不一致时 scrcpy 会 letterbox，顶栏看起来比画面宽
- [x] 1.8 letterbox 兜底：捕获 scrcpy stderr `Initial texture: WxH`（真实流尺寸，最权威），`ScrcpyProcess.VideoSizeDetected` → App `OnVideoTexture` 按权威比例重定窗口；`MinWidth/MinHeight` 放宽（200/320），避免钳制比例计算结果；嵌入启动与 texture 重定均留日志
- [x] 2.1 `dotnet build` 0 警告
- [x] 2.2 真机确认：无白边、画面贴底；顶栏高度 28 且与画面左右边缘对齐（无 letterbox 黑边，tray-error.log 应有 `texture WxH -> window ...` 记录）；「操作」下拉各项与托盘菜单一致且可读；勾选项有 ✓、置灰项可辨识；顶栏拖动与双击最大化仍生效；F11 全屏/退出正常
  - 2026-11-23 用户真机验证通过
