# FrameScope UIAccess 补丁

基于 wry 0.55.1，保留原 MIT / Apache-2.0 许可证。

仅当父窗口位于 UIAccess band 2 时，使用 WebView2 Composition Controller 和 DirectComposition visual tree 承载原网页，避免普通 Controller 在该窗口上返回 0x80070057。普通桌面 band 1 仍使用上游 Controller 创建路径。

组合渲染资源随 WebView 生命周期持有；原窗口的左键和鼠标移动消息转发到 Composition Controller，保留原竖排组件的拖动事件。没有替换 FrameScope 的竖排 React 组件、显示项目或设置格式。

复现和回归证据位于仓库 artifacts/20261010-uiaccess-resume：旧版关闭后仍报告开启；修复版连续三轮原生启停和三轮页面开关测试通过，原竖排窗口具有有效 HWND、band 2，全部设置保持一致。
