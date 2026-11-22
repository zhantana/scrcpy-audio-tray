# 变更差异：phone-audio-bridge（2026-11-22）

本文件只记录本次变更相对上一版的变化，完整现状见 `openspec/specs/phone-audio-bridge/spec.md`。

## ADDED

### Requirement: 无线音频转发

#### Scenario: 启动后音频通路未激活

- **GIVEN** 音频转发已启动、数据流已建立，但电脑没有声音
- **WHEN** 手机处于空闲、无人操作状态
- **THEN** 启动后自动发一次音量键（`24` 再 `25`）叫醒通路，音量前后不变
- **AND** 只用于叫醒，不得改变用户音量

### Requirement: 屏幕镜像

#### Scenario: 鼠标侧键绑定

- **GIVEN** 投屏窗口已打开
- **WHEN** 按下鼠标侧键
- **THEN** 不沿用 scrcpy 默认绑定（第 4 键最近任务、第 5 键通知栏）
- **AND** 使用 `--mouse-bind=bhb-:++++`：右键 BACK、中键 HOME、第 4 键 BACK、第 5 键忽略
- **AND** 只作用于投屏窗口，不改变系统鼠标设置

#### Scenario: 投屏窗口置顶（可选）

- **GIVEN** 菜单提供「投屏窗口置顶」，默认关闭
- **WHEN** 用户勾选
- **THEN** 重启投屏并带上 `--always-on-top`
- **AND** 状态写入 `settings.json` 的 `OnTop`，下次启动沿用
- **AND** 纯音频模式该项置灰

#### Scenario: 手机意外睡下去后的提示

- **GIVEN** 投屏运行中，画面由手机逐帧推送
- **WHEN** 手机真的睡下去
- **THEN** 托盘每秒检测一次 `interactiveState`，连续两次非 AWAKE 才判定
- **AND** 判定后在投屏窗口原位覆盖黑屏，显示「手机已息屏」和「唤醒并解锁」按钮
- **AND** 点击按钮立即唤醒并解锁，恢复投屏
- **AND** 覆盖层跟随投屏窗口位置与尺寸；进程退出后沿用最后位置
- **AND** 恢复亮屏时自动隐藏；取不到窗口句柄时才退回托盘通知
- **AND** 纯音频模式不轮询

#### Scenario: 关屏只关闭显示

- **GIVEN** `--turn-screen-off` 已生效，物理屏熄灭
- **WHEN** 直接触摸手机黑屏
- **THEN** 触摸仍然生效——关屏只关显示，不关触摸
- **AND** 免 root 无接口可单独屏蔽触摸

#### Scenario: 真关屏会中断投屏

- **GIVEN** 投屏正在运行
- **WHEN** 用电源键让设备真正休眠
- **THEN** 设备进入 `INTERACTIVE_STATE_SLEEP`，scrcpy 退出，投屏中断
- **AND** 因此「真关屏 + 投屏不断」不 root 不可兼得

## MODIFIED

### Requirement: 屏幕镜像

- 「投屏期间不自动息屏」补充：保持唤醒只依赖 `screen_off_timeout`，不使用 `svc power stayon`（仅充电生效，且会覆盖用户的充电保持唤醒设置）

### Requirement: 自动解锁与反馈

- 状态判断改双源（`interactiveState` 优先，`screenState` 兜底）
- 拉起密码框改三级降级（`dismiss-keyguard` → `keyevent 66` → 单指上滑）
- 判断密码键盘改用截图大小，`uiautomator` 读不到该界面
- PIN 输入后先查结果，未解开再补回车
