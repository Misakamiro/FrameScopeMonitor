import {
  Activity,
  BarChart3,
  CircleGauge,
  FileStack,
  Info,
  Settings2,
} from "lucide-react";
import type {
  ChartSeries,
  Metric,
  NavigationItem,
  ProcessPreview,
  ReportPreview,
  SettingFieldPreview,
  TargetPreview,
} from "../types";
import type {
  BridgeEventEnvelope,
  BridgeEventType,
  BridgeRequestType,
  ConfigPayload,
  FrameScopeBridgeAdapter,
  FrameScopeConfig,
  FrameScopeTargetConfig,
  LongActionAcceptedPayload,
  ProcessInfo,
  ProcessesRefreshedEventPayload,
  ReportActionPayload,
  ReportListItem,
  ReportListPayload,
  ReportProgressEventPayload,
  StateSnapshotPayload,
  StatusEventPayload,
  TargetsPayload,
} from "../bridge/contract";

export const mockPreviewLabel = "Mock preview - browser-only bridge adapter";

export const navigationItems: NavigationItem[] = [
  {
    id: "overview",
    label: "概览",
    description: "监控状态与最近会话",
    icon: CircleGauge,
  },
  {
    id: "targets",
    label: "目标",
    description: "选择要监控的游戏进程",
    icon: Activity,
  },
  {
    id: "reports",
    label: "报告",
    description: "查看和打开性能报告",
    icon: FileStack,
  },
  {
    id: "settings",
    label: "设置",
    description: "调整采样与保存选项",
    icon: Settings2,
  },
  {
    id: "about",
    label: "关于",
    description: "连接状态与技术边界",
    icon: Info,
  },
];

export const overviewMetrics: Metric[] = [
  {
    label: "监听状态",
    value: "待接入",
    detail: "Web bridge 未连接",
    tone: "diagnostics",
  },
  {
    label: "启用目标",
    value: "6",
    detail: "来自 mock config preview",
    tone: "primary",
    trend: "+2",
  },
  {
    label: "最近 FPS",
    value: "142.6",
    detail: "静态视觉样本",
    tone: "success",
  },
  {
    label: "报告队列",
    value: "1",
    detail: "生成中样式预览",
    tone: "warning",
  },
];

export const processPreview: ProcessPreview[] = [
  {
    name: "VALORANT-Win64-Shipping.exe",
    pid: 12940,
    cpu: 18.4,
    memory: "2.8 GB",
    io: "42 MB/s",
    status: "watching",
  },
  {
    name: "TslGame-Win64-Shipping.exe",
    pid: 0,
    cpu: 0,
    memory: "-",
    io: "-",
    status: "idle",
  },
  {
    name: "FrameScopeProcessSampler.exe",
    pid: 8840,
    cpu: 1.8,
    memory: "82 MB",
    io: "3 MB/s",
    status: "running",
  },
  {
    name: "ProtectedAntiCheat.exe",
    pid: 4304,
    cpu: 4.2,
    memory: "310 MB",
    io: "1 MB/s",
    status: "blocked",
  },
];

export const targetPreview: TargetPreview[] = [
  {
    game: "Valorant",
    process: "VALORANT-Win64-Shipping.exe",
    enabled: true,
    sampleMs: 100,
    lastSeen: "12:41",
  },
  {
    game: "PUBG",
    process: "TslGame-Win64-Shipping.exe",
    enabled: true,
    sampleMs: 100,
    lastSeen: "未发现",
  },
  {
    game: "CS2",
    process: "cs2.exe",
    enabled: false,
    sampleMs: 100,
    lastSeen: "昨天",
  },
  {
    game: "Cyberpunk 2077",
    process: "Cyberpunk2077.exe",
    enabled: true,
    sampleMs: 250,
    lastSeen: "05-20",
  },
];

export const reportPreview: ReportPreview[] = [
  {
    id: "r-20260520-1241",
    game: "Valorant",
    timestamp: "2026-05-20 12:41",
    status: "ready",
    fps: "142.6 avg",
    path: "framescope-runs\\Valorant\\Valorant-20260520-1241",
  },
  {
    id: "r-20260520-1158",
    game: "PUBG",
    timestamp: "2026-05-20 11:58",
    status: "generating",
    fps: "处理中",
    path: "framescope-runs\\PUBG\\PUBG-20260520-1158",
  },
  {
    id: "r-20260519-2314",
    game: "CS2",
    timestamp: "2026-05-19 23:14",
    status: "failed",
    fps: "N/A",
    path: "framescope-runs\\CS2\\CS2-20260519-2314",
  },
];

export const settingsPreview: SettingFieldPreview[] = [
  {
    label: "数据目录",
    value: "%LOCALAPPDATA%\\FrameScopeMonitorData\\framescope-runs",
    helper: "只展示字段结构，本轮不写入配置。",
    kind: "text",
  },
  {
    label: "监听轮询间隔",
    value: "1000 ms",
    helper: "后端接入后由 C# 配置校验。",
    kind: "number",
  },
  {
    label: "自动打开报告",
    value: "开启",
    helper: "静态 toggle 状态，不触发真实保存。",
    kind: "toggle",
  },
  {
    label: "日志保留",
    value: "14 天",
    helper: "视觉预览值，不清理真实日志。",
    kind: "select",
  },
];

export const chartSeries: ChartSeries[] = [
  {
    label: "FPS",
    tone: "success",
    points: [44, 55, 51, 68, 62, 76, 72, 88, 84, 92, 89, 96],
  },
  {
    label: "Frame time",
    tone: "primary",
    points: [71, 64, 67, 52, 58, 46, 49, 39, 42, 34, 38, 31],
  },
  {
    label: "CPU",
    tone: "warning",
    points: [30, 34, 36, 42, 39, 44, 48, 45, 51, 49, 54, 53],
  },
];

export const reportTrendSeries: ChartSeries[] = [
  {
    label: "报告生成耗时",
    tone: "diagnostics",
    points: [62, 58, 48, 52, 41, 39, 35, 32],
  },
  {
    label: "样本完整度",
    tone: "success",
    points: [74, 76, 82, 79, 88, 91, 90, 93],
  },
];

export const pageKickerIcon = BarChart3;

const mockStartedAt = new Date().toISOString();

const mockConfig: FrameScopeConfig = {
  PollIntervalMs: 1000,
  DataRoot: "%LOCALAPPDATA%\\FrameScopeMonitorData\\framescope-runs",
  OpenReportOnComplete: true,
  EnableVerboseLogs: false,
  EnablePerformanceDiagnosticsLogs: false,
  AutoGenerateDiagnosticReport: false,
  LogRetentionDays: 14,
  MaxLogDiskMb: 100,
  MonitorScript: "native-csharp",
  Targets: targetPreview.map((target) => ({
    Enabled: target.enabled,
    Name: target.game,
    ProcessName: target.process,
    SampleIntervalMs: target.sampleMs,
    ProcessSampleIntervalMs: Math.max(100, target.sampleMs),
    SlowSampleIntervalMs: 1000,
    OpenReportOnComplete: true,
  })),
};

const mockProcesses: ProcessInfo[] = processPreview.map((process) => ({
  processName: process.name,
  processId: process.pid,
  windowTitle: process.status === "watching" ? "预览窗口" : "",
  displayText: `${process.name}${process.pid ? ` (${process.pid})` : ""}`,
}));

const mockReportItems: ReportListItem[] = reportPreview.map((report, index) => {
  const runDir = `mock://framescope-runs/${report.game}/${report.id}`;
  return {
    reportId: report.id,
    game: report.game,
    processName: index === 0 ? "VALORANT-Win64-Shipping.exe" : index === 1 ? "TslGame-Win64-Shipping.exe" : "cs2.exe",
    time: report.timestamp,
    runDir,
    reportHtml: `${runDir}/charts/framescope-interactive-report.html`,
    monitorExitCode: report.status === "failed" ? 1 : 0,
    reportExists: report.status !== "generating",
    runDirExists: true,
    canOpenReport: report.status === "ready",
    canOpenDirectory: true,
    canRegenerate: true,
    reportSizeBytes: report.status === "ready" ? 842_144 : 0,
    lastWriteTime: mockStartedAt,
    reportKind: report.status === "ready" ? "full" : "pending",
    frameCount: report.status === "ready" ? 240 : 0,
    hasFrameData: report.status === "ready",
  };
});

export function createMockBridgeAdapter(): FrameScopeBridgeAdapter {
  const listeners = new Map<BridgeEventType, Set<(event: BridgeEventEnvelope) => void>>();
  let savedConfig: FrameScopeConfig = cloneConfig(mockConfig);
  let monitorRunning = false;
  let requestCounter = 0;
  const failureModes = readMockFailureModes();

  const publish = (event: BridgeEventEnvelope) => {
    const bucket = listeners.get(event.type);
    if (!bucket) return;
    for (const listener of bucket) listener(event);
  };

  return {
    environment: "mock",
    request: async <TPayload>(
      type: BridgeRequestType,
      payload: Record<string, unknown> = {},
    ): Promise<TPayload> => {
      requestCounter += 1;
      const requestId = `mock-${requestCounter}`;
      await delay(120);

      if (type === "state.snapshot") {
        return buildMockSnapshot(savedConfig) as TPayload;
      }

      if (type === "config.get") {
        return buildMockConfigPayload(savedConfig, "loaded") as TPayload;
      }

      if (type === "config.save") {
        throwIfMockFailure(failureModes, type);
        const nextConfig = (payload as { config?: FrameScopeConfig }).config;
        if (!nextConfig) {
          throw new Error("Mock config.save requires payload.config.");
        }
        savedConfig = cloneConfig(nextConfig);
        publish({
          type: "event.status",
          payload: {
            requestId,
            status: "config.saved",
            enabledTargetCount: savedConfig.Targets.filter((target) => target.Enabled).length,
          },
          sentAt: new Date().toISOString(),
        });
        return buildMockConfigPayload(savedConfig, "saved") as TPayload;
      }

      if (type === "targets.get") {
        throwIfMockFailure(failureModes, type);
        return buildMockTargetsPayload(savedConfig, "loaded") as TPayload;
      }

      if (type === "targets.save") {
        throwIfMockFailure(failureModes, type);
        if (payloadHasPathAuthority(payload)) {
          throw new Error("Mock targets.save rejects path authority.");
        }
        const targets = (payload as { targets?: FrameScopeTargetConfig[] }).targets;
        if (!Array.isArray(targets)) {
          throw new Error("Mock targets.save requires payload.targets.");
        }
        savedConfig = {
          ...savedConfig,
          DataRoot: typeof payload.dataRoot === "string" ? payload.dataRoot : savedConfig.DataRoot,
          OpenReportOnComplete:
            typeof payload.openReportOnComplete === "boolean"
              ? payload.openReportOnComplete
              : savedConfig.OpenReportOnComplete,
          Targets: cloneTargets(targets),
        };
        publish({
          type: "event.status",
          payload: {
            requestId,
            status: "targets.saved",
            enabledTargetCount: savedConfig.Targets.filter((target) => target.Enabled).length,
          } satisfies StatusEventPayload,
          sentAt: new Date().toISOString(),
        });
        return buildMockTargetsPayload(savedConfig, "saved") as TPayload;
      }

      if (type === "processes.refresh") {
        throwIfMockFailure(failureModes, type);
        const query = String((payload as { query?: string }).query ?? "");
        const accepted = {
          status: "accepted",
          requestId,
          message: "进程刷新请求已接受。浏览器预览不会读取真实系统进程。",
        };
        window.setTimeout(() => {
          const normalizedQuery = query.trim().toLowerCase();
          const processes = normalizedQuery
            ? mockProcesses.filter((process) => process.processName.toLowerCase().includes(normalizedQuery))
            : mockProcesses;
          publish({
            type: "event.processesRefreshed",
            payload: {
              requestId,
              status: "completed",
              query,
              count: processes.length,
              truncated: false,
              processes,
              refreshedAt: new Date().toISOString(),
            } satisfies ProcessesRefreshedEventPayload,
            sentAt: new Date().toISOString(),
          });
        }, 320);
        return accepted as TPayload;
      }

      if (type === "reports.list") {
        throwIfMockFailure(failureModes, type);
        return buildMockReportsPayload() as TPayload;
      }

      if (type === "reports.open" || type === "reports.openDirectory") {
        throwIfMockFailure(failureModes, type);
        if (payloadHasPathAuthority(payload)) {
          throw new Error(`${type} accepts only reportId in mock preview.`);
        }
        const report = findMockReport(String(payload.reportId ?? ""));
        const status = type === "reports.open" ? "opened" : "directory_opened";
        const response = {
          ...report,
          status,
          message:
            type === "reports.open"
              ? "报告打开操作已在预览中完成。"
              : "目录打开操作已在预览中完成。",
        } satisfies ReportActionPayload;
        publish({
          type: "event.reportsChanged",
          payload: response,
          sentAt: new Date().toISOString(),
        });
        return response as TPayload;
      }

      if (type === "reports.regenerate") {
        if (payloadHasPathAuthority(payload)) {
          throw new Error("reports.regenerate accepts only reportId in mock preview.");
        }
        const report = findMockReport(String(payload.reportId ?? ""));
        const accepted = buildMockAccepted(requestId, "reports.regenerate");
        publishReportProgress(publish, requestId, "reports.regenerate", "report.regenerating", 5, {
          reportId: report.reportId,
          runDir: report.runDir,
          reportHtml: report.reportHtml,
        });
        window.setTimeout(() => {
          if (failureModes.has("reports.regenerate")) {
            publishReportProgress(publish, requestId, "reports.regenerate", "error", 100, {
              reportId: report.reportId,
              runDir: report.runDir,
              reportHtml: report.reportHtml,
              ok: false,
              message: "预览中的报告重新生成失败。",
              code: "mock_report_regenerate_failed",
            });
            return;
          }
          publishReportProgress(publish, requestId, "reports.regenerate", "completed", 100, {
            reportId: report.reportId,
            runDir: report.runDir,
            reportHtml: report.reportHtml,
            message: "预览中的报告已重新生成。",
          });
        }, 360);
        return accepted as TPayload;
      }

      if (type === "diagnostics.generate") {
        if (payloadHasPathAuthority(payload)) {
          throw new Error("diagnostics.generate accepts only an optional reportId in mock preview.");
        }
        const reportId = typeof payload.reportId === "string" ? payload.reportId : "";
        const report = reportId ? findMockReport(reportId) : mockReportItems[0];
        const accepted = buildMockAccepted(requestId, "diagnostics.generate");
        publishReportProgress(publish, requestId, "diagnostics.generate", "diagnostics.generating", 5, {
          reportId: report?.reportId ?? "",
          runDir: report?.runDir ?? "mock://framescope-runs",
        });
        window.setTimeout(() => {
          if (failureModes.has("diagnostics.generate")) {
            publishReportProgress(publish, requestId, "diagnostics.generate", "error", 100, {
              reportId: report?.reportId ?? "",
              runDir: report?.runDir ?? "mock://framescope-runs",
              ok: false,
              message: "预览中的诊断生成失败。",
              code: "mock_diagnostics_failed",
            });
            return;
          }
          publishReportProgress(publish, requestId, "diagnostics.generate", "completed", 100, {
            reportId: report?.reportId ?? "",
            runDir: report?.runDir ?? "mock://framescope-runs",
            markdownPath: "mock://diagnostics/frame-scope-diagnostic.md",
            jsonPath: "mock://diagnostics/frame-scope-diagnostic.json",
            message: "预览中的诊断已生成。",
          });
        }, 360);
        return accepted as TPayload;
      }

      if (type === "monitor.start" || type === "monitor.stop") {
        const accepted = buildMockAccepted(requestId, type);
        window.setTimeout(() => {
          if (failureModes.has(type)) {
            publish({
              type: "event.error",
              payload: {
                requestId,
                type,
                code: "mock_monitor_action_failed",
                message: "预览中的监控操作失败。",
              },
              sentAt: new Date().toISOString(),
            });
            return;
          }
          monitorRunning = type === "monitor.start";
          publish({
            type: "event.status",
            payload: {
              requestId,
              status: monitorRunning ? "monitor.started" : "monitor.stopped",
              action: type,
              message: monitorRunning ? "预览中的监控已启动。" : "预览中的监控已停止。",
              pid: monitorRunning ? 4242 : 0,
            } satisfies StatusEventPayload,
            sentAt: new Date().toISOString(),
          });
        }, 320);
        return accepted as TPayload;
      }

      throw new Error(`Unsupported mock bridge request: ${type}`);
    },
    subscribe: (type, listener) => {
      const typedListener = listener as (event: BridgeEventEnvelope) => void;
      const bucket = listeners.get(type) ?? new Set<(event: BridgeEventEnvelope) => void>();
      bucket.add(typedListener);
      listeners.set(type, bucket);
      return () => {
        const current = listeners.get(type);
        current?.delete(typedListener);
        if (current?.size === 0) listeners.delete(type);
      };
    },
  };
}

function buildMockSnapshot(config: FrameScopeConfig): StateSnapshotPayload {
  return {
    bridgeStatus: "mock-preview",
    bridgeVersion: 1,
    generatedAt: new Date().toISOString(),
    root: "browser-preview",
    watcher: {
      running: false,
      pid: 0,
      statePath: "mock://framescope-watcher-state.json",
      completedRuns: reportPreview.filter((report) => report.status === "ready").length,
      lastReport: reportPreview[0]?.path ?? "",
      lastError: "",
    },
    config: {
      exists: true,
      path: "mock://framescope-config.json",
      enabledTargetCount: config.Targets.filter((target) => target.Enabled).length,
      targetCount: config.Targets.length,
      dataRoot: config.DataRoot,
    },
    reports: {
      historyPath: "mock://framescope-history.jsonl",
      historyExists: false,
    },
  };
}

function buildMockConfigPayload(config: FrameScopeConfig, status: ConfigPayload["status"]): ConfigPayload {
  return {
    status,
    configPath: "mock://framescope-config.json",
    config: cloneConfig(config),
    enabledTargetCount: config.Targets.filter((target) => target.Enabled).length,
    targetCount: config.Targets.length,
    resolvedDataRoot: config.DataRoot,
    loadedAt: mockStartedAt,
  };
}

function buildMockTargetsPayload(config: FrameScopeConfig, status: TargetsPayload["status"]): TargetsPayload {
  return {
    status,
    configPath: "mock://framescope-config.json",
    dataRoot: config.DataRoot,
    resolvedDataRoot: config.DataRoot,
    openReportOnComplete: config.OpenReportOnComplete,
    enabledTargetCount: config.Targets.filter((target) => target.Enabled).length,
    targetCount: config.Targets.length,
    targets: cloneTargets(config.Targets),
    loadedAt: new Date().toISOString(),
  };
}

function buildMockReportsPayload(): ReportListPayload {
  return {
    status: "loaded",
    historyPath: "mock://framescope-history.jsonl",
    dataRoot: mockConfig.DataRoot,
    count: mockReportItems.length,
    reports: mockReportItems.map((report) => ({ ...report })),
    loadedAt: new Date().toISOString(),
  };
}

function findMockReport(reportId: string): ReportListItem {
  const report = mockReportItems.find((item) => item.reportId === reportId);
  if (!report) {
    throw new Error("预览报告不存在。");
  }
  return { ...report };
}

function buildMockAccepted(requestId: string, action: string): LongActionAcceptedPayload {
  return {
    status: "accepted",
    requestId,
    action,
    message: `${action} 请求已接受。`,
  };
}

function publishReportProgress(
  publish: (event: BridgeEventEnvelope) => void,
  requestId: string,
  action: string,
  status: string,
  percent: number,
  payload: Partial<ReportProgressEventPayload>,
) {
  publish({
    type: "event.reportProgress",
    payload: {
      requestId,
      status,
      action,
      ok: status !== "error",
      message: payload.message ?? `${action} ${status}.`,
      percent,
      ...payload,
    } satisfies ReportProgressEventPayload,
    sentAt: new Date().toISOString(),
  });
}

function cloneConfig(config: FrameScopeConfig): FrameScopeConfig {
  return JSON.parse(JSON.stringify(config)) as FrameScopeConfig;
}

function cloneTargets(targets: FrameScopeTargetConfig[]): FrameScopeTargetConfig[] {
  return JSON.parse(JSON.stringify(targets)) as FrameScopeTargetConfig[];
}

function payloadHasPathAuthority(payload: Record<string, unknown>) {
  return (
    "path" in payload ||
    "reportHtml" in payload ||
    "runDir" in payload ||
    "directory" in payload ||
    "file" in payload ||
    "configPath" in payload
  );
}

function readMockFailureModes() {
  if (typeof window === "undefined") return new Set<string>();
  const raw = new URLSearchParams(window.location.search).get("mockFailure") ?? "";
  return new Set(
    raw
      .split(",")
      .map((mode) => mode.trim())
      .filter(Boolean),
  );
}

function throwIfMockFailure(failureModes: Set<string>, type: BridgeRequestType) {
  if (failureModes.has(type)) {
    throw new Error(`${type} 在浏览器预览中失败。`);
  }
}

function delay(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}
