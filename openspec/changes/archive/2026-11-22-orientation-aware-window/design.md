# 设计

## 方向检测

新增 `Get-Rotation($serial)`：读 `adb shell dumpsys window`，从 `mRotation=N` 取数字。取不到返回 -1（未知），不触发任何动作，保持现状。

挂到 `Update-State` 的每秒轮询上，只在投屏运行中检测。

## 比例计算

现有 `videoRatio` 是物理分辨率的宽÷高，固定来自 `Get-ScreenSize`（竖屏 1440×3168 → 0.4545）。方向变化时按旋转值换算视频的实际显示比例：

- 旋转 0 / 180（竖屏）：`ratio = w ÷ h`（0.45）
- 旋转 90 / 270（横屏）：`ratio = h ÷ w`（2.2）

存入 `$script:videoRatio`，再调用现有的 `Adjust-WindowToVideo` 重算窗口宽度。

## Adjust-WindowToVideo 扩展

现在只按内容区高度反算宽度，横屏时算出的宽度会超过屏幕。改为分两种：

- 竖屏（ratio < 1）：高度优先，宽度 = 内容区高 × ratio，和现在一样
- 横屏（ratio > 1）：宽度优先，窗口宽度取屏幕工作区宽度减去边距，内容区高 = 内容区宽 ÷ ratio，窗口高度随之调整

调整后 `Update-MainLayout` + `Sync-EmbedSize`，画面跟着铺满。

## 状态去重

用 `$script:lastRotation` 记住上次方向。方向没变就跳过，避免每秒重排窗口。启动投屏时初始化一次当前方向。

## 回退

横屏窗口算出的高度若超过屏幕工作区，按工作区高度封顶，宽度按比例回算，保证不超出屏幕。
