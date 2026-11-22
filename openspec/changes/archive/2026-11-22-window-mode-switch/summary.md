# 窗口模式切换 — 完成总结

## 做了什么

- settings.json 增加 WindowMode（embedded 默认 / native），读取校验非法值回落
- 托盘菜单加「窗口模式 ▸ 程序窗口 / scrcpy 原生窗口」，互斥勾选；窗口菜单视图区同步
- Set-WindowMode：写设置、记日志、投屏中整路重启生效
- Start-Audio 分两支：
  - embedded：现有逻辑不变（Show-MainWindow、屏幕外建窗、嵌入、内容区尺寸）
  - native：不显示主窗口、不嵌入、不传 window-width/height（scrcpy 自开窗）、OnTop 用 --always-on-top、标题用可读设备名
- Set-OnTop 区分模式：embedded 即时改 TopMost；native 重启投屏
- 息屏提示无需改：只有 embedded=true 用窗口内覆盖层，native 自动走贴 scrcpy 窗口的独立层
- 横竖屏尺寸记忆、全屏尺寸调整在 native 下跳过（重置窗口尺寸项置灰）
- 原生模式标题不叫 ScrcpyBridge，但 Get-ScrcpyHwnd/Get-ScrcpyRect 用 MainWindowHandle，不依赖标题

## 验证

- PSParser 语法检查通过
- 安装包 1.7.0 编译成功，脚本已部署，MD5 一致
- 本机安全策略禁止执行脚本与 Add-Type，运行时未自测

## 已知限制

- 原生窗口没有菜单栏，所有操作走托盘
- 切模式会重启投屏，画面断一两秒
- 原生模式下置顶改动要重启，不能即时
