# 规范增量：手机音频桥接

## ADDED Requirements

### Requirement: 无线音频转发

系统 SHALL 通过 adb 无线连接，把 Android 设备正在播放的音频转发到电脑默认输出设备，且不在手机上安装常驻应用。

#### Scenario: 正常无线播放
- **GIVEN** 手机已开启「无线调试」并完成配对
- **WHEN** 用户点击「启动音频转发」
- **THEN** 声音从电脑默认输出设备播出，手机扬声器静音

### Requirement: 设备发现与选择

系统 SHALL 列出所有可用的 adb 设备供用户选择，并把同一台设备的重复连接记录合并为一条。

#### Scenario: 同一设备两条记录
- **GIVEN** 同一台手机同时存在 `IP:端口` 与 `adb-*._adb-tls-connect._tcp` 两条记录
- **WHEN** 用户展开「选择设备」菜单
- **THEN** 只显示 IP 形式的一条，启动时用 `-s` 精确指定

### Requirement: 设备别名

系统 SHALL 允许用户为设备设置自定义名称，并持久化保存。

#### Scenario: 设置别名
- **GIVEN** 设备当前显示为型号
- **WHEN** 用户输入别名「主力机」
- **THEN** 菜单立即显示新名称，重启后仍生效

### Requirement: 无效连接清理

系统 SHALL 提供清理无效 adb 连接的入口，且不得切断唯一可用的连接。

#### Scenario: 仅有 mDNS 连接
- **GIVEN** 设备只有 mDNS 一条记录，没有对应的 IP 形式记录
- **WHEN** 用户点击「清理无效设备」
- **THEN** 保持该连接不断开

### Requirement: 音质与延迟调节

系统 SHALL 允许在运行时切换缓冲长度与编码格式，切换后立即生效。

#### Scenario: 切换编码
- **GIVEN** `raw` 在当前 Wi-Fi 下断续
- **WHEN** 用户切换到 `Opus`
- **THEN** 以 opus + 192kbps 重启，音频恢复连续

### Requirement: 启动失败可诊断

系统 SHALL 在出错时给出可操作的原因，而不是只报底层错误。

#### Scenario: 托盘程序异常
- **GIVEN** 托盘脚本抛出未捕获异常
- **WHEN** 异常发生
- **THEN** 弹出提示框，完整堆栈写入 `tray-error.log`
