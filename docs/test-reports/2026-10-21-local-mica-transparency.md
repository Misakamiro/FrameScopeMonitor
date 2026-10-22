# 竖排 OSD：本机 Mica For Everyone 背板覆盖

## 本机定位

用户反馈创建前背景色候选仍无效，并要求同时排查本机。本轮没有继续叠加产品补丁；先采集运行实例、DWM 状态、显示设备、相关模块、策略、环境和错误事件。

当前宿主 PID 63900，已安装候选 SHA256 为 `39457CE918362047B6BACC22F53D0B9540CB8DB87ECF2FF779C301743D760504`，只读令牌查询确认 elevated=true、uiaccess=true。实际浏览器使用 WebView2 155.0.4283.62；GPU 进程加载 Windows 系统 dxgi/d3d11/dcomp 和 NVIDIA 32.0.16.1692 驱动。活动 DISPLAY1 属于 NVIDIA；GameViewer 虚拟显示设备均未附加到桌面。系统透明效果启用，高对比度的启用位未置位，未发现有效的 WebView2 参数策略。最近 12 小时有界查询未发现相关应用或显示驱动错误。这些检查不能排除所有 GPU 或运行时问题。

故障竖排 HWND 24314018，窗口类 `Tauri Window.FrameScopeLayeredOverlay`，class style=3（不含 OWNDC/CLASSDC）、band=2、NOREDIRECTIONBITMAP/LAYERED/TRANSPARENT 已设置，整窗 alpha=255。关键异常是 DwmGetWindowAttribute(DWMWA_SYSTEMBACKDROP_TYPE=38) 返回 S_OK、值 **3**，即 Desktop Acrylic，而程序要求的是 None=1。

本机 MicaForEveryone.exe PID 8568 的实际路径为 `C:\Program Files (x86)\Mica For Everyone\MicaForEveryone.exe`，版本 1.3.1.2。它的用户配置 `%LOCALAPPDATA%\Mica For Everyone\MicaForEveryone.conf` 存在全局 `BackdropPreference = Acrylic`，原先没有 FrameScope 或竖排窗口类规则。

## 本机最小修正与对照

原配置已逐字节备份到 `artifacts/20261021-local-transparency-audit/MicaForEveryone.conf.before`。原 SHA256：`951ED9367F46BD0CE8674A5C834FEF5182EAF019173F113A7AEE2491A0E45B74`。

只追加精确匹配当前竖排窗口类的规则：

```text
Class: "Tauri Window.FrameScopeLayeredOverlay" {
  BackdropPreference = None
}
```

新配置 SHA256：`6CE0618D4CD6F85ECFA5B502AFE5702F03896FC04EE6D694C52DCF162168A2CC`。Mica 自动重载配置后，对同一组已存在窗口只读复核：

| 窗口 | HWND | 修正前背板 | 修正后背板 |
|---|---:|---:|---:|
| 竖排 OSD | 24314018 | 3（Acrylic） | 1（None） |
| 主窗口 | 41751562 | 3（Acrylic） | 3（Acrylic） |

FrameScope 与 Mica 进程没有重启，产品 EXE 哈希没有改变，未发送窗口输入、改变 OSD 滑块或位置。本轮读到用户设定的竖排不透明度为 3/255，接近全透明；没有擅自将其改写成 0。窗口截图可见底下界面文字透过 OSD。2026-10-22 用户明确实机确认：“底色消失，透明度恢复”。同一运行实例上，只改变 Mica 精确类规则后，DWM 读回和用户视觉结果均恢复，确认本机全局 Acrylic 规则是本次固定底色的直接成因。

## 源码与官方资料交叉核对

- 微软将 DWMSBT_NONE 定义为不绘制系统背板，将 DWMSBT_TRANSIENTWINDOW 定义为 Windows 11 Desktop Acrylic：[DWM_SYSTEMBACKDROP_TYPE](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_systembackdrop_type)。
- Mica 的规则服务响应窗口出现和配置重载，并匹配、应用背板：[RuleService.cs](https://raw.githubusercontent.com/MicaForEveryone/MicaForEveryone/master/MicaForEveryone.Core/Services/RuleService.cs)。
- ClassRule 精确比较窗口类名且具有更高匹配优先级：[ClassRule.cs](https://raw.githubusercontent.com/MicaForEveryone/MicaForEveryone/master/MicaForEveryone.Core/Models/ClassRule.cs)。
- None=1、Acrylic=3；应用规则最终调用 DWM 设置背板：[BackdropType.cs](https://raw.githubusercontent.com/MicaForEveryone/MicaForEveryone/master/MicaForEveryone.Core/Models/BackdropType.cs)、[TargetWindow.cs](https://raw.githubusercontent.com/MicaForEveryone/MicaForEveryone/master/MicaForEveryone.Core/Models/TargetWindow.cs)。

上游源码用于解释机制；本机配置、进程版本及同一 HWND 的前后读回是本轮现场证据。

## 证据与边界

证据目录：`artifacts/20261021-local-transparency-audit`。重点为 live-window-dwm.before-rule.json、live-window-dwm.after-rule.json、mica-rule-comparison.json、composition-related-processes.json、os-display.json、loaded-modules.json 和配置备份。

用户视觉确认已于 2026-10-22 完成，尚未执行关闭重开或 Windows 重启验收。创建前透明色候选已被用户报告未解决故障，不能再把其无窗口测试或编译通过称为白底修复。本机排除规则的作用已由同一 HWND 的 DWM 前后读回和用户实机反馈共同确认。

本轮保留全局及其他规则、主窗口效果、驱动与 WebView2 版本。没有终止美化软件、修改系统注册表、重置 WebView2 profile、更新驱动、修改游戏或重新编译产品。

回退：只删除本轮新增的精确 Class 规则；Mica 热重载后全局 Acrylic 会再次作用于竖排。原配置备份可用于核对，后续若用户又修改其他规则，不应整文件覆盖恢复。
