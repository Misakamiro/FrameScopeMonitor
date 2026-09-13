# FrameScope 问题分析报告（2026-09-14）

**性质**：纯分析定位，**本报告未修改任何代码**。
**范围**：用户报告的 6 个问题（UI 4 项、OSD 帧数采集 1 项、OSD 独占全屏 1 项）。
**基线**：分支 `codex/nexbox-frontend-preview`，HEAD `90eca1a`，工作树干净。
**用户拍板的前提约束**（贯穿全文）：
1. FrameScope 是开源软件，**GPL-3.0 不构成障碍**，可直接复制开源仓库代码。
2. **OSD 的 FPS 采集必须走 ETW，绝不使用 PresentMon**——内核级反作弊（BattlEye / EAC / EA Javelin）会拒绝 PresentMon trace。ETW 常驻会话同时是 OSD 实时源与 PresentMon 被拒时的报告兜底源，这是该架构的立身之本。

---

## 摘要

| # | 问题 | 根因判定 | 置信度 | 改动量 |
|---|---|---|---|---|
| 1 | 普通液态玻璃圆角毛刺 | `backface-visibility: hidden` + 强制合成层提升 → 圆角走无抗锯齿裁剪路径 | 高 | 小 |
| 2 | 淡化切页闪烁（仅图片类背景） | 图片背景层被 `translateZ(0)` 钉成常驻合成层，与 `.page-animating` 的 backdrop-filter 离散跳变叠加 → 整屏大图重光栅化 | 高 | 小 |
| 3 | 透明玻璃在硬件页不生效 | `{...props}` 展开顺序覆盖组件样式 + 页面自建裸 Box 分支不认玻璃模式（**成片问题，14 处**） | 确认 | 中 |
| 4 | 监控读秒/进度条语义错误 | 进度条是时间驱动的假进度；**C# 侧真实进度信号已完整存在，卡在 Rust 未转发** | 确认 | 中 |
| 5 | OSD 帧数封顶 + 严重滞后 | 4ms 去重窗口硬顶 250FPS；Win32k 全局压制屏蔽高频源；10 秒滑动均值 + EMA → 端到端滞后 ≈12-13 秒 | 确认 | 大 |
| 6 | 独占全屏下 OSD 不显示 | **历史"架构限制"结论高估了边界**：窗口创建后 Z 序从不重申；现代 flip model 场景本可解决 | 高 | 小 |

**最关键的两个发现**：
- 问题 4 不需要新增任何 C# 上报逻辑——`FrameScopeReportProgress.cs` 早已产出真实百分比与 ETA，只是 Rust 的 `monitor_status` 从不读取，信号被整条掐断。
- 问题 6 的历史结论（"DirectX 独占全屏绕过 DWM，非代码 bug"）**只对传统独占全屏成立**。现代游戏多走 flip model 独立翻转，DWM 仍在链路中，而当前代码在窗口创建后**从不重申 Z 序**，这是可修的真 bug。

---

# 一、UI 问题

## 问题 1：普通液态玻璃下圆角毛刺 / 模糊

### 根因（高置信）

backdrop-filter 元素被强制提升为独立合成层，且带 `backface-visibility: hidden`，导致圆角走**无抗锯齿（non-AA）的 mask 裁剪路径**。

证据：

| 位置 | 代码 |
|---|---|
| `src/index.css:87-90` | `[data-backdrop-filter] { transform: translateZ(0); backface-visibility: hidden; }` |
| `src/components/special/liquid-glass-button.tsx:42-46` | 每个玻璃按钮 `transform: translateZ(0)` + `backfaceVisibility: "hidden"` |
| `src/components/ui/sidebar.tsx:677/685` | 容器 `translateY(-50%) translateZ(0)` + `backfaceVisibility: hidden` |

**为什么上一会话的 clipPath 实验像素 diff 为零**：`1723d19` 添加的 clipPath 与代码中既有的重复了——`sidebar.tsx:736` 已有 `clipPath: inset(0 round 1rem)`，`liquid-glass-card.tsx:59` 已有 `inset(0 round 0.75rem)`。更重要的是，**clip-path 在合成层上同样是硬边裁剪，本身不带 AA**，所以它在原理上就不可能是解法。

**blur=1 是最差工作点**：默认值在 `src/contexts/background-context.tsx:107`，滑块 `src/pages/SettingsPage.tsx:997` 的 `min={1}`。`blur(1px)` 仍完整走 backdrop 采样 + 合成管线（毛刺照常产生），却没有足够模糊量去掩盖边缘瑕疵。

**real 模式对照成立**（这是根因锁定在 backdrop-filter 合成路径的关键旁证）：`liquid-glass-card.tsx:50-52` 在 isReal 时设 `backdropFilter: "none"`，`:59` 也不生成 clipPath，改用 `index.css:276-295` 的 inset 阴影走普通绘制路径——圆角有完整 AA，用户也确实没报 real 模式的圆角问题。

### 修改方案（按收益排序）

1. **删除 `backface-visibility: hidden`**——这是 AA 丢失的主开关：`index.css:88-89`、`liquid-glass-button.tsx:44-45`、`sidebar.tsx:685/701`。
2. **改用结构裁剪**：外层父容器设 `borderRadius + overflow: hidden`（父容器不做合成层提升），内层纯 backdrop-filter 子元素不设圆角——让裁剪发生在父级的绘制阶段而非合成阶段。同时移除 `sidebar.tsx:736` 与 `liquid-glass-card.tsx:59` 的 clipPath（已证无效）。
3. **抬高最小模糊量**：`SettingsPage.tsx:997` `min={1}` → `min={6}`；`background-context.tsx:66/107` 默认 `1` → `12`。这是缓解而非根治，但能显著改善观感。

> 注意：方案 1 与问题 2 的方案 1 方向一致（都是撤销强制合成层提升），建议一并处理；但需同时验证背景竖向断层不复发（见问题 2）。

---

## 问题 2：淡化动画切页闪烁（仅预设/图片背景）

### 根因（高置信）

用户提供的「只有预设/图片背景闪、动态和 mr 背景不闪」是决定性线索。分界线在 `src/components/ui/main-layout.tsx`：

| 背景类型 | 渲染为 | 是否有合成层提升 |
|---|---|---|
| preset / image（`:243-273`） | `<img>` | **有**：`transform="translateZ(0)"`（`:255`）+ `willChange="opacity"`（`:256`） |
| dynamic（`:287`） | `<video>` | 无 |
| mr（`:306`） | `MRBackground` → WebGL canvas（`MRBackground.tsx:184` `getContext("webgl")`） | 无 |

`main-layout.tsx:254` 的注释说明了 `translateZ(0)` 的来历——**这正是历史上为消除 WebView2 整屏大图分块光栅化竖向接缝而加的那处**。

机制链条：`<video>` 与 WebGL canvas 天然是原生合成 quad，backdrop 快照重建时无需重新光栅化其纹理，故免疫。而 `<img>` 层被 `translateZ(0)` + `willChange` 钉成常驻合成层后，fade 的纯 opacity 动画使整页成为 backdrop root 的一部分；`index.css:97-101` 在动画起止把 `backdrop-filter` 在 `none ↔ blur()` 间**离散跳变**（CSS 无法在二者间插值），每次跳变迫使背景大图重新分块光栅化 → 闪一帧。

**已排除**：`main-layout.tsx:114` 的 effect 依赖（`PRESET_BACKGROUNDS` 是模块常量、`showPresetBg` 是 boolean）引用稳定，路由切换不会重跑该 effect——**图片没有被重新挂载或重新解码**。

### 验证方法

临时注释 `main-layout.tsx:255-256` 两行，若闪烁消失即确认根因。

### 修改方案

1. **首选**：把 `:255-256` 改为条件式——仅在交叉淡化期间（`layer.fading` 为真，或新层挂载后 600ms 内）施加 `transform/willChange`，静止时移除，不再常驻合成层。
2. **配套**：`src/components/ui/animated-page.tsx:111` 的 `onAnimationComplete` 里用 `requestAnimationFrame` 延后一帧再 `setAnimating(false)`，避免 backdrop-filter 恢复与合成层合并落在同一帧。
3. **若方案 1 导致竖向接缝回归**：改用 `contain: paint` 替代 `translateZ(0)`——可抑制接缝但不强制常驻合成层。

---

## 问题 3：透明液态玻璃在硬件信息页不生效

### 现状与根因（已确认）

玻璃模式字段 `liquidGlassMode: "normal" | "real"`：定义 `src/contexts/background-context.tsx:22`，默认 `"normal"`（`:67`、`:108`），持久化读 `:213-214`、写 `:331-332`；设置页下拉 `src/pages/SettingsPage.tsx:968-983`。

共享组件 `src/components/special/liquid-glass-card.tsx` 消费该字段：`:46` 算 `isReal`，`:66-69` 决定 `bg` 与 `borderColor`。real 模式下应为 `bg: "transparent"` / `borderColor: "rgba(255,255,255,0.06)"` / `backdropFilter: "none"`（`:50-53`），靠 `:82` 的 `real-liquid-glass` class（`index.css:276-295` 多层 inset 阴影）+ `:89-103` 四条边缘折射层出效果。

**直接原因（props 覆盖）**：`src/pages/HardwarePage.tsx:258`（StatCard）与 `:345`（DetailCard）向 `LiquidGlassCard` 传了 `borderColor="white"`。而 `liquid-glass-card.tsx:86` 把 `{...props}` 展开在 `{...cardStyles}`（`:83`）**之后**——调用方的 props 覆盖了组件算好的 real 模式样式。结果 real 模式下卡片得到一圈不透明白边 + 透明背景，视觉上就是「没变成透明玻璃」。

**第二个原因（自建裸 Box 分支）**：`HardwarePage.tsx:378` 只取 `liquidGlassEnabled`，**没取 `liquidGlassMode`**；`:252` 与 `:339` 用 `if (liquidGlassEnabled)` 做二分，false 分支（`:265-275`、`:356-370`）是硬编码 `bg={cardBg}` + `borderColor="white"` 的裸 `Box`，完全不认玻璃模式。

### 全量清单（同类问题：只读 `liquidGlassEnabled` + 自建裸 Box，共 14 处）

`src/pages/CpuSchedulerPage.tsx:950`、`NetworkOptimizerPage.tsx:1033/1104/1177/1214/1323/1360`、`AntiCheatOptimizePage.tsx:385`、`GameProcessOptimizePage.tsx:748/1048`、`SteamPage.tsx:792`、`DeltaForcePage.tsx:252/649/1237`、`DeltaForceRoulettePage.tsx:111`、`src/components/AnnouncementCard.tsx:209`。

另：`src/hooks/use-floating-button-style.ts` 派生的悬浮按钮（`HardwarePage.tsx:390-392` 硬编码 rgba）同样不随模式变。

### 改动清单

1. `liquid-glass-card.tsx:80-87`：把 `{...props}` 移到 `{...cardStyles}` **之前**；或在 real 模式下显式剔除调用方传入的 `bg` / `borderColor` / `backdropFilter`。**仅此一处即可修复硬件页两类卡片。**
2. `HardwarePage.tsx:258`、`:345`：删掉 `borderColor="white"`（视觉由组件统一给）。
3. `HardwarePage.tsx:252-276`、`:339-371`：删掉 `if (liquidGlassEnabled)` 二分，统一只渲染 `LiquidGlassCard`——组件内部 `:66` 已处理关闭态回落 `defaultBg`，该二分既冗余又是漏跟随的根源。同步删 `:209/:221/:287/:297` 的 `liquidGlassEnabled` 参数与 `:378` 的取值。
4. 清单中其余 14 处按同一套路收敛。**建议抽一个 `useGlassSurfaceStyle()` hook**，返回 `{bg, borderColor, backdropFilter, className}`，供不便换组件的裸 `Box` 直接展开，从根上杜绝硬编码 rgba 再次出现。

---

## 问题 4：启动监控的读秒与进度条设计错误

### 用户期望（重述）

- a) 启动监控阶段的「读秒」取消；进度条保留但**默认为空、不跑动**；
- b) 游戏结束、进入「合成报告 / 生成图表」阶段时，进度条才开始跑动并显示秒数；
- c) 语义：秒数 = 预计还有几秒完成报告合成；进度条 = 报告合成进度。

### 现状

读秒与进度条都在 `src/pages/HomePage.tsx`：
- 进度条 `:193`：`value={state.monitor.elapsedSeconds / state.config.captureDurationSeconds * 100}`，`busy` 期间恒显示，`isIndeterminate={stopping}`。
- 读秒 `:195`：`{state.monitor.elapsedSeconds} 秒`，`:197` 拼 `phaseLabels[phase]` → 截图里的「0 秒 · 等待目标进程」。

相位枚举在 `src/preview/pages/shared.tsx:90-93`：`idle / waiting / capturing / stopping / generating / completed / error`。`elapsedSeconds` 在真机路径由 `src/preview/runtime/state.ts:149` 从 `monitor_status` 灌入，来源是 `desktop-preview/src-tauri/src/backend.rs:524-529`——**用 runDir 目录名时间戳算的墙钟已用时**。

**所以当前进度条是纯时间驱动的假进度，且分母是采集时长，与报告合成毫无关系。** 用户判断「这个设计本身错误」完全正确。

### 关键发现：真实进度信号已完整存在，卡在 Rust 未转发

C# 侧**早已产出**真实进度：

| 位置 | 内容 |
|---|---|
| `src/core/FrameScopeReportProgress.cs:15-34` | `CreateFields` 产出 `ReportProgressPhase` / `ReportProgressPercent` / `ReportProgressEtaSeconds` / `ReportProgressMessage`；**`EtaSeconds` 正是「预计还剩几秒」**（`:18-23` 按已用时/百分比外推） |
| `src/reporting/FrameScopeReportGenerator.cs:57/77/125/174/225/306` | 分阶段真实百分比 5→20→35→55→80→100（读取数据/处理数据/处理进程/降采样/生成图表/完成） |
| `src/app/FrameScopeNativeMonitor.ReportProcess.cs:25` | 落盘 `runDir/report-progress.json` |
| `src/app/FrameScopeNativeMonitor.ReportStatus.cs:67/131/165` | 合并进 `status.json` |

**缺口在 Rust**：`desktop-preview/src-tauri/src/backend.rs:494` 的 `monitor_status` 只读 watcher 状态文件，只返回 `idle` / `waiting` / `capturing` 三种相位（`:508`、`:533-540`、`:542`），**从不读 runDir 下的 `status.json` / `report-progress.json`，也从不返回 `generating`**。因此前端的 `stopping` / `generating` 在真机路径几乎走不到（只有 `state.ts:104` 的浏览器模拟时钟会走），合成进度信号被整条掐断。

### 改动清单

**后端（只需 Rust 转发，C# 无需新增任何上报点）**

`backend.rs:533-540` 的 active 分支之后、`:542` 的 `waiting` 兜底之前，新增：读取 `runDir/report-progress.json`（或 `status.json` 合并后的同名字段），若存在且 `ReportProgressPercent < 100`，返回：

```json
{ "phase": "generating",
  "reportPercent": <ReportProgressPercent>,
  "reportEtaSeconds": <ReportProgressEtaSeconds>,
  "reportMessage": <ReportProgressPhase> }
```

runDir 从 `ActiveMonitors[].RunDir` 取；采集结束后 watcher 条目可能已清空，需回退到扫描数据根目录下最新 runDir——**C# 侧已有等价实现可直接参照**：`FrameScopeReportProgress.FindLatestEffectiveStatus`（`core/FrameScopeReportProgress.cs:68-114`，含 10/30 分钟新鲜度判定）。

**前端**

1. `src/preview/runtime/types.ts:51` monitor 结构新增 `reportPercent: number | null`、`reportEtaSeconds: number | null`。
2. `src/preview/runtime/state.ts:131` invoke 泛型补上新字段；`:158` 附近增加 `status.phase === "generating"` 分支，写入 `monitor.phase` 与两个新值。
3. `HomePage.tsx:193`：改为仅 `phase === "generating"` 时进度条取 `value={monitor.reportPercent ?? 0}`；其余 busy 阶段渲染 `value={0}` 的空进度条，**去掉 `isIndeterminate`**（否则停止阶段又会跑动）。
4. `HomePage.tsx:194-198`：删掉 `{elapsedSeconds} 秒`；改为仅 `generating` 阶段显示「预计还需 {reportEtaSeconds} 秒」，其余阶段只显示 `phaseLabels[phase]`。

**不建议的替代方案**：按帧数 / `HardwareSampleRows` 估算合成耗时——`ReportProgressEtaSeconds` 已是现成真值，估算方案精度更差且要读同一批文件，不划算。

---

# 二、OSD 帧数采集问题（核心）

## 问题 5：帧数无法超过屏幕刷新率 + 刷新极慢

### 5.1 当前链路全图（每一跳的延迟/节流）

| # | 跳 | 位置 | 延迟/节流 |
|---|---|---|---|
| 1 | ETW 会话（DxgKrnl + Win32k，Verbose，**keyword=0**） | `src/hardware-service/src/game_fps.rs:286-298`（GUID）、`:700-733`（EnableTraceEx2，MatchAnyKeyword=0） | 内核缓冲 64KB×8~64，`:413` 仅 REAL_TIME **无 FlushTimer** → 缓冲填满才投递，低事件率下可达**数百 ms ~ 数秒** |
| 2 | 同帧去重 + 信源优先级 | `:441-481` | Win32k 去重窗口 **4ms → 硬上限 250 FPS**（`:330`） |
| 3 | 环形缓冲 3000 帧 + **全窗口平均** | `:162-213`、`:621-622` | `average_fps()` 对整个缓冲区求平均；300FPS 下 3000 帧 = **10 秒滑动窗口** |
| 4 | EMA α=0.2 | `:629` | 叠加在已平滑的 10s 均值之上 |
| 5 | 服务端 1Hz 节流缓存 | `src/hardware-service/src/lib.rs:15` `DEFAULT_THROTTLE_MS=1000`、`:230-240` | ≤1000ms |
| 6 | overlay_cache carry-forward | `lib.rs:264` `OVERLAY_FRESH=5s`、`:271-277`、`overlay.rs:334-346` | fps 为 null 时继承旧值，最长 **5s** |
| 7 | 宿主 1Hz poller | `desktop-preview/src-tauri/src/lib.rs:673-692` | ≤1000ms |
| 8 | GDI 悬浮条 500ms 重绘 | `overlay_panel.rs:1460-1463`、`:1589` | ≤500ms（竖排 OSD 走 emit → `VerticalOverlayPage.tsx:400`） |

### 5.2 封顶根因（已确认）

**不是被刷新率封顶，是被 4ms 去重窗口封顶。**

`game_fps.rs:330` 把 Win32k 合成事件（`0xC9`）的去重窗口设为 `WIN32K_FRAME_WINDOW_TICKS = 40_000` ticks = **4ms**，`:449-456` 对间隔 <4ms 的事件一律丢弃。**4ms → 上限恰好 250 FPS**，与 240Hz 显示器观感一致，所以「看起来像」被刷新率封顶。

**放大机制**：`:459-477` 的信源优先级规定——只要 500ms 内出现过 Win32k 事件，**所有** DxgKrnl Present/Flip 事件都被丢弃。窗口化/无边框游戏恒有 Win32k 合成事件 → 真正的高频 Present 源被整条屏蔽，帧率只能由 4ms 节奏的合成事件决定。代码注释（`:327`）自称支持 10000 FPS，那只对 `SAME_FRAME_WINDOW_TICKS = 0.1ms` 路径成立，Win32k 路径享受不到。

**次要因素**：`EnableTraceEx2` 的 MatchAnyKeyword 传 **0**（`:705-708`、`:722-725`）。ETW 内核匹配式为 `EventKeyword == 0 || (EventKeyword & MatchAnyKeyword)`，而 DxgKrnl 的 present 事件均带 keyword —— 传 0 会**整类静默丢弃**，进一步把统计推向 Win32k 合成源。

### 5.3 滞后根因（已确认）

主因是**统计窗口而非轮询**：`average_fps()`（`:198-213`）对整个 3000 帧缓冲求平均，300FPS 下即 **10 秒**窗口；从 100 → 300 FPS 需约 10 秒才能完全替换掉旧帧。`:629` 的 EMA 再叠一层。`buffer.clear()` 只在**目标进程切换**时触发（`:601-610`），同进程内场景变化不清空。

端到端最坏滞后累加：

```
缓冲区 10s 窗口收敛      ≈ 10000ms   ← 主导项
EMA α=0.2 附加            + 若干帧
ETW 内核缓冲无 FlushTimer + 数百 ms
服务端 1Hz 节流           + 1000ms
宿主 1Hz poller           + 1000ms
GDI 500ms 重绘            + 500ms
────────────────────────────────────
最坏端到端               ≈ 12.5 ~ 13 秒
```

稳态可见滞后约 10-11 秒，与用户描述的「游戏 300-400 帧、OSD 还显示 100」完全吻合。

**保持旧值逻辑**：`lib.rs:271-277` + `overlay.rs:334-346` 的 carry-forward 在 fps 为 null 时继承旧值（上限 5s）；`game_fps.rs:885-895` 的 3s 过期归零**只在无匹配帧时**生效。前台判定：`:848-860` 500ms 轮询 + `:857` 500ms 去抖 + `:906` PID 变化才 `refresh_processes()` → 切游戏最坏 **~1s** 才更新目标，期间旧值继续显示。

---

## 5.4 tubatools 方案评估

### 项目概况

| 项 | 值 |
|---|---|
| 名称 | 图吧工具箱 CE（TubaWinUi3） |
| 语言 | C# 为主（5.86M），WinUI 3 / .NET 10 |
| 许可证 | GPL-3.0（**按用户拍板，非障碍**） |
| 活跃度 | 3954 star / 106 fork / 62 open issues；提交集中在 2026-08 ~ 2026-09；最新 release v1.6.1（2026-09-07） |
| 性质 | 装了 82 款第三方工具的大型工具箱，FPS 只是其中「游戏监控」一个模块 |

FPS 相关代码仅两个文件：`TubaWinUi3.WinUI3/Services/FpsService.cs`、`FpsTracker.cs`（另有 `DxTraceDiag/Program.cs` 诊断工具、`FpsServiceTests.cs` 单测）。

### 技术路线判定：**ETW，非 Hook**（代码实读确认）

`FpsService.Start()` 建的是常驻 ETW 会话：

```csharp
var session = new TraceEventSession("TubaWinUi3_FPS");
session.EnableProvider(DxgKrnlProviderId);   // 802EC45A-...-87C98277BA9D
session.EnableProvider(Win32kProviderId);    // 8C416C79-...-E56D3AA8234C
```

`FpsTracker.cs` 中 `FpsPresentEvents` 明确注明事件 ID「verified against PresentMon's Microsoft_Windows_DxgKrnl ETW header」。全仓库检索 hook / inject / detour / minhook **无任何命中**（`aida_fps.dll` 属捆绑的第三方 AIDA64 工具目录，与本模块无关）。

**结论：与 FrameScope 现有方案同路线，都是 DxgKrnl ETW。它不是新路线，是同一条路线的更好实现——完美契合「OSD 必须走 ETW、不用 PresentMon」的既定架构。反作弊风险：低（纯 ETW 消费，不注入被测进程）。**

### 它凭什么不封顶、不滞后（三处工程修正，均有代码自述）

**(1) 事件 ID 全集**（`FpsTracker.cs:21-31`，常量类 `FpsPresentEvents`）

| ID | 语义 | 层 |
|---|---|---|
| `0xAB` PresentHistory_Start / `0xD7` PresentHistoryDetailed_Start | 现代路径，全模式通用 | **Tier1** |
| `0xC9` Win32k TokenCompositionSurfaceObject | DWM 合成帧（窗口化/无边框） | **Tier2** |
| `0xB8` Present（全屏/legacy）、`0xA6` Blt_Info、`0x74` MMIOFlip_Info、`0x103` MMIOFlip_MPO、`0x182` MMIOFlip_MPO3、`0xA8` Flip_Info、`0xFC` FlipMultiPlaneOverlay、`0x10A` IndependentFlip | 内核/MPO 兜底 | **Tier3** |

旧实现只监听 `0xB8`（全屏/内核归属 present），无边框窗口游戏走 PresentHistory / MPO 路径，帧根本收不到。

> 注：`DxTraceDiag.cs:28,32` 的诊断集合额外含 `0xAC`、`0x12D`，但**生产集合不含**——移植以 `FpsPresentEvents` 为准。

**(2) 分层权威源去重算法**（`FpsTracker.cs:60-81`），执行顺序严格如下：

1. 先**无条件**更新 `LastHistoryTicks`(0xAB/0xD7) 或 `LastWin32kTicks`(0xC9)——**即使该事件随后被丢弃也更新**（`:62-65`）；
2. 同帧去重：`ticks - LastPresentTicks < dupWindow` 则丢。`dupWindow` = 0xC9 用 **4ms**、其余用 **1ms**（`:35,38,69-70`）。比较基准是**全局** `LastPresentTicks`（上一个已计入的帧），非各层各自的 last；
3. 非 History 事件，若 `ticks - LastHistoryTicks < 500ms` → 丢（`:73-74`）；
4. **仅 Tier3 的 8 个 ID**，若 `ticks - LastWin32kTicks < 500ms` → 丢（`:75-77`）。

`ModeWindowTicks = 500ms`（`:41`）。

> **与 FrameScope 的根本差异**：tubatools 里 `0xAB/0xD7` **凌驾于** `0xC9` 之上，且 `0xC9` 不压制 Tier1；而 FrameScope `:459-477` 是「有 0xC9 就屏蔽全部 DxgKrnl」。这是封顶的核心差异点。

**(3) 瞬时 FPS**（`:92,190-194`）：`SampleCount = 60` 环形缓冲，`fps = (count-1) / (最新ticks - 最旧ticks)`。300FPS 下窗口仅 **200ms**。**无 EMA**。帧时间有效区间 `[1ms, 10s)`（`:175`）→ 上限 1000 FPS，不封顶。

**(4) 1% / 0.1% low**（`:112-118, 239-298`）：绝对时间窗口 **10s / 30s**，门槛 **≥100 / ≥900 帧**（窗口内帧数，非会话累计）；缓冲存 `(绝对ticks, frameTime)` 容量 **16384**，从最新往回遍历遇过期即 `break`（`:249-254`）。口径 = 升序排序取最慢 `ceil(n*p)` 帧的**平均帧时间**再取倒数（`:289-297`）。实时读数要求窗口被时间填满否则返回 -1（`:280`），快照/报告不要求（`:317-318`）。

> 旧实现 1% low / 0.1% low 共用固定 2048 帧窗口，60fps 下 ≈34 秒——一次卡顿要泡满 34 秒才滚干净，这正是「刷新特别慢」的自述根因。

**(5) provider 参数**：`FpsService.cs:293-294` 调 `EnableProvider(guid)` **无参**，TraceEvent 默认 `Verbose(5) + matchAnyKeywords = ulong.MaxValue`；`DxTraceDiag.cs:67-68` 显式写死 `Verbose, ulong.MaxValue`。→ **全 keyword**，与 FrameScope 传 0 形成直接对比。

**(6) 进程归属**（`FpsService.cs:354-367`）：用 ETW header 的 `data.ProcessID` 建 per-pid tracker；排除自身 pid、pid 0/4、`Excluded` 名单（`:32-44`）。读数目标另走 `GetForegroundWindow → GetWindowThreadProcessId`（`:152-165`），且**刻意不做**「取 FPS 最高进程」兜底（`:121-124` 注释：桌面 1Hz 进程会抢赢，导致 OSD 卡在 1FPS）。进程名缓存 TTL 10s 防 pid 复用（`:381`）。

**(7) TraceEvent 库替 C# 做了什么（Rust 需自实现）**
- QPC → `DateTime.Ticks` 换算：**全部算法常量都是 100ns 单位**（`TimeSpan.TicksPerMillisecond = 10000`）。Rust 侧若用原始 QPC ticks，**必须按 `QueryPerformanceFrequency` 归一，否则所有窗口值错位**——这是移植的首要风险项。
- 实时会话生命周期、`ProcessTrace` 循环、同名残留会话清理（`:387-395`）。
- **关键好消息**：tubatools 只用 `ev.ID` / `ev.ProcessID` / `ev.TimeStamp` 三个 header 字段，**完全不解析 payload** → Rust 侧无需 TDH / manifest schema 解析，直接读 `EVENT_RECORD.EventHeader.{EventDescriptor.Id, ProcessId, TimeStamp}` 即可。这是移植可行性的基石。

> 另注（推测，需实测）：`GameOverlayAutoService.cs:175` 的 UI 采样默认 `Refresh = 1000ms`、下限 200ms。即 tubatools 自身的显示滞后可能也来自 UI 轮询层——印证 FrameScope 下游链路同样需要一并调整（见 5.6）。

### 路线选择：**推荐路线 A（Rust 内重写算法）**

**路线 B（直接引入 C# 组件）不可行**：
- tubatools 是 .NET 10 + file-scoped namespace / primary constructor / `or` pattern / collection expression，而 FrameScope 的 csc 4.0.30319 **只支持 C#5**，编译直接失败，降级改写量 ≈ 重写；
- 若引入 .NET 10 SDK 则增加分发体积与目标机运行时依赖；
- 若做独立采集进程则需**第二个 ETW 会话**，与「单一常驻会话兼做 OSD 源 + 报告兜底」的架构立身之本冲突，且双倍会话开销。

**路线 A 成本可控**：算法本体是纯整数/浮点逻辑约 200 行，无 Win32 依赖；`game_fps.rs` 已有完整 ETW 骨架，只改事件集合、去重、统计三块。且 `FpsServiceTests.cs` 的 30 个单测（`:146-237` 专测分层优先级四组合、`:349-478` 测时间窗口）可**直接逐条翻译成 Rust `#[test]`**，是现成的移植验收资产。

---

## 5.5 `game_fps.rs` 改造清单

| # | 位置 | 改成 | 解决症状 |
|---|---|---|---|
| 1 | `:311`、`:321` 事件常量 | 扩展到上表 11 个 ID | 无边框/MPO 游戏帧源缺失 |
| 2 | `:330` `WIN32K_FRAME_WINDOW_TICKS=40_000` | 拆成 `SAME_FRAME=10_000`(1ms) 与 `WIN32K_SAME_FRAME=40_000`(4ms)，**按事件 ID 选择**；比较基准用全局 `last_present` | **解除 250FPS 硬顶** → 1000FPS |
| 3 | `:459-477` 优先级 | 换成 tubatools 四步算法（含「0xC9 不压制 0xAB/0xD7」），`MODE_WINDOW=500ms` | 窗口化下高频 Present 源被整条屏蔽 |
| 4 | `:162-213` FrameTimeBuffer | 改 `(ticks, frame_time)` × 16384 + 绝对时间剪枝；新增 60 样本环形算瞬时 FPS `(n-1)/duration` | 10 秒滑动窗口滞后 |
| 5 | `:629` EMA α=0.2 | **删除** | 二次滞后 |
| 6 | `:700-733` EnableTraceEx2 | `MatchAnyKeyword = u64::MAX`，`Level = 5` | provider 订阅了却收不到事件 |
| 7 | `:413` LogFileMode | 加 `FlushTimer = 1`（秒粒度，最小 1s）；**同时把 BufferSize 降到 4-8KB** | 低事件率下投递延迟数百 ms~数秒。**单靠 FlushTimer 压不到亚秒，小 buffer 才是关键** |
| 8 | 帧时间过滤 | `[0.001, 10.0)` 秒 | 双源假帧污染 Avg/Max/low |
| 9 | `:1029-1064` low 计算 | 10s/30s 绝对窗口 + 100/900 门槛 + 最慢帧**平均帧时间取倒数** | low 值滞后且口径高估 |

## 5.6 下游链路配套改动

| 层 | 现值 | 建议 | 理由 |
|---|---|---|---|
| 服务端节流 `lib.rs:15` | 1Hz | **10Hz (100ms)** | 瞬时 FPS 窗口已是 200ms@300fps，10Hz 不浪费 |
| carry-forward TTL `lib.rs:264` | 5s | **1s**（对齐 tubatools 的 2s 无帧归零，`FpsTracker.cs:330`） | 5s 会让停帧后读数糊 5 秒 |
| 宿主 poller `src-tauri/src/lib.rs:673-692` | 1Hz | **10Hz (100ms)** | 同上 |
| GDI 重绘 `overlay_panel.rs:1460-1463` | 500ms | **200-250ms** | 人眼读数 4-5Hz 足够；GDI 重绘是 CPU 大头，**不要跟到 10Hz** |

端到端 ≈ 250ms + 200ms 窗口 **< 0.5 秒**。

**CPU 关键权衡**：16384 帧排序**不可按 10Hz 跑**——把 1%/0.1% low 计算单独降到 **1-2Hz 并缓存**，只让瞬时 FPS 走 10Hz。ETW 回调本身事件驱动、无轮询成本。

## 5.7 兜底路径改动点清单（换链时必须同步修改）

这条路径是「PresentMon 被反作弊拒绝时报告的唯一数据来源」，**换 OSD 采集链必须同步核对**：

1. **取值源**：`src/hardware-service/src/game_fps.rs:1029-1064` `get_cached_fps / get_cached_fps_1low / get_cached_fps_01low`（公共 API 契约，**新链需保持同签名**）
2. **服务聚合**：`src/hardware-service/src/lib.rs:91-113` `fps_sample()`（含 5s 重试逻辑）→ `lib.rs:270` `build_overlay_data`
3. **字段落位**：`src/hardware-service/src/overlay.rs:26-28`（结构体）、`:300-302`（赋值 + 1 位小数舍入）、`:344-346`（carry-forward）
4. **管道 → 宿主**：`desktop-preview/src-tauri/src/lib.rs:673-692`（1Hz poller）；`hardware_service.rs:88`（null 快照契约）、`:327`（字段白名单，含 `fps / fps_1low / fps_01low`）
5. **CSV 第 17 列写入**：`src/monitoring/FrameScopeHardwareServiceClient.cs:131-142`（Header，`"Fps"` 为第 17 列）、`:199-213`（1Hz RecordLoop）、`:215-239`（`AppendRow`，`Text(data,"fps")`）
6. **CSV 读取**：`src/reporting/FrameScopeReportGenerator.SystemData.cs:42` `Fps = ParseNullableDouble(Get(row,h,"Fps"))`
7. **帧序列合成**：`SystemData.cs:111-121` `SynthesizeFramesFromHardwareSamples`——**注意 `:117` 有 `>1000` 硬过滤，新链支持更高帧率时必须放宽**
8. **报告入口与标注**：`FrameScopeReportGenerator.cs:66-75`（触发分支）、`:109-110`（诊断字段）、`:181-182`（`etw-fps-sampled` 状态与文案）
9. **路径定义**：`src/app/FrameScopeNativeMonitor.MonitorSession.Paths.cs:30`、`MonitorSession.cs:347`

> **额外建议**：CSV 的 1Hz 采样率对兜底报告偏低。既然 OSD 链路提到 10Hz，可考虑兜底 CSV 同步提到 2-5Hz，让 `etw-fps-sampled` 报告的帧序列更接近真实分布（需权衡 CSV 体积）。

## 5.8 验证方法

- **不封顶**：最可靠的是自写 D3D11 空场景 `Present(0,0)` 不限帧循环（轻松 1000+FPS）；游戏侧用 CS2 低画质关垂直同步。验收标准：读数跟随到 300/600/1000，**不停在 250**。
- **对照基准**：RTSS / MSI Afterburner。PresentMon 仅作**离线对照**，不进生产路径。
- **低滞后（阶跃响应）**：用可切换限帧的程序做 60→300 瞬切，测 OSD 读数到达 90% 的时间，**目标 <500ms**；反向 300→60 同测。
- **分层算法**：全屏独占 / 无边框 / 窗口化三种模式各跑一遍，确认 `0xAB` 路径不再被 `0xC9` 屏蔽。建议照 `DxTraceDiag` 同款逻辑做一个 Rust 版诊断子命令（12 秒抓取、按 pid 打 ID 分布与帧间隔中位数），这是定位「事件到底发没发」的最快手段。
- **反作弊验证（必做）**：在 EAC / BattlEye / Javelin 游戏下确认 ETW 会话仍收到事件——这是整个架构选型的前提，需实机确认而非假设。
- **回归**：翻译 `FpsServiceTests.cs:146-237`（四个分层测试）+ `:349-478`（五个时间窗口测试）为 Rust 单测。

## 5.9 移植风险

1. **QPC 单位换算错位**会让所有窗口常量失效——**首要验证项**。
2. 16384 容量在 1000FPS 下仅 16 秒，**低于 0.1% low 的 30s 窗口**——若要支持 1000FPS 需扩到 32768。
3. 移除 EMA 后读数会更跳。若观感不佳，应**加大 SampleCount 而非恢复 EMA**。

---

# 三、问题 6：独占全屏下 OSD 不显示

## 6.1 现状核实

**GDI 悬浮条**（`desktop-preview/src-tauri/src/overlay_panel.rs`）
- `:568` `CreateWindowExW(WS_EX_TOPMOST | WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT, ..., WS_POPUP)`——**创建时一次性置顶**
- `:1535-1542` 唯一的 `SetWindowPos(HWND_TOPMOST, ..., SWP_NOACTIVATE|SWP_FRAMECHANGED)`，但**仅在拖动模式切换分支内**，不是周期性重置顶
- `:1460-1463` `WM_TIMER` 500ms 只做 `repaint_overlay`，**不重申 Z 序**
- **无 `WS_EX_NOACTIVATE`**（靠 `WS_EX_TRANSPARENT` + `WS_EX_TOOLWINDOW` 规避焦点）

**竖排 Tauri WebView OSD**（`desktop-preview/src-tauri/src/lib.rs`）
- `:75` `.decorations(false).transparent(true).always_on_top(true).skip_taskbar(true)` + `.shadow(false)`
- `:91`、`:127-137` 依赖 vendored-tao 的 `WS_EX_NOREDIRECTIONBITMAP`，运行时只切换 `WS_EX_LAYERED|WS_EX_TRANSPARENT`
- 同样**无周期性置顶、无 NOACTIVATE**

**全局**：代码库中**不存在任何全屏模式检测**（grep `fullscreen` / `独占` / `exclusive` 仅命中注释）。

## 6.2 架构限制的准确边界——历史结论高估了

| 模式 | DWM 是否在链路中 | TOPMOST 是否有效 |
|---|---|---|
| **传统 exclusive fullscreen**（`SetFullscreenState` + BltModel） | 否，交换链独占扫描输出 | **确实失效**。但现代游戏已很少走这条路 |
| **Flip Model 独立翻转**（Win10 1709+ 主流） | **是**，只是把游戏 surface 直接提交给 MPO 平面 | **有效**——任意可见的 TOPMOST 分层窗口与游戏区域重叠，就会令 DWM 取消 independent flip、回退合成，OSD 反而能显示（代价是游戏帧率/延迟受损） |

**FrameScope 为何没触发降级**：`WS_EX_NOREDIRECTIONBITMAP`（`lib.rs:91`）与 GDI 窗口的 `WS_EX_LAYERED` 组合下，窗口不产生参与合成的重定向位图，DWM 可继续把它放到独立 MPO overlay 平面而非强制回合成；加之窗口创建后 Z 序**从不重申**（`:1460`），游戏 `SetFullscreenState` 抢占前台后 OSD 被**永久压到下层**。

> 即：历史结论「非代码 bug」只对传统独占全屏成立。对现代 flip model 场景，**这是可修的真 bug**。

## 6.3 方案清单

| 方案 | 成本 | 反作弊风险 | 帧率影响 | 权限 |
|---|---|---|---|---|
| **A. 周期性 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)`**（挂在已有 500ms `WM_TIMER`）+ 补 `WS_EX_NOACTIVATE` | 极低（~20 行） | 无 | 无 | 无 |
| B. 主动触发独立翻转降级（去掉 NOREDIRECTIONBITMAP / 强制合成重叠） | 低 | 无 | **负面，掉帧 5-15%** | 无 |
| C. DXGI/D3D Hook 注入 | 高 | **高，封号** | 低 | 无 |
| D. Magnification API / DirectComposition / Game Bar 覆盖层 | 中 | 低 | 中 | 无 |
| **E. ETW 检测 + UI 引导** | **极低** | 无 | 无 | 已有 admin |

**方案 D 的实情**：Magnification API 同样在 DWM 层，对真独占无效；Xbox Game Bar 覆盖层无第三方公开 API；NVIDIA / AMD **不提供**公开 overlay SDK。这条基本不可行。

**方案 E 的实现（边际成本最低，与问题 5 的改造天然协同）**：`game_fps.rs:321` 已定义并订阅 `INDEPENDENT_FLIP = 0x010A`（`:550-563` 的 `is_present` 匹配已包含），但目前**仅当作帧计数 tick，模式信息被丢弃**（`record_present` `:446-` 只区分信源优先级）。同理 `:311` `PRESENT = 0xB8` 的注释即写明「传统/全屏独占」。

因此只需在回调里按 EventId 记录 per-pid 的「最近模式」：
- `0xB8` / `0xA8`(Flip) 活跃 → 传统独占
- `0x010A` 活跃 → 独立翻转
- `0xC9`(Win32k) 活跃 → 窗口化 / 无边框

**无需 TdhGetProperty 解析 payload，零新增 ETW 开销，复用常驻 `FrameScope_FPS` 会话。** 辅助校验：窗口矩形 == 显示器分辨率 且 无 `WS_OVERLAPPEDWINDOW`。

## 6.4 推荐结论

1. **P0 — A + E 组合**：先上周期性重置顶（覆盖大量被误判为「独占」的无边框/独立翻转场景，预计可解决多数案例），同时用 ETW EventId 做模式判定，**仅在确认为 `0xB8` 传统独占时**才在 UI 提示「切换到无边框窗口模式」。两者合计改动小、零反作弊风险、零帧率代价。
2. **P1 — B 设为用户可选开关**（「强制显示 OSD（可能降低帧率）」），默认关闭。
3. **不采纳 C**（违背既定的避开 D3D Hook 策略）**与 D**（无可用公开机制）。

**多显示器注意**：`overlay_panel.rs:583`、`:559` 用 `SM_CXSCREEN`（仅主屏），OSD 置于副屏时宽度钳制与默认居中均会错位。做方案 A 时建议一并按 `MonitorFromWindow` 取实际显示器矩形。

---

# 四、实施优先级建议

| 优先级 | 项目 | 理由 |
|---|---|---|
| **P0** | 问题 5（FPS 采集链重写） | 影响产品核心价值，改动最大，建议独立成批并配齐单测 |
| **P0** | 问题 6 方案 A（周期性重置顶） | ~20 行，零风险，可能直接解决多数「独占全屏」报障 |
| **P1** | 问题 4（进度条语义） | 后端信号已就绪，只差 Rust 转发，性价比极高 |
| **P1** | 问题 3（玻璃模式成片失效） | 一处组件修复即解决硬件页；其余 14 处可渐进收敛 |
| **P2** | 问题 1 + 问题 2（合成层提升） | 二者同源，建议合并处理并一起回归背景竖向断层 |
| **P2** | 问题 6 方案 E（独占全屏检测 + 引导） | 与问题 5 的 ETW 改造天然协同，建议同批实施 |

**建议的分批**：
- 批 1：问题 5 + 问题 6（ETW 层统一改造，含方案 A/E）
- 批 2：问题 4 + 问题 3（数据链路与组件契约）
- 批 3：问题 1 + 问题 2（合成层与渲染路径，需像素级回归验证）

---

## 附：待确认事项

1. 问题 2 的根因需用「注释 `main-layout.tsx:255-256` 两行」的方式做一次实测确认。
2. 问题 5 的反作弊验证（EAC / BattlEye / Javelin 下 ETW 是否仍供数）必须实机确认，这是整个架构选型的前提。
3. 问题 6 方案 A 的实际效果需在真实「独占全屏」游戏下验证——预期能解决 flip model 场景，但无法解决传统独占全屏（那部分只能靠方案 E 的检测与引导）。
