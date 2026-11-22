# 设计

## 设置

`settings.json` 增加 `WindowMode`：'embedded'（默认）或 'native'。Read-Settings 读取，非法值回落 embedded。Save-Settings 写入。

## 切换动作

`Set-WindowMode($mode)`：
- 写 settings、保存
- 若正在投屏，`Restart-IfRunning` 后 `Start-Audio -SkipUnlock` 重启
- 更新菜单勾选状态

## Start-Audio 分支

投屏启动按 WindowMode 走两条路：

**embedded（现有逻辑不变）**：
- Show-MainWindow，scrcpy 窗口建在屏幕外，按内容区尺寸开窗
- 启动后 Start-AttachTimer 嵌入

**native（新分支）**：
- 不 Show-MainWindow、不建在屏幕外
- 加 `--window-title=ScrcpyBridge|<serial>`（多开定位用，标题可读）
- 置顶：若 OnTop 则加 `--always-on-top`
- 不传 `--window-width/--window-height`（scrcpy 自己按视频比例开窗）
- 不调用 Start-AttachTimer，不需嵌入
- 让 scrcpy 窗口正常显示在桌面

## 窗口操作差异

- `Set-OnTop`：embedded 改主窗口 TopMost 即时生效；native 时改为写设置并重启投屏（因为要靠 --always-on-top）
- 息屏提示 `Show-SleepOverlay`：embedded 用窗口内 sleepInner；native 走原来的贴 scrcpy 窗口原位逻辑（Get-ScrcpyRect 定位独立 sleepForm）
- `Set-FullScreen`、`Set-Screen` 的尺寸调整：native 模式跳过（没有我们的窗口）

## 菜单

托盘加「窗口模式」子菜单，两项互斥勾选：
- 程序窗口（embedded）
- scrcpy 原生窗口（native）

窗口菜单的视图区也加同一组，勾选状态由 Sync-WindowMenu 同步。

## 日志

模式切换写一行 `windowmode = native/embedded`，便于排查。
