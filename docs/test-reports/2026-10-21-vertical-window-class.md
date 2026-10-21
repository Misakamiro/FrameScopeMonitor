# 竖排透明度：原生窗口类与分层样式冲突

## 本机事实

用户反馈 beta.2 的竖排仍有不透明底色。本机运行路径 C:\Program Files\FrameScope\FrameScope.exe，构建标识 2.0.1-beta.2 / ecfc130669e3b2096f7d73ff3edf0763c920164f；安装目录主程序 SHA256 与发布包一致，为 099C4D4215D662AD8557909F009B7F64A9E705F84819D99CAF7217064F5DDE16。

对现有窗口的只读 Win32 查询确认：UIAccess=true、elevated=true，竖排可见、band=2、cloaked=0；配置为 vertical_panel、opacity=99。类样式 GCL_STYLE=0x00000023（CS_HREDRAW | CS_VREDRAW | CS_OWNDC），扩展样式为 0x002C0138，其中包含 WS_EX_LAYERED。没有创建测试窗口、发送输入、切换焦点或请求 UAC。

Microsoft 明确规定 WS_EX_LAYERED 不能用于带 CS_OWNDC 或 CS_CLASSDC 的窗口类。源码中 Tao 为所有窗口注册带 CS_OWNDC 的同一类，而竖排穿透代码随后附加 WS_EX_LAYERED，形成已确认的接口约束冲突。beta.2 的合成目标调整没有消除此冲突。

参考：https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles

## 排除的候选

现场还看到隐藏的 WRY_WEBVIEW 与可见的 Chrome_WidgetWin_0。曾考虑把控制器 ParentWindow 留在隐藏容器、仅将视觉树挂到顶层，但随后核对到 ICoreWebView2Environment3 明确要求 ParentWindow 与视觉树宿主一致。该候选及其“浏览器子窗口必须隐藏”的检查已撤回，未提交、未发布，也没有操作运行中的窗口。单凭子窗口可见，不能认定其就是最终白底来源。

参考：https://learn.microsoft.com/en-us/microsoft-edge/webview2/reference/win32/icorewebview2environment3

## 本轮修正

- 仅对原有竖排透明宿主（既有标题与 transparent 条件）选择独立的 .FrameScopeLayeredOverlay 窗口类。
- 该类保留重绘标志，移除 CS_OWNDC；普通窗口仍使用原类名和 CS_OWNDC。
- 必须使用不同类名：主窗口可能已经注册原类，之后再次 RegisterClassExW 不能覆盖其类样式。
- 不改变控制器 ParentWindow、视觉树、鼠标转发、穿透开关、定位、采集或报告代码。

## 无窗口原生回归

tests/vertical-window-class-tests.ps1 从生产文件提取实际 register_window_class 函数，用固定 Windows SDK 依赖编译检查。通过真实 RegisterClassExW / GetClassInfoExW 验证注册结果；不调用 CreateWindow，不执行消息循环，不创建或显示测试窗口。

保留旧行为时：普通类检查通过，另外 3 项失败，实际读回 CS_OWNDC=32，并确认普通类与竖排复用相同类名。修正后 4 项全部通过：

1. 普通类保留 CS_OWNDC。
2. 竖排类不含 CS_OWNDC / CS_CLASSDC。
3. 先注册普通类后，竖排仍获得不同且兼容的类。
4. 竖排类重复注册保持一致。

组件夹具在 C:\fs-build\b202\vertical-class-contract，复用 b201 的编译缓存；未创建第三份完整构建。现场及检查日志保存在忽略目录 artifacts/20261021-beta2-transparency-feedback。

修正后的生产宿主 release 编译及单线程单元回归：97 项通过、0 失败、12 项忽略。调查中第一次并发回归曾有一项报告测试在 remove_dir_all 清理临时夹具时返回 Windows 1168；单独复跑通过，随后完整单线程回归通过，未修改报告或清理代码。

## 验收边界

类样式冲突及其修正已由上述注册检查验证；这不等于最终透明像素已经验收。新窗口的透明背景 0／中间值／最大值、文字、拖动与穿透、重开和重启定位仍需包含新代码的实例确认；可能存在独立的 WebView2 运行时透明背景问题，本轮不把上游相近反馈当作本机已确认根因。

本轮不生成发行安装包、不发布、不覆盖本机；本机仍是 beta.2。公开标签与附件不覆盖，源码回退使用独立 revert。
