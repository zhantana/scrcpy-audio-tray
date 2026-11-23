# Summary: fix-wake-unlock

## 问题

v2 重构后「唤醒并解锁」假报成功：实际屏幕没点亮、没解锁，但托盘提示已解锁。直接原因是锁屏判定只跑 `dumpsys window policy`，而实测 `isKeyguardShowing` 只在 `dumpsys window` 里——恒判未锁，解锁流程被整体短路。

## 交付内容

1. **锁屏判定合并查询**：`AdbClient` 合并 `dumpsys window policy` + `dumpsys window` 判定（后随 harden-v2-runtime 演进为结构化 `GetScreenState`，UnlockService/DevicePoller 只消费结构化结果）。
2. **PIN 录入弹窗**：`PinPromptWindow` 在 `NEED_PIN` 时弹出录入，保存 `device-pin.json` 后自动重试；启动前解锁与手动唤醒统一走 `UnlockWithPinPrompt`（harden 后为 `UnlockWithPinPromptAsync`，跳过逻辑参数化为 `skipUnlock`，替代早期的 `_skipUnlockOnce` 字段）。
3. **如实反馈**：取消/留空/无 PIN 时不假报成功，托盘提示真实原因，不阻塞投屏启动。
4. **启动前自动解锁**：投屏启动前先解锁（`--turn-screen-off` 生效后 adb 点不亮屏幕）；唤醒后重启投屏跳过重复解锁。

## 验证

- 用户真机验证通过（2026-11-23）：投屏中唤醒→屏幕真的点亮解锁、画面恢复、提示真实；投屏启动前自动解锁。
- spec 增量已合并进主 spec「自动解锁与反馈」。
