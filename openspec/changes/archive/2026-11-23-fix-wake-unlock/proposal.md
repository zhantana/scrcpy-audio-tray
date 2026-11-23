# 变更：修复「唤醒并解锁」失效却提示成功

## Why

真机实测（2026-11-22，USB 连接）确认三层根因，按影响排序：

1. **锁屏状态读取命令错误（主因，实测坐实）**：`dumpsys window policy` 输出中 `isKeyguardShowing` 出现 **0 次**，该字段实际在 `dumpsys window` 里（出现 3 次）。v2 的 `GetDeviceState` 只跑前者 → `locked` 恒 false → `Invoke` 开头 `!locked && !forceWake` 直接返回「已解锁」→ 假成功。v1 是两条命令合并查询，v2 重构时丢了一条。停投屏、forceWake 都绕不开 `ShowKeyguard → WaitUnlocked` 内同样的错误判断，故此前两轮修复无效。
2. **PIN 录入功能缺失（实测：无 device-pin.json）**：`SavePin` 无任何调用者，设置窗口无录入入口。即使状态读对，也只会返回 `NEED_PIN`，而旧提示「请在托盘设置里填写」指向不存在的功能，死胡同。
3. **投屏占屏未处理**：v1 唤醒先停 scrcpy 拿回屏幕控制权，v2 丢了（已在前一轮补回，保留）。

## What Changes

- `AdbClient.GetDeviceState` 合并 `dumpsys window policy` + `dumpsys window`（对齐 v1），锁屏状态才能真正读到。
- 新增 PIN 录入窗口：解锁遇 `NEED_PIN` 时弹一次输入框，保存后自动继续解锁（对齐 v1「首次遇到锁屏密码询问保存」）。
- 启动前解锁失败（用户取消录入）时如实提示，不假报成功。
- 保留前一轮已实现的「停投屏 → 强唤醒解锁 → 重启投屏」三段式。

## Capabilities

### New Capabilities

无（修复既有能力）。

### Modified Capabilities

- `phone-audio-bridge` 的「自动解锁与反馈」：锁屏判定恢复真实；首次遇密码询问并保存；提示如实反映成败。

## Impact

- 影响文件：`AdbClient.cs`、`App.xaml.cs`、新增 `UI/Views/PinPromptWindow.xaml(.cs)`。
- `GetDeviceState` 输出变大（合并两条命令），`WaitUnlocked` 轮询节奏与 v1 的 350ms 合并查询一致，可接受。
