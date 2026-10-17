//! FPS 监控模块 — 内嵌 ETW 版（算法移植自图吧工具箱 TubaWinUi3 FpsTracker.cs，GPL-3.0）
//!
//! DXGI 成功 Present 提交为优先帧源（Start/Stop 配对，排除 TEST/失败/遮挡），
//! 内核 History/Win32k/Flip 为没有 runtime 数据时的估计回退；不启动 PresentMon。
//! 多线程提交按开始时间有界排序，同一目标代次不混合两种统计口径。
//! ETW 显式使用原始 QPC，归一成 100ns；目标按 PID 而非可执行文件名隔离。
//! 瞬时 FPS 为 60 样本环；Low 为 10s/30s 时间窗，样本不足必须发布 unavailable。
//! 呈现事件只提供最近信号提示，不能仅凭 B8/A8 证明传统独占或 OSD 可见。
//!
//! 架构说明：
//! - etw::fps_monitor_main 线程：管理 ETW 会话生命周期，会话中断自动重连（上限 5 次）
//! - etw 回调线程（即 ProcessTrace 回调，单线程串行）：解析事件头 → QPC 归一 →
//!   分层去重 → 目标过滤 → 统计管线
//! - process_tracker_loop 线程：追踪前台窗口，更新 TARGET_PROCESS，2s 无帧归零并
//!   bump 代次清空统计（对齐 tubatools Decay 的停帧语义）
//!
//! 需要管理员权限才能启用内核 Provider（与旧 PresentMon 方案一致）。

use std::collections::{BTreeMap, HashMap};
use std::sync::atomic::{AtomicBool, AtomicU8, AtomicU32, AtomicU64, Ordering};
use std::sync::{Mutex, OnceLock};
use std::thread;
use std::time::{Duration, Instant};

use parking_lot::RwLock;

// ============ 全局状态 ============

/// 瞬时 FPS（60 样本环形窗口），供 overlay 读取
static SMOOTHED_FPS: AtomicU32 = AtomicU32::new(0);
/// FPS 监控是否处于活跃状态
static FPS_ACTIVE: AtomicBool = AtomicBool::new(false);
/// 自身 overlay 窗口句柄（用于排除前台切换到自身 overlay）
static OVERLAY_HWND: AtomicU64 = AtomicU64::new(0);
/// 当前前台窗口 PID（钩子回调中快速存储）
static CURRENT_FG_PID: AtomicU32 = AtomicU32::new(0);
/// Debounced target identity. Executable names are not unique across processes.
static TARGET_PID: AtomicU32 = AtomicU32::new(0);
/// 当前前台目标进程名（小写 exe 文件名，如 "game.exe"）— RwLock 读多写少
static TARGET_PROCESS: RwLock<String> = parking_lot::RwLock::new(String::new());
/// 上次匹配到目标帧数据的时间戳（ms），用于检测 FPS 过期归零
static LAST_FRAME_TIME: AtomicU64 = AtomicU64::new(0);
/// 目标切换代次（每次切换/过期归零 +1），ETW 回调检测到变化后清空统计
static TARGET_GENERATION: AtomicU64 = AtomicU64::new(0);
/// 1% Low FPS（10s 窗口内最慢 1% 帧的平均帧时间取倒数）
static ONE_PCT_LOW_FPS: AtomicU64 = AtomicU64::new(0);
/// 0.1% Low FPS（30s 窗口内最慢 0.1% 帧的平均帧时间取倒数）
static ZERO_DOT_ONE_PCT_LOW_FPS: AtomicU64 = AtomicU64::new(0);

fn store_low(slot: &AtomicU64, value: f64) { slot.store(value.max(0.0).to_bits(), Ordering::Relaxed); }
fn load_low(slot: &AtomicU64) -> Option<f64> {
    let value = f64::from_bits(slot.load(Ordering::Relaxed));
    (value.is_finite() && value > 0.0).then_some(value)
}
/// 目标进程最近呈现模式（问题 6 方案 E）：0 未知 / 1 合成 / 2 独立翻转 / 3 传统独占
static PRESENT_MODE: AtomicU8 = AtomicU8::new(0);
/// 10s 窗口平均 FPS；不同时间窗口的指标不能互相钳制。
static WINDOW_AVG_FPS: AtomicU32 = AtomicU32::new(0);

pub const MODE_UNKNOWN: u8 = 0;
pub const MODE_COMPOSITED: u8 = 1;
pub const MODE_INDEPENDENT_FLIP: u8 = 2;
pub const MODE_LEGACY_EXCLUSIVE: u8 = 3;

/// 自进程 PID（ETW 回调中排除自身）
static SELF_PID: OnceLock<u32> = OnceLock::new();

// ============ QPC → 100ns 归一（移植首要风险项） ============

/// ETW EVENT_HEADER.TimeStamp 是原始 QPC ticks（会话默认 QPC 时钟），而本模块
/// 全部窗口常量都是 100ns 单位（对齐 tubatools 的 TimeSpan.Ticks 语义）。
/// 现代 Windows 的 invariant TSC 频率恰为 10MHz，QPC ticks 即 100ns，走恒等快路径；
/// 其余频率（ACPI PM timer 等）必须归一，否则所有窗口值错位。
#[inline]
fn qpc_to_100ns_with(qpc: i64, freq: i64) -> i64 {
    if freq == 10_000_000 {
        return qpc;
    }
    if freq <= 0 {
        return qpc;
    }
    ((qpc as i128) * 10_000_000i128 / freq as i128) as i64
}

static QPC_FREQ: OnceLock<i64> = OnceLock::new();

#[cfg(target_os = "windows")]
fn qpc_frequency() -> i64 {
    *QPC_FREQ.get_or_init(|| {
        // Win32_System_Performance::QueryPerformanceFrequency
        unsafe {
            let mut freq: i64 = 0;
            windows_sys::Win32::System::Performance::QueryPerformanceFrequency(&mut freq);
            if freq <= 0 {
                10_000_000
            } else {
                freq
            }
        }
    })
}

#[cfg(not(target_os = "windows"))]
fn qpc_frequency() -> i64 {
    10_000_000
}

#[inline]
fn qpc_to_100ns(qpc: i64) -> i64 {
    qpc_to_100ns_with(qpc, qpc_frequency())
}

// ============ Present 事件族与分层算法（纯逻辑，无 Win32 依赖） ============

/// 所有窗口常量统一 100ns ticks 单位
pub(crate) const TICKS_PER_SECOND: i64 = 10_000_000;

// ---- Present 事件族（值 = EventHeader.EventDescriptor.Id，与 tubatools FpsPresentEvents 一致） ----
pub(crate) const PRESENT: u16 = 0x00B8; // Present（内核 present；全屏/legacy，Tier3）
pub(crate) const PRESENT_HISTORY_START: u16 = 0x00AB; // PresentHistory_Start（Tier1，全模式）
pub(crate) const PRESENT_HISTORY_DETAILED: u16 = 0x00D7; // PresentHistoryDetailed_Start（Tier1）
pub(crate) const WIN32K_PRESENT: u16 = 0x00C9; // Win32k TokenCompositionSurfaceObject（Tier2，合成/窗口化）
pub(crate) const BLT: u16 = 0x00A6; // Blt_Info（Tier3）
pub(crate) const MMIO_FLIP: u16 = 0x0074; // MMIOFlip_Info（Tier3）
pub(crate) const MMIO_FLIP_MPO: u16 = 0x0103; // MMIOFlip_MPO（Tier3）
pub(crate) const MMIO_FLIP_MPO3: u16 = 0x0182; // MMIOFlip_MPO3（Tier3）
pub(crate) const FLIP: u16 = 0x00A8; // Flip_Info（Tier3）
pub(crate) const FLIP_MPO: u16 = 0x00FC; // FlipMultiPlaneOverlay_Info（Tier3）
pub(crate) const INDEPENDENT_FLIP: u16 = 0x010A; // IndependentFlip_Info（Tier3）

/// 1ms：同帧去重窗口（History/legacy/MPO 路径）。内核一帧发多个事件（微秒级间隔）。
pub(crate) const SAME_FRAME_WINDOW_TICKS: i64 = 10_000;
/// Legacy Win32k fallback deduplication. Its cadence is not a substitute for
/// application Present submissions; DXGI runtime events bypass this heuristic.
pub(crate) const WIN32K_SAME_FRAME_WINDOW_TICKS: i64 = 20_000;
/// 500ms：信源优先级 shadow 窗口
pub(crate) const MODE_WINDOW_TICKS: i64 = 5_000_000;

/// DxgKrnl 的 10 个 present 事件 ID（Tier1 + Tier3）
#[inline]
pub(crate) fn is_dxg_present_id(id: u16) -> bool {
    matches!(
        id,
        PRESENT
            | PRESENT_HISTORY_START
            | PRESENT_HISTORY_DETAILED
            | BLT
            | MMIO_FLIP
            | MMIO_FLIP_MPO
            | MMIO_FLIP_MPO3
            | FLIP
            | FLIP_MPO
            | INDEPENDENT_FLIP
    )
}

/// 仅 Tier3 的 8 个 ID（被 Win32k 信源压制的集合；0xC9 自身与 Tier1 不在内）
#[inline]
fn is_tier3_id(id: u16) -> bool {
    matches!(
        id,
        PRESENT
            | BLT
            | MMIO_FLIP
            | MMIO_FLIP_MPO
            | MMIO_FLIP_MPO3
            | FLIP
            | FLIP_MPO
            | INDEPENDENT_FLIP
    )
}

/// 每进程分层去重/信源/模式状态（仅由 ProcessTrace 回调线程独占读写，无需锁）
#[derive(Debug, Default, Clone)]
pub(crate) struct TierState {
    /// 最近一次被计数的 Present（同帧去重的全局基准）
    last_present: i64,
    /// 最近 PresentHistory 事件 tick（Tier1）
    last_history: i64,
    /// 最近 Win32k 合成事件 tick（Tier2）
    last_win32k: i64,
    /// 最近独立翻转事件（0x10A）tick — 模式检测用
    last_independent_flip: i64,
}

/// 分层权威源去重（tubatools FpsTracker.cs TryRecordPresent 逐行移植）。
/// 返回 false = 同帧重复或被高层信源压制，本事件不计入帧。
///
/// 执行顺序严格如下（不要改动顺序）：
/// ①无条件更新 History/Win32k（及模式）时间戳——即使随后丢弃；
/// ②同帧去重：0xC9 用 2ms、其余 1ms，基准是全局 last_present；
/// ③非 History 事件，若 500ms 内出现过 History → 丢（0xC9 也被 Tier1 压制）；
/// ④仅 Tier3 八个 ID，若 500ms 内出现过 Win32k → 丢（0xC9 不压制 Tier1）。
pub(crate) fn tier_dedup(st: &mut TierState, id: u16, ticks: i64) -> bool {
    // ① 无条件更新（即使该事件随后被丢弃）
    if matches!(id, PRESENT_HISTORY_START | PRESENT_HISTORY_DETAILED) {
        st.last_history = ticks;
    } else if id == WIN32K_PRESENT {
        st.last_win32k = ticks;
    }
    if id == INDEPENDENT_FLIP {
        st.last_independent_flip = ticks;
    }

    // ② 同帧去重（比较基准 = 上一个已计入的帧）
    let dup_window = if id == WIN32K_PRESENT {
        WIN32K_SAME_FRAME_WINDOW_TICKS
    } else {
        SAME_FRAME_WINDOW_TICKS
    };
    if ticks - st.last_present < dup_window {
        return false;
    }

    // ③ 高层信源 500ms 内活跃时，忽略低层事件（History 凌驾于一切）
    if !matches!(id, PRESENT_HISTORY_START | PRESENT_HISTORY_DETAILED)
        && ticks - st.last_history < MODE_WINDOW_TICKS
    {
        return false;
    }
    // ④ 仅 Tier3 被 Win32k 信源压制；0xC9 与 Tier1 不受此条影响
    if is_tier3_id(id) && ticks - st.last_win32k < MODE_WINDOW_TICKS {
        return false;
    }

    st.last_present = ticks;
    true
}

/// Recent signal hints only. B8/A8 without other events cannot prove exclusive
/// fullscreen, and these hints do not guarantee that an external OSD is visible.
pub(crate) fn present_mode_code(st: &TierState, now: i64) -> u8 {
    let fresh = |t: i64| t > 0 && now >= t && now - t < MODE_WINDOW_TICKS;
    let win32k = fresh(st.last_win32k);
    let indep = fresh(st.last_independent_flip);
    if win32k {
        return MODE_COMPOSITED;
    }
    if indep {
        return MODE_INDEPENDENT_FLIP;
    }
    MODE_UNKNOWN
}

// ============ 帧统计（纯逻辑，移植自 FpsTracker.cs） ============

/// 瞬时 FPS 有效帧时间区间 [1ms, 10s) → 上限 1000 FPS。
/// 下限挡掉双源/多 surface 漏网假帧；上限挡掉停顿后的陈旧间隔污染统计。
pub(crate) const MIN_FRAME_TIME_S: f64 = 0.001;
pub(crate) const MAX_FRAME_TIME_S: f64 = 10.0;

/// 1% low / 0.1% low 统计窗口与门槛（门槛 = 窗口内帧数，非会话累计）
pub(crate) const LOW1_WINDOW_TICKS: i64 = 100_000_000; // 10s
pub(crate) const LOW01_WINDOW_TICKS: i64 = 300_000_000; // 30s
pub(crate) const LOW1_MIN_FRAMES: usize = 100;
pub(crate) const LOW01_MIN_FRAMES: usize = 900;

/// 瞬时 FPS 环形样本数（300FPS 下窗口 ≈200ms）
pub(crate) const SAMPLE_COUNT: usize = 60;
/// 帧窗口容量。真正的过期口径是绝对时间；容量必须 ≥ 30s 窗口在 1000FPS 下的帧数
/// （tubatools 用 16384，@1000FPS 仅 16s 会先于时间窗口截断，这里按其自述风险扩到 32768）。
pub(crate) const FRAME_WINDOW_CAPACITY: usize = 32768;

/// 瞬时 FPS 环形缓冲：fps = (count-1) / (最新 - 最旧)
pub(crate) struct InstantRing {
    stamps: [i64; SAMPLE_COUNT],
    index: usize,
    count: usize,
}

impl InstantRing {
    fn new() -> Self {
        Self {
            stamps: [0; SAMPLE_COUNT],
            index: 0,
            count: 0,
        }
    }

    /// 最近一次样本的 tick（peek，不推进）
    fn last_tick(&self) -> Option<i64> {
        if self.count == 0 {
            None
        } else {
            Some(self.stamps[(self.index + SAMPLE_COUNT - 1) % SAMPLE_COUNT])
        }
    }

    /// 写入一个 present，返回 (count-1)/duration 秒单位的瞬时 FPS（不足 2 样本或
    /// duration ≤ 0 返回 0）。
    fn push(&mut self, ticks: i64) -> f64 {
        self.stamps[self.index] = ticks;
        self.index = (self.index + 1) % SAMPLE_COUNT;
        if self.count < SAMPLE_COUNT {
            self.count += 1;
        }
        if self.count < 2 {
            return 0.0;
        }
        let first = self.stamps[(self.index + SAMPLE_COUNT - self.count) % SAMPLE_COUNT];
        let last = self.stamps[(self.index + SAMPLE_COUNT - 1) % SAMPLE_COUNT];
        let duration = (last - first) as f64 / TICKS_PER_SECOND as f64;
        if duration > 0.0 {
            (self.count - 1) as f64 / duration
        } else {
            0.0
        }
    }

    fn clear(&mut self) {
        self.index = 0;
        self.count = 0;
    }
}

/// 帧时间窗口：存 (绝对 ticks, 帧时间秒) × FRAME_WINDOW_CAPACITY，按绝对时间剪枝。
/// 停帧期间旧帧赖在环形缓冲里不会被挤掉，靠绝对时间剪枝，切回来的第一帧就把
/// 过期数据全部丢弃（帧间隔累加式的旧实现要等新帧一根根挤出去，读数慢半拍）。
pub(crate) struct FrameWindowBuffer {
    ticks: Vec<i64>,
    frame_times: Vec<f64>,
    head: usize,
    count: usize,
}

impl FrameWindowBuffer {
    fn new(capacity: usize) -> Self {
        Self {
            ticks: vec![0; capacity],
            frame_times: vec![0.0; capacity],
            head: 0,
            count: 0,
        }
    }

    fn push(&mut self, ticks: i64, frame_time: f64) {
        self.ticks[self.head] = ticks;
        self.frame_times[self.head] = frame_time;
        self.head = (self.head + 1) % self.ticks.len();
        if self.count < self.ticks.len() {
            self.count += 1;
        }
    }

    fn clear(&mut self) {
        self.head = 0;
        self.count = 0;
    }

    fn is_empty(&self) -> bool {
        self.count == 0
    }

    /// 收集最近 window_ticks 内的帧时间（最新在前）。从最新往回遍历，一遇到过期帧
    /// 即停（后面全部过期）。返回时 scratch 被复用清空后填充，filled = 窗口是否被
    /// 时间填满 —— 判据是「缓冲中存在比窗口更老的帧（收集因遇到过期帧而中断，而非
    /// 帧数耗尽）」。不能用「窗口内首尾跨度 >= windowTicks」判：等间隔帧流的跨度
    /// 因整数截尾恒比窗口差零到一个帧间隔（60Hz@10s 窗口差 400 ticks），会永远
    /// 判为未填满、1% low 永远显示 "--"。
    fn collect_window(&self, window_ticks: i64, scratch: &mut Vec<f64>) -> bool {
        scratch.clear();
        if self.count == 0 {
            return false;
        }
        let cap = self.ticks.len();
        let newest = self.ticks[(self.head + cap - 1) % cap];
        let min_tick = newest - window_ticks;
        let mut filled = false;
        for k in 0..self.count {
            let idx = (self.head + cap * 2 - 1 - k) % cap;
            if self.ticks[idx] < min_tick {
                filled = true; // 存在比窗口更老的帧 → 数据已覆盖整个时间窗口
                break;
            }
            scratch.push(self.frame_times[idx]);
        }
        filled
    }

    /// 1% / 0.1% low：窗口里最慢 ceil(n*percentile) 帧的「平均帧时间」取倒数
    /// （PresentMon / CapFrameX 标准口径；对最差帧的瞬时 FPS 直接平均会因 1/x 凸性
    /// 系统性高估）。样本不足 minFrames 返回 -1；require_full_window 时窗口未被
    /// 时间填满也返回 -1（启动期半截窗口的坏帧会放大最差百分位）。
    fn calc_percentile_low(
        &self,
        percentile: f64,
        window_ticks: i64,
        min_frames: usize,
        require_full_window: bool,
    ) -> f64 {
        let mut scratch = Vec::new();
        let filled = self.collect_window(window_ticks, &mut scratch);
        if scratch.len() < min_frames {
            return -1.0;
        }
        if require_full_window && !filled {
            return -1.0;
        }
        scratch.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
        let n = scratch.len();
        // 真百分位帧数，刻意不设「最少 3 帧」兜底：那会把口径悄悄放大且随窗口
        // 填充进度漂移。样本不足的问题交给 min_frames 门槛解决。
        let worst = ((n as f64 * percentile).ceil() as usize).clamp(1, n);
        let sum: f64 = scratch[n - worst..].iter().sum();
        let avg = sum / worst as f64;
        if avg > 0.0 {
            1.0 / avg
        } else {
            -1.0
        }
    }
}

/// 目标进程帧统计器：瞬时环 + 帧窗口 + 会话累计。
/// 帧时间 = 相邻两个 present 的 tick 差（与 tubatools OnPresent 一致：环存全部
/// 计入帧，帧窗口只收有效帧间隔）。
pub(crate) struct FpsTracker {
    ring: InstantRing,
    window: FrameWindowBuffer,
    total_frames: u64,
    total_frame_time: f64,
}

impl FpsTracker {
    pub(crate) fn new() -> Self {
        Self {
            ring: InstantRing::new(),
            window: FrameWindowBuffer::new(FRAME_WINDOW_CAPACITY),
            total_frames: 0,
            total_frame_time: 0.0,
        }
    }

    /// 记入一个已通过分层去重的 present（100ns ticks）。返回瞬时 FPS。
    pub(crate) fn on_present(&mut self, ticks: i64) -> f64 {
        self.record_present(ticks, MIN_FRAME_TIME_S)
    }

    fn on_runtime_present(&mut self, ticks: i64) -> f64 {
        // Successful API calls are already paired; a sub-ms interval is not
        // evidence of duplicate kernel signals.
        self.record_present(ticks, 0.0)
    }

    fn record_present(&mut self, ticks: i64, minimum_frame_time: f64) -> f64 {
        let prev = self.ring.last_tick();
        let fps = self.ring.push(ticks);
        if let Some(prev) = prev {
            let frame_time = (ticks - prev) as f64 / TICKS_PER_SECOND as f64;
            if frame_time > 0.0 && frame_time >= minimum_frame_time && frame_time < MAX_FRAME_TIME_S {
                self.window.push(ticks, frame_time);
                self.total_frames += 1;
                self.total_frame_time += frame_time;
            }
        }
        fps
    }

    pub(crate) fn instant_fps(&self) -> f64 {
        // 复用环里的读数语义：on_present 返回值即最新瞬时 FPS，这里重算一次
        // （读取频率远低于写入频率，重算成本可忽略且不引入跨线程状态）
        if self.ring.count < 2 {
            return 0.0;
        }
        let cap = SAMPLE_COUNT;
        let first = self.ring.stamps[(self.ring.index + cap - self.ring.count) % cap];
        let last = self.ring.stamps[(self.ring.index + cap - 1) % cap];
        let duration = (last - first) as f64 / TICKS_PER_SECOND as f64;
        if duration > 0.0 {
            (self.ring.count - 1) as f64 / duration
        } else {
            0.0
        }
    }

    pub(crate) fn total_frames(&self) -> u64 {
        self.total_frames
    }

    /// 会话平均 FPS（总帧数 / 总帧时间；对瞬时 FPS 求均值会被假帧抬高）
    pub(crate) fn session_avg_fps(&self) -> f64 {
        if self.total_frames > 0 && self.total_frame_time > 0.0 {
            self.total_frames as f64 / self.total_frame_time
        } else {
            0.0
        }
    }

    /// 10s 窗口平均 FPS（与 1% low 同窗口、同剪枝口径）
    pub(crate) fn window_avg_fps(&self) -> f64 {
        let mut scratch = Vec::new();
        if !self.window.collect_window(LOW1_WINDOW_TICKS, &mut scratch) && scratch.is_empty() {
            return 0.0;
        }
        if scratch.is_empty() {
            return 0.0;
        }
        let sum: f64 = scratch.iter().sum();
        if sum > 0.0 {
            scratch.len() as f64 / sum
        } else {
            0.0
        }
    }

    pub(crate) fn one_percent_low(&self) -> f64 {
        self.window
            .calc_percentile_low(0.01, LOW1_WINDOW_TICKS, LOW1_MIN_FRAMES, true)
    }

    pub(crate) fn point_one_percent_low(&self) -> f64 {
        self.window
            .calc_percentile_low(0.001, LOW01_WINDOW_TICKS, LOW01_MIN_FRAMES, true)
    }

    pub(crate) fn is_window_empty(&self) -> bool {
        self.window.is_empty()
    }

    pub(crate) fn clear(&mut self) {
        self.ring.clear();
        self.window.clear();
        self.total_frames = 0;
        self.total_frame_time = 0.0;
    }
}

// ============ 排除名单与进程名匹配 ============

/// 排除系统/桌面进程（文件名去 .exe 后缀后大小写不敏感比较，移植自图吧工具箱 Excluded 名单）
fn is_excluded_process(name: &str) -> bool {
    const EXCLUDED: &[&str] = &[
        "dwm", "explorer", "searchhost", "shellexperiencehost",
        "startmenuexperiencehost", "runtimebroker", "applicationframehost",
        "sihost", "taskhostw", "ctfmon", "msedgewebview2", "microsoftedge",
        "searchapp", "svchost", "csrss", "smss", "lsass", "wininit",
        "services", "winlogon", "fontdrvhost", "dllhost", "conhost",
        "taskmgr", "systemsettings", "windowsterminal", "cmd", "powershell",
        "catpawai", "textinputhost", "shell", "memory compression",
        "registry", "memcompression", "idle", "system", "ntoskrnl",
        "interrupt", "dpcs", "framescope",
    ];
    let stem = strip_exe_suffix(name);
    EXCLUDED.iter().any(|e| stem.eq_ignore_ascii_case(e))
}

/// 判断进程是否为非游戏进程（大小写不敏感，避免 to_lowercase 堆分配）
fn is_non_game_process(name: &str) -> bool {
    const NON_GAME_PROCESSES: &[&str] = &[
        "explorer.exe", "dwm.exe", "windowsterminal.exe", "cmd.exe",
        "powershell.exe", "conhost.exe", "catpawai.exe", "framescope-hardware-service.exe",
        "msedgewebview2.exe", "searchhost.exe", "shellexperiencehost.exe",
        "sihost.exe", "ctfmon.exe", "textinputhost.exe",
        "applicationframehost.exe", "winlogon.exe", "fontdrvhost.exe",
        "systemsettings.exe", "taskmgr.exe",
    ];
    NON_GAME_PROCESSES.iter().any(|p| name.eq_ignore_ascii_case(p))
}

/// 去除 .exe 后缀（大小写不敏感）
#[inline]
fn strip_exe_suffix(s: &str) -> &str {
    // Byte slicing on a non-char boundary would panic inside the ETW
    // callback (panic across FFI = process abort), so slice via str::get.
    if s.len() >= 4 {
        if let (Some(head), Some(tail)) = (s.get(..s.len() - 4), s.get(s.len() - 4..)) {
            if tail.eq_ignore_ascii_case(".exe") {
                return head;
            }
        }
    }
    s
}

fn clear_cached_statistics() {
    SMOOTHED_FPS.store(0, Ordering::Relaxed);
    ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
    ZERO_DOT_ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
    WINDOW_AVG_FPS.store(0, Ordering::Relaxed);
    LAST_FRAME_TIME.store(0, Ordering::Relaxed);
    PRESENT_MODE.store(MODE_UNKNOWN, Ordering::Relaxed);
}

fn select_target(pid: u32, name: &str) {
    // The callback holds the matching read guard through publication.
    let mut target = TARGET_PROCESS.write();
    *target = name.to_string();
    TARGET_PID.store(pid, Ordering::Relaxed);
    clear_cached_statistics();
    TARGET_GENERATION.fetch_add(1, Ordering::Relaxed);
}

/// 当前 Unix 毫秒时间戳
#[inline]
fn now_unix_ms() -> u64 {
    std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_millis() as u64
}

// ============ Windows 前台窗口钩子 ============

#[cfg(target_os = "windows")]
mod win32_fg {
    use super::*;
    use windows_sys::Win32::UI::WindowsAndMessaging::*;

    /// 初始化时获取当前前台窗口 PID
    pub fn init_foreground_process() {
        unsafe {
            let hwnd = GetForegroundWindow();
            if hwnd.is_null() {
                return;
            }
            let mut pid = 0u32;
            GetWindowThreadProcessId(hwnd, &mut pid);
            if pid != 0 {
                CURRENT_FG_PID.store(pid, Ordering::Relaxed);
            }
        }
    }
}

// ============ ETW 采集（内嵌会话） ============

#[cfg(target_os = "windows")]
mod etw {
    use super::*;
    use std::mem::size_of;
    use windows_sys::Win32::Foundation::CloseHandle;
    use windows_sys::Win32::System::Diagnostics::Etw::*;
    use windows_sys::Win32::System::Threading::{
        OpenProcess, QueryFullProcessImageNameW, PROCESS_QUERY_LIMITED_INFORMATION,
    };

    // ---- Provider GUID（与 tubatools FpsService.cs 完全一致） ----
    // Microsoft-Windows-DxgKrnl: 802EC45A-1E99-4B83-9920-87C98277BA9D
    const DXGKRNL_PROVIDER: windows_sys::core::GUID = windows_sys::core::GUID {
        data1: 0x802EC45A,
        data2: 0x1E99,
        data3: 0x4B83,
        data4: [0x99, 0x20, 0x87, 0xC9, 0x82, 0x77, 0xBA, 0x9D],
    };
    // Microsoft-Windows-Win32k: 8C416C79-D49B-4F01-A467-E56D3AA8234C
    const WIN32K_PROVIDER: windows_sys::core::GUID = windows_sys::core::GUID {
        data1: 0x8C416C79,
        data2: 0xD49B,
        data3: 0x4F01,
        data4: [0xA4, 0x67, 0xE5, 0x6D, 0x3A, 0xA8, 0x23, 0x4C],
    };
    const DXGI_PROVIDER: windows_sys::core::GUID =
        windows_sys::core::GUID::from_u128(0xca11c036_0102_4a2d_a6ad_f03cfed5d3c9);

    /// ETW 实时会话名（私有会话；停启/崩溃后靠「先停同名旧会话」防残留）
    fn session_name() -> Vec<u16> {
        "FrameScope_FPS\0".encode_utf16().collect()
    }

    /// GUID 比较（windows-sys 0.59 的 GUID 未实现 PartialEq）
    fn guid_eq(a: &windows_sys::core::GUID, b: &windows_sys::core::GUID) -> bool {
        a.data1 == b.data1 && a.data2 == b.data2 && a.data3 == b.data3 && a.data4 == b.data4
    }

    /// ETW 线程句柄（stop 时 join，确保会话清理完成）
    static MONITOR_THREAD: Mutex<Option<std::thread::JoinHandle<()>>> = Mutex::new(None);
    /// 当前 OpenTrace 处理句柄值（stop 时强制 CloseTrace 兜底）
    static PROCESS_HANDLE: AtomicU64 = AtomicU64::new(0);
    static SESSION_HANDLE: AtomicU64 = AtomicU64::new(0);
    static LAST_TRACE_LOSS: AtomicU64 = AtomicU64::new(0);

    /// ETW 回调运行时状态（全部由 ProcessTrace 回调线程独占，无需锁）
    struct EtwRuntime {
        /// 每进程分层去重/信源状态
        procs: HashMap<u32, TierState>,
        /// 目标进程帧统计（瞬时 FPS + 帧窗口 + 1%/0.1% low）
        tracker: FpsTracker,
        /// 1%/0.1% Low 计算计时器（排序成本高，限 1Hz —— CPU 预算硬指标）
        last_low_calc: Instant,
        /// 已处理的目标切换代次
        last_gen: u64,
        pending_presents: BTreeMap<(i64, u32), Option<bool>>,
        using_runtime: bool,
        diagnostic_frames: Option<Vec<(i64, bool, u64)>>,
    }

    impl EtwRuntime {
        fn new() -> Self {
            Self {
                procs: HashMap::new(),
                tracker: FpsTracker::new(),
                last_low_calc: Instant::now(),
                last_gen: 0,
                pending_presents: BTreeMap::new(),
                using_runtime: false,
                diagnostic_frames: None,
            }
        }

        fn drain_completed_presents(&mut self, now: i64, output: &mut Vec<i64>) {
            while let Some((&key, &result)) = self.pending_presents.first_key_value() {
                if result.is_none() && now - key.0 < 2 * TICKS_PER_SECOND {
                    break;
                }
                self.pending_presents.remove(&key);
                if result == Some(true) {
                    output.push(key.0);
                }
            }
        }
    }

    /// EVENT_TRACE_PROPERTIES + 尾部会话名缓冲区（固定堆栈布局，规避字节数组对齐问题）
    #[repr(C)]
    struct TracePropsBuffer {
        props: EVENT_TRACE_PROPERTIES,
        name: [u16; 32],
    }

    impl TracePropsBuffer {
        fn new(name: &[u16]) -> Self {
            let mut b: TracePropsBuffer = unsafe { std::mem::zeroed() };
            b.props.Wnode.BufferSize = size_of::<TracePropsBuffer>() as u32;
            b.props.Wnode.Guid = windows_sys::core::GUID::from_u128(0);
            b.props.Wnode.ClientContext = 1;
            b.props.Wnode.Flags = WNODE_FLAG_TRACED_GUID;
            // Bounded 4 MiB maximum. Small 8 KiB/64-buffer sessions lost events
            // under real desktop load and manufactured long frame intervals.
            b.props.BufferSize = 16; // KiB per buffer
            b.props.FlushTimer = 1; // 秒
            b.props.MinimumBuffers = 64;
            b.props.MaximumBuffers = 256;
            b.props.LogFileMode = EVENT_TRACE_REAL_TIME_MODE;
            b.props.LoggerNameOffset = size_of::<EVENT_TRACE_PROPERTIES>() as u32;
            b.props.LogFileNameOffset = 0; // 无日志文件，纯实时
            for (i, &c) in name.iter().take(b.name.len()).enumerate() {
                b.name[i] = c;
            }
            b
        }
    }

    /// 停止同名的旧 ETW 会话（按名控制，崩溃残留也会被回收）
    fn stop_session_by_name(name: &[u16]) {
        let mut buf = TracePropsBuffer::new(name);
        unsafe {
            ControlTraceW(
                CONTROLTRACE_HANDLE { Value: 0 },
                name.as_ptr(),
                &mut buf.props,
                EVENT_TRACE_CONTROL_STOP,
            );
        }
    }

    /// 解析进程名（QueryFullProcessImageNameW，PROCESS_QUERY_LIMITED_INFORMATION 无需高权限）
    fn resolve_process_name(pid: u32) -> Option<String> {
        unsafe {
            let h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, 0, pid);
            if h.is_null() {
                return None;
            }
            let mut buf = [0u16; 4096];
            let mut size = buf.len() as u32;
            let ok = QueryFullProcessImageNameW(h, 0, buf.as_mut_ptr(), &mut size);
            CloseHandle(h);
            if ok == 0 || size == 0 {
                return None;
            }
            let path = String::from_utf16_lossy(&buf[..size as usize]);
            let fname = path
                .rsplit(['\\', '/'])
                .next()
                .unwrap_or("")
                .to_string();
            if fname.is_empty() {
                return None;
            }
            Some(fname)
        }
    }

    /// 清理长期无帧的进程状态（对齐参考实现的 5min 移除）
    fn prune_stale(rt: &mut EtwRuntime, now_ticks: i64) {
        rt.procs
            .retain(|_, st| now_ticks - st.last_present < STALE_PROC_TICKS);
    }

    /// 进程去重状态 5 分钟无帧即移除（对齐参考实现的 5min 清理）
    const STALE_PROC_TICKS: i64 = 300 * 10_000_000;

    fn enable_present_events(session: CONTROLTRACE_HANDLE, provider: &windows_sys::core::GUID, ids: &[u16]) -> u32 {
        // Match the selected event descriptors from PresentMon v2.4.1. In
        // particular, never request DxgKrnl's Performance/ForceVsync/StablePower
        // keywords. Event-ID filtering happens after provider enablement.
        let (level, any, all) = if guid_eq(provider, &DXGKRNL_PROVIDER) {
            // Every selected descriptor has Base, including pre-Win11 events
            // without Present; requiring Base keeps both versions compatible.
            (TRACE_LEVEL_INFORMATION as u8, 0x0800_0001, 0x1)
        } else if guid_eq(provider, &DXGI_PROVIDER) {
            (0, 0x8000_0000_0000_0002, 0x8000_0000_0000_0002)
        } else if guid_eq(provider, &WIN32K_PROVIDER) {
            (TRACE_LEVEL_INFORMATION as u8, 0x8000_0004_0000_1000, 0x8000_0004_0000_1000)
        } else {
            return windows_sys::Win32::Foundation::ERROR_INVALID_PARAMETER;
        };
        #[repr(C)]
        struct IdFilter {
            filter_in: u8,
            reserved: u8,
            count: u16,
            ids: [u16; 10],
        }
        let mut filter = IdFilter { filter_in: 1, reserved: 0, count: ids.len() as u16, ids: [0; 10] };
        filter.ids[..ids.len()].copy_from_slice(ids);
        let descriptor = EVENT_FILTER_DESCRIPTOR {
            Ptr: &filter as *const IdFilter as u64,
            Size: (4 + ids.len() * 2) as u32,
            Type: EVENT_FILTER_TYPE_EVENT_ID,
        };
        let parameters = ENABLE_TRACE_PARAMETERS {
            Version: ENABLE_TRACE_PARAMETERS_VERSION_2,
            EnableFilterDesc: &descriptor as *const EVENT_FILTER_DESCRIPTOR as *mut EVENT_FILTER_DESCRIPTOR,
            FilterDescCount: 1,
            ..unsafe { std::mem::zeroed() }
        };
        unsafe {
            EnableTraceEx2(session, provider, EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                level, any, all, 0, &parameters)
        }
    }

    // Capture the actual provider API arguments in unit tests without enabling
    // graphics tracing on the machine running the test suite.
    #[cfg(test)]
    std::thread_local! {
        static ENABLE_REQUEST: std::cell::RefCell<Option<(u8, u64, u64, Vec<u16>)>> = const { std::cell::RefCell::new(None) };
    }
    #[cfg(test)]
    #[allow(non_snake_case)]
    unsafe fn EnableTraceEx2(_session: CONTROLTRACE_HANDLE, _provider: *const windows_sys::core::GUID,
        _control: u32, level: u8, any: u64, all: u64, _timeout: u32, parameters: *const ENABLE_TRACE_PARAMETERS) -> u32 {
        let parameters = &*parameters;
        assert_eq!(parameters.FilterDescCount, 1);
        let descriptor = &*parameters.EnableFilterDesc;
        assert_eq!(descriptor.Type, EVENT_FILTER_TYPE_EVENT_ID);
        let bytes = std::slice::from_raw_parts(descriptor.Ptr as *const u8, descriptor.Size as usize);
        assert_eq!(bytes[0], 1);
        let count = u16::from_ne_bytes([bytes[2], bytes[3]]) as usize;
        let ids = std::slice::from_raw_parts(bytes.as_ptr().add(4) as *const u16, count).to_vec();
        ENABLE_REQUEST.with(|request| *request.borrow_mut() = Some((level, any, all, ids)));
        0
    }

    fn event_u32(record: &EVENT_RECORD, name: *const u16) -> Option<u32> {
        let property = PROPERTY_DATA_DESCRIPTOR {
            PropertyName: name as u64,
            ArrayIndex: u32::MAX,
            Reserved: 0,
        };
        let mut value = 0u32;
        let status = unsafe {
            TdhGetProperty(
                record, 0, std::ptr::null(), 1, &property,
                size_of::<u32>() as u32, &mut value as *mut u32 as *mut u8,
            )
        };
        (status == 0).then_some(value)
    }

    /// ETW 事件回调：解析事件头 → QPC 归一 → 分层去重 → 目标过滤 → 统计管线
    /// 单线程串行派发（ProcessTrace 回调），EtwRuntime 位于该线程栈上。
    unsafe extern "system" fn on_event_record(record: *mut EVENT_RECORD) {
        if record.is_null() || !FPS_ACTIVE.load(Ordering::SeqCst) {
            return;
        }

        let h = &(*record).EventHeader;
        let pid = h.ProcessId;
        if pid == 0 || pid == 4 || pid == *SELF_PID.get_or_init(|| std::process::id()) {
            return;
        }
        let _target_guard = TARGET_PROCESS.read();
        if pid != TARGET_PID.load(Ordering::Relaxed) {
            return;
        }

        let id = h.EventDescriptor.Id;
        // QPC → 100ns 归一：所有窗口常量都是 100ns 单位（移植首要风险项）
        let ticks = qpc_to_100ns(h.TimeStamp);

        let is_runtime = guid_eq(&h.ProviderId, &DXGI_PROVIDER);
        let is_present = if guid_eq(&h.ProviderId, &DXGKRNL_PROVIDER) {
            is_dxg_present_id(id)
        } else if guid_eq(&h.ProviderId, &WIN32K_PROVIDER) {
            id == WIN32K_PRESENT
        } else if is_runtime {
            matches!(id, 42 | 43 | 55 | 56)
        } else {
            false
        };
        if !is_present {
            return;
        }

        // 运行时状态（由 OpenTraceW 时传入的 Context 回填到 UserContext）
        let rt = &mut *((*record).UserContext as *mut EtwRuntime);

        // 目标切换/过期归零 → 清空统计
        let gen = TARGET_GENERATION.load(Ordering::Relaxed);
        if gen != rt.last_gen {
            rt.tracker.clear();
            rt.procs.clear();
            rt.pending_presents.clear();
            rt.using_runtime = false;
            rt.last_gen = gen;
            ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
            ZERO_DOT_ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
            WINDOW_AVG_FPS.store(0, Ordering::Relaxed);
        }

        let mut completed_ticks = Vec::new();
        if is_runtime {
            // Timeout cleanup must run even when capacity rejects a Start or
            // a Stop has no matching Start, otherwise a full queue cannot recover.
            rt.drain_completed_presents(ticks, &mut completed_ticks);
            // Pair runtime start/stop on the originating thread. TDH resolves
            // field layout for both 32/64-bit processes and manifest versions.
            if matches!(id, 42 | 55) {
                rt.pending_presents.retain(|(_, thread), result| *thread != h.ThreadId || result.is_some());
                if event_u32(&*record, windows_sys::core::w!("Flags"))
                    .map(|flags| flags & 1 == 0).unwrap_or(false)
                {
                    if rt.pending_presents.len() < 256 {
                        rt.pending_presents.insert((ticks, h.ThreadId), None);
                    }
                }
            } else {
                let key = rt.pending_presents.iter()
                    .find(|((_, thread), result)| *thread == h.ThreadId && result.is_none())
                    .map(|(key, _)| *key);
                if let Some(key) = key {
                    rt.pending_presents.insert(key, Some(
                        event_u32(&*record, windows_sys::core::w!("Result")) == Some(0),
                    ));
                }
            }
            // A later thread may complete first; commit in start order.
            rt.drain_completed_presents(ticks, &mut completed_ticks);
            PRESENT_MODE.store(
                rt.procs.get(&pid).map(|state| present_mode_code(state, ticks)).unwrap_or(MODE_UNKNOWN),
                Ordering::Relaxed,
            );
            if completed_ticks.is_empty() {
                return;
            }
        } else {
            let st = rt.procs.entry(pid).or_default();
            let accepted = tier_dedup(st, id, ticks);
            PRESENT_MODE.store(present_mode_code(st, ticks), Ordering::Relaxed);
            // API submissions are authoritative when present, not the
            // display/composition cadence. Never count both as frames.
            // Keep the source for this target generation. A real 2s expiry
            // resets the generation; a legitimate slow frame must not reset Low.
            if !accepted || rt.using_runtime {
                return;
            }
            completed_ticks.push(ticks);
        }
        if rt.using_runtime != is_runtime {
            log::info!("FPS monitor: pid={pid} source={}",
                if is_runtime { "dxgi-present" } else { "kernel-estimate" });
            rt.tracker.clear();
            ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
            ZERO_DOT_ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
            WINDOW_AVG_FPS.store(0, Ordering::Relaxed);
            rt.using_runtime = is_runtime;
        }
        LAST_FRAME_TIME.store(now_unix_ms(), Ordering::Relaxed);

        // 瞬时 FPS：60 样本环形 (n-1)/duration，事件驱动逐帧刷新，无 EMA
        let mut fps = rt.tracker.instant_fps();
        for at in completed_ticks {
            if rt.tracker.ring.last_tick().map(|last| at <= last).unwrap_or(false) {
                continue;
            }
            fps = if is_runtime {
                rt.tracker.on_runtime_present(at)
            } else {
                rt.tracker.on_present(at)
            };
            if let Some(frames) = rt.diagnostic_frames.as_mut() {
                if frames.len() < 250_000 {
                    frames.push((at, is_runtime, gen));
                }
            }
        }
        SMOOTHED_FPS.store(fps.max(0.0).round() as u32, Ordering::Relaxed);

        // 每 ~1 秒计算 1% / 0.1% Low（32768 帧排序不可高频跑，CPU 预算硬指标）
        if rt.last_low_calc.elapsed() >= Duration::from_secs(1) {
            let wavg = rt.tracker.window_avg_fps();
            WINDOW_AVG_FPS.store(wavg.max(0.0).round() as u32, Ordering::Relaxed);
            let one_low = rt.tracker.one_percent_low();
            store_low(&ONE_PCT_LOW_FPS, one_low);
            let z_one_low = rt.tracker.point_one_percent_low();
            store_low(&ZERO_DOT_ONE_PCT_LOW_FPS, z_one_low);
            rt.last_low_calc = Instant::now();
            prune_stale(rt, ticks);
        }
    }

    /// 单次 ETW 会话：创建 → 启用 Provider → 阻塞消费 → 清理。
    /// 返回 false 表示会话无法继续（需彻底放弃或重试无意义）。
    fn run_etw_session() -> bool {
        let mut name = session_name();
        // 预清理：停止可能残留的同名会话（崩溃/上次退出异常）
        stop_session_by_name(&name);

        // 1. 创建实时私有会话
        let mut session = CONTROLTRACE_HANDLE { Value: 0 };
        let mut started = false;
        for _attempt in 0..2 {
            let mut buf = TracePropsBuffer::new(&name);
            let r = unsafe {
                StartTraceW(
                    &mut session,
                    name.as_ptr(),
                    &mut buf.props as *mut EVENT_TRACE_PROPERTIES,
                )
            };
            if r == 0 {
                started = true;
                SESSION_HANDLE.store(session.Value, Ordering::SeqCst);
                LAST_TRACE_LOSS.store(0, Ordering::SeqCst);
                break;
            }
            if r == 183 {
                // ERROR_ALREADY_EXISTS：同名会话还在，停掉再试一次
                stop_session_by_name(&name);
                continue;
            }
            if r == 5 {
                // ERROR_ACCESS_DENIED
                log::error!("FPS监控: ETW 会话创建失败（需要管理员权限），错误码 {r}");
                return false;
            }
            log::error!("FPS监控: ETW 会话创建失败，错误码 {r}");
            return false;
        }
        if !started {
            log::error!("FPS监控: 无法创建 ETW 会话");
            return false;
        }

        // DXGI supplies application submissions; kernel providers remain fallback.
        let mut enabled = false;
        let runtime_status = enable_present_events(session, &DXGI_PROVIDER, &[42, 43, 55, 56]);
        if runtime_status == 0 {
            enabled = true;
        } else {
            log::warn!("FPS监控: DXGI Provider 不可用，使用内核帧源 code={runtime_status}");
        }
        if enable_present_events(session, &DXGKRNL_PROVIDER, &[
            PRESENT, PRESENT_HISTORY_START, PRESENT_HISTORY_DETAILED, BLT,
            MMIO_FLIP, MMIO_FLIP_MPO, MMIO_FLIP_MPO3, FLIP, FLIP_MPO, INDEPENDENT_FLIP,
        ]) != 0
        {
            log::warn!("FPS监控: 启用 DxgKrnl Provider 失败");
        } else {
            enabled = true;
        }
        if enable_present_events(session, &WIN32K_PROVIDER, &[WIN32K_PRESENT]) != 0
        {
            log::warn!("FPS监控: 启用 Win32k Provider 失败");
        } else {
            enabled = true;
        }
        if !enabled {
            log::error!("FPS监控: 所有 Provider 均启用失败，放弃本会话");
            stop_session_by_name(&name);
            SESSION_HANDLE.store(0, Ordering::SeqCst);
            return false;
        }

        log::info!("FPS监控: ETW 会话已启动（DXGI/DxgKrnl/Win32k，前台切换不重启）");

        // 3. 打开并阻塞消费（回调中完成统计管线）
        let mut runtime = EtwRuntime::new();
        let diagnostic_path = std::env::var("FRAMESCOPE_FPS_TRACE_PATH").ok();
        if diagnostic_path.is_some() {
            runtime.diagnostic_frames = Some(Vec::with_capacity(250_000));
        }
        let mut logfile: EVENT_TRACE_LOGFILEW = unsafe { std::mem::zeroed() };
        logfile.LoggerName = name.as_mut_ptr();
        logfile.Anonymous1.ProcessTraceMode =
            PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD | PROCESS_TRACE_MODE_RAW_TIMESTAMP;
        logfile.Anonymous2.EventRecordCallback = Some(on_event_record);
        logfile.Context = (&mut runtime as *mut EtwRuntime) as *mut core::ffi::c_void;

        let process_handle = unsafe { OpenTraceW(&mut logfile) };
        if process_handle.Value == u64::MAX {
            log::error!("FPS监控: OpenTraceW 失败");
            stop_session_by_name(&name);
            SESSION_HANDLE.store(0, Ordering::SeqCst);
            return false;
        }
        PROCESS_HANDLE.store(process_handle.Value, Ordering::SeqCst);
        if !FPS_ACTIVE.load(Ordering::SeqCst) {
            unsafe { CloseTrace(process_handle); }
            PROCESS_HANDLE.store(0, Ordering::SeqCst);
            stop_session_by_name(&name);
            SESSION_HANDLE.store(0, Ordering::SeqCst);
            return false;
        }

        let handles = [process_handle];
        let status = unsafe {
            ProcessTrace(
                handles.as_ptr(),
                1,
                std::ptr::null(),
                std::ptr::null(),
            )
        };
        unsafe {
            CloseTrace(process_handle);
        }
        PROCESS_HANDLE.store(0, Ordering::SeqCst);
        stop_session_by_name(&name);
        SESSION_HANDLE.store(0, Ordering::SeqCst);
        if let Some(path) = diagnostic_path {
            if let Err(error) = write_frame_trace(&runtime, &path) {
                log::warn!("FPS diagnostic trace: {error}");
            }
        }

        if status != 0 {
            log::warn!("FPS监控: ProcessTrace 退出 code={status}");
        } else {
            log::info!("FPS监控: ETW 会话已停止");
        }

        // 会话自然结束时若仍处于活跃状态 → 需要重连
        FPS_ACTIVE.load(Ordering::SeqCst)
    }

    /// ETW 会话生命周期主循环（复刻旧方案的「重试上限 + 30s 宽限」逻辑）
    fn fps_monitor_main() {
        const MAX_RESTARTS: u32 = 5;
        let mut restart_count = 0u32;

        while FPS_ACTIVE.load(Ordering::SeqCst) {
            let session_start = Instant::now();
            let keep = run_etw_session();

            if !keep {
                log::error!("FPS监控: ETW 会话无法建立/恢复，停止监控");
                break;
            }
            if !FPS_ACTIVE.load(Ordering::SeqCst) {
                break;
            }

            // 上次会话运行超过 30 秒说明是正常运行后断开，重置重启计数器
            if session_start.elapsed() >= Duration::from_secs(30) {
                restart_count = 0;
            }
            restart_count += 1;
            if restart_count > MAX_RESTARTS {
                log::error!("FPS监控: ETW 会话重连超过 {MAX_RESTARTS} 次，放弃");
                break;
            }
            log::warn!("FPS监控: ETW 会话中断，2秒后重连 (第{restart_count}/{MAX_RESTARTS})");
            thread::sleep(Duration::from_secs(2));
        }

        FPS_ACTIVE.store(false, Ordering::SeqCst);
        log::info!("FPS监控: ETW 采集线程退出");
    }

    /// 启动 ETW 采集线程（由 start_fps_monitor 调用）
    pub fn spawn_monitor() {
        *MONITOR_THREAD.lock().unwrap() = Some(thread::spawn(fps_monitor_main));
    }

    /// 停止 ETW 采集并等待线程退出（由 stop_fps_monitor 调用）
    ///
    /// 停止顺序：FPS_ACTIVE=false（已由调用方设置）→ 按名停会话 → 兜底 CloseTrace →
    /// join 线程，确保会话无残留。
    pub fn stop_and_join() {
        let worker = MONITOR_THREAD.lock().unwrap().take();
        let Some(worker) = worker else { return };
        // Never stop a similarly named session owned by another service instance.
        let session = SESSION_HANDLE.load(Ordering::SeqCst);
        if session != 0 {
            let mut props = TracePropsBuffer::new(&session_name());
            unsafe {
                ControlTraceW(
                    CONTROLTRACE_HANDLE { Value: session },
                    std::ptr::null(), &mut props.props, EVENT_TRACE_CONTROL_STOP,
                );
            }
        }
        // 兜底：直接关闭 trace 处理句柄强制 ProcessTrace 返回
        let ph = PROCESS_HANDLE.load(Ordering::SeqCst);
        if ph != 0 {
            unsafe {
                CloseTrace(PROCESSTRACE_HANDLE { Value: ph });
            }
        }
        let _ = worker.join();
        SESSION_HANDLE.store(0, Ordering::SeqCst);
    }

    pub fn trace_lost_events() -> bool {
        let session = SESSION_HANDLE.load(Ordering::SeqCst);
        if session == 0 { return false; }
        let mut props = TracePropsBuffer::new(&session_name());
        let status = unsafe {
            ControlTraceW(CONTROLTRACE_HANDLE { Value: session }, std::ptr::null(),
                &mut props.props, EVENT_TRACE_CONTROL_QUERY)
        };
        if status != 0 { return false; }
        let lost = props.props.EventsLost as u64 + props.props.RealTimeBuffersLost as u64;
        let previous = LAST_TRACE_LOSS.swap(lost, Ordering::SeqCst);
        if lost > previous {
            log::warn!("FPS monitor: ETW loss delta={}; events_lost={} realtime_buffers_lost={}; retained statistics require frame-source validation",
                lost - previous, props.props.EventsLost, props.props.RealTimeBuffersLost);
            return true;
        }
        false
    }

    // ---- ETW 诊断子命令（移植自 tubatools DxTraceDiag/Program.cs） ----
    // 用途：N 秒抓取、按 pid 打事件 ID 分布与帧间隔中位数 —— 定位「事件到底发没发」。

    struct DiagState {
        counts: HashMap<u32, HashMap<(u8, u16), u64>>,
        /// Keep provider/event streams separate; their combined cadence is not FPS.
        stamps: HashMap<(u32, u8, u16), Vec<i64>>,
        target: Option<(u32, EtwRuntime)>,
        max_target_fps: u32,
    }

    fn write_frame_trace(runtime: &EtwRuntime, path: &str) -> std::io::Result<()> {
        use std::io::Write;
        let file = std::fs::OpenOptions::new().write(true).create_new(true).open(path)?;
        let mut output = std::io::BufWriter::new(file);
        writeln!(output, "ticks_100ns,runtime,generation")?;
        for &(ticks, source, generation) in runtime.diagnostic_frames.iter().flatten() {
            writeln!(output, "{ticks},{source},{generation}")?;
        }
        output.flush()
    }

    impl DiagState {
        fn new() -> Self {
            Self {
                counts: HashMap::new(),
                stamps: HashMap::new(),
                target: None,
                max_target_fps: 0,
            }
        }
    }

    unsafe extern "system" fn diag_on_event(record: *mut EVENT_RECORD) {
        if record.is_null() {
            return;
        }
        let h = &(*record).EventHeader;
        let pid = h.ProcessId;
        if pid == 0 || pid == 4 {
            return;
        }
        let is_dxg = guid_eq(&h.ProviderId, &DXGKRNL_PROVIDER);
        let is_win32k = guid_eq(&h.ProviderId, &WIN32K_PROVIDER);
        let is_dxgi = guid_eq(&h.ProviderId, &DXGI_PROVIDER);
        if !is_dxg && !is_win32k && !is_dxgi {
            return;
        }
        let id = h.EventDescriptor.Id;
        let provider = if is_dxgi { 2 } else if is_win32k { 1 } else { 0 };
        let state = &mut *((*record).UserContext as *mut DiagState);
        if let Some((target_pid, runtime)) = state.target.as_mut() {
            if pid == *target_pid {
                let mut forwarded = std::ptr::read(record);
                forwarded.UserContext = runtime as *mut EtwRuntime as *mut core::ffi::c_void;
                on_event_record(&mut forwarded);
                state.max_target_fps = state.max_target_fps.max(get_cached_fps().unwrap_or(0));
            }
        }
        *state.counts.entry(pid).or_default().entry((provider, id)).or_insert(0) += 1;
        if (is_dxg && is_dxg_present_id(id)) || (is_win32k && id == WIN32K_PRESENT)
            || (is_dxgi && matches!(id, 42 | 43 | 55 | 56))
        {
            let stamps = state
                .stamps
                .entry((pid, provider, id))
                .or_default();
            if stamps.len() < 20_000 {
                stamps.push(qpc_to_100ns(h.TimeStamp));
            }
        }
    }

    /// 诊断会话入口：抓取 seconds 秒后打印报告。返回进程退出码。
    pub fn run_diag(seconds: u64, target_pid: Option<u32>, frame_csv: Option<&str>) -> i32 {
        let seconds = seconds.clamp(1, 120);
        let mut name: Vec<u16> = format!("FrameScope_Diag_{}\0", std::process::id()).encode_utf16().collect();

        let mut session = CONTROLTRACE_HANDLE { Value: 0 };
        let mut buf = TracePropsBuffer::new(&name);
        let r = unsafe { StartTraceW(&mut session, name.as_ptr(), &mut buf.props) };
        if r != 0 {
            if r == 5 {
                eprintln!("ETW 诊断: 会话创建失败（错误码 5）——需要以管理员身份运行");
            } else {
                eprintln!("ETW 诊断: 会话创建失败，错误码 {r}");
            }
            return 2;
        }
        let dxg_ok = unsafe {
            EnableTraceEx2(
                session,
                &DXGKRNL_PROVIDER,
                EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                TRACE_LEVEL_VERBOSE as u8,
                u64::MAX,
                0,
                0,
                std::ptr::null(),
            )
        } == 0;
        let dxgi_ok = unsafe {
            EnableTraceEx2(
                session, &DXGI_PROVIDER, EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                TRACE_LEVEL_VERBOSE as u8, 0x2, 0, 0, std::ptr::null(),
            )
        } == 0;
        let win32k_ok = unsafe {
            EnableTraceEx2(
                session,
                &WIN32K_PROVIDER,
                EVENT_CONTROL_CODE_ENABLE_PROVIDER,
                TRACE_LEVEL_VERBOSE as u8,
                u64::MAX,
                0,
                0,
                std::ptr::null(),
            )
        } == 0;
        eprintln!("ETW providers: DXGI={dxgi_ok} DxgKrnl={dxg_ok} Win32k={win32k_ok}");
        if !dxg_ok && !win32k_ok && !dxgi_ok {
            stop_session_by_name(&name);
            return 2;
        }

        let mut state = DiagState::new();
        if let Some(pid) = target_pid.filter(|pid| *pid > 4) {
            let target_name = resolve_process_name(pid).unwrap_or_else(|| "<unknown>".into());
            select_target(pid, &target_name);
            FPS_ACTIVE.store(true, Ordering::SeqCst);
            let mut runtime = EtwRuntime::new();
            if frame_csv.is_some() {
                runtime.diagnostic_frames = Some(Vec::with_capacity(250_000));
            }
            state.target = Some((pid, runtime));
            eprintln!("ETW diagnostic target: pid={pid} name={target_name} (explicit PID, not foreground tracking)");
        }
        let mut logfile: EVENT_TRACE_LOGFILEW = unsafe { std::mem::zeroed() };
        logfile.LoggerName = name.as_mut_ptr();
        logfile.Anonymous1.ProcessTraceMode =
            PROCESS_TRACE_MODE_REAL_TIME | PROCESS_TRACE_MODE_EVENT_RECORD | PROCESS_TRACE_MODE_RAW_TIMESTAMP;
        logfile.Anonymous2.EventRecordCallback = Some(diag_on_event);
        logfile.Context = (&mut state as *mut DiagState) as *mut core::ffi::c_void;

        let process_handle = unsafe { OpenTraceW(&mut logfile) };
        if process_handle.Value == u64::MAX {
            eprintln!("ETW 诊断: OpenTraceW 失败");
            stop_session_by_name(&name);
            FPS_ACTIVE.store(false, Ordering::SeqCst);
            return 2;
        }

        // 定时停止：ProcessTrace 阻塞期间由后台线程按名停会话
        let stop_name = name.clone();
        thread::spawn(move || {
            thread::sleep(Duration::from_secs(seconds.max(1)));
            stop_session_by_name(&stop_name);
        });

        let handles = [process_handle];
        let status = unsafe { ProcessTrace(handles.as_ptr(), 1, std::ptr::null(), std::ptr::null()) };
        unsafe {
            CloseTrace(process_handle);
        }
        stop_session_by_name(&name);
        FPS_ACTIVE.store(false, Ordering::SeqCst);
        if status != 0 {
            eprintln!("ETW 诊断: ProcessTrace 退出 code={status}");
            return 2;
        }

        print_diag_report(&state, seconds);
        if let Some(path) = frame_csv {
            if let Some((_, runtime)) = state.target.as_ref() {
                if let Err(error) = write_frame_trace(runtime, path) {
                    eprintln!("diagnostic CSV: {error}");
                    return 2;
                }
            }
        }
        0
    }

    fn median_interval_ms(stamps: &[i64]) -> Option<(f64, f64, f64)> {
        if stamps.len() < 3 {
            return None;
        }
        let mut deltas: Vec<f64> = stamps
            .windows(2)
            .map(|w| (w[1] - w[0]).max(0) as f64 / 10_000.0)
            .collect();
        deltas.sort_by(|a, b| a.partial_cmp(b).unwrap_or(std::cmp::Ordering::Equal));
        let pick = |p: f64| deltas[((deltas.len() - 1) as f64 * p) as usize];
        Some((pick(0.5), pick(0.01), pick(0.99)))
    }

    fn print_diag_report(state: &DiagState, seconds: u64) {
        println!("=== FrameScope ETW FPS 诊断（抓取 {seconds}s，QPC 频率 {}Hz）===", qpc_frequency());
        if let Some((pid, runtime)) = state.target.as_ref() {
            println!("TARGET pid={pid} source={} last_fps={} max_fps={} accepted_intervals={} low1={:?} low01={:?}",
                if runtime.using_runtime { "dxgi-present" } else { "kernel-estimate" },
                get_cached_fps().unwrap_or(0), state.max_target_fps, runtime.tracker.total_frames(),
                get_cached_1low_fps(), get_cached_01low_fps());
        }
        let mut pids: Vec<(u32, u64)> = state
            .counts
            .iter()
            .map(|(pid, ids)| {
                (*pid, ids.values().sum::<u64>())
            })
            .collect();
        pids.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(&b.0)));
        if pids.is_empty() {
            println!("（未收到 DXGI/DxgKrnl/Win32k 事件）");
            return;
        }
        for (pid, total) in pids.iter().take(24) {
            let name = resolve_process_name(*pid).unwrap_or_else(|| "<unknown>".into());
            println!("\nPID {pid} ({}) — events={total}", name);
            let mut ids: Vec<((u8, u16), u64)> = state
                .counts
                .get(pid)
                .map(|m| m.iter().map(|(k, v)| (*k, *v)).collect())
                .unwrap_or_default();
            ids.sort_by(|a, b| b.1.cmp(&a.1).then(a.0.cmp(&b.0)));
            for ((provider, id), n) in ids {
                let provider_name = match provider { 2 => "DXGI", 1 => "Win32k", _ => "DxgKrnl" };
                print!("  {provider_name}/0x{id:04X}: {n}");
                if let Some(stamps) = state.stamps.get(&(*pid, provider, id)) {
                    if let Some((median, lo, hi)) = median_interval_ms(stamps) {
                        print!("  event interval median={median:.3}ms p01={lo:.3}ms p99={hi:.3}ms samples={}", stamps.len());
                    }
                }
                println!();
            }
        }
    }

    #[cfg(test)]
    mod callback_tests {
        use super::*;

        static CALLBACK_TEST_LOCK: Mutex<()> = Mutex::new(());

        #[test]
        fn stopping_inactive_monitor_joins_owned_worker_without_touching_other_sessions() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            FPS_ACTIVE.store(false, Ordering::SeqCst);
            *MONITOR_THREAD.lock().unwrap() = Some(std::thread::spawn(|| {
                std::thread::sleep(Duration::from_millis(20));
            }));
            stop_fps_monitor();
            let leftover = MONITOR_THREAD.lock().unwrap().take();
            let had_leftover = leftover.is_some();
            if let Some(handle) = leftover { handle.join().unwrap(); }
            assert!(!had_leftover, "failed worker must be joined even after it cleared active");
        }

        #[test]
        fn present_event_without_mode_payload_does_not_prove_exclusive() {
            let mut state = TierState::default();
            tier_dedup(&mut state, PRESENT, 100 * TICKS_PER_SECOND);
            assert_eq!(present_mode_code(&state, 100 * TICKS_PER_SECOND), MODE_UNKNOWN);
        }

        struct Reset;

        impl Drop for Reset {
            fn drop(&mut self) {
                FPS_ACTIVE.store(false, Ordering::SeqCst);
                CURRENT_FG_PID.store(0, Ordering::Relaxed);
                TARGET_PID.store(0, Ordering::Relaxed);
                SMOOTHED_FPS.store(0, Ordering::Relaxed);
                ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
                ZERO_DOT_ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
                WINDOW_AVG_FPS.store(0, Ordering::Relaxed);
                LAST_FRAME_TIME.store(0, Ordering::Relaxed);
                *TARGET_PROCESS.write() = String::new();
            }
        }

        fn setup(pid: u32) -> (EtwRuntime, Reset) {
            FPS_ACTIVE.store(true, Ordering::SeqCst);
            CURRENT_FG_PID.store(pid, Ordering::Relaxed);
            TARGET_PID.store(pid, Ordering::Relaxed);
            TARGET_GENERATION.store(0, Ordering::Relaxed);
            *TARGET_PROCESS.write() = "test-game.exe".into();
            (EtwRuntime::new(), Reset)
        }

        fn emit(rt: &mut EtwRuntime, pid: u32, ticks: i64) {
            let mut event: EVENT_RECORD = unsafe { std::mem::zeroed() };
            event.EventHeader.ProcessId = pid;
            event.EventHeader.ProviderId = DXGKRNL_PROVIDER;
            event.EventHeader.EventDescriptor.Id = PRESENT_HISTORY_START;
            event.EventHeader.TimeStamp =
                ((ticks as i128) * qpc_frequency() as i128 / TICKS_PER_SECOND as i128) as i64;
            event.UserContext = rt as *mut EtwRuntime as *mut core::ffi::c_void;
            unsafe { on_event_record(&mut event) };
        }

        fn emit_dxgi(rt: &mut EtwRuntime, ticks: i64, flags: u32, result: u32) {
            emit_dxgi_half(rt, ticks, 7, 42, flags);
            emit_dxgi_half(rt, ticks, 7, 43, result);
        }

        fn emit_dxgi_half(rt: &mut EtwRuntime, ticks: i64, thread: u32, id: u16, value: u32) {
            emit_dxgi_layout(rt, ticks, thread, id, value, false);
        }

        fn emit_dxgi_layout(rt: &mut EtwRuntime, ticks: i64, thread: u32, id: u16, value: u32, is_32bit: bool) {
            let mut event: EVENT_RECORD = unsafe { std::mem::zeroed() };
            event.EventHeader.ProcessId = 42;
            event.EventHeader.ThreadId = thread;
            event.EventHeader.Flags = if is_32bit {
                EVENT_HEADER_FLAG_32_BIT_HEADER as u16
            } else {
                EVENT_HEADER_FLAG_64_BIT_HEADER as u16
            };
            event.EventHeader.ProviderId = windows_sys::core::GUID::from_u128(
                0xca11c036_0102_4a2d_a6ad_f03cfed5d3c9,
            );
            event.EventHeader.TimeStamp =
                ((ticks as i128) * qpc_frequency() as i128 / TICKS_PER_SECOND as i128) as i64;
            event.UserContext = rt as *mut EtwRuntime as *mut core::ffi::c_void;
            let mut start = Vec::new();
            if is_32bit {
                start.extend_from_slice(&1234u32.to_le_bytes());
            } else {
                start.extend_from_slice(&1234u64.to_le_bytes());
            }
            start.extend_from_slice(&value.to_le_bytes());
            start.extend_from_slice(&[0u8; 12]);
            let mut stop = value.to_le_bytes();
            event.EventHeader.EventDescriptor.Id = id;
            if matches!(id, 42 | 55) {
                event.UserData = start.as_mut_ptr() as *mut core::ffi::c_void;
                event.UserDataLength = start.len() as u16;
            } else {
                event.UserData = stop.as_mut_ptr() as *mut core::ffi::c_void;
                event.UserDataLength = stop.len() as u16;
            }
            unsafe { on_event_record(&mut event) };
        }

        #[test]
        fn callback_reads_32bit_and_multiplane_runtime_payloads() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            for (is_32bit, start, stop) in [(true, 42, 43), (false, 55, 56), (true, 55, 56)] {
                let (mut rt, _reset) = setup(42);
                for i in 0..100 {
                    let at = 100 * TICKS_PER_SECOND + i * 100_000;
                    emit_dxgi_layout(&mut rt, at, 7, start, 0, is_32bit);
                    emit_dxgi_layout(&mut rt, at + 10, 7, stop, 0, is_32bit);
                }
                assert_eq!(get_cached_fps(), Some(100), "32bit={is_32bit} event={start}");
            }
        }

        #[test]
        fn successful_high_rate_runtime_intervals_are_not_discarded_as_duplicates() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            for i in 0..13201 {
                emit_dxgi(&mut rt, 100 * TICKS_PER_SECOND + i * TICKS_PER_SECOND / 1200, 0, 0);
            }
            assert_eq!(get_cached_fps(), Some(1200));
            assert!((rt.tracker.one_percent_low() - 1200.0).abs() < 1.0);
        }

        #[test]
        fn one_hz_runtime_does_not_alternate_sources_or_reset() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            for i in 0..20 {
                let at = (100 + i) * TICKS_PER_SECOND;
                emit_dxgi_half(&mut rt, at, 7, 42, 0);
                emit(&mut rt, 42, at + 10);
                emit_dxgi_half(&mut rt, at + 20, 7, 43, 0);
            }
            assert_eq!(get_cached_fps(), Some(1));
            assert_eq!(rt.tracker.total_frames(), 19);
        }

        #[test]
        fn long_runtime_frame_keeps_low_history_despite_interleaved_kernel_event() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            let base = 100 * TICKS_PER_SECOND;
            for i in 0..3301 {
                emit_dxgi(&mut rt, base + i * 100_000, 0, 0);
            }
            let before = rt.tracker.total_frames();
            let next = base + 33 * TICKS_PER_SECOND + 7_000_000;
            emit_dxgi_half(&mut rt, next, 7, 42, 0);
            emit(&mut rt, 42, next + 100);
            emit_dxgi_half(&mut rt, next + 200, 7, 43, 0);
            assert_eq!(rt.tracker.total_frames(), before + 1);
            assert!(rt.tracker.one_percent_low() > 0.0);
            assert!(rt.tracker.one_percent_low() < 100.0);
        }

        #[test]
        fn overlapping_threads_commit_successful_presents_in_start_order() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            let base = 100 * TICKS_PER_SECOND;
            emit_dxgi(&mut rt, base, 0, 0);
            emit_dxgi_half(&mut rt, base + 10_000, 7, 42, 0);
            emit_dxgi_half(&mut rt, base + 20_000, 8, 42, 0);
            emit_dxgi_half(&mut rt, base + 30_000, 8, 43, 0);
            emit_dxgi_half(&mut rt, base + 40_000, 7, 43, 0);
            assert_eq!(get_cached_fps(), Some(1000));
            assert_eq!(rt.tracker.total_frames(), 2);
        }

        #[test]
        fn completed_frames_survive_thread_reuse_while_an_earlier_thread_is_pending() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            let base = 100 * TICKS_PER_SECOND;
            emit_dxgi(&mut rt, base, 0, 0);
            emit_dxgi_half(&mut rt, base + 100_000, 7, 42, 0);
            emit_dxgi_half(&mut rt, base + 200_000, 8, 42, 0);
            emit_dxgi_half(&mut rt, base + 210_000, 8, 43, 0);
            emit_dxgi_half(&mut rt, base + 300_000, 8, 42, 0);
            emit_dxgi_half(&mut rt, base + 310_000, 8, 43, 0);
            emit_dxgi_half(&mut rt, base + 400_000, 7, 43, 0);
            assert_eq!(rt.tracker.total_frames(), 3);
            assert_eq!(get_cached_fps(), Some(100));
        }

        #[test]
        fn missing_stop_cannot_permanently_block_a_full_runtime_queue() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            let base = 100 * TICKS_PER_SECOND;
            emit_dxgi_half(&mut rt, base, 7, 42, 0);
            for i in 1..2000 {
                let at = base + i * TICKS_PER_SECOND / 600;
                emit_dxgi_half(&mut rt, at, 8, 42, 0);
                emit_dxgi_half(&mut rt, at + 10, 8, 43, 0);
            }
            assert!(rt.tracker.total_frames() > 255, "timed-out head must not deadlock publication");
            assert_eq!(get_cached_fps(), Some(600));
            assert!(rt.pending_presents.len() < 256);
        }

        #[test]
        fn callback_counts_600_runtime_presents_despite_200hz_composition() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            for i in 0..1200 {
                let ticks = 100 * TICKS_PER_SECOND + i * TICKS_PER_SECOND / 600;
                emit_dxgi(&mut rt, ticks, 0, 0);
                if i % 3 == 0 {
                    let mut event: EVENT_RECORD = unsafe { std::mem::zeroed() };
                    event.EventHeader.ProcessId = 42;
                    event.EventHeader.ProviderId = WIN32K_PROVIDER;
                    event.EventHeader.EventDescriptor.Id = WIN32K_PRESENT;
                    event.EventHeader.TimeStamp =
                        ((ticks as i128) * qpc_frequency() as i128 / TICKS_PER_SECOND as i128) as i64;
                    event.UserContext = &mut rt as *mut EtwRuntime as *mut core::ffi::c_void;
                    unsafe { on_event_record(&mut event) };
                }
            }
            assert_eq!(get_cached_fps(), Some(600));
            let before = rt.tracker.total_frames();
            emit_dxgi(&mut rt, 103 * TICKS_PER_SECOND, 1, 0);
            emit_dxgi(&mut rt, 104 * TICKS_PER_SECOND, 0, 0x887a000a);
            emit_dxgi(&mut rt, 105 * TICKS_PER_SECOND, 0, 0x087a0001);
            assert_eq!(rt.tracker.total_frames(), before, "TEST, failed and occluded calls are not frames");
        }

        #[test]
        fn switching_target_clears_statistics_before_new_frames_arrive() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            emit(&mut rt, 42, 100 * TICKS_PER_SECOND);
            emit(&mut rt, 42, 100 * TICKS_PER_SECOND + 100_000);
            assert_eq!(get_cached_fps(), Some(100));
            select_target(43, "test-game.exe");
            assert_eq!(get_cached_fps(), None);
            assert_eq!(get_cached_1low_fps(), None);
            emit(&mut rt, 42, 100 * TICKS_PER_SECOND + 200_000);
            assert_eq!(get_cached_fps(), None);
            emit(&mut rt, 43, 101 * TICKS_PER_SECOND);
            assert_eq!(get_cached_fps(), None);
            emit(&mut rt, 43, 101 * TICKS_PER_SECOND + 50_000);
            assert_eq!(get_cached_fps(), Some(200));
        }

        #[test]
        fn runtime_frames_expire_old_kernel_mode_evidence() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            let at = 100 * TICKS_PER_SECOND;
            rt.procs.entry(42).or_default().last_win32k = at;
            PRESENT_MODE.store(MODE_COMPOSITED, Ordering::Relaxed);
            emit_dxgi(&mut rt, at + 2 * TICKS_PER_SECOND, 0, 0);
            assert_eq!(get_cached_present_mode(), None);
        }

        #[test]
        fn trace_explicitly_requests_the_qpc_clock_used_by_callback() {
            let props = TracePropsBuffer::new(&session_name());
            assert_eq!(props.props.Wnode.ClientContext, 1);
            assert_ne!(props.props.Wnode.Flags & WNODE_FLAG_TRACED_GUID, 0);
        }

        #[test]
        fn provider_enable_excludes_unneeded_driver_controls_and_keeps_frame_events() {
            let selected = [PRESENT, PRESENT_HISTORY_START, PRESENT_HISTORY_DETAILED, BLT,
                MMIO_FLIP, MMIO_FLIP_MPO, MMIO_FLIP_MPO3, FLIP, FLIP_MPO, INDEPENDENT_FLIP];
            enable_present_events(CONTROLTRACE_HANDLE { Value: 1 }, &DXGKRNL_PROVIDER, &selected);
            let (level, any, all, ids) = ENABLE_REQUEST.with(|request| request.borrow_mut().take().unwrap());
            assert_eq!(any & (0x4000_0000_0000_0000 | 0x8 | 0x1000), 0,
                "FPS collection must not request Performance, ForceVsync or StablePower");
            assert_eq!(level, TRACE_LEVEL_INFORMATION as u8);
            assert_eq!(ids, selected);
            // The selected events all carry Base. Older Windows descriptors
            // omit Present; both old and new events must survive the filter.
            for keyword in [0x4000_0000_0000_0001u64, 0x4000_0000_0800_0001] {
                assert_ne!(keyword & any, 0);
                assert_eq!(keyword & all, all);
            }
            for (provider, selected, keyword, max_level) in [
                (DXGI_PROVIDER, vec![42,43,55,56], 0x8000_0000_0000_0002u64, 0u8),
                (WIN32K_PROVIDER, vec![WIN32K_PRESENT], 0x8000_0004_0000_1000u64, 4u8),
            ] {
                enable_present_events(CONTROLTRACE_HANDLE { Value: 1 }, &provider, &selected);
                let (level, any, all, ids) = ENABLE_REQUEST.with(|request| request.borrow_mut().take().unwrap());
                assert_eq!(ids, selected);
                assert_eq!(level, max_level);
                assert_eq!(any, keyword);
                assert_eq!(all, keyword);
            }
        }

        #[test]
        fn diagnostics_keeps_provider_identity_for_colliding_event_ids() {
            let mut state = DiagState::new();
            let mut event: EVENT_RECORD = unsafe { std::mem::zeroed() };
            event.EventHeader.ProcessId = 42;
            event.EventHeader.EventDescriptor.Id = 42;
            event.UserContext = &mut state as *mut DiagState as *mut core::ffi::c_void;
            for provider in [DXGI_PROVIDER, DXGKRNL_PROVIDER] {
                event.EventHeader.ProviderId = provider;
                unsafe { diag_on_event(&mut event) };
            }
            assert_eq!(state.counts.get(&42).unwrap().len(), 2);
        }

        #[test]
        fn callback_does_not_mix_background_instance_with_same_executable() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            for i in 0..100 {
                emit(&mut rt, 42, 100 * TICKS_PER_SECOND + i * 100_000);
            }
            let expected = get_cached_fps();
            for i in 0..100 {
                emit(&mut rt, 43, 101 * TICKS_PER_SECOND + i * 20_000);
            }
            assert_eq!(get_cached_fps(), expected, "background PID must not replace target FPS");
        }

        #[test]
        fn callback_clears_low_cache_when_sparse_stream_loses_required_samples() {
            let _lock = CALLBACK_TEST_LOCK.lock().unwrap_or_else(|e| e.into_inner());
            let (mut rt, _reset) = setup(42);
            let base = 100 * TICKS_PER_SECOND;
            for i in 0..3301 {
                emit(&mut rt, 42, base + i * 100_000);
            }
            rt.last_low_calc = Instant::now() - Duration::from_secs(2);
            emit(&mut rt, 42, base + 3301 * 100_000);
            assert!(get_cached_1low_fps().is_some());
            assert!(get_cached_01low_fps().is_some());
            for i in 1..=40 {
                rt.last_low_calc = Instant::now() - Duration::from_secs(2);
                emit(&mut rt, 42, base + 34 * TICKS_PER_SECOND + i * TICKS_PER_SECOND);
            }
            assert_eq!(get_cached_1low_fps(), None);
            assert_eq!(get_cached_01low_fps(), None);
        }
    }
}

// ============ 进程追踪线程 ============

/// 进程追踪线程：追踪前台窗口，更新 TARGET_PROCESS，检测 FPS 过期归零
///
/// - 500ms 轮询兜底（Hook 已足够可靠，轮询仅兜底）
/// - 去抖动 500ms（减少 Alt+Tab 抖动）
fn process_tracker_loop() {
    use sysinfo::{Pid, System};

    let mut sys = System::new();
    let mut last_pid: u32 = 0;

    // 前台切换去抖动状态
    let mut pending_target: Option<(u32, String)> = None;
    let mut pending_since: Option<Instant> = None;
    const DEBOUNCE_DELAY: Duration = Duration::from_millis(500);
    // 目标进程最近一次确认呈现模式（问题 6 方案 E 的日志去抖）
    let mut last_logged_mode: u8 = 0;

    while FPS_ACTIVE.load(Ordering::SeqCst) {
        thread::sleep(Duration::from_millis(500));
        if !FPS_ACTIVE.load(Ordering::SeqCst) {
            break;
        }

        #[cfg(target_os = "windows")]
        // ETW loss is diagnostic information. It must not erase an otherwise
        // valid 10/30-second Low window: under desktop load a lost provider
        // buffer is common, and clearing here made Low stay unavailable forever.
        let _ = etw::trace_lost_events();

        // 1. 兜底轮询前台窗口（防止 Hook 漏发）
        #[cfg(target_os = "windows")]
        unsafe {
            use windows_sys::Win32::UI::WindowsAndMessaging::*;
            let hwnd = GetForegroundWindow();
            if !hwnd.is_null() {
                let overlay = OVERLAY_HWND.load(Ordering::Relaxed) as usize;
                if overlay == 0 || hwnd as usize != overlay {
                    let mut pid = 0u32;
                    GetWindowThreadProcessId(hwnd, &mut pid);
                    if pid != 0 {
                        CURRENT_FG_PID.store(pid, Ordering::Relaxed);
                    }
                }
            }
        }

        // 2. 检测 FPS 过期（无匹配数据超过 2 秒 → 归零，对齐 tubatools Decay 的
        //    2s 无帧归零语义；同时 bump 代次让 ETW 回调在下一帧清空环形缓冲，
        //    否则恢复后瞬时 FPS 会横跨停顿区间算出一段错误的低值）
        let now = now_unix_ms();
        let last_frame = LAST_FRAME_TIME.load(Ordering::Relaxed);
        if last_frame > 0 && now > last_frame + 2000 {
            let _target_guard = TARGET_PROCESS.write();
            if LAST_FRAME_TIME.load(Ordering::Relaxed) == last_frame {
                log::info!(
                    "FPS监控: 帧率数据已过期 ({}ms 无匹配)，FPS 归零",
                    now - last_frame
                );
                clear_cached_statistics();
                TARGET_GENERATION.fetch_add(1, Ordering::Relaxed);
            }
        }

        // 3. 查询进程名（耗时操作放在后台线程）
        let pid = CURRENT_FG_PID.load(Ordering::Relaxed);
        if pid == 0 {
            continue;
        }

        // PID 变化时才刷新进程列表
        if pid != last_pid {
            sys.refresh_processes();
            last_pid = pid;
        }

        if let Some(process) = sys.process(Pid::from_u32(pid)) {
            let name = process.name().to_string();
            let is_game = !name.is_empty() && !is_excluded_process(&name) && !is_non_game_process(&name);
            let selected_pid = if is_game { pid } else { 0 };

            if selected_pid == TARGET_PID.load(Ordering::Relaxed) {
                // 与当前目标一致，取消待提交的变更
                pending_target = None;
                pending_since = None;
                // 呈现模式变化日志（诊断「独占全屏 OSD 不显示」时先看这行）
                let mode = PRESENT_MODE.load(Ordering::Relaxed);
                if mode != last_logged_mode && mode != MODE_UNKNOWN {
                    let mode_name = match mode {
                        MODE_LEGACY_EXCLUSIVE => "传统独占全屏",
                        MODE_INDEPENDENT_FLIP => "独立翻转(flip model)",
                        MODE_COMPOSITED => "窗口化/无边框(合成)",
                        _ => "未知",
                    };
                    log::info!("FPS监控: 目标 {} 呈现模式 → {mode_name}", name);
                    last_logged_mode = mode;
                }
            } else {
                // 进程已变化，启动/更新去抖动计时
                if pending_target.as_ref() != Some(&(pid, name.clone())) {
                    pending_target = Some((pid, name.clone()));
                    pending_since = Some(Instant::now());
                }

                // 检查去抖动是否到期
                if let (Some((pending_pid, pending)), Some(since)) = (&pending_target, pending_since) {
                    if since.elapsed() >= DEBOUNCE_DELAY {
                        select_target(
                            if is_game { *pending_pid } else { 0 },
                            if is_game { pending.as_str() } else { "" },
                        );
                        last_logged_mode = 0;

                        log::info!(
                            "FPS监控: 前台进程 → {} (pid={}){}",
                            pending,
                            pid,
                            if is_game { "" } else { " (非游戏)" }
                        );

                        pending_target = None;
                        pending_since = None;
                    }
                }
            }
        }
    }
}

// ============ 公共 API（与旧方案完全兼容） ============

/// 启动 FPS 监控
pub fn fps_monitor_active() -> bool {
    FPS_ACTIVE.load(Ordering::SeqCst)
}

pub fn start_fps_monitor() {
    if FPS_ACTIVE.load(Ordering::SeqCst) {
        return;
    }

    // ETW 内核 Provider 需要管理员权限（与旧 PresentMon 方案一致）
    if !crate::optimization::is_admin() {
        log::error!("FPS监控: 需要管理员权限才能开启帧率监控（ETW 内核事件采集）");
        return;
    }

    FPS_ACTIVE.store(true, Ordering::SeqCst);

    #[cfg(target_os = "windows")]
    {
        win32_fg::init_foreground_process();
        // The SetWinEventHook registration was removed: the hook was raised
        // on a thread without a message pump, so its callback never fired and
        // the 500ms GetForegroundWindow poll below was always the real
        // mechanism.
    }

    // 启动 ETW 采集线程
    #[cfg(target_os = "windows")]
    etw::spawn_monitor();

    // 启动前台窗口追踪线程
    thread::spawn(|| {
        process_tracker_loop();
    });

    log::info!("FPS监控: 已启动 (内嵌 ETW — DxgKrnl/Win32k Present 事件)");
}

/// 停止 FPS 监控
pub fn stop_fps_monitor() {
    FPS_ACTIVE.store(false, Ordering::SeqCst);

    // 停止 ETW 会话并等待采集线程退出（确保无残留会话）
    #[cfg(target_os = "windows")]
    etw::stop_and_join();

    SMOOTHED_FPS.store(0, Ordering::Relaxed);
    ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
    ZERO_DOT_ONE_PCT_LOW_FPS.store(0, Ordering::Relaxed);
    WINDOW_AVG_FPS.store(0, Ordering::Relaxed);
    LAST_FRAME_TIME.store(0, Ordering::Relaxed);
    TARGET_GENERATION.store(0, Ordering::Relaxed);
    PRESENT_MODE.store(MODE_UNKNOWN, Ordering::Relaxed);
    *TARGET_PROCESS.write() = String::new();
    TARGET_PID.store(0, Ordering::Relaxed);
    CURRENT_FG_PID.store(0, Ordering::Relaxed);

    log::info!("FPS监控: 已停止");
}

/// 获取缓存的瞬时 FPS 值（60 样本环形窗口）
pub fn get_cached_fps() -> Option<u32> {
    let fps = SMOOTHED_FPS.load(Ordering::Relaxed);
    if fps == 0 {
        None
    } else {
        Some(fps)
    }
}

/// 获取缓存的 1% Low FPS 值
///
/// 口径（PresentMon / CapFrameX 标准）：10s 绝对时间窗口内最慢 1% 帧的
/// 平均帧时间取倒数；窗口未填满或帧数 <100 返回 None。
pub fn get_cached_1low_fps() -> Option<f64> {
    load_low(&ONE_PCT_LOW_FPS)
}

/// 获取缓存的 0.1% Low FPS 值
///
/// 口径：30s 绝对时间窗口内最慢 0.1% 帧的平均帧时间取倒数；
/// 窗口未填满或帧数 <900 返回 None。
pub fn get_cached_01low_fps() -> Option<f64> {
    load_low(&ZERO_DOT_ONE_PCT_LOW_FPS)
}

/// PID currently selected by the ETW tracker for an in-process overlay.
pub fn current_target_pid() -> u32 {
    TARGET_PID.load(Ordering::Relaxed)
}

/// 获取缓存的 10s 窗口平均 FPS。
pub fn get_cached_window_avg_fps() -> Option<u32> {
    let fps = WINDOW_AVG_FPS.load(Ordering::Relaxed);
    if fps == 0 {
        None
    } else {
        Some(fps)
    }
}

/// 获取目标进程最近的呈现模式（问题 6 方案 E）
///
/// 返回值："composited"（窗口化/无边框）/ "independent-flip"（独立翻转）/
/// "legacy-exclusive"（传统独占全屏）。None = 无活跃信号或非目标进程。
pub fn get_cached_present_mode() -> Option<&'static str> {
    match PRESENT_MODE.load(Ordering::Relaxed) {
        MODE_COMPOSITED => Some("composited"),
        MODE_INDEPENDENT_FLIP => Some("independent-flip"),
        MODE_LEGACY_EXCLUSIVE => Some("legacy-exclusive"),
        _ => None,
    }
}

/// ETW 诊断子命令（`--etw-diag [秒]`）：抓取 N 秒 present 事件，按 pid 打印
/// 事件 ID 分布与帧间隔中位数 —— 定位「事件到底发没发」的最快手段。
#[cfg(target_os = "windows")]
pub fn run_etw_diag(seconds: u64, target_pid: Option<u32>, frame_csv: Option<&str>) -> i32 {
    etw::run_diag(seconds, target_pid, frame_csv)
}

/// 设置自身 overlay 窗口句柄（用于排除前台切换到自身 overlay）
pub fn set_overlay_hwnd(hwnd: u64) {
    OVERLAY_HWND.store(hwnd, Ordering::SeqCst);
    log::info!("FPS监控: Overlay窗口句柄设置为 {hwnd:#X}");
}

/// 清除自身 overlay 窗口句柄
pub fn clear_overlay_hwnd() {
    OVERLAY_HWND.store(0, Ordering::SeqCst);
    log::info!("FPS监控: Overlay窗口句柄已清除");
}

/// 清理资源
pub fn cleanup() {
    stop_fps_monitor();
}

// ============ 单测：分层算法 + 时间窗口 + QPC 换算 ============
// 分层/时间窗口测试逐条翻译自 tubatools FpsServiceTests.cs（GPL-3.0），
// 是移植验收资产；QPC 换算测试锁死移植首要风险项。

#[cfg(test)]
mod tests {
    use super::*;

    /// ms → 100ns ticks
    fn tick(ms: i64) -> i64 {
        ms * 10_000
    }

    // ---- QPC 换算（首要风险项） ----

    #[test]
    fn qpc_identity_at_10mhz_invariant_tsc() {
        // 现代 Windows invariant TSC：QPF = 10MHz，QPC ticks 即 100ns，必须恒等
        assert_eq!(qpc_to_100ns_with(0, 10_000_000), 0);
        assert_eq!(qpc_to_100ns_with(1, 10_000_000), 1);
        assert_eq!(qpc_to_100ns_with(12_345_678_901, 10_000_000), 12_345_678_901);
        assert_eq!(qpc_to_100ns_with(i64::MAX / 2, 10_000_000), i64::MAX / 2);
    }

    #[test]
    fn qpc_conversion_at_3579545hz_acpi_pm_timer() {
        // 老硬件 ACPI PM timer 3.579545MHz：1s = 3_579_545 QPC → 10_000_000 (100ns)
        let freq = 3_579_545i64;
        assert_eq!(qpc_to_100ns_with(freq, freq), 10_000_000); // 1s → 1s
        // 1ms = 3579.545 QPC；整数输入 3579 ticks → 9998 (100ns)，截尾在预期内
        assert_eq!(qpc_to_100ns_with(freq / 1000, freq), 9_998);
        // 差值不变性：窗口计算用的是差值，换算必须保序且线性
        let a = qpc_to_100ns_with(1_000_000, freq);
        let b = qpc_to_100ns_with(1_000_000 + freq, freq);
        assert_eq!(b - a, 10_000_000); // 1s 间隔 → 1s
    }

    #[test]
    fn qpc_conversion_at_14318180hz() {
        // 14.31818MHz TSC 变体：100ms = 1_431_818 QPC → 1_000_000 (100ns)
        let freq = 14_318_180i64;
        let converted = qpc_to_100ns_with(freq / 10, freq);
        assert!((converted - 1_000_000i64).abs() <= 1, "got {converted}");
    }

    #[test]
    fn qpc_degenerate_frequency_falls_back_to_identity() {
        assert_eq!(qpc_to_100ns_with(50_000, 0), 50_000);
        assert_eq!(qpc_to_100ns_with(50_000, -1), 50_000);
    }

    // ---- 分层权威源去重（翻译自 FpsServiceTests.cs:146-237） ----

    #[test]
    fn tier_present_history_dominates_all_tiers() {
        // PresentHistory (0xAB/0xD7) 是现代 Windows 的权威逐帧信源——包括全屏
        // （同一帧 0xB8 也会发）。双计会让 FPS 翻倍，所以 History 流动时（500ms
        // 窗口内）legacy 与 win32k 全部被压制。
        let mut st = TierState::default();

        // t=10s 的 history 事件
        assert!(tier_dedup(&mut st, PRESENT_HISTORY_START, tick(10_000)));
        assert_eq!(st.last_history, tick(10_000));

        // 5ms 后同一帧的 legacy Present + win32k 事件 —— 都被压制
        assert!(!tier_dedup(&mut st, PRESENT, tick(10_000) + tick(5)));
        assert!(!tier_dedup(
            &mut st,
            WIN32K_PRESENT,
            tick(10_000) + tick(5) + 500
        ));
        assert_eq!(st.last_present, tick(10_000)); // 压制不改去重基准

        // 下一帧经 history 正常计入
        assert!(tier_dedup(&mut st, PRESENT_HISTORY_DETAILED, tick(20_000)));
    }

    #[test]
    fn tier_present_history_expires_then_lower_tier_counts() {
        let mut st = TierState::default();

        // t=1s history
        assert!(tier_dedup(&mut st, PRESENT_HISTORY_START, tick(1_000)));
        // 300ms 后 win32k 仍被压制（500ms 模式窗口内）
        assert!(!tier_dedup(&mut st, WIN32K_PRESENT, tick(1_300)));
        // 模式窗口过期后，win32k 成为逐帧信源
        assert!(tier_dedup(&mut st, WIN32K_PRESENT, tick(2_000)));
        assert!(tier_dedup(&mut st, WIN32K_PRESENT, tick(2_100)));
    }

    #[test]
    fn tier_win32k_tracks_composited_frames_and_shadows_legacy() {
        // 无边框窗口游戏（MPO 关、无 present history）经 DWM 合成呈现——逐帧
        // 信源是 Win32k 合成面事件 (0xC9) 而非 DxgKrnl legacy 事件。win32k 流动
        // 时，混杂的 legacy present 不得重复计帧。
        let mut st = TierState::default();
        let mut counted = 0;

        assert!(tier_dedup(&mut st, WIN32K_PRESENT, tick(10_000)));
        counted += 1;
        assert!(!tier_dedup(&mut st, PRESENT, tick(10_000) + 500)); // 同帧杂散 legacy
        assert!(tier_dedup(&mut st, WIN32K_PRESENT, tick(10_000) + 167_000)); // ~60Hz
        counted += 1;
        assert!(!tier_dedup(&mut st, BLT, tick(10_000) + 167_500)); // 同帧 MPO blt
        assert!(tier_dedup(&mut st, WIN32K_PRESENT, tick(10_000) + 333_000));
        counted += 1;
        assert_eq!(counted, 3);
    }

    #[test]
    fn tier_fullscreen_no_history_uses_legacy_events() {
        // 无 present history 的系统上的全屏独占：只有 legacy 内核事件，win32k 不出现
        let mut st = TierState::default();

        assert!(tier_dedup(&mut st, PRESENT, tick(10_000)));
        assert!(tier_dedup(&mut st, PRESENT, tick(10_000) + 167_000));
        assert!(tier_dedup(&mut st, MMIO_FLIP, tick(10_000) + 334_000));
    }

    #[test]
    fn tier_mpo_events_without_history_count_once_per_frame() {
        // 同帧的 MPO blt + flip 事件相隔微秒级；去重窗口内只有第一个可计帧
        let mut st = TierState::default();
        let mut tracker = FpsTracker::new();

        assert!(tier_dedup(&mut st, PRESENT, tick(10_000)));
        assert!(!tier_dedup(&mut st, BLT, tick(10_000) + 500)); // 同帧 0.05ms
        assert!(!tier_dedup(&mut st, MMIO_FLIP, tick(10_000) + 1_000)); // 同帧 0.1ms
        assert!(tier_dedup(&mut st, MMIO_FLIP, tick(10_000) + 20_000)); // 下一帧 2ms
        assert_eq!(tracker.on_present(tick(10_000)), 0.0); // 首帧无 FPS
        tracker.on_present(tick(10_000) + 20_000);
        assert_eq!(tracker.total_frames(), 1); // 2 presents → 1 间隔
        assert!((tracker.instant_fps() - 500.0).abs() < 0.1); // 2ms 间隔 → 500 FPS
    }

    #[test]
    fn tier_same_frame_windows_uncap_history_but_cap_win32k_single_source() {
        // 设计口径：History/legacy 路径 1ms 去重 → 上限 1000FPS；0xC9 单一信源
        // 路径 2ms → 500FPS（tubatools 原值 4ms 在本机无必要：etw-diag 实测 C9
        // 每合成帧恰好 1 个，无多 surface 重复）。
        // 注：TierState 初始值为 0（tubatools 同款语义），真实 QPC ticks 巨大，
        // 仅会话启动后 ~500ms 内有对非 History 层的预热瞬态；测试从 100s 基址
        // 起步以避开该瞬态。
        let base = tick(100_000);
        let mut st = TierState::default();
        assert!(tier_dedup(&mut st, PRESENT_HISTORY_START, base));
        assert!(!tier_dedup(&mut st, PRESENT_HISTORY_DETAILED, base + 5_000)); // 0.5ms < 1ms 丢
        assert!(tier_dedup(&mut st, PRESENT_HISTORY_START, base + 11_000)); // 1.1ms ≥ 1ms 收

        let mut st2 = TierState::default();
        assert!(tier_dedup(&mut st2, WIN32K_PRESENT, base));
        assert!(!tier_dedup(&mut st2, WIN32K_PRESENT, base + 15_000)); // 1.5ms < 2ms 丢
        assert!(tier_dedup(&mut st2, WIN32K_PRESENT, base + 25_000)); // 2.5ms ≥ 2ms 收
    }

    // ---- 呈现模式检测（问题 6 方案 E） ----

    #[test]
    fn present_mode_detection_by_shadow_relations() {
        let mut st = TierState::default();
        // A Present event alone does not establish the window's presentation mode.
        assert!(tier_dedup(&mut st, PRESENT, tick(10_000)));
        assert_eq!(
            present_mode_code(&st, tick(10_000)),
            MODE_UNKNOWN
        );
        // 传统 + 独立翻转 → 独立翻转优先
        assert!(tier_dedup(&mut st, INDEPENDENT_FLIP, tick(10_010)));
        assert_eq!(
            present_mode_code(&st, tick(10_010)),
            MODE_INDEPENDENT_FLIP
        );
        // 独立翻转 + 合成 → 合成（win32k 更新）
        assert!(tier_dedup(&mut st, WIN32K_PRESENT, tick(10_020)));
        assert_eq!(present_mode_code(&st, tick(10_020)), MODE_COMPOSITED);
        // 全部超时 → 未知
        assert_eq!(present_mode_code(&st, tick(11_000)), MODE_UNKNOWN);
    }

    // ---- 瞬时 FPS / 帧时间过滤 ----

    fn feed(tracker: &mut FpsTracker, n: usize, frame_ticks: i64, start: i64) -> i64 {
        let mut t = start;
        for _ in 0..n {
            t += frame_ticks;
            tracker.on_present(t);
        }
        t
    }

    #[test]
    fn instant_fps_tracks_60hz_within_one_second_window() {
        let mut tracker = FpsTracker::new();
        feed(&mut tracker, 120, tick(1000) / 60, 0); // 60Hz × 2s
        let fps = tracker.instant_fps();
        assert!((59.0..=61.0).contains(&fps), "got {fps}");
        assert_eq!(tracker.total_frames(), 119);
        // 会话平均与瞬时一致（均匀帧流）
        assert!((tracker.session_avg_fps() - 60.0).abs() < 0.1);
    }

    #[test]
    fn instant_fps_reaches_1000fps_ceiling_not_capped_at_250() {
        // 1.1ms 帧间隔（≈909FPS，tier 层 1ms 同帧去重放行的最快节奏）——统计层
        // 不得另有 250 封顶；生产上限 1000FPS 来自 [1ms,10s) 帧时间过滤
        let mut tracker = FpsTracker::new();
        feed(&mut tracker, 200, 11_000, 0); // 1.1ms × 200
        let fps = tracker.instant_fps();
        assert!((900.0..=920.0).contains(&fps), "got {fps}");
        assert_eq!(tracker.total_frames(), 199); // 1.1ms ≥ 1ms 下限，全部入窗
    }

    #[test]
    fn frame_time_filter_rejects_sub_millisecond_fake_frames() {
        // 0.1ms 假帧（双源重复事件漏网时）不进帧窗口——否则 Avg/low 全部失真。
        // 注意假帧与下一真帧之间的「跨帧间隔」（≈15.7ms）是合法帧时间，会入窗：
        // 生产路径上这类假帧根本到不了统计层（tier 层 1ms 同帧去重已拦截），
        // 此测试只验证帧时间过滤器本身的语义。
        let mut tracker = FpsTracker::new();
        let frame_ticks = tick(1000) / 60;
        let mut t = 0i64;
        for _ in 0..60 {
            t += frame_ticks;
            tracker.on_present(t);
            tracker.on_present(t + 1_000); // 0.1ms 假帧
        }
        // 120 presents → 119 间隔：60 个 0.1ms 假帧间隔全部被过滤，剩 59 个合法
        assert_eq!(tracker.total_frames(), 59);
    }

    #[test]
    fn frame_time_filter_rejects_stall_gaps() {
        // 停顿 15s 后的恢复帧：15s 间隔 ≥10s 上限，不进窗口（但推进瞬时环）
        let mut tracker = FpsTracker::new();
        let frame_ticks = tick(1000) / 60;
        let t = feed(&mut tracker, 30, frame_ticks, 0);
        tracker.on_present(t + tick(15_000));
        assert_eq!(tracker.total_frames(), 29); // 停顿间隔未计入
        // 恢复后的下一帧间隔正常
        tracker.on_present(t + tick(15_000) + frame_ticks);
        assert_eq!(tracker.total_frames(), 30);
    }

    // ---- 1%/0.1% low 时间窗口（翻译自 FpsServiceTests.cs:349-478） ----

    #[test]
    fn low_rolls_old_frames_out_of_window() {
        // 滚动窗口语义：启动/加载期的慢帧必须随窗口滚动自然退出，
        // 1% low 反映「当前画面」而不是整个会话的累计。
        let mut tracker = FpsTracker::new();
        let fast = tick(1000) / 60; // 16.67ms @60FPS
        let slow = tick(1000) / 30; // 33.33ms @30FPS
        let mut t = 0i64;

        t = feed(&mut tracker, 5000, fast, t);
        let low = tracker.one_percent_low();
        assert!((55.0..=65.0).contains(&low), "got {low}"); // 纯 60fps

        t = feed(&mut tracker, 100, slow, t);
        let low = tracker.one_percent_low();
        assert!((28.0..=32.0).contains(&low), "got {low}"); // 慢帧进窗口 → ~30

        t = feed(&mut tracker, 3000, fast, t);
        let low = tracker.one_percent_low();
        assert!((55.0..=65.0).contains(&low), "got {low}"); // 慢帧滚出 → ~60
    }

    #[test]
    fn point_one_percent_low_needs_enough_frames_in_window() {
        // 0.1% low 要有统计意义必须有足够多的帧——样本不够必须返回 -1，
        // 而不是拿一两帧最差帧的噪声填数字。门槛按「窗口内帧数」（900 帧）算。
        let mut tracker = FpsTracker::new();
        let frame_ticks = tick(1000) / 60;
        let mut t = 0i64;

        t = feed(&mut tracker, 500, frame_ticks, t);
        assert_eq!(tracker.point_one_percent_low(), -1.0);

        // 1900 帧 ≈ 31.7s ≥ 30s 窗口：帧数门槛（900）和时间填满门槛同时满足才出数
        feed(&mut tracker, 1400, frame_ticks, t);
        let low = tracker.point_one_percent_low();
        assert!((55.0..=65.0).contains(&low), "got {low}");
    }

    #[test]
    fn low_masked_until_window_filled() {
        // 开测头几秒窗口只有半截：启动期坏帧会把 1%/0.1% low 放大成离谱读数
        // ——这段时间必须显示 "--"，不能拿半截窗口硬算。
        let mut tracker = FpsTracker::new();
        let frame_ticks = tick(1000) / 60;
        let mut t = 0i64;

        // 5s：帧数够（299 ≥ 100）但 10s 窗口没填满
        t = feed(&mut tracker, 300, frame_ticks, t);
        assert_eq!(tracker.one_percent_low(), -1.0);
        assert_eq!(tracker.point_one_percent_low(), -1.0);

        // 累计 ≈11.7s：1% 窗口填满
        t = feed(&mut tracker, 400, frame_ticks, t);
        let low = tracker.one_percent_low();
        assert!((55.0..=65.0).contains(&low), "got {low}");
        assert_eq!(tracker.point_one_percent_low(), -1.0); // 30s 窗口仍未填满
    }

    #[test]
    fn low_best_effort_mode_for_short_sessions() {
        // 报告/快照是「整段会话」语义：会话比窗口短时按已有帧算（CapFrameX 对
        // 整段录制的口径），不能因为滚动窗口没填满就在报告里开天窗。
        let mut tracker = FpsTracker::new();
        let frame_ticks = tick(1000) / 60;
        feed(&mut tracker, 300, frame_ticks, 0); // 5s 短会话

        assert_eq!(tracker.one_percent_low(), -1.0); // 实时读数：窗口没填满 → 屏蔽
        let best_effort = tracker.window.calc_percentile_low(
            0.01,
            LOW1_WINDOW_TICKS,
            LOW1_MIN_FRAMES,
            false,
        );
        assert!((55.0..=65.0).contains(&best_effort), "got {best_effort}");
    }

    #[test]
    fn one_percent_low_recovers_within_window_seconds() {
        // 「刷新特别慢」的回归测试：一次卡顿必须在窗口时长（1% low = 10s）内
        // 滚干净。旧实现固定帧数窗口，60fps 下要 34 秒才恢复，读数像被钉住。
        let mut tracker = FpsTracker::new();
        let fast = tick(1000) / 60; // 16.67ms @60FPS
        let slow = tick(1000) / 30; // 33.33ms @30FPS
        let mut t = 0i64;

        t = feed(&mut tracker, 3000, fast, t); // 50s 平稳
        let low = tracker.one_percent_low();
        assert!((55.0..=65.0).contains(&low), "got {low}");

        t = feed(&mut tracker, 60, slow, t); // 2s 卡顿
        let low = tracker.one_percent_low();
        assert!((28.0..=34.0).contains(&low), "got {low}");

        t = feed(&mut tracker, 660, fast, t); // 11s 平稳 → 卡顿滚出窗口
        let low = tracker.one_percent_low();
        assert!((55.0..=65.0).contains(&low), "got {low}");
    }

    #[test]
    fn one_percent_low_window_is_time_based_not_frame_count() {
        // 帧数窗口在低帧率下会变得极长。改成时间窗口后，30fps 下同样只需
        // ~10 秒就能恢复——同一段画面在高低帧率下口径一致。
        let mut tracker = FpsTracker::new();
        let fast = tick(1000) / 30; // 33.33ms @30FPS
        let slow = tick(1000) / 15; // 66.67ms @15FPS
        let mut t = 0i64;

        t = feed(&mut tracker, 900, fast, t); // 30s 平稳
        let low = tracker.one_percent_low();
        assert!((28.0..=34.0).contains(&low), "got {low}");

        t = feed(&mut tracker, 60, slow, t); // 4s 卡顿
        let low = tracker.one_percent_low();
        assert!((13.0..=17.0).contains(&low), "got {low}");

        t = feed(&mut tracker, 330, fast, t); // 11s → 卡顿滚出
        let low = tracker.one_percent_low();
        assert!((28.0..=34.0).contains(&low), "got {low}");
    }

    #[test]
    fn one_percent_low_uses_true_percentile_count() {
        // 真 1%——不再有「最少取 3 帧」的兜底：那会把口径悄悄放大成 3%。
        // 20fps（50ms/帧）→ 10s 窗口 ≈ 200 帧 → 真 1% = 最差 2 帧。
        let mut tracker = FpsTracker::new();
        let fast = tick(1000) / 20; // 50ms
        let slow = tick(1000) / 10; // 100ms
        let mut t = 0i64;

        t = feed(&mut tracker, 300, fast, t); // 15s 平稳
        t += slow;
        tracker.on_present(t); // 两帧慢帧
        t += slow;
        tracker.on_present(t);

        // 最差 2 帧都是 100ms → 平均帧时间 100ms → 10 FPS。
        // 若兜底取 3 帧，会混进一帧 50ms → 83ms → 12 FPS，不再落在此区间。
        let low = tracker.one_percent_low();
        assert!((9.5..=10.5).contains(&low), "got {low}");
    }

    #[test]
    fn window_avg_matches_uniform_stream() {
        let mut tracker = FpsTracker::new();
        feed(&mut tracker, 700, tick(1000) / 70, 0); // 70Hz × 10s
        let avg = tracker.window_avg_fps();
        assert!((69.0..=71.0).contains(&avg), "got {avg}");
        tracker.clear();
        assert_eq!(tracker.window_avg_fps(), 0.0);
    }

    #[test]
    fn clear_resets_all_statistics() {
        let mut tracker = FpsTracker::new();
        feed(&mut tracker, 500, tick(1000) / 60, 0);
        tracker.clear();
        assert_eq!(tracker.total_frames(), 0);
        assert_eq!(tracker.instant_fps(), 0.0);
        assert_eq!(tracker.one_percent_low(), -1.0);
        assert!(tracker.is_window_empty());
    }

    #[test]
    fn ring_wraps_at_sample_count() {
        // 超过 60 样本后环形覆盖：FPS 稳定在帧率而不是漂移
        let mut tracker = FpsTracker::new();
        feed(&mut tracker, SAMPLE_COUNT * 3, tick(1000) / 120, 0); // 120Hz × 3 圈
        let fps = tracker.instant_fps();
        assert!((119.0..=121.0).contains(&fps), "got {fps}");
    }
}
