# 竖排透明度及 PUBG 全屏可见性调查

## 已核实的现场

两个反馈来自不同电脑，不能混用现场结论。

本机：正在运行 `C:\Program Files\FrameScope\FrameScope.exe`，构建信息为 `2.0.1-beta.1 / 47b7a61`；令牌 UIAccess=1、elevated=1。竖排窗口可见、band=2、分层 alpha=255，保存的背景不透明度为 138。

用户 PUBG 压缩包：`PUBG--BATTLEGROUNDS-20261019-204500-p8740-25289ebd.zip`。用户确认三种悬浮窗样式在全屏均不显示。原始包里有 78,195 条应用 Present 数据，最后一份 live-frame-stats 有 FPS=168.38、1% Low=102.04、0.1% Low=73.53；这只能证明采集在产出数据，不能证明悬浮窗可见。

完整显示追踪尝试只留下 12 条 PresentMode=Other，状态记录 full-trace-stalled，之后禁用了显示时序追踪；主 CSV 没有 PresentMode。因此不能将本次游戏呈现方式判定为独占、独立翻转或桌面合成中的任意一种。

## 本机透明度复现

用户明确允许独立短时透明度测试及正常 UAC。测试复用生产版本已编译的 tao / wry 和运行时 UIAccess 接力源码，使用独立 WebView 缓存；不操作游戏、不修改当前应用配置。小窗口自动关闭。

WebView UA 为 Chromium / Edge 155，HTML 和 body 的计算背景均为 rgba(0,0,0,0)。不透明度 0%、50%、100% 下，窗口内测试点分别为 RGB(211,211,211)、(114,114,114)、(17,17,17)，红蓝背景切换不影响结果。带／不带 WS_EX_LAYERED 都一样。对照窗口测试点确实在红蓝之间切换，排除了背景控制窗口未绘制。

只关闭运行时 SolidColorLayerBackground 特征的候选没有改善，因此未加入生产软件。页面显示后再次设置透明背景的时序候选另行对照；未通过实测的候选不算修复。

上游有相近的运行时透明背景回归报告，但它不是本项目已经修复的证明：

- https://github.com/MicrosoftEdge/WebView2Feedback/issues/5752
- https://github.com/MicrosoftEdge/WebView2Feedback/issues/5481

## PUBG 所缺证据及采集方式

新增只读脚本 `tools/Collect-OverlayVisibility.ps1`，采集现场以下信息：

- FrameScope 构建版本、路径、UIAccess／管理员令牌查询结果；读取失败保留错误，不能当成 false。
- FrameScope 与 PUBG 窗口的可见性、物理矩形、band、cloaked、分层 alpha、前台归属。
- 显示器物理区域以及保存的悬浮窗样式、位置和定位基准。

脚本不启用 ETW、不启动程序或服务、不发送输入、不调整窗口、不写应用配置。采样期间只短暂设置并恢复采样线程自己的 DPI 上下文，以读取物理坐标。没有游戏进程时仍可用于诊断桌面悬浮窗。

在用户电脑，将脚本放在任意目录，在该目录运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Collect-OverlayVisibility.ps1
```

启动后 5 秒内切回 PUBG 全屏，保持游戏前台和悬浮窗开启约 20 秒。JSON 输出到桌面，文件名以 `FrameScope-overlay-` 开头。现场 JSON 才能区分窗口没有创建／已隐藏、位置越界、令牌或 band 不符、窗口已创建但被全屏呈现覆盖等情况。

## 脚本验证及边界

已执行单次只读采样，确认能读取本机 2.0.1-beta.1 构建、band=2 竖排窗口和一个显示器；结果与独立 Win32 读取一致。

原始用户数据及测试输出保存在 `artifacts/20261020-overlay-and-disk`，不进入 Git。本文没有将本机窗口状态当成 PUBG 用户的窗口状态，也不宣称三个问题已全部修复。未重新打包、发布或覆盖本机安装版。
