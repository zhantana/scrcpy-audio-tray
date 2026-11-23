# 本次变更差异：v2 运行时加固

本变更绝大多数内容是实现质量整改，不改变对外行为语义，不产生 spec delta。唯一新增的用户可见行为：

## ADDED

### Requirement: 启动失败可诊断（新增场景）

#### Scenario: 配置文件损坏

- **GIVEN** `settings.json` 内容不是合法 JSON
- **WHEN** 托盘启动
- **THEN** 该文件改名留档（`settings.json.corrupt-<时间戳>`），不覆盖原文件
- **AND** 右下角气泡提示一次「配置文件损坏，已按默认设置启动」
- **AND** 程序以默认配置继续运行，不弹对话框、不崩溃
