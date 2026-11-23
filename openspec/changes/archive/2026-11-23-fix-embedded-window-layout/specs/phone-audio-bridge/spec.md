# 手机音频桥接规范 · 增量（fix-embedded-window-layout）

## Modified Requirements

### Requirement: 单行顶部栏

投屏时 SHALL 提供一个本程序自己的窗口，窗口最上方只有一行，从左到右依次是标题、菜单、窗口按钮，不得拆成标题栏加菜单栏两行。

#### Scenario: 顶栏始终可见可点（新增）

- **GIVEN** 程序窗口模式下 scrcpy 画面已成功嵌入内容区
- **WHEN** 用户查看窗口顶部或点击顶栏上的操作菜单、最小化、关闭按钮
- **THEN** 顶栏完整显示，不被 scrcpy 画面覆盖
- **AND** scrcpy 子窗口的定位原点为内容区左上角，纵向偏移等于顶栏实际高度（换算为物理像素）
- **AND** 顶栏按钮点击与顶栏拖动、双击最大化均正常响应

#### Scenario: 画面嵌入（修订）

- **GIVEN** scrcpy 已启动并创建窗口
- **WHEN** 系统完成嵌入
- **THEN** 去掉该窗口的标题栏、可调边框与 `WS_POPUP`，作为子窗口填入内容区
- **AND** 改完样式后以 `SWP_FRAMECHANGED` 让新样式立即生效
- **AND** 子窗口在父客户区中的坐标包含内容区相对窗口左上角的偏移，使画面正好落在内容区、不压顶栏
- **AND** 所有传给 Win32 的坐标与尺寸为物理像素；进程在任何窗口创建前声明 DPI 感知，避免二次缩放
- **AND** 画面等比缩放，不拉伸变形

### Requirement: 记忆各方向窗口尺寸

系统 SHALL 分别记忆竖屏与横屏的窗口尺寸，切换方向时恢复该方向上次的尺寸，不用切换前的状态当基准。

#### Scenario: 启动时按视频比例定尺寸（新增）

- **GIVEN** 程序窗口模式，手机已连接
- **WHEN** 投屏启动
- **THEN** 先按视频流宽高比计算窗口尺寸并设置主窗口 `Width/Height`，再启动 scrcpy
- **AND** scrcpy 启动参数带 `--window-width/--window-height`，尺寸与窗口内容区一致
- **AND** 内容区比例贴合视频比例，画面外黑边不可见，不出现大片灰屏/黑屏

#### Scenario: 尺寸计算真正接线（新增）

- **GIVEN** `ScreenService` 提供 `GetVideoRatio()` / `DesiredWindowSize()` / `RememberSize()`
- **WHEN** 投屏启动、窗口拖动结束、横竖屏方向变化
- **THEN** 应用调用上述方法计算、应用并记忆尺寸，而不是让主窗口停留在 XAML 写死的尺寸
