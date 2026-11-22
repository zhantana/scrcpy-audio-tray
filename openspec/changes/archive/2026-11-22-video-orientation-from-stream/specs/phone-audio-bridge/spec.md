## ADDED Requirements

### Requirement: 按视频流真实方向定窗口

系统 SHALL 从 scrcpy 虚拟屏的实际宽高判断投屏方向，并据此定窗口比例，保证 App 横竖屏切换时画面完整。

#### Scenario: 视频横屏

- **GIVEN** 手机视频 App 横屏播放，scrcpy 虚拟屏为 3168x1440
- **WHEN** 投屏运行
- **THEN** 读到宽高比 2.2
- **AND** 窗口为宽扁比例，画面完整不被裁

#### Scenario: 视频转竖屏

- **GIVEN** 视频从横屏转回竖屏，虚拟屏变 1440x3168
- **WHEN** 轮询检测到比例变化
- **THEN** 更新为 0.45，窗口改回窄高
- **AND** 音频与解锁不受影响

#### Scenario: 读不到虚拟屏

- **GIVEN** scrcpy 虚拟屏尚未建立或 adb 读不到
- **WHEN** 检测执行
- **THEN** 沿用物理屏比例，不报错
- **AND** 读到后自动纠正

#### Scenario: 检测可诊断

- **GIVEN** 投屏运行中
- **WHEN** 每次读虚拟屏尺寸、比例发生变化
- **THEN** 写入 tray-error.log，含尺寸与比例
