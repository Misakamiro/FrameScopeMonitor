# 会话交接文档：淡化切换闪烁 + 圆角破碎（未解决） + 白屏根修回顾

**会话日期**: 2026-09-13 下午 ~ 2026-09-14 凌晨
**分支**: `codex/nexbox-frontend-preview`，本会话净提交 `3366d97 → 1723d19`（共 6 笔，仅本地未 push）
**状态**: ⚠️ 白屏已根修；**淡化切换闪烁与圆角破碎两个问题未解决**，已做完整取证与方向排除，交接给下一会话。

---

## 一、本会话已完成（全部实机验证）

### 1. 白屏根修（`d2d3151`）—— 最重要的成果

**真凶：任何非 null 的 CSP 都会让 WebView2 的 DComp 呈现管线冻结在首帧**（"白屏"= 壁纸+搜索框的首帧，之后渲染进程持续出帧但一帧都上不了屏）。

关键证据链（全部可复现，工具已入库）：
- 渲染进程无辜：CDP `Page.captureScreenshot` 抓到的帧是完整玻璃 UI；DOM/样式表/布局/hit-test 全部正常。
- 问题在"帧→窗口表面"的提交层：屏幕（PrintWindow 与肉眼）停留在首帧。
- Bisect 矩阵（PrintWindow 客观判定）：123500f / 723612c / 566ffb9 + `csp: null` 全部渲染；723612c / 55f5282 + `script-src 'self' 'unsafe-inline'` 全部白屏（叠加 `--disable-gpu-compositing` / `--use-angle=gl` / `--disable-direct-composition` / `--disable-gpu` 四种 GPU 开关均无效）。
- **铁律 1 最终形态**：这个应用 CSP 必须为 null。此前"style-src 会杀 Emotion"的认知只是冰山一角，非 null CSP 直接冻结呈现。SEC-001 处置改回 null，其余加固（命令注入修复、依赖锁定、单实例）保留。

**教训（写进流程）**：多改动合流后做对照实验，必须先 bisect 回干净基线，否则会得出被污染的"X 无辜"错误结论——本次 CSP 被错误排除就是因为三方实验跑在含其它新改动的构建上。

### 2. 其它已交付修复

| 提交 | 内容 |
|---|---|
| `3366d97` | PawnIO 打包修复（四处构建脚本）+ **pawnio.rs 参数 bug**（官方安装器是短横线 `-install -silent`，原 `/install` 弹窗挂起）+ 采集诊断脚本随包 + vite vitest exclude 修复 |
| `723612c` | 安全审计修复：命令注入去 PowerShell 化、依赖 50 项精确锁定、PRIVACY.md（CSP 部分后被 d2d3151 改回 null） |
| `566ffb9` | 审计 F-1/F-2/F-3：单实例插件（tauri-plugin-single-instance，须注册为第一个插件）、卸载删数据改为删整个 `%LOCALAPPDATA%\FrameScopeMonitorData`、显卡物理优先排序（Provider + ReportGenerator 两处） |
| `d80047d` | 淡化切换闪烁第一次尝试：`AnimatePresence mode="wait"` → `popLayout`（交叉淡化）——**用户实测闪烁仍在**（见下） |
| `8dd5051` | 主页离场淡化（主页常驻挂载特殊处理：离开时 opacity 1→0 + 弹出绝对定位 + 完成后 display:none；进入时淡入）——同样未消除用户所见闪烁 |
| `1723d19` | 圆角实验（**无效果**，见下） |

### 3. 产物状态

- **用户机器已安装**：02:26 构建（= HEAD `1723d19`，含圆角实验，无功能差异），`C:\Program Files\FrameScope\`
- **桌面副本**：`Desktop\FrameScope_1.0.0_x64-setup.exe` = 02:26 构建，SHA256 开头 `74213495`
- 测试基线：cargo 宿主 5/5、硬件服务 20/20、vitest 14/14、C# 27 套件全 PASS（`tests/run-all-with-timeout.ps1`）
- 工作树干净，全部已提交

---

## 二、⚠️ 未解决问题 1：淡化模式页面切换闪烁

**用户描述**：设置里切换动画选"淡化"时，切换页面有闪烁；`popLayout` 交叉淡化 + 主页离场淡化都上了之后**用户确认仍闪烁**，且用户判断"问题应该不是动画的问题"。

**已排除的方向**：
- ❌ 动画空窗（mode="wait" 的透明度归零间隙）——popLayout 交叉淡化后仍闪
- ❌ 主页瞬间消失——已加离场淡化
- ❌ DPI 非整数缩放——实测 100%（96 DPI）
- ❌ 渲染进程帧问题——CDP 抓帧完美（白屏会话已证）
- ❌ GPU 渲染路径——四种 GPU 开关无一对闪烁有影响

**已确证的相关事实**：
- `.page-animating` 机制：动画期间全局禁用 backdrop-filter（`index.css:97`），动画结束瞬间整体恢复。组件有 `transition: backdrop-filter 0.45s` 声明，但 **CSS 无法在 `none` ↔ `blur(Npx)` 间插值（离散跳变）**，"平滑浮现"实际不存在——动画结束瞬间整页玻璃"唰"地变毛玻璃，这是每页切换必然发生的一次全页级视觉跳变，嫌疑最大。
- 圆角破碎（见问题 2）与闪烁可能同源：合成动画期间的裁剪/模糊失效。

**下一步建议（按优先级）**：
1. **先搞清"闪的是什么"**：让用户用手机慢动作拍摄切换过程，或开发者用 PrintWindow 连拍（脚本见下）+ CDP 触发导航，拿到闪烁帧的客观样本。不要再造轮子猜。
2. **验证 backdrop-filter 跳变假说**：动画结束后不恢复 blur（把 `.page-animating` 的恢复延迟 300ms 或直接常驻 blur(0px)→blur(Npx) 用可插值写法 `blur(0px)` 替代 `none` + 组件 transition），看闪烁是否消失。
3. 若 2 无效：怀疑 WebView2 153.0.4234.32（很新，9-12 才更新）的合成回归——尝试回滚 Evergreen runtime 到上一个稳定大版本对比。

---

## 三、⚠️ 未解决问题 2：全 UI 圆角破碎（用户判定为"边缘破碎/锯齿/毛边"）

**用户截图**：侧栏胶囊（会话 image-cache `image-3777fc4983b0d808950b913797fa155a.png`），用户确认形态为第 2 类（边缘破碎/毛边），并称"全部圆角"都有问题。

**已尝试并证实无效**：
- `1723d19`：侧栏模糊移入带 `borderRadius+clipPath inset(0 round 1rem)` 的绝对内层 + LiquidGlassCard 自身加 `clipPath inset(0 round 0.75rem)`——**前后 PrintWindow 像素 diff = None（零变化）**。几何裁剪不是（唯一）根因。

**重大误判教训**：侧栏区域的"直角玻璃边"最初被我判定为 backdrop-filter 溢出伪影，实为**壁纸内容（储物柜门缝）透过半透明玻璃的正常显示**。静态截图判读必须先排除壁纸内容干扰。

**下一步建议**：
1. **让用户开关"液态玻璃"设置做对照**：关玻璃（实色卡片、纯 CSS 圆角）后圆角是否正常？正常 → 锁定 backdrop-filter 合成路径；仍异常 → 基础圆角渲染问题（Chromium 153 回归嫌疑加大）。
2. 让用户标记问题的精确元素与出现时机（常驻 vs 动画中 vs 滚动中）。
3. 若锁定 backdrop-filter：已知 Chromium 对 `backdrop-filter`+圆角+transform 场景的裁剪缺陷族（crbug 993861 家族），可尝试给玻璃元素加 `isolation: isolate` 或把容器 `transform: translateY(-50%) translateZ(0)` 改为非 3D 定位（`top: calc(50% - 高度/2)`）逐个验证。注意 `translateZ(0)` 是为缓解旧背景断层加的，动它前先确认断层不复发。

---

## 四、取证工具（已入库 `tools/`，复用方法）

| 脚本 | 用途 | 前置条件 |
|---|---|---|
| `printwindow-capture.ps1` | PrintWindow(PW_RENDERFULLCONTENT) 抓窗口实际呈现内容，**无需前台**，输出 `fs-pw.png` + 中心像素采样 | 无 |
| `cdp-shot.ps1` | CDP 抓渲染进程内部帧（分辨"渲染器坏"还是"表面/呈现坏"） | 窗口配置临时加 `additionalBrowserArgs: "--remote-debugging-port=9333"` 后重建 |
| `cdp-probe.ps1` / `cdp-probe2.ps1` / `cdp-probe4.ps1` | 样式表/资源加载、elementFromPoint 布局、visibility/rAF 活性 | 同上 |
| `where-is-webview.ps1` | 枚举 Chrome_WidgetWin 窗口归属与父子关系（可靠版用 GetParent） | 无 |
| `tests/run-all-with-timeout.ps1` | C# 27 套件带超时批量跑（套件判定按 stdout `: PASS`，别信 PS 的 ExitCode） | 无 |
| `tools/diagnostics/diagnose-pubg-capture.ps1` | 采集失败自助诊断（随安装器分发） | 无 |

**判读基准**：正常渲染时窗口中心像素约 (200±30, 190±30, 190±30) 玻璃色；白屏/壁纸态约 (172,167,174) 灰。但**不要只信单点采样**（壁纸内容会干扰），保存 PNG 直接看图。

---

## 五、遗留决策项（累计）

- **SEC-004 代码签名**：等用户采购证书（唯一遗留的安全审计项）
- **PUBG 采集真因**：等用户游戏机的 `framescope-runs\PUBG*\status.json`
- **本轮新增**：淡化闪烁 + 圆角破碎（见上，工具与方向已备好）
- 安装器 P3：体积优化、NSIS 退役时机
- 单实例插件注意：必须注册为 Builder 的第一个插件

---

## 六、环境与坑速查（本会话新增/踩实）

- **git-bash 里 powershell -File 的路径反斜杠会被 bash 吃掉**（`tools\Package-` → `toolsPackage-`），一律用正斜杠
- **tauri build 报 os error 32**：残留 FrameScope/msedgewebview2 子进程锁 exe，全部 Stop-Process 后等几秒再跑；SearchHost 等系统组件会重生 webview2 子进程属正常
- **重装前必须确认旧卸载器完成页已关 + `%TEMP%\framescope_uninstall_cleanup.ps1` 的 PowerShell 进程已杀**，否则自删脚本会连新装目录一起删
- **PS 读 Start-Process 的 ExitCode 有怪癖**；C# 套件 PASS 判定按 stdout
- **打包三步**：`Package-Installer.ps1 -StageOnly` → `npx tauri build` → `build-installer.ps1 -SkipPayload`
- **PrintWindow(PW_RENDERFULLCONTENT) 是无需前台的窗口呈现取证**，配合像素 diff 可客观验证任何视觉修复
- 构建产物中 app 前端是构建时嵌入（vite dist 进 exe），改前端必须重跑 tauri build
