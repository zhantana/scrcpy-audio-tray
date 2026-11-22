# 任务清单：修复唤醒解锁失效却提示成功

- [x] 1. App 增加 `_skipUnlockOnce` 一次性标志字段。
- [x] 2. 抽出 `StartScreen`/`StopScreen`；`StartScreen` 启动投屏前调 `_unlock.Invoke(serial)` 做启动前自动解锁，`_skipUnlockOnce` 为真时跳过并复位。
- [x] 3. `DoWake` 改为三段式：投屏运行中先 `StopScreen()` → `forceWake=true` 解锁 → 置 `_skipUnlockOnce=true` 并 `StartScreen()` 重启。
- [x] 4. `DoWake` 结果提示按 `Result.Ok` / `NEED_PIN` 分类，失败如实提示，不再假报成功。
- [x] 5. 编译 `dotnet build` 0 错误 0 警告、自检通过；已 publish 并覆盖到本地发布目录（旧版已备份）。
- [ ] 5a. 【实测修正】`AdbClient.GetDeviceState` 合并 `dumpsys window policy` + `dumpsys window`（实测 policy 里无 isKeyguardShowing，恒判未锁是假成功的直接原因）。
- [ ] 5b. 【实测补充】新增 `PinPromptWindow`，`NEED_PIN` 时弹窗录入 PIN 并保存后重试；`StartScreen`/`DoWake` 统一走 `UnlockWithPinPrompt`。
- [ ] 5c. 启动前解锁失败（取消录入）时托盘如实提示，不阻塞投屏。
- [x] 6. ~~真机验证~~ → 重新验证（待用户）：投屏中点唤醒→屏幕真的点亮解锁、画面恢复、提示真实；投屏启动前自动解锁。确认后归档到 `openspec/changes/archive/`，更新主 spec。
