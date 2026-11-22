## ADDED Requirements

### Requirement: 自定义安装目录

安装程序 SHALL 允许用户在安装时选择安装位置。

#### Scenario: 选择目录安装

- **GIVEN** 用户运行安装包
- **WHEN** 进入安装向导
- **THEN** 出现「选择安装位置」页，默认 `%LOCALAPPDATA%\ScrcpyAudioTray`
- **AND** 用户可改为任意有写权限的目录
- **AND** 程序、配置、日志、scrcpy 全部落在所选目录，功能不受影响
