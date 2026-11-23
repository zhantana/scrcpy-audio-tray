## ADDED Requirements

### Requirement: v2 重构架构

系统 SHALL 提供基于 C# / .NET 8 / WPF 的 v2 实现，替代 PowerShell 外壳，同时保留 v1 成果可回退。

#### Scenario: v2 与 v1 并存

- **GIVEN** v1 PowerShell 版本已存在
- **WHEN** v2 开发完成
- **THEN** v1 源码与安装包完整保留，不作删除或修改
- **AND** v2 使用独立构建产物，可单独安装与卸载
- **AND** 两者读写同一套配置格式，不互相破坏

#### Scenario: 配置向后兼容

- **GIVEN** 用户已有 v1 的 settings.json、设备别名、解锁密码
- **WHEN** 首次运行 v2
- **THEN** 原样读入所有 v1 字段与数据文件
- **AND** 历史垃圾值（如 HotkeyPause = "None"）回落到默认值
- **AND** 用户不需要重新配对设备或重设快捷键

## MODIFIED Requirements

### Requirement: 屏幕镜像

系统 SHALL 通过 scrcpy 提供投屏，画质与帧数参数由可视化设置控制。

#### Scenario: 编码参数可调

- **GIVEN** 用户打开画质设置
- **WHEN** 调整码率、编码格式、帧率、分辨率
- **THEN** 参数写入 scrcpy 启动参数并在下次投屏生效
- **AND** 帧率可选到手机原生 90，默认 60
- **AND** 码率默认 16Mbps，编码格式可选 H.264 / H.265 / AV1
- **AND** 不改变 scrcpy 源码，仅通过命令行参数

#### Scenario: 窗口模式

- **GIVEN** 用户选择窗口模式
- **WHEN** 选择原生窗口
- **THEN** scrcpy 自己开窗，最稳定，操作走托盘
- **WHEN** 选择程序窗口
- **THEN** 画面嵌入带单行顶栏的 WPF 窗口
- **AND** 嵌入失败时自动降级到原生窗口并提示一次，音频与解锁不受影响

### Requirement: 连接方式

系统 SHALL 支持 USB 有线与 WiFi 无线两种 adb 连接，提供蓝牙不可用的说明。行为与 v1 一致，不因重构退化。

### Requirement: 自动解锁与反馈

系统 SHALL 提供跨设备通用的解锁流程与息屏提示，行为与 v1 一致（dismiss-keyguard → 回车 → 上滑 → PIN 四级降级），不因重构退化。

### Requirement: 手机媒体暂停快捷键

系统 SHALL 提供可自定义的全局媒体暂停快捷键，行为与 v1 一致，含进程提权后游戏内可用。

### Requirement: 投屏开关快捷键

系统 SHALL 提供可自定义的投屏开关快捷键，行为与 v1 一致。

### Requirement: 记忆各方向窗口尺寸

系统 SHALL 分别记忆横竖屏窗口尺寸，行为与 v1 一致；原生窗口模式下由 scrcpy 自行管理，不做尺寸记忆。
