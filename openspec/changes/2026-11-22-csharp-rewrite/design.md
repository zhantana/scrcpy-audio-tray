# 技术设计

## 技术选型

| 项 | 选 | 理由 |
| --- | --- | --- |
| 语言 / 框架 | C# .NET 8 + WPF | Windows 原生托盘、热键、多窗口、DPI、数据绑定成熟稳定 |
| 投屏引擎 | scrcpy 5.0.1 + adb（外部进程） | 不改源码，沿用已验证的采集能力 |
| 进程启动 | `ProcessStartInfo` + `CreateNoWindow` | 不弹 cmd 窗；需要提权时用 `runas` |
| 配置 | `settings.json`（System.Text.Json） | 兼容 v1 字段 |
| 打包 | Inno Setup | 沿用现有安装脚本与自定义目录功能 |

不用 WinForms，因为 WPF 的布局、样式、多窗口、高 DPI 和多显示器处理更好；界面要长期演进，WPF 更合适。

## 分层架构

```
┌─────────────────────────────────────────────┐
│  UI 层 (WPF)                                │
│  TrayIcon / MainWindow / SettingsWindow     │
│  Screenshots / DeviceManager / About        │
└──────────────────┬──────────────────────────┘
                   │  MVVM + Command
┌──────────────────▼──────────────────────────┐
│  应用层 (Services)                          │
│  ScreenService  AudioService  DeviceService │
│  HotkeyService  UnlockService  ConfigService│
│  SleepMonitor  OrientationService           │
└──────────────────┬──────────────────────────┘
                   │
┌──────────────────▼──────────────────────────┐
│  基础设施层                                 │
│  ScrcpyProcess  AdbClient  NativeMethods    │
│  JsonStore  Logger                          │
└─────────────────────────────────────────────┘
```

## 模块职责

- **ScrcpyProcess**：启动 / 停止 scrcpy，拼参数，管 stdout/stderr，进程崩溃检测与重启。所有参数集中在一处构造，杜绝散落。
- **AdbClient**：封装设备发现（`devices -l`）、连接 / 断开、mdns、无线配对、屏幕尺寸 / 方向、媒体会话查询、`wm dismiss-keyguard`、键事件。所有 adb 调用带序列号，支持多设备。
- **DeviceService**：设备列表、别名、连接方式判定（USB / WiFi）、记忆地址、无效连接清理。
- **ScreenService**：投屏开关、窗口模式（原生 / 嵌入）、置顶、全屏、帧率 / 码率 / 编码格式设置；窗口尺寸记忆。
- **AudioService**：音频启停、编码 / 缓冲、媒体暂停键。
- **HotkeyService**：全局热键注册（暂停 / 投屏开关），录制窗口，冲突检测，与游戏管理员权限兼容（进程提权）。
- **UnlockService**：通用解锁三级降级（dismiss-keyguard → keyevent → 上滑 → PIN），息屏检测与唤醒。
- **ConfigService / JsonStore**：settings.json 读写 + v1 字段迁移。
- **Logger**：写 `tray-error.log`，替代现在散落的 `Out-File -Append`。

## 数据兼容

v1 字段原样保留并读进来：`Buffer, Codec, Serial, Screen, OnTop, MaxFps, WifiTargets, HotkeyPause, HotkeyScreen, SizePortrait, SizeLandscape, WindowMode`。新增字段给默认值。读到 `None` 这类历史垃圾值按现有逻辑回落到默认。

设备别名 `device-names.json`、解锁密码 `device-pin.json` 沿用，路径改到安装目录，格式不变。

## 窗口模式

- **原生（默认）**：scrcpy 自己开窗，最稳，无菜单栏；所有操作走托盘。
- **嵌入（可选）**：主窗口带单行顶栏（标题 / 菜单 / 最小化 / 关闭），用 `SetParent` 把 scrcpy 窗口嵌进内容区。失败自动降级到原生并提示一次。

两种模式共用同一套 Service，只换 UI 呈现。

## 迁移策略

1. v2 与 v1 并存不同目录，安装包加任务让用户选装哪个（或先只装 v2，v1 保留源码与产物可回退）
2. 每完成一个模块就对照 v1 行为验证
3. 全部模块就绪、实测稳定后，再把默认安装切到 v2

## 测试

- 每模块配最小可运行验证（如 AdbClient 对真机读设备、ScrcpyProcess 起停一次）
- 关键回归：音频不丢样本、多投屏关一路另一路有声、热键在游戏里生效、横竖屏尺寸记忆、覆盖安装与卸载干净
