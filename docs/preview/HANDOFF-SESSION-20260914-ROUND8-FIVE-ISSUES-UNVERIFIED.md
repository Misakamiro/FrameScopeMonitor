# 会话交接文档：五问题两轮修复均未确认生效，需重新定位并修复测试

**交接日期**: 2026-09-14
**交接人**: ZCode（Round 7/8 两轮修复的执行者）
**接手方**: Codex
**仓库**: `C:\Users\misakamiro\Documents\FrameScopeMonitor-Rebuild`
**分支**: `codex/nexbox-frontend-preview`，HEAD `febd7ec`（本地提交，**永不 push**，无上游）
**前置阅读**: `docs/preview/HANDOFF.md`（26+ 条铁律，全部有效）；本文档取代此前所有交接文档对"根因已定位"的表述。

---

## 零、给接手者的最重要三句话

1. **用户对五个问题的实测结论是：全部依旧存在。** 此前两轮修复（Round 7：616b049/c8d2f09/a82e8ec；Round 8：1d05100）声称的"根因"与"已修复"**全部降级为未验证假设**——其中至少四条的修复在用户机器上没有产生可感知的效果。**不要相信本文档和历史文档里的任何根因结论，一切以你自己的实测为准。**
2. 每个问题的"已做尝试"清单在第一节，目的是让你**避免重复已经失败的路径**，而不是让你沿着同样的思路继续。每个问题都给了"为什么不确定"与"建议的重新定位步骤"。
3. 硬约束（用户拍板，不可挑战）：**OSD FPS 采集只走 ETW，绝不用 PresentMon**（反作弊拒其 trace）；FrameScope 是开源软件，GPL-3.0 不构成障碍，可参考/移植开源代码；`tauri.conf.json` 的 `csp` 必须为 `null`（任何非 null CSP = 白屏，实锤过）；提交只进本地，**不 push**。

---

## 一、五问题逐条：现状、已试过的路、为什么不确定、你的待办

> 用户原话级别的问题描述（2026-09-14 第二次实测反馈）：
> 1. 全屏独占的游戏 OSD 还是不显示
> 2. OSD 帧数没办法超过 200 帧
> 3. OSD 的 1% 和 0.1% 出现了显示异常，显示数据不对
> 4. 淡化造成的闪屏还是没有解决
> 5. 圆角问题依旧存在

---

### 问题 1：全屏独占的游戏 OSD 不显示

**已做尝试（均未确认生效）**
- Round 7 方案 A：GDI 悬浮条 `WM_TIMER`（250ms）内 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` 周期重申；窗口创建补 `WS_EX_NOACTIVATE`；竖排 WebView OSD 加了独立 500ms 线程做同样的事（`desktop-preview/src-tauri/src/lib.rs` 搜 "周期性重申 TOPMOST"）。
- Round 7 方案 E + Round 8：`game_fps.rs` 按 ETW 事件 ID 推断呈现模式（`PRESENT_MODE` 原子量 → overlay `present_mode` 字段）；竖排 OSD 在 `legacy-exclusive` 时显示提示条（VerticalOverlayPage.tsx）。
- Round 8 多显示器修正：GDI 宽度钳制按 `MonitorFromPoint`。

**为什么不确定**
- 没有任何一份**用户实际游戏场景**下的数据：`present_mode` 检测从未在用户的游戏上输出过日志，不知道游戏被判定成什么模式、判定是否正确。
- 不确定用户用的是 GDI 条形 OSD 还是竖排 WebView OSD（两者的置顶路径都改过，但都没验证过）。
- 周期置顶的代码路径**从未被运行时验证**（SetWindowPos 返回值、窗口实际 Z 序都没抓过）。
- "flip model 下 TOPMOST 有效"是理论推断，本机从未复现过"独占全屏 + OSD 应显示"的场景。

**你的待办（重新定位）**
1. 让用户复现一次"全屏独占不显示"，立即抓两样东西：`framescope-watcher.log`/服务日志里的「呈现模式 →」行；`Get-Process` 后用工具枚举 OSD 窗口的 Z 序（`GetTopWindow` 链、`WindowFromPoint` 在 OSD 坐标上的命中）。
2. 写一个最小实验：`tools/fps-bench` 已支持 `exclusive` 模式（D3D11 SetFullscreenState），跑起来直接验证 OSD 在真·独占下的表现与 `present_mode` 日志——这是本机可复现的独占环境。
3. 确认周期置顶线程真的在跑（加日志或断点），确认 `is_visible()` 门控没有把线程挡住。
4. 如果确认是真·独占（`present_mode=legacy-exclusive`），修复方向只剩方案 B（强制合成重叠，掉帧 5-15%，**需用户拍板的开关**）或引导切无边框（提示条已有，验证它能显示出来）。

**验证标准**：在 fps-bench exclusive 模式或用户游戏下，OSD 可见；或至少提示条正确出现且日志模式判定正确。

---

### 问题 2：OSD 帧数没办法超过 200

**已做尝试（均未确认生效）**
- Round 7：`EnableTraceEx2` MatchAnyKeyword 0→u64::MAX；去重窗口 History/legacy 1ms、Win32k 4ms；FlushTimer=1s+8KB 缓冲；瞬时 FPS 60 样本环（删 3000 帧均值+EMA）。
- Round 8：Win32k 窗口 4ms→2ms（依据是本机 etw-diag 实测 C9 每合成帧恰好 1 个）。

**此前会话声称的机制（未验证假设，可推翻）**
- "本机 Win11 无 0xAB/0xD7 PresentHistory；合成路径帧信号=0xC9，节奏=DWM 合成率≤刷新率（199Hz），所以 199-200 是'上屏帧率'的物理上限"——这个结论**可能是真相，也可能是给未定位到的 bug 找的解释**。
- 它建立在 `tools/fps-bench` 的诊断上，而 fps-bench 从后台 shell 启动、窗口可能从未真正获得前台（DWM 对无焦点窗口节流/合并 present），**"flip 全屏零逐帧事件"的结论被这个实验设计缺陷污染**。真实游戏（CS2）运行中的事件分布**从未抓到过**（两次尝试启动 CS2 均失败）。

**为什么不确定**
- 用户说"游戏 300-400 帧"——如果那是游戏内计数器/RTSS 的读数，说明渲染提交确实 300+，PresentMon 口径的事件应当存在；要么我们的订阅/过滤丢了它（keyword？level？provider？payload？），要么前台目标判定错了（读数其实来自别的进程——诊断时确实发生过：bench 没抢到前台，读数一直是终端进程的 200Hz 合成节奏）。
- 每次实测读数都是 ~200，与"读数来自某个 199Hz/200Hz 的无关进程"的假设**同样兼容**。目标进程切换链路（500ms 轮询+500ms 去抖+`GetForegroundWindow`）在全屏游戏下是否正常工作，从未验证。
- 2ms 窗口改动后用户尚未复测。

**你的待办（重新定位）**
1. **最高优先级：拿到用户游戏运行中的 `--etw-diag` 输出**（`C:\Program Files\FrameScope\FrameScopeHardwareService.exe --etw-diag 12`，游戏前台跑 12 秒）。看游戏 pid 下有没有高频逐帧事件（0xB8/0x10A/0xAB/0xD7/0xC9 各多少、帧间隔中位数多少）。这一份数据直接裁决"物理上限"vs"我们的 bug"。
2. 同时抓服务日志确认「前台进程 → xxx.exe」的切换是否正确（`log` 走 stderr，安装版日志在数据目录或需自建文件日志——如果取不到，给硬件服务加文件日志是合理改动）。
3. 若 diag 显示事件存在但读数仍 200：逐层排查（`tier_dedup` 的压制关系、TARGET 匹配、`SMOOTHED_FPS` 更新、服务 10Hz 节流、`sanitize_for_gdi` 取整）。用 `tools/poll_overlay.py <秒>` 直连管道看原始读数，配合 fps-bench 的 `borderless 300/600/1000` 档位做阶梯验证（**注意先把 bench 前台化问题修掉**：给 bench 加 `SetForegroundWindow` 之后仍要确认前台真的切过去了，或改成用户手动点击窗口再开始）。
4. 若 diag 显示真的没有逐帧事件：再考虑"上屏率口径"解释，但必须先排除订阅姿势问题——对照实验可用 PresentMon（先 `logman stop "NT Kernel Logger" -ets` 清掉可能复发的泄漏会话；PM 2.x 只在优雅停止时落盘 CSV，stdout 有缓冲坑）。

**验证标准**：游戏内游戏计数器 300FPS 时，OSD 读数 ≥250（或给出有数据支撑的口径结论并让用户认可）。

---

### 问题 3：OSD 的 1% / 0.1% 显示异常，数据不对

**已做尝试（均未确认生效）**
- Round 8：新增 `WINDOW_AVG_FPS`（10s 窗口平均，1Hz 更新），`overlay.rs` 把 `fps_1low/fps_01low` 钳到 ≤ 窗口平均（理由：瞬时 FPS 60 样本 vs low 10s/30s 跨窗口倒挂）。C9 窗口收紧也顺带影响 low 的帧源。

**为什么不确定**
- **用户看到的"不对"具体是什么形态从未被确认**：是 low 比 FPS 高？恒定不动？离谱的大值/小值？显示 0 或 "--"？没有截图没有数值。Round 8 的 clamp 只是修了"倒挂"这一种猜测。
- 其他候选真 bug 未排除：
  - low 1Hz 缓存 + `>0 才写入`：窗口变空/游戏停帧时旧值滞留（过期清零依赖 2s 无帧路径，但游戏在菜单/加载画面有零星合成帧时**不会**触发过期，旧 low 会长期挂着）；
  - 目标切换/过期清零依赖 generation bump，但 generation 只在有新帧到达时才清缓冲——菜单零星帧会以错误基准混入；
  - 合成+翻转混合流里 `collect_window` 的口径污染；
  - 显示层（GDI/竖排）对 `None` 的渲染。
- 单测（45/45）全部是纯逻辑测试，**没有一个覆盖"真实事件流顺序"下的 low 行为**。

**你的待办（重新定位）**
1. 先向用户要一张"显示不对"的 OSD 截图 + 当时的游戏场景描述（这是最省事的定位信息，别跳过）。
2. 用 `tools/fps-bench` 构造可控场景：恒速 300（low 应 ≈300）→ 切 60（low 应在 10s 内收敛到 ≈60）→ 切回 300（应恢复），用 `tools/poll_overlay.py` 记录曲线。若曲线与预期不符，问题在采集/统计层；若曲线正确，问题在显示层或用户场景特殊（如游戏菜单）。
3. 审查 low 的完整生命周期：计算 → 原子量 → 过期清零 → 目标切换清零 → overlay clamp → GDI/竖排渲染。重点：菜单零星帧期间的滞留旧值。

**验证标准**：fps-bench 阶跃场景下 low 收敛行为正确且曲线平滑；用户确认显示正常。

---

### 问题 4：淡化动画切页闪烁

**已做尝试（均未确认生效）**
- Round 7：背景 `<img>` 层 `translateZ(0)+willChange` 改为条件式（仅 fading/entering 期间，600ms 后由清理定时器撤销）；`animated-page.tsx` 的 `onAnimationComplete` 用 rAF 延后一帧移除 `.page-animating`。
- Round 8：**删除了 `.page-animating` 强制 `backdrop-filter:none` 的规则**（判定跳变本身是闪烁源）。动画期间玻璃改为继续采样（接受 0.4s 淡动画内的采样偏差）。

**为什么不确定**
- Round 8 的修复**完全没做过视觉验证**——改完直接打包交付，PrintWindow 静态截图无法验证时域闪烁。
- 机制分析可能不完整：闪烁可能有多重来源（背景 img 重光栅化只是其一；framer-motion 对页面容器的合成提升、玻璃卡的 backdrop 重建、交叉淡化两个背景层叠加等都可能参与）。
- **有历史前科的高风险点**：被删的 `.page-animating` 规则当年是为了修"动画期间玻璃先透明、动画结束瞬间突变"而加的。删掉它可能让旧问题回归（动画期间玻璃采样错误→视觉突变）。用户上一轮没提这个，但他在用的配置下未必暴露。
- 用户的复现条件（预设/图片背景 + 淡化模式 + 特定 blur 值 + 特定页面组合）没有记录，本机自动化复现闪烁（时域伪影）困难。

**你的待办（重新定位）**
1. **先复现**：配置预设背景 + 淡化切换模式（设置页），人工观察切页。必须真实眼睛看（录屏 60fps 逐帧看也行），静态截图无用。
2. 若闪烁仍在：恢复二分法定位——依次 (a) 注释背景层条件式 translateZ 的判断恢复常驻提升；(b) 恢复 `.page-animating` 跳变规则；(c) 把 `.page-animating` 改为只保留 blur 数值不变但禁用 transition。每步人工验证，锁定触发组合。
3. 若闪烁仍在且定位困难：尝试未试过的方案——背景 `<img>` 换成 `<canvas>`（drawImage 一次，canvas 是原生合成 quad，与"动态背景 video 不闪、mr WebGL 不闪"的用户线索完全对齐——这是最强的一条未被使用的线索）。
4. 若出现"动画期间玻璃变透明/突变"（旧问题回归）：回滚 Round 8 的规则删除，改为在动画期间保持上一次的 blur 函数值不变（禁用 transition 而不是关掉 filter）。

**验证标准**：用户配置下连续切页 20 次，肉眼零闪烁；且动画期间玻璃无透明/突变回归。

---

### 问题 5：普通液态玻璃圆角毛刺

**已做尝试（均未确认生效）**
- Round 7：删除三处 `backface-visibility:hidden`（index.css `[data-backdrop-filter]`、liquid-glass-button、sidebar 两容器）。
- Round 8：结构裁剪重构——`liquid-glass-card.tsx` 把 backdrop-filter 挪到 `zIndex:-1` 的 `::before`（无圆角），根元素保留 bg+borderRadius+overflow:hidden+zIndex:0（建立 stacking context，防止 -1 伪元素掉到页面级底层）；`sidebar.tsx` 容器去 translateZ、内层模糊层去 borderRadius+clipPath，靠容器绘制阶段裁剪。

**为什么不确定**
- Round 8 的重构只做了**静态截图粗验**（玻璃效果正常、背景透过+模糊可见），**圆角弧线的 AA 没有验证成**——验证时动态壁纸在动 + 坐标估计反复偏差，最终没有拿到一张弧线特写。
- 验证过程中踩过一个真坑：曾把玻璃色也挪进 ::before，结果蒙版失效（负 z 层的绘制顺序与 backdrop 采样交互），说明这套 DOM 结构对 Chromium 内部行为敏感，可能还有未发现的副作用。
- `LiquidGlassCard` 全站 186 处使用，结构改动（根新增 zIndex:0、overflow:hidden、children 堆叠关系变化）可能影响个别调用方（glow 层、_hover、绝对定位子元素、依赖 visible overflow 的内容）。
- `overflow:hidden` 会裁掉卡片内超出边界的内容——某些调用方可能依赖 visible（下拉、溢出角标）。
- blur=1（最差工作点）下是否改善未验证。

**你的待办（重新定位）**
1. 先向用户要圆角毛刺的**特写截图**（哪个页面哪张卡），拿到确切复现位置。
2. 亮色壁纸 + PrintWindow 抓图 + 定位卡片角精确坐标（自动扫描：找 borderColor 的跳变点）+ 8x 放大看弧线。对比对象：real 模式（无 backdrop-filter，理论上有 AA）。
3. blur=1 下复测（设置页滑块调到最小）。
4. 全站回归 `LiquidGlassCard` 的调用方：找依赖 `overflow: visible` 的卡片（下拉菜单、溢出徽标）、检查 hover 效果和内部绝对定位元素。
5. 若结构裁剪后仍有毛刺：下一层假设是 WebView2/Chromium 对 backdrop-filter 元素整体走非 AA 路径（与容器无关），需换思路（如 real 模式的多层 inset 阴影方案平替，或升级 WebView2 Runtime 测试）。

**验证标准**：亮色壁纸上 8x 放大弧线平滑无阶梯；全站抽查无布局回归；用户认可。

---

## 二、诊断工具（本轮新增，都在仓库里）

| 工具 | 用法 | 说明 |
|---|---|---|
| `tools/fps-bench` | `cargo build --release` 后 `fps-bench.exe <windowed\|borderless\|exclusive> <target_fps> [秒]`，0=不限帧 | D3D11 flip 信号源，窗口标题显示真实帧率；**已知缺陷：从后台启动可能拿不到前台（DWM 节流），实验前确认前台归属** |
| `tools/poll_overlay.py` | `python tools/poll_overlay.py <秒> > out.csv`（CSV 到 stdout） | 直连 `\\.\pipe\FrameScopeHardwareService` 每 200ms 记录 fps/1low/0.1low/present_mode，**不需要跑 UI** |
| `--etw-diag` | `FrameScopeHardwareService.exe --etw-diag [秒]`（需管理员） | 按 pid 打印 DxgKrnl/Win32k 全部事件 ID 分布 + 帧间隔中位数——**定位"事件到底发没发"的第一手段** |
| `tools/printwindow-capture.ps1` | 现有 | PrintWindow(PW_RENDERFULLCONTENT) 抓窗口实际呈现，无需前台 |

**环境事实（2026-09-14 实测）**：本机显示器 2560x1440@199Hz；Win11 24H2+；桌面 composited 应用（浏览器/webview/壁纸引擎）的 C9 节奏 = 199-200Hz；已清理 2 个泄漏的 "NT Kernel Logger" ETW 会话（会复发——强杀进程树就泄漏，`logman query -ets` 检查）。

## 三、本轮两次修复为什么失败的教训（避免重蹈覆辙）

1. **Round 7 失败主因**：根因分析建立在"读审计文档+读代码"上，没有先拿游戏实机数据。keyword 修复是对的（诊断确认 DxgKrnl 事件恢复了），但"封顶"的主导机制没找对。
2. **Round 8 失败主因**：诊断工具（fps-bench）自身有实验设计缺陷（后台窗口被 DWM 节流污染数据），从被污染的数据推出了"物理上限"结论；视觉问题（闪烁/圆角）的修复做完后没有真正验证就交付。
3. **共同教训**：静态截图验证不了时域问题（闪烁）；动态壁纸下单点像素采样验证不了 AA；"读数 200"与"读数来自错误的目标进程"两个假设当时没有区分开。

## 四、建议工作流

```
0. 读 HANDOFF.md 铁律（必读）+ 本文档
1. 逐问题：先拿数据（etw-diag / poll_overlay / 用户截图）→ 再定位 → 再改
2. 每个 UI 修复必须实机人工验证（dev 启动：desktop-preview 下 `npx vite --host 127.0.0.1 --port 1430` +
   `src-tauri/target/debug/framescope-ui-preview.exe`；注意单实例锁，先停 FrameScope 进程链）
3. 采集侧修复用 fps-bench 阶梯验证（先修好 bench 的前台化问题）
4. 全量测试基线：cargo test（hardware-service 45/45 + 宿主 5/5）、npx tsc --noEmit、
   npx vitest run（14/14）、npm run build
5. 打包三步：tools/Package-Installer.ps1 -StageOnly → npx tauri build（desktop-preview 下）→
   installer/build-installer.ps1 -SkipPayload；核验 exe 时间戳；静默安装 `...setup.exe /S`
6. 本地提交（不 push），更新 HANDOFF.md
```

## 五、关键文件地图（本五问题相关）

| 文件 | 内容 |
|---|---|
| `src/hardware-service/src/game_fps.rs` | ETW 采集 + 分层去重 + 瞬时/low 统计（Round 7 重写、Round 8 修正）；纯逻辑在模块顶层可单测 |
| `src/hardware-service/src/overlay.rs` | OverlayData 聚合 + low 钳制 + present_mode 字段 |
| `src/hardware-service/src/lib.rs` | 服务节流 10Hz + carry-forward 1s |
| `desktop-preview/src-tauri/src/overlay_panel.rs` | GDI OSD（窗口创建/WM_TIMER 周期置顶/NOACTIVATE/多显示器） |
| `desktop-preview/src-tauri/src/lib.rs` | 宿主 poller 10Hz + 竖排 OSD 创建/置顶线程 |
| `desktop-preview/src-tauri/src/backend.rs` | monitor_status（generating 相位转发） |
| `desktop-preview/src/components/special/liquid-glass-card.tsx` | 玻璃卡（Round 8 ::before 结构裁剪） |
| `desktop-preview/src/components/ui/sidebar.tsx` | 侧栏玻璃容器 |
| `desktop-preview/src/components/ui/main-layout.tsx` | 背景层（条件式 translateZ） |
| `desktop-preview/src/components/ui/animated-page.tsx` | 页面切换动画（.page-animating 的宿主） |
| `desktop-preview/src/index.css` | `.page-animating` 规则（Round 8 改为仅 .liquid-glass-skip-anim 生效，实际当前无人挂该类——相当于规则已停用） |
| `desktop-preview/src/pages/VerticalOverlayPage.tsx` | 竖排 OSD 渲染（legacy-exclusive 提示条） |

## 六、需要用户配合的事项（遇到就向用户要，不要自行假设）

1. 游戏运行中的 `--etw-diag` 输出（问题 2 的裁决性数据）。
2. 问题 3"显示不对"的截图与场景描述。
3. 问题 5 圆角毛刺的特写截图与所在页面。
4. 用户用的 OSD 形态（GDI 条形 or 竖排）与玻璃设置（模式/blur 值）。
5. 实机游戏验证结果（CS2 关垂直同步的读数、闪烁与圆角的肉眼判定）。
