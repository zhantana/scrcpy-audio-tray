# 设计：全局热键与媒体键注入

## 热键怎么接

用 Win32 `RegisterHotKey`，不需要键盘钩子，不碰其他程序的按键。要点：

- 托盘本身没有窗口（只有 NotifyIcon + ApplicationContext），所以建一个 `NativeWindow` 专门收消息
- `WM_HOTKEY`（0x0312）到达时触发事件
- 程序退出时 `UnregisterHotKey`，否则热键会残留到进程结束才释放

C# 侧只需要一个继承 `NativeWindow` 的小类，暴露 `Register` / `Unregister` 和一个 `HotkeyPressed` 事件，由 PowerShell 订阅。

## 键值表示

存字符串，形如 `Ctrl+Alt+P`、`F8`、`MediaPlayPause`。解析成：

- modifiers：Alt=1、Ctrl=2、Shift=4、Win=8（Win32 的 `MOD_*`）
- vk：`System.Windows.Forms.Keys` 的枚举值

没有修饰键时 modifiers=0，照样能注册，满足「不强制组合键」。

数字键的枚举名是 `D1`…`D9`，录制时按 `1` 要映射成 `D1`，否则解析失败。

## 录制窗口

一个 200×120 的小 Form，`KeyPreview = true`：

- `KeyDown` 里忽略单独的 Control / Alt / Shift，等到真正的键再确定
- 按下即保存并关闭，Esc 取消
- 不做「必须带修饰键」的校验，但底部写一行提示：单键会全局占用，推荐 F 键

## 发键前先探一下

`adb shell dumpsys media_session`，输出里出现 `package=` 就说明手机上有媒体会话，再发 `input keyevent 85`。
没有会话时不发，弹一行「手机上没在播放」，避免个别机型把自带音乐 App 拉起来。

成功不弹通知——画面和声音当场就变了，属于噪音。

## 状态

电脑端不记录「当前是暂停还是播放」。媒体状态以手机为准，`dumpsys` 的 state 各版本格式不一，猜错比不猜更烦。

## 失败处理

`RegisterHotKey` 返回 false 通常是组合键被别的软件占了。此时提示一次，菜单里的快捷键显示置灰，其他功能不受影响。
