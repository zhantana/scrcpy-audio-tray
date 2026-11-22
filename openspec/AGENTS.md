# AGENTS.md

给后续接手的人（或 AI）看的操作规则。

## 目录规则

- `openspec/specs/` 是**现状**：已经实现并验证过的能力。
- `openspec/changes/` 是**待做**：正在进行的变更。
- `openspec/changes/archive/` 是**历史**：已完成的变更，带日期前缀，只读。

## 改代码前

1. 读 `openspec/project.md` 的全局约束，尤其编码与行尾规则。
2. 读 `openspec/specs/phone-audio-bridge/spec.md` 确认现有行为。
3. 有变更先建 `openspec/changes/<动词开头-kebab-case>/`，写 proposal，再动手。

## 硬性规则

- **不改 scrcpy 源码**，只用官方发布包。
- `.bat` 写完必须转 **GBK + CRLF**，否则中文会截断。
- `.ps1` 写完必须转 **UTF-8 BOM + CRLF**。
- 写 `.bat` 时避免：`for /d` 多级通配、`title` 里出现 `->`、UTF-8 下长中文行。
- 涉及 adb 多设备时必须用 `-s <serial>`，不要用 `-e`。
- 手机端不得安装常驻 App。

## 验证要求

- PowerShell 改动后跑语法检查：`[Parser]::ParseInput`。
- 涉及设备逻辑时，用真实 `adb devices -l` 输出验证解析，别只靠推理。
- 无法实测的部分必须在 `summary.md` 里明确标注为「未验证」，不要写成已完成。
