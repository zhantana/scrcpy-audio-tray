# 实现摘要

## 交付物

| 文件 | 改动 |
|---|---|
| `scrcpy-audio-tray.ps1` | 解锁重构、常亮、息屏覆盖层、音频唤醒、鼠标绑定、窗口置顶 |
| `settings.json` | 新增 `OnTop` 字段，默认 `false` |
| `device-timeout.json` | 记录各设备原始息屏时间，用于还原 |
| `openspec/specs/phone-audio-bridge/spec.md` | 补齐 12 个场景 |

## 实测数据

设备：vivo 测试机 / Android 16（1440×3168）

| 项目 | 结果 |
|---|---|
| 解锁全流程 | 点亮 → 系统命令 → 回车 → 上滑 → 输 6 位 → 回车，成功 |
| 解锁耗时 | 3.3s（优化前 8.1s） |
| 息屏时间拉满 | 75 秒仍 `INTERACTIVE_STATE_AWAKE`，有效 |
| 投屏中真关屏 | 设备入睡、scrcpy 退出，投屏中断（故不采用） |
| `--mouse-bind=bhb-:++++` | 参数被接受，无报错 |
| `--always-on-top` | 参数被接受，无报错 |

## 过程中确认的事实

1. `--turn-screen-off` 期间 `interactiveState` 仍报 AWAKE，且 `screencap` 仍能截到完整画面——截的是合成层，不能据此判断关屏失败
2. 关屏只关显示，触摸仍生效，黑屏上仍会误触；不 root 无法单独屏蔽触摸
3. 屏幕睡着时注入按键完全无效，必须先点亮
4. `svc power stayon` 只在充电时生效，无线调试无效，还会覆盖用户自己的充电保持唤醒设置，故不使用

## 遗留

- 图案锁、指纹、人脸、含符号密码无法自动解锁
- 托盘 UI 只能在用户本机验证，沙箱内无法拉起
- 深度定制 ROM 若拦截 ENTER 拉 bouncer，仍可能解不开，此时依赖「不自动息屏」绕过
