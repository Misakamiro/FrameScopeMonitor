import type { LucideIcon } from "lucide-react";

export type AppPage = "overview" | "targets" | "reports" | "settings" | "about";

export type Tone =
  | "neutral"
  | "primary"
  | "success"
  | "warning"
  | "danger"
  | "diagnostics";

export interface NavigationItem {
  id: AppPage;
  label: string;
  description: string;
  icon: LucideIcon;
}

export interface Metric {
  label: string;
  value: string;
  detail: string;
  tone: Tone;
  trend?: string;
}

export interface ProcessPreview {
  name: string;
  pid: number;
  cpu: number;
  memory: string;
  io: string;
  status: "running" | "idle" | "watching" | "blocked";
}

export interface TargetPreview {
  game: string;
  process: string;
  enabled: boolean;
  sampleMs: number;
  lastSeen: string;
}

export interface ReportPreview {
  id: string;
  game: string;
  timestamp: string;
  status: "ready" | "generating" | "failed";
  fps: string;
  path: string;
}

export interface ChartSeries {
  label: string;
  tone: Tone;
  points: number[];
}

export interface SettingFieldPreview {
  label: string;
  value: string;
  helper: string;
  kind: "text" | "number" | "toggle" | "select";
}
