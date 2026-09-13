export type PreviewScenario = "normal" | "multi-gpu" | "missing-sensors" | "empty" | "failure";
export type MonitorPhase = "idle" | "waiting" | "capturing" | "stopping" | "generating" | "completed" | "error";
export interface PreviewTarget {
  id: string;
  name: string;
  executable: string;
  enabled: boolean;
}
export interface PreviewReport {
  kind: "full" | "partial" | "diagnostic" | "error";
  operation?: "regenerating" | "diagnosing" | "ready";
  diagnostic?: string;
  id: string;
  targetId: string;
  targetName: string;
  createdAt: string;
  durationSeconds: number;
  averageFps: number | null;
  low1Fps: number | null;
  low01Fps: number | null;
  frameTimeMs: number | null;
  samples: Array<{ second: number; fps: number | null; frameTimeMs: number | null }>;
  /** True for in-memory preview fixtures; absent/false for backend reports. */
  simulated?: boolean;
  /** Real backend reports carry the run directory (for open-directory etc.). */
  runDir?: string;
}
export interface PreviewConfig {
  pollIntervalMs: number;
  autoOpenReport: boolean;
  detailedLogging: boolean;
  diagnosticLogging: boolean;
  captureDurationSeconds: number;
  sampleIntervalMs: number;
  autoStart: boolean;
  outputDirectory: string;
  theme: "system" | "light" | "dark";
}
export interface PreviewState {
  version: 1;
  revision: number;
  tick: number;
  scenario: PreviewScenario;
  targets: PreviewTarget[];
  selectedTargetId: string | null;
  reports: PreviewReport[];
  config: PreviewConfig;
  monitor: {
    phase: MonitorPhase;
    targetId: string | null;
    elapsedSeconds: number;
    startedAt: string | null;
    reportId: string | null;
    error: string | null;
    samples: PreviewReport["samples"];
    /** 报告合成进度（generating 相位时来自 C# report-progress.json 的真实百分比） */
    reportPercent: number | null;
    /** 预计还剩几秒完成报告合成 */
    reportEtaSeconds: number | null;
  };
  activeGpuIndex: number;
  overlay: { enabled: boolean; dragMode: boolean; settings: OverlaySettings };
  hotkey: string;
  hotkeysEnabled: boolean;
  overlayHotkeyEnabled: boolean;
}
export interface PreviewActionPayloads {
  "scenario.set": PreviewScenario;
  "target.add": { name: string; executable: string; enabled?: boolean };
  "target.update": { id: string; name?: string; executable?: string; enabled?: boolean };
  "target.remove": { id: string };
  "target.select": { id: string };
  "monitor.start": { targetId?: string } | undefined;
  "monitor.stop": undefined;
  "monitor.reset": undefined;
  "report.remove": { id: string };
  "report.regenerate": { id: string };
  "report.diagnose": { id: string };
  "config.update": Partial<PreviewConfig>;
  "preview.reset": undefined;
}
export type PreviewAction = keyof PreviewActionPayloads;
export type PreviewListener = (state: PreviewState) => void;
export interface OverlaySettings {
  display_items: Array<{ id: string; label: string; enabled: boolean }>;
  custom_items: Array<{ id: string; text: string; color: string; enabled: boolean }>;
  opacity: number;
  style: string;
  font: string;
  font_size: number;
  item_width: number;
  font_color: string;
  position_x?: number | null;
  position_y?: number | null;
  vertical_position_x?: number | null;
  vertical_position_y?: number | null;
  delta_password_maps?: string[];
}
export interface GpuSensorData {
  name: string;
  hardware_type: string;
  temperature: number | null;
  usage: number | null;
  fan_speed: number | null;
  power: number | null;
  clock: number | null;
  memory_clock: number | null;
  vram_used: number | null;
  vram_total: number | null;
  voltage: number | null;
}
export interface OverlayHardwareData {
  fps: number | null;
  fps_1low: number | null;
  fps_01low: number | null;
  cpu_usage: number | null;
  cpu_temp: number | null;
  cpu_clock: number | null;
  cpu_voltage: number | null;
  cpu_power: number | null;
  cpu_fan_speed: number | null;
  gpu_temp: number | null;
  gpu_usage: number | null;
  gpu_fan_speed: number | null;
  gpu_power: number | null;
  gpu_clock: number | null;
  gpu_voltage: number | null;
  gpu_memory_clock: number | null;
  memory_usage: number | null;
  ssd_temp: number | null;
  delta_password: string | null;
  game_ping: number | null;
  gpu_vram_used: number | null;
  gpu_vram_total: number | null;
  net_time_offset_ms: number | null;
  cpu_core_clocks: Array<number | null>;
  cpu_core_voltages: Array<number | null>;
  gpu_sensors: GpuSensorData[];
  active_gpu_index: number;
}
