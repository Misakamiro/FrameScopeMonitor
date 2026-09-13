# 会话交接文档：六问题分析完成，待实施

**会话日期**: 2026-09-14
**分支**: `codex/nexbox-frontend-preview`，HEAD `90eca1a`，**工作树干净，本会话未改任何代码**
**本会话产出**: `docs/audit/2026-09-14-issue-analysis.md`（完整分析报告）+ 本交接文档
**交接目的**: 下一会话按本文档实施。分析已做完，**不要重新调研，直接实施**。

---

## 零、给接手者的三句话

1. 用户报告了 6 个问题，**根因已全部定位到 文件:行号 级别**，方案已给出，详见 `docs/audit/2026-09-14-issue-analysis.md`。本文档是实施指南，报告是证据与细节。
2. **两条用户拍板的硬约束**（贯穿全部实施，不要再质疑或提问）：
   - FrameScope 是**开源软件，GPL-3.0 不构成障碍**，可以直接复制开源仓库代码。
   - **OSD 的 FPS 采集必须走 ETW，绝不使用 PresentMon**。原因：内核级反作弊（BattlEye / EAC / EA Javelin）会拒绝 PresentMon 的 trace。ETW 常驻会话同时是 OSD 实时数据源、以及 PresentMon 被拒时报告的兜底数据源——**这是该架构的立身之本**。不要提议改用 PresentMon 2.x 实时流。
3. 实施前**必读** `docs/preview/HANDOFF.md` 的铁律章节（26 条），本文档第五节只列本轮特别相关的。

---

## 一、六个问题总览

| # | 问题 | 根因一句话 | 置信度 | 改动量 | 建议批次 |
|---|---|---|---|---|---|
| 1 | 普通液态玻璃圆角毛刺 | `backface-visibility: hidden` + 合成层提升 → 圆角走无抗锯齿裁剪路径 | 高 | 小 | 批 3 |
| 2 | 淡化切页闪烁（仅图片类背景） | 图片背景层被 `translateZ(0)` 钉成常驻合成层，与 `.page-animating` 的 backdrop-filter 离散跳变叠加 | 高 | 小 | 批 3 |
| 3 | 透明玻璃在硬件页不生效 | `{...props}` 展开顺序覆盖组件样式 + 14 处页面自建裸 Box 不认玻璃模式 | 确认 | 中 | 批 2 |
| 4 | 监控读秒/进度条语义错误 | 假进度；**C# 真实进度信号已存在，卡在 Rust 未转发** | 确认 | 中 | 批 2 |
| 5 | OSD 帧数封顶 + 滞后 12 秒 | 4ms 去重窗口硬顶 250FPS；Win32k 全局压制屏蔽高频源；10s 滑动均值 + EMA | 确认 | 大 | 批 1 |
| 6 | 独占全屏下 OSD 不显示 | **历史"架构限制"结论高估了边界**；窗口创建后 Z 序从不重申 | 高 | 小 | 批 1 |

**建议分批**（同批内根因同源，便于一起回归）：
- **批 1 = 问题 5 + 问题 6**：ETW 层统一改造。问题 6 的检测方案复用问题 5 的事件 ID 解析，天然协同。
- **批 2 = 问题 4 + 问题 3**：数据链路与组件契约，互不干扰。
- **批 3 = 问题 1 + 问题 2**：二者同源（都是撤销强制合成层提升），**必须一起做并一起回归背景竖向断层**。

---

## 二、批 1：OSD FPS 采集链重写 + 独占全屏（P0）

### 2.1 背景：为什么要重写

用户反馈：OSD 帧数**上不去屏幕刷新率**（实际游戏 300-400 帧时显示不上去）、**刷新极慢**（游戏已 300-400 帧，OSD 还显示之前的 100 帧）。用户判断"NexBox 原来的采集链不可用"，点名要换成 `https://github.com/luolangaga/tubatools` 的方案。

**调研结论：tubatools 的 FPS 模块就是 DxgKrnl ETW 路线**（全仓库检索 hook/inject/detour/minhook 无任何命中），不碰 PresentMon，**完全符合用户的架构约束**。它不是新路线，是同一条路线的更好实现。

### 2.2 路线选择：**Rust 内重写算法**（已决策，不要推翻）

**不要**把 tubatools 的 C# 代码直接引入，原因：
- tubatools 是 .NET 10 + file-scoped namespace / primary constructor / `or` pattern / collection expression，而本项目的 **csc 4.0.30319 只支持 C#5**，编译直接失败，降级改写量 ≈ 重写；
- 引入 .NET 10 SDK 会增加分发体积与目标机运行时依赖；
- 做独立采集进程则需**第二个 ETW 会话**，与"单一常驻会话兼做 OSD 源 + 报告兜底"的架构冲突，且双倍开销。

**改造现有 `src/hardware-service/src/game_fps.rs`**。算法本体是纯整数/浮点逻辑约 200 行，无 Win32 依赖，ETW 骨架已有。

**关键可行性依据**：tubatools 只用 ETW header 的 `ev.ID` / `ev.ProcessID` / `ev.TimeStamp` 三个字段，**完全不解析 payload** → Rust 侧无需 TDH / manifest schema 解析，直接读 `EVENT_RECORD.EventHeader.{EventDescriptor.Id, ProcessId, TimeStamp}` 即可。

### 2.3 需要从 tubatools 抄的算法（源码坐标）

抓取方式：`curl -sL https://raw.githubusercontent.com/luolangaga/tubatools/master/TubaWinUi3.WinUI3/Services/FpsTracker.cs`（同理 `FpsService.cs`、`DxTraceDiag/Program.cs`、`FpsServiceTests.cs`）。

**(1) 事件 ID 全集**（`FpsTracker.cs:21-31`，常量类 `FpsPresentEvents`）

| ID | 语义 | 层 |
|---|---|---|
| `0xAB` PresentHistory_Start / `0xD7` PresentHistoryDetailed_Start | 现代路径，全模式通用 | **Tier1** |
| `0xC9` Win32k TokenCompositionSurfaceObject | DWM 合成帧（窗口化/无边框） | **Tier2** |
| `0xB8` Present(全屏/legacy)、`0xA6` Blt_Info、`0x74` MMIOFlip_Info、`0x103` MMIOFlip_MPO、`0x182` MMIOFlip_MPO3、`0xA8` Flip_Info、`0xFC` FlipMultiPlaneOverlay、`0x10A` IndependentFlip | 内核/MPO 兜底 | **Tier3** |

> `DxTraceDiag.cs:28,32` 的诊断集合额外含 `0xAC`、`0x12D`，但**生产集合不含**——以 `FpsPresentEvents` 为准。

**(2) 分层权威源去重算法**（`FpsTracker.cs:60-81`），执行顺序严格如下，**不要改动顺序**：

1. 先**无条件**更新 `LastHistoryTicks`(0xAB/0xD7) 或 `LastWin32kTicks`(0xC9)——**即使该事件随后被丢弃也要更新**（`:62-65`）；
2. 同帧去重：`ticks - LastPresentTicks < dupWindow` 则丢。`dupWindow` = 0xC9 用 **4ms**、其余用 **1ms**（`:35,38,69-70`）。**比较基准是全局 `LastPresentTicks`（上一个已计入的帧），不是各层各自的 last**；
3. 非 History 事件，若 `ticks - LastHistoryTicks < 500ms` → 丢（`:73-74`）；
4. **仅 Tier3 的 8 个 ID**，若 `ticks - LastWin32kTicks < 500ms` → 丢（`:75-77`）。

`ModeWindowTicks = 500ms`（`:41`）。

> **与现有实现的根本差异**：tubatools 里 `0xAB/0xD7` **凌驾于** `0xC9` 之上，`0xC9` 不压制 Tier1；而 `game_fps.rs:459-477` 是"有 0xC9 就屏蔽全部 DxgKrnl"。**这是封顶的核心差异点。**

**(3) 瞬时 FPS**（`:92,190-194`）：`SampleCount = 60` 环形缓冲，`fps = (count-1) / (最新ticks - 最旧ticks)`。300FPS 下窗口仅 200ms。**无 EMA**。帧时间有效区间 `[1ms, 10s)`（`:175`）→ 上限 1000FPS。

**(4) 1% / 0.1% low**（`:112-118, 239-298`）：绝对时间窗口 **10s / 30s**，门槛 **≥100 / ≥900 帧**（窗口内帧数，非会话累计）；缓冲存 `(绝对ticks, frameTime)` 容量 **16384**，从最新往回遍历遇过期即 `break`（`:249-254`）。口径 = 升序排序取最慢 `ceil(n*p)` 帧的**平均帧时间**再取倒数（`:289-297`）。实时读数要求窗口被时间填满否则返回 -1（`:280`），快照/报告不要求（`:317-318`）。

**(5) provider 参数**：`FpsService.cs:293-294` 的 `EnableProvider(guid)` 无参 → TraceEvent 默认 `Verbose(5) + matchAnyKeywords = ulong.MaxValue`；`DxTraceDiag.cs:67-68` 显式写死 `Verbose, ulong.MaxValue`。→ **全 keyword**。

**(6) 进程归属**（`FpsService.cs:354-367`）：用 ETW header 的 `ProcessID` 建 per-pid tracker；排除自身 pid、pid 0/4、`Excluded` 名单（`:32-44`）。读数目标另走 `GetForegroundWindow → GetWindowThreadProcessId`（`:152-165`），且**刻意不做"取 FPS 最高进程"兜底**（`:121-124` 注释：桌面 1Hz 进程会抢赢，导致 OSD 卡在 1FPS）。进程名缓存 TTL 10s 防 pid 复用（`:381`）。

### 2.4 `game_fps.rs` 改造清单（逐项照做）

| # | 位置 | 改成 | 解决症状 |
|---|---|---|---|
| 1 | `:311`、`:321` 事件常量 | 扩展到上表 11 个 ID | 无边框/MPO 游戏帧源缺失 |
| 2 | `:330` `WIN32K_FRAME_WINDOW_TICKS=40_000` | 拆成 `SAME_FRAME=10_000`(1ms) 与 `WIN32K_SAME_FRAME=40_000`(4ms)，**按事件 ID 选择**；比较基准用全局 `last_present` | **解除 250FPS 硬顶** → 1000FPS |
| 3 | `:459-477` 优先级 | 换成 2.3(2) 的四步算法（含"0xC9 不压制 0xAB/0xD7"），`MODE_WINDOW=500ms` | 窗口化下高频 Present 源被整条屏蔽 |
| 4 | `:162-213` FrameTimeBuffer | 改 `(ticks, frame_time)` × 16384 + 绝对时间剪枝；新增 60 样本环形算瞬时 FPS `(n-1)/duration` | 10 秒滑动窗口滞后 |
| 5 | `:629` EMA α=0.2 | **删除** | 二次滞后 |
| 6 | `:700-733` EnableTraceEx2 | `MatchAnyKeyword = u64::MAX`，`Level = 5` | provider 订阅了却收不到事件 |
| 7 | `:413` LogFileMode | 加 `FlushTimer = 1`（秒粒度，最小 1s）；**同时把 BufferSize 降到 4-8KB** | 低事件率下投递延迟数百 ms~数秒。**单靠 FlushTimer 压不到亚秒，小 buffer 才是关键** |
| 8 | 帧时间过滤 | `[0.001, 10.0)` 秒 | 双源假帧污染 Avg/Max/low |
| 9 | `:1029-1064` low 计算 | 10s/30s 绝对窗口 + 100/900 门槛 + 最慢帧**平均帧时间取倒数** | low 值滞后且口径高估 |

**现状根因备查**（改之前理解为什么）：
- **封顶不是刷新率造成的**，是 `:330` 的 4ms 去重窗口 → 上限恰好 250 FPS，与 240Hz 显示器观感一致所以容易误判。放大机制是 `:459-477` 的 Win32k 全局压制：窗口化/无边框游戏恒有 `0xC9` 事件 → 高频 Present 源被整条屏蔽。
- **滞后主因是统计窗口不是轮询**：`average_fps()`（`:198-213`）对整个 3000 帧缓冲求平均，300FPS 下 = 10 秒窗口；`:629` 的 EMA 再叠一层。`buffer.clear()` 只在目标进程切换时触发（`:601-610`），同进程内场景变化不清空。
- 端到端最坏滞后 ≈ 12.5-13 秒：`10000ms(缓冲窗口) + EMA + 数百ms(ETW无FlushTimer) + 1000ms(服务节流) + 1000ms(宿主poller) + 500ms(GDI重绘)`。

### 2.5 下游链路配套改动

| 层 | 位置 | 现值 | 建议 | 理由 |
|---|---|---|---|---|
| 服务端节流 | `src/hardware-service/src/lib.rs:15` `DEFAULT_THROTTLE_MS` | 1Hz | **10Hz (100ms)** | 瞬时 FPS 窗口已是 200ms@300fps |
| carry-forward TTL | `lib.rs:264` `OVERLAY_FRESH` | 5s | **1s**（对齐 tubatools 的 2s 无帧归零，`FpsTracker.cs:330`） | 5s 会让停帧后读数糊 5 秒 |
| 宿主 poller | `desktop-preview/src-tauri/src/lib.rs:673-692` | 1Hz | **10Hz (100ms)** | 同上 |
| GDI 重绘 | `overlay_panel.rs:1460-1463` | 500ms | **200-250ms** | 人眼读数 4-5Hz 足够；**不要跟到 10Hz** |

目标端到端 ≈ 250ms + 200ms 窗口 **< 0.5 秒**。

> **CPU 关键权衡（必须遵守）**：16384 帧排序**不可按 10Hz 跑**——把 1%/0.1% low 计算单独降到 **1-2Hz 并缓存**，只让瞬时 FPS 走 10Hz。ETW 回调本身事件驱动、无轮询成本。这是游戏中运行的监控软件，性能预算是硬指标（现基线：监控全链 ~0.8-1.1% CPU / ~340MB，改后需复测）。

### 2.6 兜底路径改动点清单（**换链时必须同步核对，不可遗漏**）

这条路径是"PresentMon 被反作弊拒绝时报告的唯一数据来源"，是架构立身之本：

1. **取值源**：`src/hardware-service/src/game_fps.rs:1029-1064` `get_cached_fps / get_cached_fps_1low / get_cached_fps_01low`（公共 API 契约，**新链需保持同签名**）
2. **服务聚合**：`src/hardware-service/src/lib.rs:91-113` `fps_sample()`（含 5s 重试）→ `lib.rs:270` `build_overlay_data`
3. **字段落位**：`src/hardware-service/src/overlay.rs:26-28`（结构体）、`:300-302`（赋值 + 1 位小数舍入）、`:344-346`（carry-forward）
4. **管道 → 宿主**：`desktop-preview/src-tauri/src/lib.rs:673-692`（poller）；`hardware_service.rs:88`（null 快照契约）、`:327`（字段白名单，含 `fps / fps_1low / fps_01low`）
5. **CSV 第 17 列写入**：`src/monitoring/FrameScopeHardwareServiceClient.cs:131-142`（Header，`"Fps"` 为第 17 列）、`:199-213`（1Hz RecordLoop）、`:215-239`（`AppendRow`，`Text(data,"fps")`）
6. **CSV 读取**：`src/reporting/FrameScopeReportGenerator.SystemData.cs:42`
7. **帧序列合成**：`SystemData.cs:111-121` `SynthesizeFramesFromHardwareSamples`——**注意 `:117` 有 `>1000` 硬过滤，新链支持更高帧率时必须放宽**
8. **报告入口与标注**：`FrameScopeReportGenerator.cs:66-75`（触发分支）、`:109-110`（诊断字段）、`:181-182`（`etw-fps-sampled` 状态与文案）
9. **路径定义**：`src/app/FrameScopeNativeMonitor.MonitorSession.Paths.cs:30`、`MonitorSession.cs:347`

> **可选优化**：CSV 的 1Hz 采样率对兜底报告偏低。既然 OSD 链路提到 10Hz，可考虑兜底 CSV 同步提到 2-5Hz，让 `etw-fps-sampled` 报告的帧序列更接近真实分布（需权衡 CSV 体积）。**这项属增强，非必须，建议与用户确认后再做。**

### 2.7 问题 6：独占全屏 OSD 不显示

**现状核实**：
- GDI 悬浮条 `overlay_panel.rs:568` 创建时带 `WS_EX_TOPMOST`，**但窗口创建后 Z 序从不重申**——`:1460-1463` 的 `WM_TIMER` 500ms 只做 `repaint_overlay`；`:1535-1542` 唯一的 `SetWindowPos(HWND_TOPMOST)` 仅在拖动模式切换分支内。**无 `WS_EX_NOACTIVATE`**。
- 竖排 Tauri WebView OSD `desktop-preview/src-tauri/src/lib.rs:75` `.always_on_top(true)`，`:91`/`:127-137` 依赖 vendored-tao 的 `WS_EX_NOREDIRECTIONBITMAP`。同样无周期性置顶、无 NOACTIVATE。
- 代码库中**不存在任何全屏模式检测**（grep `fullscreen`/`独占`/`exclusive` 仅命中注释）。

**历史结论需要修正**：此前"DirectX 独占全屏绕过 DWM，非代码 bug"的结论**只对传统 exclusive fullscreen 成立**。现代游戏多走 **Flip Model 独立翻转**（Win10 1709+ 主流），此时 **DWM 仍在链路中**，TOPMOST 本该有效。FrameScope 之所以不显示，是因为 `WS_EX_NOREDIRECTIONBITMAP` + `WS_EX_LAYERED` 让 DWM 可以把 OSD 放到独立 MPO 平面而不强制回退合成，加之 Z 序从不重申，游戏 `SetFullscreenState` 抢占前台后 OSD 被**永久压到下层**。**对 flip model 场景这是可修的真 bug。**

**实施（P0 = A + E 组合）**：

- **方案 A（先做，约 20 行，零风险）**：把 `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` 挂到已有的 500ms `WM_TIMER` 上做周期性重置顶；补 `WS_EX_NOACTIVATE`。预计可解决多数被误判为"独占"的无边框/独立翻转场景。
  - **多显示器注意**：`overlay_panel.rs:583`、`:559` 用 `SM_CXSCREEN`（仅主屏），OSD 置于副屏时宽度钳制与默认居中均会错位。做方案 A 时**一并按 `MonitorFromWindow` 取实际显示器矩形**。
- **方案 E（与批 1 的 ETW 改造天然协同，边际成本近零）**：`game_fps.rs:321` 已定义并订阅 `INDEPENDENT_FLIP = 0x010A`（`:550-563` 的 `is_present` 匹配已包含），但目前**仅当作帧计数 tick，模式信息被丢弃**。在回调里按 EventId 记录 per-pid 的"最近模式"：`0xB8`/`0xA8` 活跃 → 传统独占；`0x010A` 活跃 → 独立翻转；`0xC9` 活跃 → 窗口化/无边框。**无需解析 payload，零新增 ETW 开销。** 辅助校验：窗口矩形 == 显示器分辨率 且 无 `WS_OVERLAPPEDWINDOW`。仅在确认为 `0xB8` 传统独占时，才在 UI 提示"切换到无边框窗口模式"。

**P1（可选，需用户拍板）**：方案 B = 主动触发独立翻转降级（去掉 NOREDIRECTIONBITMAP / 强制合成重叠），代价是**游戏掉帧 5-15%**，建议做成默认关闭的用户开关"强制显示 OSD（可能降低帧率）"。

**不采纳**：D3D Hook 注入（违背既定的避开 Hook 策略，有封号风险）；Magnification API / Game Bar / 厂商 overlay SDK（Magnification 同样在 DWM 层对真独占无效；Game Bar 无第三方公开 API；NVIDIA/AMD 不提供公开 overlay SDK）。

### 2.8 批 1 验证方法

- **不封顶**：自写 D3D11 空场景 `Present(0,0)` 不限帧循环（轻松 1000+FPS）；游戏侧用 CS2 低画质关垂直同步。**验收标准：读数跟随到 300/600/1000，不停在 250。**
- **对照基准**：RTSS / MSI Afterburner。**PresentMon 仅作离线对照，不进生产路径。**
- **低滞后（阶跃响应）**：用可切换限帧的程序做 60→300 瞬切，测 OSD 读数到达 90% 的时间，**目标 <500ms**；反向 300→60 同测。
- **分层算法**：全屏独占 / 无边框 / 窗口化三种模式各跑一遍，确认 `0xAB` 路径不再被 `0xC9` 屏蔽。**建议照 `DxTraceDiag` 同款逻辑做一个 Rust 版诊断子命令**（12 秒抓取、按 pid 打 ID 分布与帧间隔中位数）——这是定位"事件到底发没发"的最快手段，强烈建议先做这个再改算法。
- **反作弊验证（必做）**：在 EAC / BattlEye / Javelin 游戏下确认 ETW 会话仍收到事件——**这是整个架构选型的前提，需实机确认而非假设**。本机可用 CS2（VAC）与 BF6（Javelin，Steam 版 rungameid/2807960）。
- **兜底链路回归**：造一次 PresentMon 被拒场景（BF6），确认仍能出 `etw-fps-sampled` 的 full 报告。
- **单测**：把 `FpsServiceTests.cs:146-237`（四个分层优先级测试）+ `:349-478`（五个时间窗口测试）**逐条翻译成 Rust `#[test]`**——这是现成的移植验收资产，别自己凭空编测试。
- **性能复测**：监控全链 CPU/内存需回到 ~0.8-1.1% / ~340MB 量级。

### 2.9 批 1 已知风险

1. **QPC 单位换算错位**——tubatools 全部算法常量是 **100ns 单位**（`TimeSpan.TicksPerMillisecond = 10000`）。Rust 侧若用原始 QPC ticks，**必须按 `QueryPerformanceFrequency` 归一，否则所有窗口值全部错位**。**这是首要验证项**，建议先写个单测锁死换算。
2. 16384 容量在 1000FPS 下仅 16 秒，**低于 0.1% low 的 30s 窗口**——若要支持 1000FPS 需扩到 32768。
3. 移除 EMA 后读数会更跳。若观感不佳，**应加大 SampleCount 而非恢复 EMA**。

---

## 三、批 2：进度条语义 + 玻璃模式（P1）

### 3.1 问题 4：启动监控的读秒与进度条

**用户期望（原话转述）**：
- a) 启动监控阶段的「读秒」**取消**；进度条保留但**默认为空、不跑动**；
- b) 游戏结束、进入「合成报告 / 生成图表」阶段时，进度条才开始跑动并显示秒数；
- c) 语义：秒数 = **预计还有几秒完成报告合成**；进度条 = **报告合成进度**。

**现状**：`src/pages/HomePage.tsx:193` 进度条 `value={state.monitor.elapsedSeconds / state.config.captureDurationSeconds * 100}`，`busy` 期间恒显示，`isIndeterminate={stopping}`；`:195` 读秒 `{state.monitor.elapsedSeconds} 秒`，`:197` 拼 `phaseLabels[phase]` → 截图里的「0 秒 · 等待目标进程」。`elapsedSeconds` 由 `src/preview/runtime/state.ts:149` 从 `monitor_status` 灌入，源头是 `desktop-preview/src-tauri/src/backend.rs:524-529`——**用 runDir 目录名时间戳算的墙钟已用时**。即纯时间驱动的假进度，分母还是采集时长，与报告合成毫无关系。**用户说"这个设计本身错误"是对的。**

**★ 关键发现：真实进度信号已完整存在，C# 一行都不用改**

| 位置 | 内容 |
|---|---|
| `src/core/FrameScopeReportProgress.cs:15-34` | `CreateFields` 产出 `ReportProgressPhase` / `ReportProgressPercent` / `ReportProgressEtaSeconds` / `ReportProgressMessage`；**`EtaSeconds` 正是"预计还剩几秒"**（`:18-23` 按已用时/百分比外推） |
| `src/reporting/FrameScopeReportGenerator.cs:57/77/125/174/225/306` | 分阶段真实百分比 5→20→35→55→80→100（读取数据/处理数据/处理进程/降采样/生成图表/完成） |
| `src/app/FrameScopeNativeMonitor.ReportProcess.cs:25` | 落盘 `runDir/report-progress.json` |
| `src/app/FrameScopeNativeMonitor.ReportStatus.cs:67/131/165` | 合并进 `status.json` |

**缺口在 Rust**：`desktop-preview/src-tauri/src/backend.rs:494` 的 `monitor_status` 只读 watcher 状态文件，只返回 `idle`/`waiting`/`capturing` 三种相位（`:508`、`:533-540`、`:542`），**从不读 runDir 下的 `status.json` / `report-progress.json`，也从不返回 `generating`**。前端的 `stopping`/`generating` 在真机路径几乎走不到（只有 `state.ts:104` 的浏览器模拟时钟会走）。**信号被整条掐断。**

**改动清单**：

**后端（只需 Rust 转发）**——`backend.rs:533-540` 的 active 分支之后、`:542` 的 `waiting` 兜底之前，新增：读取 `runDir/report-progress.json`（或 `status.json` 合并后的同名字段），若存在且 `ReportProgressPercent < 100`，返回：
```json
{ "phase": "generating",
  "reportPercent": <ReportProgressPercent>,
  "reportEtaSeconds": <ReportProgressEtaSeconds>,
  "reportMessage": <ReportProgressPhase> }
```
runDir 从 `ActiveMonitors[].RunDir` 取；**采集结束后 watcher 条目可能已清空，需回退到扫描数据根目录下最新 runDir**——C# 侧已有等价实现可直接参照：`FrameScopeReportProgress.FindLatestEffectiveStatus`（`core/FrameScopeReportProgress.cs:68-114`，含 10/30 分钟新鲜度判定）。

**前端**：
1. `src/preview/runtime/types.ts:51` monitor 结构新增 `reportPercent: number | null`、`reportEtaSeconds: number | null`。
2. `src/preview/runtime/state.ts:131` invoke 泛型补上新字段；`:158` 附近增加 `status.phase === "generating"` 分支。
3. `HomePage.tsx:193`：仅 `phase === "generating"` 时进度条取 `value={monitor.reportPercent ?? 0}`；其余 busy 阶段渲染 `value={0}` 的空进度条，**去掉 `isIndeterminate`**（否则停止阶段又会跑动）。
4. `HomePage.tsx:194-198`：删掉 `{elapsedSeconds} 秒`；仅 `generating` 阶段显示「预计还需 {reportEtaSeconds} 秒」，其余阶段只显示 `phaseLabels[phase]`。

> **不要**改成按帧数 / `HardwareSampleRows` 估算合成耗时——`ReportProgressEtaSeconds` 已是现成真值，估算精度更差且要读同一批文件。

**验证**：跑一次真实采集（CS2 或 cmd.exe 测试目标），观察：启动后进度条空、无读秒；游戏退出后进入 generating，进度条按 5→20→35→55→80→100 跑动，秒数递减。

### 3.2 问题 3：透明液态玻璃在硬件页不生效

玻璃模式字段 `liquidGlassMode: "normal" | "real"`：定义 `src/contexts/background-context.tsx:22`，默认 `"normal"`（`:67`、`:108`），持久化读 `:213-214` / 写 `:331-332`；设置页下拉 `src/pages/SettingsPage.tsx:968-983`。

**根因 1（props 覆盖）**：`src/pages/HardwarePage.tsx:258`（StatCard）与 `:345`（DetailCard）向 `LiquidGlassCard` 传了 `borderColor="white"`。而 `src/components/special/liquid-glass-card.tsx:86` 把 `{...props}` 展开在 `{...cardStyles}`（`:83`）**之后**——调用方 props 覆盖了组件算好的 real 模式样式（`:50-53` 本应是 `bg: transparent` / `borderColor: rgba(255,255,255,0.06)` / `backdropFilter: none`）。结果 real 模式下卡片是一圈不透明白边 + 透明背景，看起来就是"没变成透明玻璃"。

**根因 2（自建裸 Box 分支）**：`HardwarePage.tsx:378` 只取 `liquidGlassEnabled`，**没取 `liquidGlassMode`**；`:252` 与 `:339` 用 `if (liquidGlassEnabled)` 二分，false 分支（`:265-275`、`:356-370`）是硬编码 `bg={cardBg}` + `borderColor="white"` 的裸 `Box`，完全不认玻璃模式。

**改动清单**：
1. `liquid-glass-card.tsx:80-87`：把 `{...props}` 移到 `{...cardStyles}` **之前**；或在 real 模式下显式剔除调用方传入的 `bg`/`borderColor`/`backdropFilter`。**仅此一处即可修复硬件页两类卡片。**
2. `HardwarePage.tsx:258`、`:345`：删掉 `borderColor="white"`。
3. `HardwarePage.tsx:252-276`、`:339-371`：删掉 `if (liquidGlassEnabled)` 二分，统一只渲染 `LiquidGlassCard`——组件内部 `:66` 已处理关闭态回落 `defaultBg`，该二分既冗余又是漏跟随的根源。同步删 `:209/:221/:287/:297` 的参数与 `:378` 的取值。
4. **全量清单（同类问题，14 处）**：`src/pages/CpuSchedulerPage.tsx:950`、`NetworkOptimizerPage.tsx:1033/1104/1177/1214/1323/1360`、`AntiCheatOptimizePage.tsx:385`、`GameProcessOptimizePage.tsx:748/1048`、`SteamPage.tsx:792`、`DeltaForcePage.tsx:252/649/1237`、`DeltaForceRoulettePage.tsx:111`、`src/components/AnnouncementCard.tsx:209`。另 `src/hooks/use-floating-button-style.ts` 派生的悬浮按钮（`HardwarePage.tsx:390-392` 硬编码 rgba）同样不随模式变。
5. **建议**：抽一个 `useGlassSurfaceStyle()` hook 返回 `{bg, borderColor, backdropFilter, className}`，供不便换组件的裸 `Box` 直接展开，从根上杜绝硬编码 rgba 再次出现。

> 第 4 项 14 处可渐进收敛，**优先保证硬件页（用户实际报的）先好**。

---

## 四、批 3：合成层与渲染路径（P2，二者同源必须一起做）

### 4.1 问题 1：普通液态玻璃圆角毛刺

**根因**：backdrop-filter 元素被强制提升为独立合成层，且带 `backface-visibility: hidden`，圆角走**无抗锯齿（non-AA）的 mask 裁剪路径**。

证据：`src/index.css:87-90` `[data-backdrop-filter] { transform: translateZ(0); backface-visibility: hidden; }`；`src/components/special/liquid-glass-button.tsx:42-46`；`src/components/ui/sidebar.tsx:677/685`。

**★ 为什么上一会话的 clipPath 实验像素 diff 为零**：`1723d19` 加的 clipPath 与既有的重复了——`sidebar.tsx:736` 早有 `inset(0 round 1rem)`，`liquid-glass-card.tsx:59` 早有 `inset(0 round 0.75rem)`。**而且 clip-path 在合成层上同样是硬边裁剪、本身不带 AA，原理上就不可能是解法。不要再试这个方向。**

**blur=1 是最差工作点**：默认值 `background-context.tsx:107`，滑块 `SettingsPage.tsx:997` `min={1}`。`blur(1px)` 仍完整走 backdrop 采样 + 合成管线（毛刺照常产生），却没有足够模糊量掩盖边缘瑕疵。

**real 模式对照成立**（根因锁定在 backdrop-filter 合成路径的关键旁证）：`liquid-glass-card.tsx:50-52` isReal 时 `backdropFilter: "none"`、`:59` 也不生成 clipPath，改用 `index.css:276-295` 的 inset 阴影走普通绘制路径——圆角有完整 AA，用户也确实没报 real 模式的圆角问题。

**改动方案（按收益排序）**：
1. **删除 `backface-visibility: hidden`**（AA 丢失的主开关）：`index.css:88-89`、`liquid-glass-button.tsx:44-45`、`sidebar.tsx:685/701`。
2. **改用结构裁剪**：外层父容器 `borderRadius + overflow: hidden`（父容器**不做**合成层提升），内层纯 backdrop-filter 子元素不设圆角——让裁剪发生在父级绘制阶段而非合成阶段。同时移除 `sidebar.tsx:736` 与 `liquid-glass-card.tsx:59` 的 clipPath（已证无效）。
3. **抬高最小模糊量**（缓解非根治）：`SettingsPage.tsx:997` `min={1}` → `min={6}`；`background-context.tsx:66/107` 默认 `1` → `12`。**这项改默认值会影响现有用户观感，建议先跟用户确认。**

### 4.2 问题 2：淡化动画切页闪烁

**用户给的线索是决定性的**：只在「预设背景」或「图片背景」下闪，「动态背景」和「mr」不闪。

分界线在 `src/components/ui/main-layout.tsx`：

| 背景类型 | 渲染为 | 合成层提升 |
|---|---|---|
| preset / image（`:243-273`） | `<img>` | **有**：`transform="translateZ(0)"`（`:255`）+ `willChange="opacity"`（`:256`） |
| dynamic（`:287`） | `<video>` | 无 |
| mr（`:306`） | `MRBackground` → WebGL canvas（`MRBackground.tsx:184` `getContext("webgl")`） | 无 |

`main-layout.tsx:254` 的注释说明了 `translateZ(0)` 的来历——**这正是历史上为消除 WebView2 整屏大图分块光栅化竖向接缝而加的那处**（历史铁律里提过的 translateZ(0)）。

**机制**：`<video>` 与 WebGL canvas 天然是原生合成 quad，backdrop 快照重建时无需重新光栅化其纹理，故免疫。而 `<img>` 层被 `translateZ(0)` + `willChange` 钉成常驻合成层后，fade 的纯 opacity 动画使整页成为 backdrop root 的一部分；`index.css:97-101` 在动画起止把 `backdrop-filter` 在 `none ↔ blur()` 间**离散跳变**（CSS 无法在二者间插值），每次跳变迫使背景大图重新分块光栅化 → 闪一帧。

**已排除**：`main-layout.tsx:114` 的 effect 依赖（`PRESET_BACKGROUNDS` 是模块常量、`showPresetBg` 是 boolean）引用稳定，路由切换不会重跑该 effect——**图片没有被重新挂载或重新解码**，别往这个方向查。

**★ 实施第一步（务必先做）**：临时注释 `main-layout.tsx:255-256` 两行，跑一次切页，**若闪烁消失即确认根因**。确认后再动手改。

**改动方案**：
1. **首选**：`:255-256` 改为条件式——仅在交叉淡化期间（`layer.fading` 为真，或新层挂载后 600ms 内）施加 `transform/willChange`，静止时移除，不再常驻合成层。
2. **配套**：`src/components/ui/animated-page.tsx:111` 的 `onAnimationComplete` 里用 `requestAnimationFrame` 延后一帧再 `setAnimating(false)`，避免 backdrop-filter 恢复与合成层合并落在同一帧。
3. **若方案 1 导致竖向接缝回归**：改用 `contain: paint` 替代 `translateZ(0)`——可抑制接缝但不强制常驻合成层。

### 4.3 批 3 回归要求（重要）

问题 1 的方案 1 与问题 2 的方案 1 **方向一致**（都是撤销强制合成层提升），建议一并处理。但**必须一起回归背景竖向断层**——那是 `translateZ(0)` 当年要解决的问题，撤掉后可能复发。

**验证方法**：用 `tools/printwindow-capture.ps1`（PrintWindow(PW_RENDERFULLCONTENT)，**无需前台**）抓图做像素级前后对比。判读时注意：
- **不要只信单点像素采样**——壁纸内容会干扰（上一会话把"壁纸储物柜门缝透过玻璃"误判成 backdrop-filter 溢出伪影，教训）。保存 PNG 直接看图。
- **判透明/判玻璃必须在亮色内容上做**（铁律：黑壁纸上什么都看不出，已二次踩实）。
- 圆角毛刺的验证建议：切到亮色壁纸 + 放大截图看侧栏胶囊边缘。

---

## 五、实施须知（铁律速查，本轮相关）

> 完整 26 条铁律见 `docs/preview/HANDOFF.md`，这里只列本轮最可能踩的。

### 构建与验证
1. **`tauri.conf.json` 的 `csp` 必须为 `null`**——任何非 null CSP 都会冻结 WebView2 的 DComp 呈现管线导致白屏（PrintWindow bisect 定案，`d2d3151`）。**不要因为任何理由改动它。**
2. **构建顺序**：必须先 `npm run build`（dist 嵌入 exe），漏跑会用旧 dist 缓存构建。
3. **build 后必须核验 exe 时间戳**——`build.ps1` 的 csc 失败可被 shell 输出过滤掩盖（已两次踩坑）。另注意 `tauri build` 在 exe 被占用时 vite 成功但链接失败且整链 exit 0。
4. **改前端必须重跑 `tauri build`**（前端是构建时嵌入）；**改 payload 必须重跑 `cargo build`**（`include_bytes!` 是构建时嵌入）。
5. **打包三步**：`tools/Package-Installer.ps1 -StageOnly` → `npx tauri build` → `installer/build-installer.ps1 -SkipPayload`。
6. **测试基线**（改动后需全绿）：cargo 宿主 5/5、硬件服务 20/20、vitest 14/14、C# 27 套件（`tests/run-all-with-timeout.ps1`，**套件 PASS 判定按 stdout 的 `': PASS'`，PS 读 Start-Process 的 ExitCode 有怪癖不可信**）。

### 环境坑
7. **git-bash 会吃路径反斜杠**：`powershell -File tools\X.ps1` 会变成 `toolsX.ps1`——**一律用正斜杠**。`taskkill /IM` 和 `/S` 参数同样被 MSYS 吃掉，用 PowerShell `Start-Process -ArgumentList`。
8. **含中文的 .ps1 必须 UTF-8 BOM**（否则 PS5.1 按 ANSI 解析直接 ParserError，已三次踩实）。Write 工具默认写的是无 BOM 文件。
9. **`tauri build` 报 os error 32**：残留 FrameScope/msedgewebview2 子进程锁 exe，全部 Stop-Process 后等几秒再跑。

### Rust 侧
10. **MutexGuard 临时值作引用传参、被调方再锁同一 Mutex = 同线程死锁**——`f(&MUTEX.lock().clone())` 的 guard 活到语句尾。**修 OSD 相关代码时高度警惕**（Round 5 就是栽在这里，cargo/静态审计都查不出）。先 clone 到 owned 局部。
11. **mpsc worker send Result 时，`recv()?` 剥的是 `RecvError` 信封，内层 Result 必须再解一次**。
12. **热路径 = 「存内存 + 通知重绘」缺一不可**，文件写成功 ≠ 内存更新（改 OSD 设置热路径时注意）。
13. **Rust 写 int 字段必须 `json!(i64)`**，写 `1000.0` 浮点会让 C# JavaScriptSerializer 整体抛异常 → ConfigStore 静默回退默认配置 → watcher 全盲。

### C# 侧
14. **老 csc 4.0.30319 只支持 C#5**：禁 is-pattern / out-var / 字符串插值 / `using` 嵌套简写 / `when` 异常过滤器 / 自动属性初始化器（`= "";`）。**这也是批 1 不直接引入 tubatools C# 代码的原因。**

### 调试手段
15. **CDP 验证法**：`tauri.conf.json` 的 `windows[0]` 临时加 `additionalBrowserArgs: "--remote-debugging-port=9333"`，`__TAURI_INTERNALS__.invoke` 可直调宿主命令，`Runtime.evaluate` 可驱动 DOM。**用完必须删除，不可进交付配置。** 注意：WebView2 同一 user-data 目录下所有窗口的 `additionalBrowserArgs` 必须一致，主窗带 CDP 端口时动态建窗会 `ERROR_INVALID_STATE(0x8007139F)`。
16. **PrintWindow(PW_RENDERFULLCONTENT)** 是无需前台的窗口呈现取证（`tools/printwindow-capture.ps1`），配合像素 diff 可客观验证任何视觉修复。
17. **多改动合流后做对照实验，必须先 bisect 回干净基线**——上一会话把 CSP 错误排除，就是因为三方实验跑在含其它新改动的构建上（白屏 postmortem 的核心教训）。

### 实机测试技巧
18. BF6 是 **Steam 版**（rungameid/2807960，无需 EA Desktop）；CS2 启动先弹"完美世界/国际服"选择对话框（点开始过掉），不点不渲染 → 零 present。
19. 游戏窗口 a11y 只有 1 个元素，点击要用 `get_app_state` **窗口级截图**绑 frame（全屏 screenshot 会绑到错误 pid 被 frontmost 校验拒）。
20. **强杀进程树会泄漏 "NT Kernel Logger" ETW 会话** → PresentMon 静默零事件（`logman stop` 清理）。**批 1 反复起停 ETW 会话时特别注意这条。**

---

## 六、当前状态与遗留

### 代码状态
- 分支 `codex/nexbox-frontend-preview`，HEAD `90eca1a`，**工作树干净**。
- **仅本地提交，不推 GitHub**（用户已拍板，该分支无上游，勿主动 push；若用户日后要求推送需重新确认）。
- 本会话**未改任何代码**，只新增 `docs/audit/2026-09-14-issue-analysis.md` 与本文档。

### 上一会话遗留（已被本轮分析覆盖或推进）
- ~~淡化闪烁未解决~~ → 本轮定位，见 4.2
- ~~圆角破碎未解决~~ → 本轮定位，见 4.1
- **SEC-004 代码签名**：等用户采购证书（唯一遗留的安全审计项，非本轮范围）
- **PUBG 采集真因**：等用户游戏机的 `framescope-runs\PUBG*\status.json`（注：批 1 的 ETW 改造有可能顺带改善，但不要假设）
- 安装器 P3：体积优化（79.4MB）、NSIS 链路退役时机
- backend_root 签名校验取舍、管道 ACL（用户拍板范围）

### 需要用户拍板的事项（实施中遇到再问，不要自行决定）
1. 批 1 的 **CSV 兜底采样率从 1Hz 提到 2-5Hz**（增强项，影响 CSV 体积）。
2. 批 1 的**方案 B**（强制 OSD 显示，掉帧 5-15%）是否要做成用户开关。
3. 批 3 的**模糊程度默认值从 1 改到 12**（影响现有用户观感）。

---

## 七、取证工具速查

| 脚本 | 用途 | 前置条件 |
|---|---|---|
| `tools/printwindow-capture.ps1` | PrintWindow 抓窗口实际呈现内容，**无需前台**，输出 PNG + 中心像素采样 | 无 |
| `tools/cdp-shot.ps1` | CDP 抓渲染进程内部帧（分辨"渲染器坏"还是"呈现坏"） | 需临时加 CDP 端口 |
| `tools/cdp-probe*.ps1` | 样式表/资源加载、elementFromPoint 布局、visibility/rAF 活性 | 同上 |
| `tools/where-is-webview.ps1` | 枚举 Chrome_WidgetWin 窗口归属与父子关系 | 无 |
| `tests/run-all-with-timeout.ps1` | C# 27 套件带超时批量跑 | 无 |
| `tools/diagnostics/diagnose-pubg-capture.ps1` | 采集失败自助诊断（随安装器分发） | 无 |

**建议新增**（批 1 用）：Rust 版 ETW 诊断子命令，照 tubatools `DxTraceDiag/Program.cs` 的逻辑——12 秒抓取、按 pid 打印事件 ID 分布与帧间隔中位数。这是"事件到底发没发"最快的定位手段。

---

## 八、推荐的实施顺序

```
1. 读 docs/audit/2026-09-14-issue-analysis.md（完整证据）
2. 批 1（P0，最大）
   2.1 先做 Rust 版 ETW 诊断子命令 → 摸清本机各模式下的事件分布
   2.2 写 QPC 单位换算单测锁死（首要风险项）
   2.3 game_fps.rs 九项改造 + 翻译 tubatools 单测
   2.4 下游四层节流调整 + CPU 复测
   2.5 兜底路径九个点逐一核对
   2.6 问题 6 方案 A（周期性重置顶 + 多显示器修正）+ 方案 E（模式检测）
   2.7 CS2 / BF6 实机验证（含反作弊供数确认）
3. 批 2（P1）
   3.1 问题 4：backend.rs 转发 + 前端四处改动 → 真实采集验证
   3.2 问题 3：liquid-glass-card.tsx 一处修复 → 硬件页验证 → 其余 14 处渐进收敛
4. 批 3（P2）
   4.1 先注释 main-layout.tsx:255-256 验证问题 2 根因
   4.2 问题 1 + 问题 2 一起改
   4.3 PrintWindow 像素回归 + 背景竖向断层回归（亮色壁纸下判读）
5. 全量测试基线 + 重新打包
```

**每批完成后**：跑全量测试基线、核验 exe 时间戳、本地提交（不 push）、更新 `docs/preview/HANDOFF.md`。
