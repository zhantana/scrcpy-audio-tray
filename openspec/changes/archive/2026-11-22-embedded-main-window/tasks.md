# 任务

## 1. 基础设施

- [x] 1.1 扩展 `Ensure-Win32`：补 `SetParent`、`SetWindowLong`/`GetWindowLong`、`SetWindowPos`、`ShowWindow`、`PostMessage`、`SetFocus`、`SetProcessDPIAware`、`FindWindow`
- [x] 1.2 进程启动后立即调用 `SetProcessDPIAware()`，失败忽略

## 2. 主窗口与单行顶栏

- [x] 2.1 `Add-Type` 编译 `BorderlessForm : Form`：`WndProc` 处理 `WM_NCHITTEST`（边缘 4px 返回各方向的缩放值）、`FormBorderStyle=None`、`MinimumSize`
- [x] 2.2 建顶栏 Panel（`Dock=Top`，高 36）：左侧标题 Label、紧随的 `MenuStrip`、右侧最小化 / 最大化 / 关闭按钮
- [x] 2.3 顶栏拖动：`ReleaseCapture` + `WM_NCLBUTTONDOWN/HTCAPTION`；最大化状态下先还原再定位
- [x] 2.4 顶栏双击最大化 / 还原
- [x] 2.5 按钮自绘：悬停变浅，关闭按钮悬停变红
- [x] 2.6 内容区 `Panel`（`Dock=Fill`）作为嵌入容器
- [x] 2.7 底部状态条：编码方式、缓冲 ms、帧率、连接方式
- [x] 2.8 关闭按钮 → 停止投屏并隐藏窗口；`FormClosing` 里拦截，不退出进程

## 3. 菜单统一

- [x] 3.1 把菜单项定义抽成一个函数，返回菜单项集合，托盘 `ContextMenuStrip` 与窗口 `MenuStrip` 各取一份
- [x] 3.2 勾选状态、可用状态在 `Update-State` 里同时刷新两处
- [x] 3.3 设备子菜单动态构建逻辑复用现有 `Update-DeviceMenu`

## 4. 画面嵌入

- [x] 4.1 `Start-Audio` 去掉 `-WindowStyle Hidden`、去掉 `--always-on-top`，加 `--window-title=ScrcpyBridge|<serial>`
- [x] 4.2 `Wait-ScrcpyWindow`：最多 5 秒轮询 `MainWindowHandle`，也支持按标题 `FindWindow`
- [x] 4.3 `Attach-ScrcpyWindow`：去边框样式 → `SetParent` → `SetWindowPos` 填满 → `ShowWindow`
- [x] 4.4 `Panel.Resize` 同步子窗口尺寸（含首次嵌入后的一次校正）
- [x] 4.5 `Panel.MouseDown` 时 `SetFocus` 到子窗口，保证键盘输入进手机
- [x] 4.6 加入可开关的自检输出，记录每一步的返回值，失败时写 `tray-error.log`

## 5. 降级路径

- [x] 5.1 嵌入任一步失败：恢复原窗口样式、显示 scrcpy 自己的窗口、隐藏主窗口
- [x] 5.2 托盘提示一次「画面已回退到独立窗口」
- [x] 5.3 降级状态下睡眠遮罩仍用原独立浮窗逻辑

## 6. 联动改造

- [x] 6.1 `Set-OnTop` 改为直接改 `TopMost`，不再重启
- [x] 6.2 `Set-Screen` 开启时创建并显示主窗口，关闭时隐藏
- [x] 6.3 `Stop-Audio` 用 `PostMessage(WM_CLOSE)` 收尾，超时再 `Kill`；清空嵌入句柄
- [x] 6.4 `Show-SleepOverlay` / `Hide-SleepOverlay` 改为操作内容区内的覆盖面板
- [x] 6.5 托盘双击：无画面则启动，有画面但窗口隐藏则显示窗口
- [x] 6.6 纯音频模式不创建窗口

## 7. 验证

- [x] 7.1 语法检查（`PSParser::Tokenize`）
- [ ] 7.2 实机：投屏出现菜单栏、画面不遮挡、点击滑动正常、键盘输入正常
- [ ] 7.3 实机：缩放窗口画面跟随、置顶即时生效不闪断
- [ ] 7.4 实机：息屏提示在窗口内显示，唤醒按钮可用
- [ ] 7.5 实机：关闭窗口停止投屏、托盘双击恢复
- [ ] 7.6 实机：拖动窗口、拖四边缩放、双击顶部栏最大化与还原、贴边分屏均正常
- [ ] 7.7 实机：切换设备、改帧率后重新嵌入正常

## 8. 收尾

- [x] 8.1 更新 `README.txt` 与 `build/gen_readme.py`
- [x] 8.2 版本升到 1.3.0，编译 `dist\ScrcpyAudioTray-Setup-1.3.0.exe`
- [x] 8.3 部署脚本到安装目录并备份旧版
- [x] 8.4 补 `summary.md` 并归档到 `openspec/changes/archive/`
- [x] 8.5 追加工作日志
