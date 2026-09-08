import type { PreviewAction, PreviewActionPayloads, PreviewState, PreviewListener, PreviewReport, OverlaySettings } from "./types";
import { connectStateBus, readPersistedState, persistState, isMainWindow, isDesktop, nativeOperation, backendInvoke, emitPreviewEvent } from "./platform";
import { invoke } from "./core";
import { FIXTURE_EPOCH, overlayFixture } from "./fixtures";

export const defaultOverlay: OverlaySettings = {
  display_items: [
    ["fps", "FPS"], ["fps_1low", "1% Low"], ["fps_01low", "0.1% Low"], ["cpu_usage", "CPU占用"],
    ["cpu_temp", "CPU温度"], ["cpu_clock", "CPU频率"], ["cpu_voltage", "CPU电压"], ["cpu_power", "CPU功耗"],
    ["cpu_fan_speed", "CPU风扇转速"], ["gpu_usage", "GPU占用"], ["gpu_temp", "GPU温度"],
    ["gpu_clock", "GPU频率"], ["gpu_power", "GPU功耗"], ["gpu_vram", "GPU显存占用"],
    ["gpu_memory_clock", "GPU显存频率"], ["gpu_voltage", "GPU电压"], ["gpu_fan_speed", "GPU风扇转速"],
    ["memory_usage", "内存占用"], ["ssd_temp", "硬盘温度"], ["time", "时间"],
    ["game_ping", "游戏延迟"], ["delta_password", "三角洲密码"],
  ].map(([id, label]) => ({ id, label, enabled: ["fps", "cpu_usage", "gpu_usage", "gpu_temp", "memory_usage", "game_ping"].includes(id) })),
  custom_items: [], opacity: 200, style: "default", font: "Microsoft YaHei", font_size: 13,
  item_width: 130, font_color: "#ffffff",
};
function initial(): PreviewState {
  return {
    version: 1, revision: 0, tick: 0, scenario: "normal",
    targets: [{ id: "cs2", name: "Counter-Strike 2", executable: "cs2.exe", enabled: true },
      { id: "valorant", name: "无畏契约", executable: "VALORANT-Win64-Shipping.exe", enabled: false }],
    selectedTargetId: "cs2",
    reports: (["full", "partial", "diagnostic", "error"] as const).map((kind, index) => ({
      id: `sample-${kind}`, kind, targetId: "cs2", targetName: "Counter-Strike 2",
      createdAt: new Date(FIXTURE_EPOCH - index * 3600000).toISOString(), durationSeconds: 120,
      averageFps: index < 2 ? 144 : null, low1Fps: index < 2 ? 112 : null,
      low01Fps: index < 2 ? 94 : null, frameTimeMs: index < 2 ? 6.94 : null, samples: [], simulated: true,
    })),
    config: { captureDurationSeconds: 20, sampleIntervalMs: 1000, pollIntervalMs: 1000,
      autoStart: false, autoOpenReport: false, detailedLogging: false, diagnosticLogging: false,
      outputDirectory: "PreviewData/reports", theme: "system" },
    monitor: { phase: "idle", targetId: null, elapsedSeconds: 0, startedAt: null, reportId: null, error: null, samples: [] },
    activeGpuIndex: 0, overlay: { enabled: false, dragMode: false, settings: structuredClone(defaultOverlay) },
    hotkey: "Shift+F10", hotkeysEnabled: true, overlayHotkeyEnabled: true,
  };
}
const restored = readPersistedState();
let state = restored ?? initial();
if (restored) {
  state = { ...initial(), ...restored, config: { ...initial().config, ...restored.config },
    monitor: initial().monitor, overlay: { ...restored.overlay, enabled: false, dragMode: false } };
}
const listeners = new Set<PreviewListener>();
let bus: ReturnType<typeof connectStateBus> | undefined;
let timer: ReturnType<typeof setInterval> | undefined;
let users = 0;
export function getPreviewState(): PreviewState { return state; }
export function subscribePreviewState(listener: PreviewListener) { listeners.add(listener); return () => { listeners.delete(listener); }; }
export function mutatePreview(mutator: (draft: PreviewState) => void): void {
  const next = structuredClone(state);
  mutator(next);
  next.revision = Math.max(state.revision + 1, Date.now());
  state = next;
  persistState(state);
  bus?.send(state);
  listeners.forEach(fn => fn(state));
}
function finishReport(next: PreviewState) {
  const target = next.targets.find(t => t.id === next.monitor.targetId);
  const values = next.monitor.samples.filter(s => s.fps != null);
  const fps = values.length ? values.reduce((sum, s) => sum + s.fps!, 0) / values.length : null;
  const report: PreviewReport = {
    id: `preview-${Date.now()}`, kind: fps == null ? "diagnostic" : next.scenario === "missing-sensors" ? "partial" : "full",
    targetId: target?.id ?? "", targetName: target?.name ?? "模拟会话", createdAt: new Date().toISOString(),
    durationSeconds: next.monitor.elapsedSeconds, averageFps: fps, low1Fps: fps == null ? null : fps * .78,
    low01Fps: fps == null ? null : fps * .66, frameTimeMs: fps == null ? null : 1000 / fps,
    samples: next.monitor.samples, simulated: true,
  };
  next.reports.unshift(report);
  // Keep browser mode bounded: each report drags its samples along.
  if (next.reports.length > 50) next.reports.length = 50;
  next.monitor.phase = "completed";
  next.monitor.reportId = report.id;
}
function tick() {
  if (isDesktop()) {
    // Desktop: the host poller owns hardware data (overlay cache +
    // vertical-overlay-data events), and the real watcher owns the capture
    // state machine — reconcile the local phase with the backend each tick.
    // The fixture machine below must never run alongside it: one missed
    // reconciliation (backend busy, pipe hiccup) would let it finish a
    // simulated run and inject a simulated report into the real list.
    if (isMainWindow()) void syncDesktopMonitor();
    return;
  }
  mutatePreview(next => {
    next.tick++;
    const monitor = next.monitor;
    if (monitor.phase === "waiting") {
      if (next.scenario === "failure") { monitor.phase = "error"; monitor.error = "Simulated：采样器启动失败"; }
      else monitor.phase = "capturing";
    } else if (monitor.phase === "capturing") {
      monitor.elapsedSeconds++;
      const data = overlayFixture(next);
      monitor.samples.push({ second: monitor.elapsedSeconds, fps: data.fps, frameTimeMs: data.fps ? 1000 / data.fps : null });
      if (monitor.elapsedSeconds >= next.config.captureDurationSeconds) monitor.phase = "stopping";
    } else if (monitor.phase === "stopping") monitor.phase = "generating";
    else if (monitor.phase === "generating") finishReport(next);
  });
  const data = overlayFixture(state);
  void emitPreviewEvent("vertical-overlay-data", data).catch(console.error);
}

// ---- Real capture pipeline (desktop) ----

let desktopSyncBusy = false;
let targetsSynced = false;
// Watcher CompletedRuns when the current capture began; used to tell a real
// finished run (report regenerated) from an abnormally exited watcher.
let completedRunsBaseline: number | null = null;

function mapBackendReport(raw: any): PreviewReport {
  const kind = ["full", "partial", "diagnostic", "error"].includes(raw?.kind) ? raw.kind : "full";
  return {
    id: String(raw?.id ?? ""), kind,
    targetId: String(raw?.processName ?? ""), targetName: String(raw?.targetName ?? ""),
    createdAt: String(raw?.createdAt ?? ""), durationSeconds: Number(raw?.durationSeconds ?? 0),
    averageFps: raw?.averageFps ?? null, low1Fps: raw?.low1Fps ?? null,
    low01Fps: raw?.low01Fps ?? null, frameTimeMs: raw?.frameTimeMs ?? null,
    samples: [], simulated: false, runDir: raw?.runDir,
  };
}

async function syncDesktopMonitor(): Promise<void> {
  if (desktopSyncBusy) return;
  desktopSyncBusy = true;
  try {
    const status = await invoke<{ running: boolean; phase: string; elapsedSeconds?: number; targetName?: string; completedRuns?: number }>("monitor_status");
    mutatePreview(next => {
      const monitor = next.monitor;
      // The backend watcher is the source of truth for capture state; local
      // phases only add the "completed + refresh reports" edge.
      if (status.phase === "capturing") {
        if (monitor.phase !== "capturing") {
          monitor.phase = "capturing";
          monitor.startedAt = monitor.startedAt ?? new Date().toISOString();
          monitor.samples = [];
          // A capture can start before we ever observed "waiting" (game
          // already running): capture the baseline here too, or a real
          // finish would compare against the current count and always
          // misreport as error.
          if (completedRunsBaseline == null && typeof status.completedRuns === "number") {
            completedRunsBaseline = status.completedRuns;
          }
        }
        monitor.elapsedSeconds = status.elapsedSeconds ?? monitor.elapsedSeconds;
        // The watcher may have started a different enabled target than the
        // one selected in the UI (alreadyRunning / auto-capture); align the
        // selection with what is actually being recorded.
        const activeName = typeof status.targetName === "string" ? status.targetName : "";
        if (activeName) {
          const match = next.targets.find(t => t.name === activeName || t.id === activeName);
          if (match) {
            monitor.targetId = match.id;
            if (next.selectedTargetId !== match.id) next.selectedTargetId = match.id;
          }
        }
      } else if (status.phase === "waiting") {
        if (monitor.phase !== "waiting") {
          Object.assign(monitor, { ...initial().monitor, phase: "waiting" });
        }
        if (completedRunsBaseline == null && typeof status.completedRuns === "number") {
          completedRunsBaseline = status.completedRuns;
        }
      } else if (["waiting", "capturing", "stopping", "generating"].includes(monitor.phase)) {
        // Watcher gone or idle: the run ended. A report means a real finished
        // run; no new report means the pipeline died abnormally.
        void refreshDesktopReports();
        const base = completedRunsBaseline ?? status.completedRuns ?? 0;
        const hadNewReport = typeof status.completedRuns === "number" && status.completedRuns > base;
        monitor.phase = hadNewReport ? "completed" : "error";
        monitor.error = hadNewReport ? null : "采集进程异常退出，未能生成报告";
        completedRunsBaseline = null;
      }
    });
  } catch { /* backend not deployed: keep whatever local phase is running. */ }
  finally { desktopSyncBusy = false; }
}

export async function refreshDesktopReports(): Promise<void> {
  if (!isDesktop()) return;
  try {
    const payload = await backendInvoke<{ reports: any[] }>("reports_list");
    const mapped = (payload.reports ?? []).map(mapBackendReport).filter(report => report.id);
    mutatePreview(next => { next.reports = mapped; });
  } catch (error) { console.error("reports_list failed", error); }
}

let targetIdCounter = 0;
function nextTargetId(): string {
  targetIdCounter += 1;
  return `target-${Date.now()}-${targetIdCounter}`;
}
let targetsSyncRetryTimer: ReturnType<typeof setTimeout> | undefined;
async function syncTargetsFromBackend(): Promise<void> {
  try {
    const payload = await invoke<{ targets: any[] }>("targets_get");
    // Only a successful read lifts the guard: marking synced up front would
    // let a later edit persist a stale local table over the real config.
    targetsSynced = true;
    const targets = (payload.targets ?? []) as PreviewState["targets"];
    if (!Array.isArray(targets) || !targets.length) return;
    mutatePreview(next => {
      next.targets = targets;
      if (!next.targets.some(t => t.id === next.selectedTargetId)) {
        next.selectedTargetId = next.targets.find(t => t.enabled)?.id ?? next.targets[0]?.id ?? null;
      }
    });
  } catch (error) {
    console.error("targets_get failed", error);
    if (!targetsSynced) {
      // Backend not up yet (not deployed / cold start): keep retrying so the
      // guard lifts as soon as the real config becomes readable.
      targetsSyncRetryTimer = setTimeout(() => { void syncTargetsFromBackend(); }, 5000);
    }
  }
}

function persistTargetsToBackend(): void {
  // Before the first successful sync, state.targets may be a stale localStorage
  // table (or the hardcoded initial pair); saving it would silently overwrite
  // the real backend config. Surface nothing else — the retry loop lifts the
  // guard, after which edits flow through normally.
  if (!targetsSynced) return;
  void backendInvoke("targets_save", { targets: state.targets })
    .catch(error => console.error("targets_save failed", error));
}
export function startPreviewRuntime(): () => void {
  users++;
  if (!bus) bus = connectStateBus(incoming => {
    if (incoming.version !== 1 || incoming.revision <= state.revision) return;
    state = incoming;
    listeners.forEach(fn => fn(state));
  });
  if (!timer && isMainWindow()) timer = setInterval(tick, 1000);
  if (isDesktop() && isMainWindow() && !targetsSynced) {
    // syncTargetsFromBackend sets targetsSynced itself once it succeeds and
    // retries until then — never mark it here.
    void syncTargetsFromBackend();
    void refreshDesktopReports();
  }
  let closed = false;
  return () => {
    if (closed) return;
    closed = true;
    users--;
    if (users <= 0) {
      if (timer) clearInterval(timer); timer = undefined;
      if (targetsSyncRetryTimer) { clearTimeout(targetsSyncRetryTimer); targetsSyncRetryTimer = undefined; }
      bus?.close(); bus = undefined;
    }
  };
}
export function monitorBusy(): boolean {
  return ["waiting", "capturing", "stopping", "generating"].includes(state.monitor.phase);
}
const busy = monitorBusy;
export async function previewAction<K extends PreviewAction>(action: K, payload?: PreviewActionPayloads[K]): Promise<void> {
  const p = payload as any;
  if (action === "preview.reset") { mutatePreview(next => Object.assign(next, initial())); return; }
  if (action === "scenario.set") {
    if (!["normal", "multi-gpu", "missing-sensors", "empty", "failure"].includes(p)) throw new Error("无效模拟场景");
    if (busy()) throw new Error("请先结束模拟采集");
    mutatePreview(next => {
      next.scenario = p; next.tick = 0; next.activeGpuIndex = 0;
      if (p === "empty") { next.targets = []; next.reports = []; next.selectedTargetId = null; }
      else if (!next.targets.length) { next.targets = initial().targets; next.selectedTargetId = "cs2"; }
    });
    return;
  }
  if (action.startsWith("target.")) {
    if (busy()) throw new Error("采集中不能修改目标");
    const existing = state.targets.find(t => t.id === p?.id);
    if (action !== "target.add" && !existing) throw new Error("目标不存在");
    if (action === "target.add" || action === "target.update") {
      const name = (p.name ?? existing?.name ?? "").trim();
      const executable = (p.executable ?? existing?.executable ?? "").trim();
      if (!name || !/^[^\\/:*?"<>|]+\.exe$/i.test(executable)) throw new Error("请输入名称和有效的 .exe 进程名");
      if (state.targets.some(t => t.id !== existing?.id && t.executable.toLowerCase() === executable.toLowerCase())) throw new Error("进程名已存在");
      mutatePreview(next => {
        if (existing) Object.assign(next.targets.find(t => t.id === existing.id)!, p, { name, executable });
        else next.targets.push({ id: nextTargetId(), name, executable, enabled: p.enabled ?? true });
      });
      persistTargetsToBackend();
    } else if (action === "target.remove") mutatePreview(next => {
      next.targets = next.targets.filter(t => t.id !== p.id);
      if (next.selectedTargetId === p.id) next.selectedTargetId = next.targets[0]?.id ?? null;
    });
    else if (action === "target.select") mutatePreview(next => { next.selectedTargetId = p.id; });
    else throw new Error(`Unsupported preview action: ${action}`);
    if (action === "target.remove") persistTargetsToBackend();
    return;
  }
  if (action === "monitor.start") {
    if (busy()) throw new Error("模拟监控已经启动");
    const target = state.targets.find(t => t.id === (p?.targetId ?? state.selectedTargetId));
    if (!target?.enabled) throw new Error("请先选择已启用目标");
    // The real watcher must be up before the UI flips to "waiting"; its
    // failures (missing exe, disabled target) surface verbatim.
    if (isDesktop()) await backendInvoke("monitor_start", { targetId: target.executable });
    mutatePreview(next => { next.monitor = { ...initial().monitor, phase: "waiting", targetId: target.id, startedAt: new Date().toISOString() }; });
    return;
  }
  if (action === "monitor.stop") {
    if (!["waiting", "capturing"].includes(state.monitor.phase)) throw new Error("没有正在运行的模拟采集");
    if (isDesktop()) {
      // Real stop kills the pipeline tree; there is no report to finalize.
      await backendInvoke("monitor_stop");
      mutatePreview(next => { next.monitor = { ...initial().monitor, phase: "idle" }; });
    } else {
      mutatePreview(next => { next.monitor.phase = "stopping"; });
    }
    return;
  }
  if (action === "monitor.reset") { if (busy()) throw new Error("请先停止监控"); mutatePreview(next => { next.monitor = initial().monitor; }); return; }
  if (action === "config.update") {
    const cfg = { ...state.config, ...p };
    if (!Number.isFinite(cfg.captureDurationSeconds) || cfg.captureDurationSeconds < 1 || cfg.captureDurationSeconds > 3600
      || !Number.isFinite(cfg.sampleIntervalMs) || cfg.sampleIntervalMs < 500 || cfg.sampleIntervalMs > 5000
      || !Number.isFinite(cfg.pollIntervalMs) || cfg.pollIntervalMs < 250 || cfg.pollIntervalMs > 10000
      || !cfg.outputDirectory?.trim()) throw new Error("配置值超出允许范围");
    mutatePreview(next => { next.config = cfg; });
    if (isDesktop()) {
      // M6: the settings page used to be a lie on desktop - local state only.
      // Persist the fields the capture chain honors; captureDurationSeconds
      // and outputDirectory have no backend counterpart and stay preview-local.
      void backendInvoke("config_save", { config: {
        sampleIntervalMs: cfg.sampleIntervalMs,
        pollIntervalMs: cfg.pollIntervalMs,
        autoOpenReport: cfg.autoOpenReport,
        detailedLogging: cfg.detailedLogging,
        diagnosticLogging: cfg.diagnosticLogging,
      } }).catch(error => console.error("config_save failed", error));
    }
    return;
  }
  if (action.startsWith("report.")) {
    const report = state.reports.find(r => r.id === p?.id);
    if (!report) throw new Error("报告不存在");
    if (action === "report.remove") { mutatePreview(next => { next.reports = next.reports.filter(r => r.id !== p.id); }); return; }
    if (action === "report.regenerate") {
      mutatePreview(next => { next.reports.find(r => r.id === p.id)!.operation = "regenerating"; });
      if (isDesktop()) {
        try {
          await backendInvoke("report_regenerate", { id: p.id });
          await refreshDesktopReports();
          mutatePreview(next => { next.reports.find(r => r.id === p.id)!.operation = "ready"; });
        } catch (error) {
          mutatePreview(next => {
            const item = next.reports.find(r => r.id === p.id);
            if (item) { item.operation = "ready"; item.diagnostic = String(error); }
          });
          throw error;
        }
        return;
      }
      mutatePreview(next => {
        const item = next.reports.find(r => r.id === p.id);
        if (!item) return;
        item.operation = "ready";
        item.diagnostic = "模拟报告重新生成完成。";
      });
      return;
    }
    if (action === "report.diagnose") {
      mutatePreview(next => { next.reports.find(r => r.id === p.id)!.operation = "diagnosing"; });
      await new Promise(resolve => setTimeout(resolve, 600));
      mutatePreview(next => {
        const item = next.reports.find(r => r.id === p.id);
        if (!item) return;
        item.operation = "ready";
        item.diagnostic = `模拟诊断：${item.kind}；未执行真实采样、驱动检查或文件扫描。`;
      });
      return;
    }
  }
  throw new Error(`Unsupported preview action: ${action}`);
}
