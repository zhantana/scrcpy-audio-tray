# 设计：修复唤醒解锁失效却提示成功

## 上下文

v1 已验证的流程（`scrcpy-audio-tray.ps1` 的唤醒菜单回调）：

```powershell
# 投屏关屏时屏幕归 scrcpy 管，adb 点不亮，先停掉让它把屏幕还回来
$restart = $false
if ((Is-Running) -and $settings.Screen) {
    Stop-Audio
    $restart = $true
    Start-Sleep -Milliseconds 400
}
$r = Invoke-Unlock $serial -AllowPrompt -ForceWake:([bool]$restart)
if ($restart) { Start-Audio -SkipUnlock }
```

v2 现状：

- `App.DoWake()` → `_unlock.Invoke(serial)`，停投屏与重启全无，`forceWake` 也没传。
- `App.ToggleScreen()` → 直接 `_screen.Start()`，启动前解锁全无。
- `UnlockService.Invoke`：`if (!locked && !forceWake) return new Result(true, "已解锁");` —— 投屏时 `dumpsys` 假报 `interactiveState=AWAKE`，`locked` 读到 false，`forceWake=false`，直接返回成功，是"提示成功却没作用"的直接原因。

## 关键决策

### 决策 0（2026-11-22 实测修正）：锁屏状态必须两条命令合并读取

真机实测：`dumpsys window policy` 输出只含 `screenState`/`interactiveState`，`isKeyguardShowing` 出现 0 次；该字段在 `dumpsys window` 输出中（出现 3 次）。因此：

- `AdbClient.GetDeviceState` 合并两条命令的输出（与既有 `GetScreenState` 的做法、v1 的合并查询一致）。
- 不改 `UnlockService` 的字符串判断——输入对了判断自然对，避免再引入新逻辑。
- `WaitUnlocked` 轮询间隔维持现状；合并查询约数百 ms，与 v1 的 350ms 同量级。

### 决策 5：PIN 录入放 UI 层，服务层只认 NEED_PIN

- 新增 `PinPromptWindow`（PasswordBox + 保存并解锁/取消）。
- App 抽 `UnlockWithPinPrompt(serial, forceWake)`：`Invoke` 返回 `NEED_PIN` → 弹窗 → `SavePin` → 重试一次 `Invoke`；取消则原样返回 `NEED_PIN`。
- `StartScreen` 与 `DoWake` 都走该方法，行为一致。
- 理由：`UnlockService` 保持无 UI 依赖，弹窗由编排层做，与热键录制窗口同模式。

### 决策 6：启动前解锁失败不阻塞投屏

- `StartScreen` 中解锁未成功（含用户取消录入）时，托盘提示「没能自动解锁」，投屏照常启动（会停在锁屏画面），与 v1 行为一致。

### 决策 1：把「屏幕控制权」当作唤醒的前置条件

投屏运行时 `--turn-screen-off` 让 scrcpy 持有显示电源，adb 注入的 WAKEUP 会被立刻关回。`Invoke` 必须在无 scrcpy 占屏时调用。因此 `DoWake` 严格复刻 v1 的「停 → 解 → 起」三段式，不试图在投屏中直接唤醒。

### 决策 2：用 App 层一次性标志跳过重复解锁，不往 ScrcpyOptions 塞无关字段

- 新增字段 `_skipUnlockOnce`（bool，默认 false）。
- `ToggleScreen` 里，启动投屏前：`if (!_skipUnlockOnce) _unlock.Invoke(serial)`；用过即复位为 false。
- `DoWake` 重启投屏前置 `_skipUnlockOnce = true`，令重启不再解一次。
- 理由：解锁是编排层（App）的职责，`ScreenService`/`ScrcpyProcess` 只管进程参数，保持分层干净。

### 决策 3：状态判断以真实设备状态为准，forceWake 时跳过「已解锁」短路

- `DoWake` 传 `forceWake: true`（投屏中停掉后强制走点亮流程）。
- `UnlockService.Invoke` 的 `!locked && !forceWake → 已解锁` 短路在 forceWake 下自然不生效，无需改动该方法逻辑。
- 不强信 `interactiveState`：投屏时它假报 AWAKE；停掉 scrcpy 后状态恢复真实，`GetDeviceState` 读到的 `isKeyguardShowing` 才作为解锁判据。

### 决策 4：提示如实反映结果

- `DoWake` 按 `r.Ok` 与 `r.Message` 分类提示：`NEED_PIN` → 提示需要密码；`Ok` → 显示 r.Message；失败 → 显示失败原因。
- 不再无条件以「唤醒解锁 / 成功」措辞呈现。

## 风险与降级

- 停投屏→重启会让画面闪断一两秒，与 v1 一致，可接受；音频同进程会短暂中断。
- 停投屏后到解锁完成期间，若手机真睡眠（非仅显示电源关闭），`WaitUnlocked` 可能超时返回失败——如实提示，不谎报成功。
- PIN 未保存时不阻塞投屏重启，仅提示，与 v1 / 现有 spec 一致。
- 无 PIN 的滑动锁：`dismiss-keyguard` 成功后 `ShowKeyguard` 返回 true，正常。

## 未验证项

- 真机目视/交互：投屏中点唤醒后屏幕是否真的点亮解锁、画面是否恢复、提示是否真实——需用户实测。
- forceWake 下 `GetDeviceState` 在各 ROM 的真实返回值未实测。
