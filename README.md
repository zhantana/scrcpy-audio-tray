# 手机音频托盘 (ScrcpyAudioTray)

把安卓手机正在播放的声音经 USB / Wi-Fi 实时转发到电脑（scrcpy 音频通道），附带屏幕投屏、托盘控制、唤醒解锁等能力。Windows 桌面工具，无需在手机上安装常驻 App。

## 版本

| 版本 | 技术栈 | 状态 |
|---|---|---|
| **v2**（当前开发） | C# / .NET 8 + WPF + WinForms 托盘 | `src/PhoneAudioTray/` |
| v1（遗留） | PowerShell 5.1 + WinForms | `legacy/v1/`，仅维护，不再加功能 |

依赖：[scrcpy 5.x](https://github.com/Genymobile/scrcpy)（含 adb），`winget install Genymobile.scrcpy`。只使用官方发布包，不改 scrcpy 源码。

## 目录结构

```
├── src/PhoneAudioTray/     v2 主程序（C#）
│   ├── App.xaml.cs         应用宿主：托盘、热键、投屏/解锁编排
│   ├── Services/           业务服务（投屏、嵌入、解锁、热键、托盘、配置…）
│   ├── Infrastructure/     底层封装（adb、scrcpy 进程、Win32 API、日志）
│   ├── Models/             数据模型
│   ├── UI/Views/           WPF 窗口
│   └── Resources/          托盘图标
├── legacy/v1/              v1 全部遗留（脚本、启动器、图标、安装脚本）
├── build/                  构建辅助：图标生成、README 生成、Inno Setup 脚本
├── openspec/               规格驱动开发（现状 specs / 待做 changes / 归档 archive）
├── README-V2.txt           v2 用户说明（随安装包分发，GBK 编码）
├── installer-v2.iss → build/installer-v2.iss
└── CLAUDE.md               AI 协作规范
```

## 开发（v2）

```bash
# 编译（要求 .NET 8 SDK，Windows）
dotnet build src/PhoneAudioTray

# 自检（验证 adb / scrcpy / 设备在线）
dotnet run --project src/PhoneAudioTray -- --selftest

# 发布单文件 exe
dotnet publish src/PhoneAudioTray -c Release

# 生成安装包（Inno Setup 6，先 publish 到 dist-v2/）
iscc build/installer-v2.iss
```

v2 运行时读写自身目录下的 `settings.json` / `device-pin.json`（发布目录），与仓库根目录无耦合。

## 开发（v1 遗留）

入口 `legacy/v1/启动-手机音频托盘.vbs`；各 `.bat` 为一次性任务脚本。图标由 `python build/build_icons.py` 生成（输出到 `legacy/v1/`）。

## 本地文件（不入库）

以下文件含个人配置或密码，已被 `.gitignore` 排除，**不得提交**：

- `device-pin.json` — 锁屏 PIN（明文）
- `settings.json` / `device-names.json` / `wifi_target.txt` — 个人偏好与设备信息
- `dist/`、`dist-v2/` — 构建产物
- `scrcpy/` — scrcpy 二进制

## 规格与变更流程

项目使用 [OpenSpec](https://github.com/openspec-dev/openspec) 风格的规格驱动流程，规则见 `openspec/AGENTS.md`：

1. 改动行为前先在 `openspec/changes/<kebab-case>/` 写 proposal / design / tasks。
2. 完成并实测后归档到 `openspec/changes/archive/`，合并 delta 进 `openspec/specs/`。
3. 未实测的部分在 summary 里如实标注「未验证」。
