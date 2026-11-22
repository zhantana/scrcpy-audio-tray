# 完成情况

## 改了什么

- 全局快捷键，默认 `Ctrl+Alt+P`，按下向手机发 `input keyevent 85`（媒体播放/暂停）
- 菜单新增「暂停 / 继续（手机）」，右侧显示当前键
- 新增「快捷键」子菜单：修改…（显示当前键）/ 禁用 / 恢复默认
- 录制窗口按下即保存，Esc 取消；允许单键，底部提示单键会全局占用
- 快捷键存 `settings.json` 的 `HotkeyPause`，空字符串表示禁用

## 实现要点

- 托盘无窗口，用 `NativeWindow` 子类建句柄收 `WM_HOTKEY`（C# 部分用 `Add-Type` 编译）
- `Add-Type` 整段包在 try/catch 里，编译失败只失去快捷键功能，不影响托盘启动
- 发键前查 `dumpsys media_session`，没有 `package=` 就提示「手机上没在播放」，不盲发
- 打开录制窗口前先注销旧热键，否则按到当前键会被系统吞掉，录不进去
- 退出与异常分支都会注销热键

## 语义边界

- 这是手机端暂停：视频、音乐 App 真的停住，电脑同时无声
- 游戏不响应媒体键，按了会走到「没有可暂停的媒体」的提示
- 不改电脑端音量，不做画面定格

## 验证

- `PSParser::Tokenize` 语法检查通过
- 安装包 1.2.0 编译成功；脚本与 README 已部署到 `%LOCALAPPDATA%\ScrcpyAudioTray`，与源文件 MD5 一致
- **未做**：运行时验证。本机安全策略禁止执行 PowerShell 脚本，也禁止 `Add-Type` 编译，因此热键注册与录制窗口的实际表现没能自测

## 风险

- C# 编译在目标机若失败（缺 WinForms 引用），会静默失去快捷键功能，`hkAvailable=false`，其余功能正常
- 单键热键会全局抢占该键，例如把 `A` 设成暂停键后，任何程序里打 A 都会被吞掉。录制窗口有提示，但不阻止

## 修复：录制按键被存成 "None"（1.2.1）

现象：录制窗口里按任何键，保存后都变成 `None`。

原因：`Add_KeyDown` 的事件脚本块里直接写了 `$e`。PowerShell 不会自动给这个变量，读到的是 `$null`；`$null.KeyCode` 取不到值，`[int]$k` 变成 0，而 `Keys` 枚举的 0 就是 `None`。

修复：

- 事件块改为 `param($sender, $keyArgs)`，显式接住 `KeyEventArgs`
- 关闭窗口改用 `$sender.Close()`，不再依赖 `$this`
- `KeyCode` 为 `Keys.None` 时不保存、不关窗，提示「这个键不行，换一个」
- `Set-PauseHotkey` 增加校验，解析不出来的值拒绝写盘
- 菜单右侧在未设置时改用 `$null`，避免 WinForms 退回去显示 `None`
- 启动时读到 `None` 视为垃圾值，回落到 `Ctrl+Alt+P`；已存在的 `settings.json` 已就地修正

## 修复：游戏里按快捷键无效（1.4.1）

现象：电脑玩游戏时按暂停键（Pause、Scroll 都试过）无反应，托盘没有提示。换键无效。

原因：游戏以管理员权限运行，托盘是普通权限。Windows 不允许低权限进程接收高权限窗口的键盘输入，热键在游戏里被系统拦下，根本没送到托盘。换什么键都一样。

排查：直接用 adb 发 `keyevent 85` 给手机上 B 站的媒体会话，播放状态从 PAUSED 变 PLAYING（position 3532→6433），确认发键链路正常，问题只在「游戏里按键 → 托盘」这一段。

修复：启动脚本从 `WScript.Shell.Run` 改为 `Shell.Application.ShellExecute` + `runas`，托盘以管理员权限运行。代价是每次启动弹一次 UAC。

验证：用户实测通过。

注意：托盘提权后，覆盖安装或卸载前需先右键退出托盘，否则普通权限的 stop-tray 停不掉它。
