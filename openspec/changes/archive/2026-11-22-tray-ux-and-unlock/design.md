# 设计说明

## 解锁：为什么不做「硬解」，而做「不锁屏」

Android 没有提交锁屏凭据的接口，所有自动解锁本质都是模拟按键，路径完全取决于各厂商的锁屏界面。因此：

- 保留最小的按键注入能力（数字 / 字母 KEYCODE 是 AOSP 固定值，跨品牌一致）
- 拉起密码框按「标准度」降级，不写死任何机型的私有手势
- 真正的兜底是 `screen_off_timeout`——只要不进锁屏，就不需要解锁

实测依据：该机 `screen_off_timeout=60000`，点亮后密码还没输完屏幕又睡了，这是「解不开」的主因之一。

## 状态判断：为什么优先 `interactiveState`

- `dumpsys window policy` 单次约 129ms，比 `dumpsys window`（约 227ms）快
- `interactiveState` 是 AOSP 标准字段，Android 5~16 都有
- 不得用 `grep -q`：提前退出会让 dumpsys 报 Broken pipe，被 PowerShell 当成致命错误

## 关屏：为什么不能用电源键

实测：投屏中发 `input keyevent 26`，设备进入 `INTERACTIVE_STATE_SLEEP`，scrcpy 进程直接退出，投屏中断。
因此「真关屏 + 投屏不断」不 root 无解，只能采用 `--turn-screen-off`（关显示电源）+ 不自动息屏。

## 睡眠检测：为什么每秒一次、连续两次判定

- 投屏后设备睡下不再推帧，窗口停在最后一帧，必须主动提示
- `--turn-screen-off` 期间 `interactiveState` 仍为 `AWAKE`，不会误伤正常投屏
- 连续两次才判定，避开瞬时抖动造成的误报
- 每秒一次 ≈ 每次 130~200ms 的 adb 查询，开销可接受；纯音频模式不轮询

## 鼠标绑定：为什么不用默认值

scrcpy 默认 `bhsn`：右键返回、中键桌面、第 4 键最近任务、第 5 键通知栏。电脑上侧键误触概率高，改为 `bhb-`：
第 4 键改成返回（符合「后退键」本意），第 5 键直接忽略。Shift 组合沿用默认，仍可把点击原样转发。
