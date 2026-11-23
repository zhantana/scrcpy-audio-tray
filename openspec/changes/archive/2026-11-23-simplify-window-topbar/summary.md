# Summary: simplify-window-topbar

## 交付内容

按用户三轮反馈精简投屏主窗口，全部实现并真机验证通过（2026-11-23）：

1. **底部状态条移除**：布局改两行（顶栏 / 内容区），`ChromeH` 64→28，画面区延伸到窗口底并直接贴底。运行状态信息保留在托盘菜单首行。
2. **顶栏收敛为单根「操作」菜单**：标题 TextBlock 与五根菜单（设备/音频/视图/帮助/关于）删除；`AppMenus.BuildFlatItems` 成为托盘菜单与顶栏「操作」下拉的唯一来源，帮助/关于/全屏入口并入共用序列（托盘菜单同步获得）。
3. **顶部白边移除**：`WindowChrome`（`GlassFrameThickness=0`、`CaptionHeight=0`、`ResizeBorderThickness=6`）去掉 `WindowStyle=None` 残留的系统 glass 边框；拖动/双击最大化逻辑不受影响。
4. **顶栏菜单深色样式**：主窗口隐式样式（白字、深色弹出层、✓ 勾选记号、置灰 #777），不影响托盘菜单。
5. **顶栏与画面对齐（letterbox 消除）**：
   - `DesiredWindowSize` 高度可用记忆值，宽度一律由 `(h - chrome) × r` 推出；
   - `ScrcpyProcess` 捕获 scrcpy stderr `Initial texture: WxH`（真实流尺寸，异步读防管道阻塞），经 `ScreenService.VideoSizeDetected` → App `OnVideoTexture` 按权威比例重定窗口；
   - `MinWidth/MinHeight` 放宽到 200/320，避免钳制比例计算；
   - 嵌入启动与 texture 重定均写日志（`embed start`、`texture WxH -> window`）。

## 验证

- `dotnet build` 0 警告 0 错误。
- 真机验证通过（用户确认）：无白边、画面贴底、顶栏 28 且与画面左右边缘对齐、操作菜单可读、拖动/双击最大化/F11 正常。
- 未发现未验证项。
