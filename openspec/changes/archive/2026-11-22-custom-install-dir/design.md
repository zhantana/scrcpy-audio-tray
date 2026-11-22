## 实现

installer.iss 的 [Setup] 段在 `DefaultDirName` 后加一行：

```
DisableDirPage=no
```

Inno Setup 在 `PrivilegesRequired=lowest` 且默认目录位于用户目录时，会自动跳过目录选择页；显式设为 `no` 强制显示。

路径依赖核查：主脚本 settings/日志/scrcpy 均基于 `$PSScriptRoot`，启动 VBS 基于 `ScriptFullName`，stop-tray 基于 `$PSScriptRoot`，卸载脚本 `{app}` —— 全部跟随安装目录，自定义目录无副作用。

提示用户不要选 `%ProgramFiles%` 等需管理员写入的目录（当前 lowest 权限）。
