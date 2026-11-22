# 任务

## 1. 方向检测
- [x] 1.1 新增 Get-Rotation，读 mRotation，取不到返回 -1
- [x] 1.2 新增 lastRotation，记录上次方向

## 2. 比例与窗口
- [x] 2.1 按旋转值换算 videoRatio（竖 w/h，横 h/w）
- [x] 2.2 扩展 Adjust-WindowToVideo：横屏宽度优先、竖屏高度优先
- [x] 2.3 横屏高度超屏幕时封顶并回算宽度

## 3. 轮询
- [x] 3.1 Update-State 每秒检测方向，变化才调整
- [x] 3.2 启动投屏时初始化方向
- [x] 3.3 转屏写日志

## 4. 收尾
- [x] 4.1 语法检查、编译、部署、归档
