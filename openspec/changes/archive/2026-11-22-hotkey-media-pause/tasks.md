# 任务

- [x] 设置新增 `HotkeyPause`，默认 `Ctrl+Alt+P`，空字符串表示禁用
- [x] 新增快捷键解析与格式化（modifiers + vk，数字键映射到 D0–D9）
- [x] 引入 `NativeWindow` 子类接收 `WM_HOTKEY`
- [x] 注册、注销、冲突提示
- [x] 新增 `Send-MediaPause`：先查 `dumpsys media_session`，有会话才发 `keyevent 85`
- [x] 菜单新增「暂停 / 继续（手机）」，右侧显示当前键
- [x] 「快捷键」子菜单：修改… / 禁用 / 恢复默认
- [x] 录制窗口：按下即保存，Esc 取消，单键提示
- [x] 退出时注销热键
- [x] 主 spec 增补快捷键场景
- [x] 打包 1.2.0 并部署
- [x] 归档变更
