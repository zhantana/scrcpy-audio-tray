# 设计

## 热键通道扩展

现有 `TrayHotkeyWindow` 只注册一个 `WM_HOTKEY`。改为注册两个 id：

- `hkId = 0x5150` → 暂停媒体
- `hkScreenId = 0x5151` → 投屏开关

`WndProc` 收到 `WM_HOTKEY` 时按 `wParam` 里的 id 分发到不同回调。两个 id 各自独立注册 / 注销，一个失败不影响另一个。

## 设置

`settings.json` 增加 `HotkeyScreen`，默认 `F7`。加载、保存与 `HotkeyPause` 同样处理：`None` 视为垃圾值回落默认，空字符串表示禁用。

两个值不允许相同：`Set-Hotkey` 写入前比较，相同则拒绝并提示。

## 菜单

托盘「快捷键」子菜单改为两组，各带 修改 / 禁用 / 恢复默认：

```
快捷键 ▸
  暂停键 ▸ 修改… / 禁用 / 恢复默认（Ctrl+Alt+P）
  投屏键 ▸ 修改… / 恢复默认（F7）
```

`Show-HotkeyDialog` 加一个标签参数，说明当前录的是哪个键；录制期间注销全部热键，取消后全部重注册。

## 投屏切换

`Invoke-ScreenToggle` 调用现有 `Set-Screen (-not $script:settings.Screen)`，复用重启逻辑，不新增投屏代码。切换后弹一条通知说明当前是投屏还是纯音频。

## 关联改动

- 窗口菜单「显示手机画面（投屏）」右侧显示投屏快捷键
- 快捷键说明弹窗列出两个键
- 退出与异常退出的清理路径注销两个 id
