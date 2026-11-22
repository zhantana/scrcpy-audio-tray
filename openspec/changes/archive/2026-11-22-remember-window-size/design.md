# 设计

## 设置项

`settings.json` 增加两个数组：

- `SizePortrait`: `[宽, 高]`，竖屏窗口尺寸，默认空
- `SizeLandscape`: `[宽, 高]`，横屏窗口尺寸，默认空

Read-Settings 读取为 int 数组，Save-Settings 写回。

## 尺寸选择

`Adjust-WindowToVideo` 里，按当前 ratio 分支：

- 竖屏（ratio < 1）：若 `SizePortrait` 有值，直接用；否则按现有默认算法（高度取工作区 95%、宽度按比例）算一次并记住
- 横屏（ratio > 1）：若 `SizeLandscape` 有值，直接用；否则按现有默认算法（宽度取屏幕 95%）算一次并记住

这样每个方向第一次按默认值，之后固定用记忆值，不再继承切换前状态。

## 用户手动调整

主窗口 `Resize` 结束时（WM_EXITSIZEMOVE 或 Resize 事件里判断鼠标已松开），把当前尺寸写入当前方向：

- 由 `Adjust-WindowToVideo` 主动改的尺寸不算（用标志位 `$script:programResizing` 跳过）
- 用户拖动改的才记住

写一个 `Remember-CurrentSize`，按 `videoRatio` 判断方向，存对应数组并 Save-Settings。加节流：只在尺寸真的变了且不是程序改的时候写。

## 恢复默认

提供一个「重置窗口尺寸」入口（托盘或窗口菜单），清空两个数组，下次按默认值重算。

## 日志

记忆写入时记一行 `size portrait=WxH` / `size landscape=WxH`，便于核对。
