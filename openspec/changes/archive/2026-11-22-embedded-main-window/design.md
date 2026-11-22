# 设计：窗口宿主 + 画面嵌入

## 为什么走「自己开窗口」

scrcpy 的窗口是 SDL2 建的普通顶层窗口，没有菜单栏，也没有提供「外部插入控件」的接口。能做到菜单栏的只有两条路：

1. 在 scrcpy 窗口上贴一条自绘的条 —— 不是真正的菜单栏，跟随窗口时要靠定时器校正，最大化、DPI 变化、拖动时都会漂
2. 本程序自己创建窗口，菜单栏用系统 `MenuStrip`，把 scrcpy 的画面作为子窗口塞进内容区

选 2。菜单用系统 `MenuStrip` 控件，外观、行为、`Alt` 助记键都是 Windows 原生的。窗口本身去掉系统标题栏，标题、菜单、窗口按钮排在同一行，满足「不分两行」的要求。

## 窗口结构

```
Form（无边框，FormBorderStyle=None，可缩放）
├── Panel 顶栏（Dock=Top，高 36px）
│   ├── Label 标题（左，显示「手机音频 - 设备名」）
│   ├── MenuStrip（紧随标题，设备 / 音频 / 视图 / 帮助 / 关于）
│   └── 按钮组（右：最小化 / 最大化 / 关闭）
└── Panel 内容区（Dock=Fill）
    ├── 嵌入的 scrcpy 窗口（跨进程子窗口）
    ├── 覆盖层 Panel（手机已息屏提示 + 唤醒按钮，平时 Visible=false）
    └── 状态条 Label（Dock=Bottom，显示编码 / 缓冲 / 帧率 / 连接方式）
```

标题和菜单在同一行，符合用户要求。标准窗体做不到这一点：系统标题栏是 `WS_CAPTION` 画的非客户区，任何子控件都只能放在它的下方，所以只能去掉系统标题栏自己画。

## 顶栏怎么实现

无边框窗口丢掉了系统提供的窗口行为，必须逐项补回来，否则窗口拖不动、缩不了。

| 行为 | 实现 |
| --- | --- |
| 拖动窗口 | 顶栏 `MouseDown` → `ReleaseCapture()` + `SendMessage(hwnd, WM_NCLBUTTONDOWN, HTCAPTION, 0)`。走系统拖动，顺带免费获得贴边分屏、Win+方向键 |
| 双击最大化 / 还原 | 顶栏 `MouseDoubleClick` 切换 `WindowState` |
| 拖边缘缩放 | 子类化窗口过程处理 `WM_NCHITTEST`，边缘 4px 返回 `HTLEFT` / `HTRIGHT` / `HTTOP` / `HTBOTTOM` / 四角对应值，其余返回 `HTCLIENT` |
| 最大化时可拖回 | 顶栏拖动前判断 `WindowState=Maximized`，先还原再按鼠标位置定位 |
| 最小尺寸 | `MinimumSize`，避免缩到画面看不见 |
| 圆角与阴影 | 保持默认即可。要 Win11 圆角再加 `DwmSetWindowAttribute`，不作为必需项 |

`WM_NCHITTEST` 用 `Add-Type` 编译一个 `BorderlessForm : Form`，在 `WndProc` 里处理，PowerShell 侧只负责往这个窗体上挂控件。子类化必须写在 C# 里，PowerShell 无法安全地替换已有窗体的窗口过程。

顶栏颜色跟内容区一致（深色），按钮用自绘：悬停变浅，关闭按钮悬停变红。

## 嵌入流程

```
Start-Audio
  → scrcpy 启动（加 --window-title=<唯一标题>）
  → 轮询最多 5 秒：进程存在 且 MainWindowHandle != 0
  → ShowWindow(hwnd, SW_HIDE)            先藏起来，避免闪一下
  → SetWindowLong(hwnd, GWL_STYLE, 去掉 WS_CAPTION|WS_THICKFRAME|WS_MINIMIZEBOX|WS_MAXIMIZEBOX|WS_SYSMENU|WS_POPUP)
  → SetWindowLong(hwnd, GWL_EXSTYLE, 去掉 WS_EX_APPWINDOW|WS_EX_WINDOWEDGE) + WS_EX_CHILD 语义由 SetParent 处理
  → SetParent(hwnd, panel.Handle)
  → SetWindowPos(hwnd, 0,0,0, panel.W, panel.H, SWP_FRAMECHANGED|SWP_NOZORDER|SWP_NOACTIVATE)
  → ShowWindow(hwnd, SW_SHOW)
  → 主窗口 Show()
```

`--window-title` 用固定前缀加设备序列号，便于多实例时精确匹配自己的那个窗口，不会误抓别人的 scrcpy。

## 尺寸同步

`Panel.Resize` 时对子窗口调 `SetWindowPos`，宽高直接取 `Panel.ClientSize`。scrcpy 收到 `WM_SIZE` 会按客户区重新布局画面（等比，多余区域黑边），不会失真。

不在每次 `Update-State` 里同步，只在 `Resize` 事件里做，避免每秒几十次跨进程窗口调用。

## DPI

PowerShell 宿主默认是 DPI 不感知，取到的是缩放后的逻辑坐标，嵌入子窗口在 125%/150% 缩放下会错位。

处理方式：进程启动最早期调用 `SetProcessDPIAware()`（`user32.dll`），让本进程按物理像素工作。必须在创建任何窗口之前调用，所以放在 `Add-Type` 编译 Win32 声明之后、创建主窗口之前。声明失败时忽略，退回逻辑坐标，最坏是画面略微偏移，不影响可用性。

## 焦点与输入

- 点击画面区域后，焦点应落到 scrcpy 子窗口，键盘和鼠标映射才正常。`SetParent` 后系统会自动处理，但需要在 `Panel.MouseDown` 里补一次 `SetFocus(hwnd)`
- 菜单栏获得焦点时，scrcpy 会收到 `WM_KILLFOCUS`，scrcpy 会因此放开所有按下的键（这是它自带的安全行为），属于正常现象
- 全屏：隐藏顶栏和状态条，`WindowState=Maximized`，画面铺满。`Esc` 键或「视图 → 全屏」退出

## 睡眠遮罩改造

原来是独立顶层窗体贴在 scrcpy 客户区上，靠 `GetClientRect` + `ClientToScreen` 定位。改成主窗口内的覆盖 Panel 后：

- 覆盖层 `Dock=Fill`，放在内容区最上层，`BringToFront()` 显示
- 不再需要 Win32 定位，删掉 `Get-ScrcpyRect` 的定位用途（保留函数给降级模式用）
- 「唤醒并解锁」按钮逻辑不变

## 置顶

改为 `$form.TopMost = $settings.OnTop`，即时生效，写盘即可，不重启 scrcpy。

启动参数里的 `--always-on-top` 去掉：嵌入后它是子窗口，这个参数没有意义。

## 关闭与停止

- 点主窗口右上角 ×：停止投屏（与「音频 → 停止」等价），主窗口隐藏，托盘保留
- 托盘双击：有画面则重新显示主窗口，无画面则启动
- 托盘「退出」/ 窗口「关于 → 退出」：停止投屏 + 释放热键 + 移除托盘图标 + 退出进程
- `Stop-Audio` 里原来用 `CloseMainWindow()`，嵌入后子窗口没有系统菜单，改为 `PostMessage(WM_CLOSE)`，1.2 秒不退再 `Kill()`

## 降级路径

以下任一情况判定嵌入失败：等不到窗口句柄、`SetParent` 返回 0、`SetWindowPos` 失败。

处理：主窗口不显示，scrcpy 窗口恢复原来的样式（把去掉的 style 加回去）并显示，托盘提示一次「画面已回退到独立窗口」。功能不受影响，只是没有菜单栏。

## 对现有代码的改动点

| 位置 | 改动 |
| --- | --- |
| 新增 `BorderlessForm` | `Add-Type` 编译 C# 窗体类，`WndProc` 里处理 `WM_NCHITTEST` 做边缘缩放；顶栏拖动走 `WM_NCLBUTTONDOWN` |
| `Start-Audio` | 去掉 `-WindowStyle Hidden` 与 `--always-on-top`；加 `--window-title`；启动后调 `Attach-ScrcpyWindow` |
| `Stop-Audio` | `CloseMainWindow` 改 `PostMessage(WM_CLOSE)`；停止时清空嵌入句柄、隐藏主窗口 |
| `Set-OnTop` | 不再 `Restart-IfRunning`，直接改 `TopMost` |
| `Set-Screen` | 关闭投屏时隐藏主窗口；开启时创建并显示 |
| `Ensure-Win32` | 补 `SetParent` / `SetWindowLong` / `SetWindowPos` / `ShowWindow` / `PostMessage` / `SetProcessDPIAware` / `SetFocus` |
| `Show-SleepOverlay` / `Hide-SleepOverlay` | 改为操作主窗口内的覆盖 Panel；降级模式下仍用原浮窗 |
| 菜单构造 | 抽出一份菜单项定义，同时供托盘 `ContextMenuStrip` 和窗口 `MenuStrip` 使用，避免两处状态不同步 |
| `Update-State` | 同步窗口标题、状态条、两个菜单的勾选状态 |

## 兼容性

Windows 10 / 11 通用。嵌入用的是 20 年前就有的 `SetParent`，不依赖新 API。scrcpy 版本变化可能改变窗口类名，所以用 `--window-title` 定位而不是靠类名匹配。
