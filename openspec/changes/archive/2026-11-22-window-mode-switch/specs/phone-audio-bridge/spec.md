## ADDED Requirements

### Requirement: 窗口模式切换

系统 SHALL 允许用户在「程序窗口」和「scrcpy 原生窗口」之间切换投屏显示方式，默认程序窗口。

#### Scenario: 默认程序窗口

- **GIVEN** 从未改过设置
- **WHEN** 开始投屏
- **THEN** 画面嵌入带菜单栏的程序窗口，行为与现状一致

#### Scenario: 切到原生窗口

- **GIVEN** 用户在托盘菜单选「scrcpy 原生窗口」
- **WHEN** 切换生效
- **THEN** 若正在投屏则重启一次
- **AND** scrcpy 用自己的窗口显示画面，我们用主窗口隐藏
- **AND** 没有菜单栏，用托盘菜单操作

#### Scenario: 原生窗口下的置顶

- **GIVEN** 处于原生窗口模式
- **WHEN** 用户开启窗口置顶
- **THEN** 通过 --always-on-top 生效，重启投屏
- **AND** 程序窗口模式仍是即时生效

#### Scenario: 原生窗口下的息屏提示

- **GIVEN** 处于原生窗口模式，手机息屏
- **WHEN** 判定息屏
- **THEN** 在 scrcpy 窗口原位显示黑屏提示与唤醒按钮
- **AND** 不在我们的主窗口内显示

#### Scenario: 不受影响的功能

- **GIVEN** 任一窗口模式
- **WHEN** 使用音频转发、解锁、暂停键、投屏开关键
- **THEN** 功能与模式无关，照常工作

#### Scenario: 设置持久

- **GIVEN** 用户选择某模式
- **WHEN** 重启托盘
- **THEN** 沿用该模式
