# CLAUDE.md — AI 协作规范

## 项目一句话

Windows 桌面工具：通过 scrcpy 音频通道把手机声音转发到电脑，附带投屏、托盘控制、唤醒解锁。v2 为 C#/.NET 8（`src/PhoneAudioTray/`），v1 为 PowerShell 遗留（`legacy/v1/`，只维护不新增）。

## 常用命令

```bash
dotnet build src/PhoneAudioTray                      # 编译（0 警告是基线）
dotnet run --project src/PhoneAudioTray -- --selftest  # 自检：adb/scrcpy/设备在线
dotnet publish src/PhoneAudioTray -c Release         # 发布单文件 exe
iscc build/installer-v2.iss                          # 打安装包（Inno Setup 6）
python build/build_icons.py                          # 生成托盘图标 → legacy/v1/
```

## 架构速览

- `App.xaml.cs` — 唯一编排层：托盘菜单、热键、投屏启动/停止、唤醒解锁流程、休眠监听。窗口尺寸/嵌入偏移等跨服务逻辑也在这里。
- `Services/` — 业务服务，各管一摊：`ScreenService`（scrcpy 启动与视频参数）、`EmbedService`（子窗口嵌入）、`UnlockService`（唤醒解锁）、`SleepMonitor`、`HotkeyService`、`TrayIconService`、`ConfigService`。
- `Infrastructure/` — 底层封装：`AdbClient`（adb shell）、`ScrcpyProcess`、`NativeMethods`（Win32）、`JsonStore`、`Logger`。
- 运行时配置读写 `AppContext.BaseDirectory`（exe 所在目录），不是仓库目录。

## 硬性规则

1. **敏感文件不得提交**：`device-pin.json`（明文 PIN）、`settings.json`、`device-names.json`、`wifi_target.txt`、`dist*/`、`scrcpy/`。文档里也不得出现真实 PIN、设备序列号、局域网 IP、本机用户路径。
2. **编码与行尾**（历史坑，见 `openspec/AGENTS.md`）：
   - `.bat` 必须 **GBK + CRLF**；`.ps1` 必须 **UTF-8 BOM + CRLF**；`.iss` / 源码 UTF-8。
   - `.bat` 避免 `for /d` 多级通配、`title` 里出现 `->`。
3. **不改 scrcpy 源码**，只用官方发布包；涉及 adb 多设备时必须 `-s <serial>`。
4. **openspec 工作流**：行为变更先建 `openspec/changes/<动词开头-kebab-case>/`（proposal → design → tasks），实测通过后归档并合并 spec delta。无法实测的部分在 summary 里标注「未验证」，不写「已完成」。
5. 手机端不得安装常驻 App。

## 提交约定

- Conventional Commits + 中文描述：`fix(v2): ...`、`feat: ...`、`chore: ...`。
- 一个逻辑变更一个 commit；涉及 openspec 文档时与对应代码放同一个 commit。
- Windows 下网络若直连 GitHub 失败，走本地代理 `127.0.0.1:7890`（已在 git 全局配置）。
