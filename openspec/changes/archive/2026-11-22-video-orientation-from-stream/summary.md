# 按视频流真实尺寸定窗口 — 完成总结

## 排查过程（重要）

上一版（1.6.0）用 `mRotation` 检测方向，完全无效。实测确认原因：

- 手机自动旋转关闭：`accelerometer_rotation=0`，`user_rotation=0`
- 所以 `mRotation` 恒为 0，App 横屏播放时它不变，检测代码永远不触发
- 日志里一条 orientation 记录都没有，是硬证据

真正的信号：`dumpsys display` 里 `DisplayDeviceInfo{"scrcpy"...}` 那行的宽×高。
实测当前值 `3168 x 1440`（横屏，ratio 2.2），而物理屏是 `1440x3168`（竖屏）。
视频 App 横屏时，scrcpy 虚拟屏（=视频流）变横，窗口却按物理屏竖屏比例定，画面被裁。

## 做了什么

- 新增 Get-VideoSize，读 scrcpy 虚拟屏宽高，正则取 `, W x H,` 避开 uniqueId 里的数字
- Update-Orientation 改用视频尺寸算 ratio，按 ratio 变化去重（容差 0.02），不再用 mRotation
- Start-Audio 启动时优先用 Get-VideoSize 定比例，读不到回退 wm size
- 比例变化时写日志 `orientation videosize=WxH ratio=...`
- 删掉 Get-Rotation 和 lastRotation

## 验证

- 正则已用真实 dumpsys 行验证：正确提取 3168x1440
- PSParser 语法检查通过
- 安装包 1.6.1 编译成功，脚本已部署，MD5 一致
- 本机安全策略禁止执行脚本，运行时行为未自测

## 如何验证是否生效

投屏中把视频切横竖屏，看 tray-error.log 有没有 `orientation videosize=...` 行。
有 → 检测工作；窗口随之变宽扁/窄高。没有 → 读虚拟屏失败。
