# C# 重构任务

## 0. 前置（本次已完成）
- [x] 检测 git 仓库现状
- [x] 编写 proposal / design / spec / tasks 文档
- [x] 保留 v1 成果不动

## 1. 工程骨架
- [x] 1.1 新建 .NET 8 WPF 工程，建 Infrastructure / Services / UI 分层
- [x] 1.2 引入依赖（System.Text.Json、WinForms NotifyIcon 方案）
- [ ] 1.3 建 Inno Setup v2 打包脚本，默认目录、自定义目录、提权启动沿用 v1

## 2. 基础设施层
- [x] 2.1 AdbClient：设备发现、连接、带序列号调用、屏幕/视频尺寸、媒体会话
- [x] 2.2 ScrcpyProcess：参数集中构造、起停、不弹控制台
- [x] 2.3 JsonStore + Logger：配置读写、v1 字段迁移、日志
- [ ] 2.4 NativeMethods：热键注册、窗口嵌入、置顶所需 Win32 声明（强类型 P/Invoke）

## 3. 应用层
- [x] 3.1 ConfigService：设置模型 + 兼容迁移
- [x] 3.2 DeviceService：设备列表 / 别名 / 连接类型 / 记忆地址
- [x] 3.3 ScreenService：投屏开关、窗口模式、编码参数、窗口尺寸记忆
- [x] 3.4 MediaService：媒体暂停（音频其余并入 ScrcpyOptions）
- [x] 3.5 HotkeyService：双热键注册、冲突检测（录制窗口待做）
- [x] 3.6 UnlockService：通用解锁四级降级（SleepMonitor 待做）
- [x] 3.7 方向跟随并入 ScreenService.GetVideoRatio

## 4. UI 层
- [x] 4.1 托盘图标与菜单（基础入口）
- [ ] 4.2 主窗口：原生模式（无嵌入）先可用
- [ ] 4.3 设置窗口：画质 / 帧率 / 编码 / 快捷键 / 设备
- [ ] 4.4 窗口模式：单行顶栏 + 嵌入（可选，失败降级）

## 5. 迁移与验证
- [ ] 5.1 配置兼容：用 v1 的 settings.json 直接启动 v2
- [ ] 5.2 功能对照：13 项需求逐条实测
- [ ] 5.3 回归：音频不丢样本、多投屏关一路另一路有声、热键游戏内生效、横竖屏尺寸记忆
- [ ] 5.4 安装 / 覆盖 / 卸载干净
- [ ] 5.5 v2 稳定后切换默认安装包
