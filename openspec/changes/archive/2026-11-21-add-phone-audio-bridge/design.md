# 技术设计

## 为什么是 scrcpy

安卓要录「系统正在播放的声音」，App 必须实现 `AudioPlaybackCapture`（Android 10+ 的录屏授权）。绝大多数音频传输软件只采麦克风，方向不对。

scrcpy 从 1.22 起支持 `--audio-source=output`，是少数能直接抓内部音频且免费无广告的方案。

| 方案 | 抓内部音频 | 结论 |
|---|---|---|
| scrcpy | ✅ | 采用 |
| AudioRelay | ✅ | 可用，需手机装 App，作为备选 |
| VBAN Talkie | ❌ 仅麦克风 | 排除 |
| SonoBus | ❌ 音乐协作，麦克风输入 | 排除 |
| SoundWire | ⚠️ 延迟 300ms + 广告 | 排除 |
| 系统蓝牙 | ❌ Windows 无 A2DP Sink | 排除 |

## 连接链路

```
手机 ──Wi-Fi──> adb over TCP ──> scrcpy-server（临时推入/运行）──> PCM 流 ──> 电脑默认输出
```

配对一次后，密钥存在手机上，后续靠 mDNS（`_adb-tls-connect._tcp`）自动发现。

## 延迟构成与调参

| 环节 | 可调参数 | 取值 | 备注 |
|---|---|---|---|
| 手机端采集 | `--audio-buffer` | 40 | 太小会 underrun 爆音 |
| 传输编码 | `--audio-codec` | raw / opus | 无线不稳时换 opus + 192kbps |
| 电脑端播放 | `--audio-output-buffer` | 10 | **默认已是 10，不要调大** |

实测：有线 40–60ms，无线 60–100ms。

## 已知坑与对策

1. **多设备歧义**：同一台手机在 adb 里有 `IP:端口` 和 `adb-*._adb-tls-connect._tcp` 两条记录，`scrcpy -e` 无法选择。对策：解析 `adb devices -l`，按 model 去重，用 `-s` 精确指定。
2. **批处理编码**：`.bat` 用 UTF-8 + `chcp 65001` 会让长中文行被截断。对策：改 GBK + CRLF。
3. **代码页被改写**：scrcpy 启动时把控制台代码页改成 UTF-8，导致后续中文乱码。对策：`chcp 936` 复原。
4. **`for /d` 不接受多级通配**：路径回退逻辑失效。对策：改为逐层 `Get-ChildItem` 等价写法。
5. **`title` 里的 `->`**：被 cmd 当重定向符，生成垃圾文件。对策：转义为 `-^>`。
6. **vivo 断连**：关闭配对弹窗会断连。对策：文档中明确要求保持打开。

## 托盘程序架构

单进程 PowerShell + WinForms `ApplicationContext`，无窗口：

- `NotifyIcon` + `ContextMenuStrip` 承载全部交互
- `Timer` 每 2 秒刷新状态（只读进程句柄，不调 adb，避免开销）
- 设备列表懒加载，仅在「选择设备」子菜单展开时查询
- 单实例通过 `Global\ScrcpyAudioTrayUI` 互斥量保证

## 别名存储

`device-names.json`，key 优先用 **model**（IP 会变），查不到再退回 serial。读取时先 `TrimStart(U+FEFF)` 去掉 BOM，否则 `ConvertFrom-Json` 失败。
