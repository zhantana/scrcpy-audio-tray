# Summary: 2026-11-22-csharp-rewrite

## 交付内容

v1（PowerShell，`legacy/v1/`）整体重写为 C#/.NET 8 WPF 托盘程序（`src/PhoneAudioTray/`）：

- 分层架构：`App.xaml.cs` 编排层 + `Services/`（Screen/Embed/Unlock/Device/Config）+ `Infrastructure/`（AdbClient/ScrcpyProcess/NativeMethods/JsonStore/Logger/HotkeyWindow）
- 单进程承载音频与画面（拆分会导致音频丢样本）；scrcpy 官方发布包调用，不改源码
- 13 项 v1 需求全部对照实现：无线音频转发、设备发现/切换/添加/配对/别名/清理、连接方式分组、屏幕镜像（嵌入/原生双模式、关屏、置顶、帧率/码率/编码）、单行顶栏、自动解锁、启动行为、音质调节、双快捷键、方向跟随、方向尺寸记忆、窗口模式
- v1 `settings.json` 兼容读入；安装包（Inno Setup，`build/installer-v2.iss`）与 v1 独立 AppId 并存

## 验证

- `dotnet build` 0 警告；`--selftest` 通过；自定义目录安装验证通过，与 v1 并存。
- 功能对照与回归的真机实测由后续 `restore-v1-feature-parity` / `harden-v2-runtime` 变更覆盖并经用户真机确认（2026-11-23）。
- 遗留修正（唤醒解锁假成功、嵌入顶栏被盖、方向轮询缺失）由 `fix-wake-unlock` / `fix-embedded-window-layout` / parity 变更承接，均已归档。
- 默认安装包已切换为 v2（`build/` 仅保留 installer-v2.iss）。
