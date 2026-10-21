# 竖排透明合成源码修正（静态审查，未运行验收）

用户要求优先修复竖排透明度，随后明确叫停测试，要求直接从代码查找问题。此后仅阅读、修改和静态核对源码，没有继续启动测试窗口、编译、运行测试、打包或安装。

## 源码问题

生产版本 `wry/src/webview2/mod.rs` 在 UIAccess band=2 时创建 CompositionController，但仍将它和 DirectComposition target 都绑定到普通 `WRY_WEBVIEW` 子窗口。顶层透明宿主由 tao 设置了 NOREDIRECTIONBITMAP，子窗口则走普通重定向路径。HTML 调整的是视觉内容的 alpha，不能删除中间 HWND 的底层表面。

此前独立候选曾将 composition 目标改到顶层，但遗漏了原有 `parent_subclass_proc` 的假设：WM_SIZE 读取 controller.ParentWindow 后，无条件把该窗口移到 (0,0)。当 controller.ParentWindow 已经等于顶层宿主时，这会移动整个悬浮窗。此前固定屏幕坐标的候选采样不能证明其内容渲染正确，因此不计为通过。

宿主还有一个明确的初始化顺序问题：首次设置穿透时，在设置 WS_EX_LAYERED 位之前调用 SetLayeredWindowAttributes。原窗口未具有分层样式时，这个调用不能正确初始化 alpha。

## 本轮改动

- 仅对 band=2、transparent=true、非子 WebView 的路径使用顶层透明宿主作为 composition controller 和 visual target 的宿主。普通窗口与嵌入式子 WebView 保持原创建路径。
- 保留 Wry 的中间 HWND 用于既有消息调度和 bounds 记录，但保持其隐藏；再次设置 WebView 可见性时，也不重新显示该中间 HWND。真实内容的可见性由 controller.SetIsVisible 控制。
- WM_SIZE 只移动与消息宿主不同的 controller 父窗口，防止顶层宿主被当成子窗口重定位。
- 鼠标转发绑定真实 composition 宿主。每个转发子类使用唯一 ID，视图释放时解除并释放对应 COM 引用；宿主先销毁的路径不会再次释放已移除的子类数据。
- 当前这类顶层合成视图不支持普通子窗口 reparent，明确拒绝该操作，避免将 visual target 留在旧宿主。FrameScope 竖排窗口不使用 reparent。
- `lib.rs` 先设置分层／穿透样式，再初始化 alpha=255；透明属性和系统背板设置完成后再显示窗口。背景不透明度仍由原 DOM 控制，文字不跟随整窗淡出。

## 静态核对范围

已逐项阅读构造、错误返回、WM_SIZE、显隐、移动通知、鼠标转发、reparent 和释放路径；核对分支限定及新字段初始化，完成差异空白检查。

此版本代码**没有编译或运行验证**，不能宣称实际白底已经消失。发布、覆盖安装及再次运行测试都未执行。此前试验仅保留在 artifacts 和已管理的组件构建目录中，没有替换本机软件。

修复独立提交在 `codex/2.0`；如需回退，使用该提交的 revert。磁盘温度修复与 PUBG 只读诊断工具属于之前的独立提交，不受本次回退影响。

## 2026-10-21 测试版发布前补充

用户授权将此前修复发布为测试版后，执行生产宿主的 release 模式编译和无窗口单元回归。首次编译发现 `GetWindowSubclass` 的最后一个参数需要 `Option<*mut usize>`，修正为 `Some(&mut data)` 后通过编译；宿主回归 97 项通过、0 失败、12 项忽略。忽略项未执行，未启动独立透明度窗口、游戏或 UAC 测试。上述结果补齐编译和单元回归证据，实际透明效果仍待测试版用户验收。
