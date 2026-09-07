import type { PreviewAction, PreviewActionPayloads, PreviewState, PreviewListener, PreviewReport, OverlaySettings } from "./types";
import { connectStateBus, readPersistedState, persistState, isMainWindow, isDesktop, nativeOperation, emitPreviewEvent } from "./platform";
import { FIXTURE_EPOCH, overlayFixture } from "./fixtures";

export const defaultOverlay: OverlaySettings = {
  display_items: [
    ["fps", "FPS"], ["fps_1low", "1% Low"], ["fps_01low", "0.1% Low"], ["cpu_usage", "CPU占用"],
    ["cpu_temp", "CPU温度"], ["cpu_clock", "CPU频率"], ["cpu_voltage", "CPU电压"], ["cpu_power", "CPU功耗"],
    ["cpu_fan_speed", "CPU风扇转速"], ["gpu_usage", "GPU占用"], ["gpu_temp", "GPU温度"],
    ["gpu_clock", "GPU频率"], ["gpu_power", "GPU功耗"], ["gpu_vram", "GPU显存占用"],
    ["gpu_memory_clock", "GPU显存频率"], ["gpu_voltage", "GPU电压"], ["gpu_fan_speed", "GPU风扇转速"],
    ["memory_usage", "内存占用"], ["ssd_temp", "硬盘温度"], ["time", "时间"],
  ].map(([id, label]) => ({ id, label, enabled: ["fps", "cpu_usage", "gpu_usage", "gpu_temp", "memory_usage"].includes(id) })),
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
  next.monitor.phase = "completed";
  next.monitor.reportId = report.id;
}
function tick() {
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
  if (isDesktop()) {
    // Desktop: the host poller owns hardware data — it refreshes the overlay
    // cache and emits vertical-overlay-data with real service snapshots, so
    // the frontend no longer pushes fixtures on this path.
  } else {
    const data = overlayFixture(state);
    void emitPreviewEvent("vertical-overlay-data", data).catch(console.error);
  }
}
export function startPreviewRuntime(): () => void {
  users++;
  if (!bus) bus = connectStateBus(incoming => {
    if (incoming.version !== 1 || incoming.revision <= state.revision) return;
    state = incoming;
    listeners.forEach(fn => fn(state));
  });
  if (!timer && isMainWindow()) timer = setInterval(tick, 1000);
  let closed = false;
  return () => {
    if (closed) return;
    closed = true;
    users--;
    if (users <= 0) { if (timer) clearInterval(timer); timer = undefined; bus?.close(); bus = undefined; }
  };
}
const busy = () => ["waiting", "capturing", "stopping", "generating"].includes(state.monitor.phase);
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
        else next.targets.push({ id: `target-${Date.now()}`, name, executable, enabled: p.enabled ?? true });
      });
    } else if (action === "target.remove") mutatePreview(next => {
      next.targets = next.targets.filter(t => t.id !== p.id);
      if (next.selectedTargetId === p.id) next.selectedTargetId = next.targets[0]?.id ?? null;
    });
    else if (action === "target.select") mutatePreview(next => { next.selectedTargetId = p.id; });
    else throw new Error(`Unsupported preview action: ${action}`);
    return;
  }
  if (action === "monitor.start") {
    if (busy()) throw new Error("模拟监控已经启动");
    const target = state.targets.find(t => t.id === (p?.targetId ?? state.selectedTargetId));
    if (!target?.enabled) throw new Error("请先选择已启用目标");
    mutatePreview(next => { next.monitor = { ...initial().monitor, phase: "waiting", targetId: target.id, startedAt: new Date().toISOString() }; });
    return;
  }
  if (action === "monitor.stop") {
    if (!["waiting", "capturing"].includes(state.monitor.phase)) throw new Error("没有正在运行的模拟采集");
    mutatePreview(next => { next.monitor.phase = "stopping"; }); return;
  }
  if (action === "monitor.reset") { if (busy()) throw new Error("请先停止监控"); mutatePreview(next => { next.monitor = initial().monitor; }); return; }
  if (action === "config.update") {
    const cfg = { ...state.config, ...p };
    if (!Number.isFinite(cfg.captureDurationSeconds) || cfg.captureDurationSeconds < 1 || cfg.captureDurationSeconds > 3600
      || !Number.isFinite(cfg.sampleIntervalMs) || cfg.sampleIntervalMs < 500 || cfg.sampleIntervalMs > 5000
      || !Number.isFinite(cfg.pollIntervalMs) || cfg.pollIntervalMs < 250 || cfg.pollIntervalMs > 10000
      || !cfg.outputDirectory?.trim()) throw new Error("配置值超出允许范围");
    mutatePreview(next => { next.config = cfg; }); return;
  }
  if (action.startsWith("report.")) {
    const report = state.reports.find(r => r.id === p?.id);
    if (!report) throw new Error("报告不存在");
    if (action === "report.remove") { mutatePreview(next => { next.reports = next.reports.filter(r => r.id !== p.id); }); return; }
    if (action === "report.regenerate" || action === "report.diagnose") {
      mutatePreview(next => { next.reports.find(r => r.id === p.id)!.operation = action === "report.regenerate" ? "regenerating" : "diagnosing"; });
      await new Promise(resolve => setTimeout(resolve, 600));
      mutatePreview(next => {
        const item = next.reports.find(r => r.id === p.id);
        if (!item) return;
        item.operation = "ready";
        item.diagnostic = action === "report.diagnose" ? `模拟诊断：${item.kind}；未执行真实采样、驱动检查或文件扫描。` : "模拟报告重新生成完成。";
      });
      return;
    }
  }
  throw new Error(`Unsupported preview action: ${action}`);
}
