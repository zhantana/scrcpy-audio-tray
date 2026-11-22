# 项目：手机音频无线桥接（Android → Windows）

把安卓手机正在播放的声音，经 Wi-Fi 实时送到电脑，用电脑耳机收听。附带托盘控制面板。

## 技术栈

| 层 | 选型 | 说明 |
|---|---|---|
| 音频引擎 | scrcpy 5.0.1（Genymobile 官方） | `winget install Genymobile.scrcpy` 安装，源码未改动 |
| 连接 | adb（随 scrcpy 附带） | 无线调试配对 + mDNS 自动发现 |
| 控制面板（v2，当前） | C# / .NET 8 + WPF | 托盘 + 窗口，代码在 `src/PhoneAudioTray/` |
| 控制面板（v1，遗留） | PowerShell 5.1 + WinForms | `legacy/v1/`，只维护不新增 |
| 一键脚本 | 批处理 `.bat` | GBK 编码、CRLF 行尾 |
| 启动器 | VBScript | 隐藏黑窗口 |

## 文件约定

```
src/PhoneAudioTray/          v2 主程序（C# / .NET 8）
legacy/v1/                   v1 全部遗留：启动器、主程序、图标、安装脚本
legacy/v1/启动-手机音频托盘.vbs     v1 托盘启动器（日常入口）
legacy/v1/scrcpy-audio-tray.ps1    v1 托盘主程序
legacy/v1/icons/scrcpy.png   投屏窗口图标（SCRCPY_ICON_DIR 读取，文件名固定 scrcpy.png）
build/                       构建辅助：图标生成、README 生成、Inno Setup 脚本
device-names.json            设备别名（运行时生成，v1 在 legacy/v1/ 下、v2 在 exe 目录）
device-pin.json              解锁 PIN（运行时生成，明文存储，不入库）
settings.json                缓冲/编码/投屏开关/窗口置顶/帧率（运行时生成）
device-timeout.json          各设备原始息屏时间，用于还原（运行时生成）
wifi_target.txt              上次连接地址（运行时生成）
```

## 图标规范

- 图形：圆形底 + 左侧圆点 + 右侧声波弧，停止态灰色、运行态青蓝渐变。
- 16–48px 使用 BMP 条目，64px 以上使用 PNG 条目（兼容旧版 .NET 的 `Icon` 加载）。
- 16/20px 只画两道弧，否则小尺寸会糊成一团。
- 改动图标后执行 `python build/build_icons.py` 重新生成（输出到 `legacy/v1/`）。

## 全局约束

- 只用官方 scrcpy，不自建编译版本。
- `.bat` 必须 **GBK 编码 + CRLF**。UTF-8 + `chcp 65001` 会让长中文行被截断。
- `.ps1` 必须 **UTF-8 BOM + CRLF**，否则中文乱码。
- 不写注册表、不设开机自启、不改系统默认音频设备。
- 手机端不安装常驻 App（scrcpy 的 server 用完即删）。

## 关键参数

| 参数 | 有线 | 无线 | 作用 |
|---|---|---|---|
| `--audio-codec` | `raw` | `raw`（信号差改 `opus`） | PCM 无损，省编解码排队 |
| `--audio-buffer` | 25 | 40 | 手机端采集缓冲 |
| `--audio-output-buffer` | 10 | 10 | 电脑端播放缓冲，延迟主来源 |
| `--audio-source=output` | 必填 | 必填 | 抓系统输出而非麦克风 |

## 已知平台差异

- Android 11 以下没有 `AudioPlaybackCapture`，抓不到内部音频。
- vivo / 部分国产 ROM：关掉「无线调试」的配对弹窗会断连，必须保持打开。
- 同一台手机在 adb 里可能同时存在 IP 形式和 `_adb-tls-connect._tcp` 形式两条记录，必须去重，否则 `scrcpy` 报多设备歧义。
