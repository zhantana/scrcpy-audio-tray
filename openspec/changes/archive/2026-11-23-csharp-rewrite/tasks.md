# C# 重构任务

## 0. 前置（本次已完成）
- [x] 检测 git 仓库现状
- [x] 编写 proposal / design / spec / tasks 文档
- [x] 保留 v1 成果不动

## 1. 工程骨架
- [x] 1.1 新建 .NET 8 WPF 工程，建 Infrastructure / Services / UI 分层
- [x] 1.2 引入依赖（System.Text.Json、WinForms NotifyIcon 方案）
- [x] 1.3 建 Inno Setup v2 打包脚本（installer-v2.iss），独立目录+自定义目录

## 2. 基础设施层
- [x] 2.1 AdbClient：设备发现、连接、带序列号调用、屏幕/视频尺寸、媒体会话
- [x] 2.2 ScrcpyProcess：参数集中构造、起停、不弹控制台
- [x] 2.3 JsonStore + Logger：配置读写、v1 字段迁移、日志
- [x] 2.4 NativeMethods：热键/窗口嵌入/置顶 Win32 强类型声明

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
- [x] 4.2 主窗口：单行顶栏 + 嵌入/降级
- [x] 4.3 设置窗口：画质 / 帧率 / 编码 / 缓冲 + 快捷键录制窗口
- [x] 4.4 窗口模式切换 + 嵌入失败降级

## 5. 迁移与验证
- [x] 5.1 配置兼容：v1 settings.json 直接读入
- [x] 5.2 功能对照：13 项需求逐条实测 → 由后续 restore-v1-feature-parity / harden-v2-runtime 的真机实测覆盖（2026-11-23 用户确认通过）
- [x] 5.3 回归：音频不丢样本、多投屏关一路另一路有声、热键游戏内生效、横竖屏尺寸记忆 → 同上，随 parity 6.3 真机回归通过
- [x] 5.4 安装验证：自定义目录 D://soft//ScrcpyAudioTrayV2 成功，与 v1 并存
- [x] 5.5 v2 稳定后切换默认安装包 → `build/` 目录已只保留 installer-v2.iss（默认安装包即 v2），v1 脚本留在 legacy；2026-11-23 归档确认
