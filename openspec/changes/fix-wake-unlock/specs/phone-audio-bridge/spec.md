# 手机音频桥接规范 · 增量（fix-wake-unlock）

## Modified Requirements

### Requirement: 自动解锁与反馈

系统 SHALL 在投屏启动前让设备进入可用（已解锁）状态，结果以右下角通知反馈，不弹对话框。

#### Scenario: 锁屏状态判定（修订）

- **GIVEN** 需要判断设备是否锁着（启动前解锁、手动唤醒、等待解锁结果）
- **WHEN** 读取锁屏状态
- **THEN** 合并 `dumpsys window policy` 与 `dumpsys window` 两条命令的输出后判定
- **AND** `isKeyguardShowing` 只存在于 `dumpsys window`（实测），只跑 `window policy` 会恒判未锁、假报「已解锁」
- **AND** 不得以 `interactiveState`/`screenState` 判断锁屏，只以 Keyguard 字段为准

#### Scenario: 首次遇到锁屏密码（v2 补齐）

- **GIVEN** 手机有锁屏密码，本机还没存过（无 device-pin.json）
- **WHEN** 解锁流程需要密码（返回 NEED_PIN）
- **THEN** 弹出一次输入框询问密码，保存后立刻自动重试解锁
- **AND** 取消或留空则跳过解锁并如实提示，不阻塞投屏启动
- **AND** 该能力在 v2 重构后缺失（SavePin 无调用者），本变更恢复，行为与 v1 一致

#### Scenario: 投屏中手动唤醒（修订）

- **GIVEN** 投屏正在运行且屏幕被 scrcpy 关闭（`--turn-screen-off`，屏幕控制权归 scrcpy）
- **WHEN** 用户点击「唤醒并解锁」或息屏提示层的「唤醒并解锁」
- **THEN** 先停掉 scrcpy 把屏幕控制权还给 adb，等待约 400ms
- **AND** 以 `forceWake=true` 执行唤醒解锁，绕过「投屏假报 AWAKE/已解锁」导致的成功短路
- **AND** 解锁后重新以投屏模式启动 scrcpy，且跳过重复的启动前解锁，避免解两次、闪两次
- **AND** 通知如实反映解锁结果：成功才提示成功，失败提示真实原因，不得因假状态提示「已解锁」却没有任何作用
- **AND** 没有 PIN / PIN 未保存时不阻塞投屏重启，只提示需要密码

#### Scenario: 投屏启动前自动解锁（v2 补齐）

- **GIVEN** 用户开启投屏或程序启动即投屏
- **WHEN** scrcpy 启动之前
- **THEN** 执行一次解锁流程，解锁成功后才启动 scrcpy，避免投出锁屏画面
- **AND** 唤醒后重启投屏的那一轮通过一次性标志跳过此步，不重复解锁
- **AND** 该能力在 v2 重构后一度缺失，本变更恢复，行为与 v1 一致
