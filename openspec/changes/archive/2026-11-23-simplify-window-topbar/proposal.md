# Proposal: simplify-window-topbar

## 背景与动机

用户对投屏主窗口提出三项简化（v2 竖屏窗口画面空间紧张，每一条非画面像素都直接压缩可用画面）：

1. **底部状态条移除**：运行状态/模式/编码/缓冲/帧率在托盘菜单首行（`AppMenus.BuildTrayMenu` 的 `h.StatusText`）已可看，窗口内重复展示占用 24px。
2. **顶栏菜单收敛为一根「操作」**：原五根菜单（设备/音频/视图/帮助/关于）层级深、点击次数多，全部功能收敛到单根「操作」下拉，顶栏只留「操作 + 最小化/关闭」。
3. **顶部白边移除**：`WindowStyle=None` 下 WPF 残留系统 glass 边框，顶栏上沿有一条白线。

另附带：顶栏菜单为 WPF 默认样式，黑字叠深色顶栏不可读，改深色样式（纯 UI 样式，无 spec 影响）。

## 变更范围

- `MainWindow.xaml`：三行布局改为顶栏 40 / 内容区两行；移除标题 TextBlock 与状态条；`WindowChrome`（`GlassFrameThickness=0`、`CaptionHeight=0`，拖动/双击最大化仍由 TopBar 鼠标逻辑处理）；顶栏 `MenuItem` 深色隐式样式（白字、深色弹出层、勾选记号、置灰），仅作用于主窗口菜单树。
- `AppMenus.cs`：托盘菜单与顶栏「操作」共用同一份扁平项序列（`BuildFlatItems`）；`BuildViewMenu`、五根菜单拆分、`IMenuHost.DeviceTitle` 删除；原「帮助/关于/全屏」入口并入共用序列，托盘菜单同步获得这些入口。
- `MainWindow.xaml.cs`：删除 `SetStatus`、`SetDeviceTitle`。
- `App.xaml.cs`：`ChromeH` 64→40；删除 `DeviceTitle` 属性与两处 Set 调用。

## 影响

- 窗口高度中非画面部分从 64 DIP 减为 40 DIP，同样窗口高度下画面多 24 DIP。
- 托盘右键菜单新增「全屏 F11 / 快捷键说明 / 查看日志 / 关于」项（原先只在窗口顶栏可见）。
- spec「单行顶部栏 → 窗口结构」随本变更修改（见 specs delta）。
