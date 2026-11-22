# 设计

## 尺寸来源

新增 `Get-VideoSize($serial)`：读 `dumpsys display`，匹配 `DisplayDeviceInfo{"scrcpy"` 那一行的 `宽 x 高`，返回 `@(w,h)`。取不到返回空数组。

不用 `wm size`（物理屏固定竖屏），不用 `mRotation`（自动旋转关闭时恒为 0）。

## 比例计算

`ratio = w / h`。scrcpy 虚拟屏会随 App 内容方向变化：竖屏 0.45，横屏 2.2。

## 轮询

`Update-Orientation` 改为调用 `Get-VideoSize`，算出的 ratio 与 `$script:videoRatio` 比较：

- 差异小于 0.02：无变化，跳过
- 变化：更新 `$script:videoRatio`，调用 `Adjust-WindowToVideo`，写日志

去掉 `lastRotation` 和 `Get-Rotation`，改用 ratio 去重。

## 启动

`Start-Audio` 里原本用 `Get-ScreenSize` 算比例，改为优先用 `Get-VideoSize`。若读不到（scrcpy 还没建虚拟屏），回退到物理屏比例，轮询就绪后自动纠正。

## Adjust-WindowToVideo

保持现有分两支逻辑：ratio < 1 竖屏高度优先，ratio > 1 横屏宽度优先、高度封顶。不改。

## 日志

- `Get-VideoSize` 每次读到写一行 `videosize WxH`
- 比例变化时写 `orientation ratio=...`
- 便于确认检测链路是否工作
