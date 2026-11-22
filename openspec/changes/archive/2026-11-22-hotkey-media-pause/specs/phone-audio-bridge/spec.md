# 本次变更差异：手机媒体暂停快捷键

## ADDED

### Requirement: 手机媒体暂停快捷键

系统 SHALL 提供全局快捷键，向当前设备发送媒体播放/暂停键，并允许用户自定义该快捷键。

#### Scenario: 默认快捷键

- **GIVEN** 托盘正在运行，手机通过 USB 或 Wi-Fi 连接
- **WHEN** 用户按下 `Ctrl+Alt+P`
- **THEN** 向设备发送 `input keyevent 85`
- **AND** 手机上正在播放的视频或音乐暂停，电脑耳机同时无声
- **AND** 再按一次恢复播放

#### Scenario: 自定义快捷键

- **GIVEN** 用户打开「快捷键 → 修改…」
- **WHEN** 按下 `F8`
- **THEN** 弹窗立即记录并关闭
- **AND** 新快捷键写入 `settings.json` 的 `HotkeyPause`
- **AND** 菜单右侧显示 `F8`

#### Scenario: 单键与组合键

- **GIVEN** 用户录制时只按了一个键
- **WHEN** 确认
- **THEN** 照常保存，不要求必须带 Ctrl/Alt/Win
- **AND** 录制窗口提示单键会全局占用，推荐使用 F 键

#### Scenario: 禁用快捷键

- **GIVEN** 用户选择「快捷键 → 禁用」
- **WHEN** 按下任何键
- **THEN** 不发送媒体键
- **AND** `HotkeyPause` 写为空字符串

#### Scenario: 快捷键被占用

- **GIVEN** 目标组合键已被其他软件注册
- **WHEN** 托盘启动或用户修改快捷键
- **THEN** 弹出提示，要求换一个
- **AND** 其他功能不受影响

#### Scenario: 没有可暂停的媒体

- **GIVEN** 手机上没有任何媒体会话
- **WHEN** 用户按下快捷键
- **THEN** 不发送按键
- **AND** 提示「手机上没在播放」
- **AND** 不启动手机上的音乐应用

#### Scenario: 游戏不响应

- **GIVEN** 手机前台是游戏，且游戏未注册媒体会话
- **WHEN** 用户按下快捷键
- **THEN** 提示没有可暂停的媒体
- **AND** 不声称支持游戏暂停
