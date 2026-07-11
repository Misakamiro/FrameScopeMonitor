import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type {
  BridgeEnvironment,
  ConfigPayload,
  ErrorEventPayload,
  FrameScopeBridgeAdapter,
  FrameScopeConfig,
  FrameScopeTargetConfig,
  HostWindowChangedEventPayload,
  LogsOpenDirectoryPayload,
  LongActionAcceptedPayload,
  MonitorActionAcceptedPayload,
  ProcessInfo,
  ProcessesRefreshedEventPayload,
  ProcessesRefreshAcceptedPayload,
  ReportActionPayload,
  ReportListPayload,
  ReportProgressEventPayload,
  StateSnapshotPayload,
  StatusEventPayload,
  TargetsPayload,
} from "../bridge/contract";
import { BridgeRequestError, createFrameScopeBridgeAdapter } from "../bridge/webviewBridge";

export type AsyncStatus = "idle" | "loading" | "success" | "error";
export type ReportOperationKind = "open" | "openDirectory" | "regenerate";

export interface LoadState<TData> {
  status: AsyncStatus;
  data: TData | null;
  message: string;
  error: string;
  updatedAt: string;
}

export interface OperationState {
  status: AsyncStatus;
  message: string;
  error: string;
  requestId: string;
  updatedAt: string;
}

export interface MonitorRuntimeState {
  running: boolean | null;
  pid: number;
  message: string;
  updatedAt: string;
}

export interface FrameScopeBridgeViewState {
  environment: BridgeEnvironment;
  isMockPreview: boolean;
  snapshot: LoadState<StateSnapshotPayload>;
  config: LoadState<ConfigPayload>;
  reports: LoadState<ReportListPayload>;
  targets: LoadState<TargetsPayload>;
  processes: ProcessInfo[];
  processRefresh: OperationState;
  configSave: OperationState;
  targetsSave: OperationState;
  logsOpenDirectory: OperationState;
  monitorAction: OperationState;
  monitorRuntime: MonitorRuntimeState;
  reportOperations: Record<string, OperationState>;
  diagnosticsGenerate: OperationState;
  diagnosticsResult: ReportProgressEventPayload | null;
  lastStatusEvent: StatusEventPayload | null;
  lastErrorEvent: ErrorEventPayload | null;
  refreshSnapshot: () => Promise<void>;
  refreshConfig: () => Promise<void>;
  refreshReports: () => Promise<void>;
  refreshTargets: () => Promise<void>;
  refreshProcesses: (query?: string) => Promise<void>;
  saveConfig: (config: FrameScopeConfig) => Promise<ConfigPayload | null>;
  saveTargets: (payload: {
    targets: FrameScopeTargetConfig[];
    dataRoot?: string;
    openReportOnComplete?: boolean;
  }) => Promise<TargetsPayload | null>;
  openLogsDirectory: () => Promise<LogsOpenDirectoryPayload | null>;
  startMonitor: () => Promise<void>;
  stopMonitor: () => Promise<void>;
  openReport: (reportId: string) => Promise<ReportActionPayload | null>;
  openReportDirectory: (reportId: string) => Promise<ReportActionPayload | null>;
  regenerateReport: (reportId: string) => Promise<void>;
  generateDiagnostics: (reportId?: string) => Promise<void>;
  getReportOperationState: (kind: ReportOperationKind, reportId: string) => OperationState;
}

const emptyLoadState = <TData,>(message: string): LoadState<TData> => ({
  status: "idle",
  data: null,
  message,
  error: "",
  updatedAt: "",
});

const emptyOperationState = (message: string): OperationState => ({
  status: "idle",
  message,
  error: "",
  requestId: "",
  updatedAt: "",
});

const defaultReportOperationState = emptyOperationState("尚未执行报告操作。");
const VISIBLE_STATE_SNAPSHOT_INTERVAL_MS = 1000;
const HIDDEN_OR_TRAY_STATE_SNAPSHOT_INTERVAL_MS = 3000;
const IMMEDIATE_REFRESH_COALESCE_MS = 200;

export function useFrameScopeBridgeState(adapterOverride?: FrameScopeBridgeAdapter): FrameScopeBridgeViewState {
  const adapter = useMemo(() => adapterOverride ?? createFrameScopeBridgeAdapter(), [adapterOverride]);
  const [snapshot, setSnapshot] = useState<LoadState<StateSnapshotPayload>>(
    emptyLoadState("尚未读取状态。"),
  );
  const [config, setConfig] = useState<LoadState<ConfigPayload>>(
    emptyLoadState("尚未读取设置。"),
  );
  const [reports, setReports] = useState<LoadState<ReportListPayload>>(
    emptyLoadState("尚未读取报告。"),
  );
  const [targets, setTargets] = useState<LoadState<TargetsPayload>>(
    emptyLoadState("尚未读取目标。"),
  );
  const [processes, setProcesses] = useState<ProcessInfo[]>([]);
  const [processRefresh, setProcessRefresh] = useState<OperationState>(
    emptyOperationState("尚未刷新进程。"),
  );
  const [configSave, setConfigSave] = useState<OperationState>(
    emptyOperationState("本次会话尚未保存设置修改。"),
  );
  const [targetsSave, setTargetsSave] = useState<OperationState>(
    emptyOperationState("本次会话尚未保存目标修改。"),
  );
  const [logsOpenDirectory, setLogsOpenDirectory] = useState<OperationState>(
    emptyOperationState("尚未打开日志目录。"),
  );
  const [monitorAction, setMonitorAction] = useState<OperationState>(
    emptyOperationState("尚未启动或停止监控。"),
  );
  const [monitorRuntime, setMonitorRuntime] = useState<MonitorRuntimeState>({
    running: null,
    pid: 0,
    message: "正在等待监控状态。",
    updatedAt: "",
  });
  const [reportOperations, setReportOperations] = useState<Record<string, OperationState>>({});
  const [diagnosticsGenerate, setDiagnosticsGenerate] = useState<OperationState>(
    emptyOperationState("尚未生成诊断。"),
  );
  const [diagnosticsResult, setDiagnosticsResult] = useState<ReportProgressEventPayload | null>(null);
  const [lastStatusEvent, setLastStatusEvent] = useState<StatusEventPayload | null>(null);
  const [lastErrorEvent, setLastErrorEvent] = useState<ErrorEventPayload | null>(null);

  const latestProcessRequestId = useRef("");
  const latestMonitorRequestId = useRef("");
  const completedMonitorRequestId = useRef("");
  const monitorRuntimeOverrideUntil = useRef(0);
  const latestDiagnosticsRequestId = useRef("");
  const reportRequestMap = useRef(new Map<string, { kind: ReportOperationKind; reportId: string }>());
  const processEventTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
  const monitorEventTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
  const diagnosticsEventTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
  const reportEventTimeouts = useRef(new Map<string, ReturnType<typeof setTimeout>>());
  const snapshotRefreshInFlight = useRef(false);
  const snapshotRefreshQueued = useRef(false);
  const snapshotRefreshRunner = useRef<(silent?: boolean) => Promise<void>>(async () => {});
  const immediateSnapshotRefreshTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
  const currentSnapshotIntervalMs = useRef(VISIBLE_STATE_SNAPSHOT_INTERVAL_MS);
  const lastWatcherReportSignature = useRef("");
  const refreshReportsRef = useRef<() => Promise<void>>(async () => {});

  const clearProcessEventTimeout = useCallback(() => {
    if (!processEventTimeout.current) return;
    clearTimeout(processEventTimeout.current);
    processEventTimeout.current = null;
  }, []);

  const clearMonitorEventTimeout = useCallback(() => {
    if (!monitorEventTimeout.current) return;
    clearTimeout(monitorEventTimeout.current);
    monitorEventTimeout.current = null;
  }, []);

  const clearDiagnosticsEventTimeout = useCallback(() => {
    if (!diagnosticsEventTimeout.current) return;
    clearTimeout(diagnosticsEventTimeout.current);
    diagnosticsEventTimeout.current = null;
  }, []);

  const clearReportEventTimeout = useCallback((requestId: string) => {
    const timeout = reportEventTimeouts.current.get(requestId);
    if (!timeout) return;
    clearTimeout(timeout);
    reportEventTimeouts.current.delete(requestId);
  }, []);

  const clearImmediateSnapshotRefreshTimeout = useCallback(() => {
    if (!immediateSnapshotRefreshTimeout.current) return;
    clearTimeout(immediateSnapshotRefreshTimeout.current);
    immediateSnapshotRefreshTimeout.current = null;
  }, []);

  const setReportOperation = useCallback(
    (kind: ReportOperationKind, reportId: string, next: OperationState) => {
      setReportOperations((current) => ({
        ...current,
        [createReportOperationKey(kind, reportId)]: next,
      }));
    },
    [],
  );

  const requestSnapshot = useCallback(async (silent = false) => {
    if (snapshotRefreshInFlight.current) {
      snapshotRefreshQueued.current = true;
      return;
    }

    snapshotRefreshInFlight.current = true;
    if (!silent) {
      setSnapshot((current) => ({
        ...current,
        status: "loading",
        message: "正在读取监控状态。",
        error: "",
      }));
    }

    try {
      const data = await adapter.request<StateSnapshotPayload>("state.snapshot", {}, { timeoutMs: 8000 });
      const now = new Date().toISOString();
      const watcherReportSignature = `${data.watcher.completedRuns}|${data.watcher.lastReport}`;
      const hiddenOrTray = data.host.windowVisible === false;
      currentSnapshotIntervalMs.current = hiddenOrTray
        ? HIDDEN_OR_TRAY_STATE_SNAPSHOT_INTERVAL_MS
        : VISIBLE_STATE_SNAPSHOT_INTERVAL_MS;

      setSnapshot({
        status: "success",
        data,
        message: "监控状态已读取。",
        error: "",
        updatedAt: now,
      });
      const effectiveMonitorRunning = data.watcher.running || monitorRuntimeOverrideUntil.current > Date.now();
      if (data.watcher.running) monitorRuntimeOverrideUntil.current = 0;
      setMonitorRuntime({
        running: effectiveMonitorRunning,
        pid: data.watcher.pid,
        message: data.watcher.running ? "监控 worker 正在运行。" : "监控 worker 未启动。",
        updatedAt: data.generatedAt || now,
      });

      if (lastWatcherReportSignature.current && watcherReportSignature !== lastWatcherReportSignature.current) {
        void refreshReportsRef.current();
      }
      lastWatcherReportSignature.current = watcherReportSignature;
    } catch (error) {
      setSnapshot((current) => ({
        ...current,
        status: "error",
        message: "监控状态读取失败。",
        error: getErrorMessage(error),
        updatedAt: new Date().toISOString(),
      }));
    } finally {
      snapshotRefreshInFlight.current = false;
      if (snapshotRefreshQueued.current) {
        snapshotRefreshQueued.current = false;
        void snapshotRefreshRunner.current(true);
      }
    }
  }, [adapter]);

  snapshotRefreshRunner.current = requestSnapshot;

  const refreshSnapshot = useCallback(() => requestSnapshot(false), [requestSnapshot]);

  const scheduleImmediateSnapshotRefresh = useCallback(() => {
    clearImmediateSnapshotRefreshTimeout();
    immediateSnapshotRefreshTimeout.current = setTimeout(() => {
      immediateSnapshotRefreshTimeout.current = null;
      void snapshotRefreshRunner.current(true);
    }, IMMEDIATE_REFRESH_COALESCE_MS);
  }, [clearImmediateSnapshotRefreshTimeout]);

  const refreshConfig = useCallback(async () => {
    setConfig((current) => ({
      ...current,
      status: "loading",
      message: "正在读取应用设置。",
      error: "",
    }));
    try {
      const data = await adapter.request<ConfigPayload>("config.get", {}, { timeoutMs: 10000 });
      setConfig({
        status: "success",
        data,
        message: "应用设置已读取。",
        error: "",
        updatedAt: new Date().toISOString(),
      });
    } catch (error) {
      setConfig((current) => ({
        ...current,
        status: "error",
        message: "应用设置读取失败。",
        error: getErrorMessage(error),
        updatedAt: new Date().toISOString(),
      }));
    }
  }, [adapter]);

  const refreshReports = useCallback(async () => {
    setReports((current) => ({
      ...current,
      status: "loading",
      message: "正在读取报告列表。",
      error: "",
    }));
    try {
      const data = await adapter.request<ReportListPayload>("reports.list", {}, { timeoutMs: 10000 });
      setReports({
        status: "success",
        data,
        message: `已读取 ${data.count} 条报告。`,
        error: "",
        updatedAt: new Date().toISOString(),
      });
    } catch (error) {
      setReports((current) => ({
        ...current,
        status: "error",
        message: "报告列表读取失败。",
        error: getErrorMessage(error),
        updatedAt: new Date().toISOString(),
      }));
    }
  }, [adapter]);

  refreshReportsRef.current = refreshReports;

  const refreshTargets = useCallback(async () => {
    setTargets((current) => ({
      ...current,
      status: "loading",
      message: "正在读取监控目标。",
      error: "",
    }));
    try {
      const data = await adapter.request<TargetsPayload>("targets.get", {}, { timeoutMs: 10000 });
      setTargets({
        status: "success",
        data,
        message: `已读取 ${data.targetCount} 个目标。`,
        error: "",
        updatedAt: new Date().toISOString(),
      });
    } catch (error) {
      setTargets((current) => ({
        ...current,
        status: "error",
        message: "监控目标读取失败。",
        error: getErrorMessage(error),
        updatedAt: new Date().toISOString(),
      }));
    }
  }, [adapter]);

  const refreshProcesses = useCallback(
    async (query = "") => {
      clearProcessEventTimeout();
      setProcessRefresh({
        status: "loading",
        message: "正在请求刷新进程。",
        error: "",
        requestId: "",
        updatedAt: new Date().toISOString(),
      });

      try {
        const accepted = await adapter.request<ProcessesRefreshAcceptedPayload>(
          "processes.refresh",
          { query },
          { timeoutMs: 10000 },
        );
        latestProcessRequestId.current = accepted.requestId;

        setProcessRefresh({
          status: "loading",
          message: accepted.message,
          error: "",
          requestId: accepted.requestId,
          updatedAt: new Date().toISOString(),
        });

        processEventTimeout.current = setTimeout(() => {
          if (latestProcessRequestId.current !== accepted.requestId) return;
          setProcessRefresh({
            status: "error",
            message: "进程刷新超时。",
            error: "等待进程列表返回超时。",
            requestId: accepted.requestId,
            updatedAt: new Date().toISOString(),
          });
        }, 15000);
      } catch (error) {
        setProcessRefresh({
          status: "error",
          message: "进程刷新失败。",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
      }
    },
    [adapter, clearProcessEventTimeout],
  );

  const saveConfig = useCallback(
    async (nextConfig: FrameScopeConfig) => {
      setConfigSave({
        status: "loading",
        message: "正在保存应用设置。",
        error: "",
        requestId: "",
        updatedAt: new Date().toISOString(),
      });
      try {
        const dataPromise = adapter.request<ConfigPayload>(
          "config.save",
          { config: nextConfig } as unknown as Record<string, unknown>,
          { timeoutMs: 10000 },
        );
        const data = await withMinimumDelay(dataPromise, 180);
        setConfig({
          status: "success",
          data,
          message: "应用设置已保存并重新读取。",
          error: "",
          updatedAt: new Date().toISOString(),
        });
        setConfigSave({
          status: "success",
          message: "应用设置已保存。",
          error: "",
          requestId: "",
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
        void refreshTargets();
        return data;
      } catch (error) {
        setConfigSave({
          status: "error",
          message: "应用设置保存失败。",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
        return null;
      }
    },
    [adapter, refreshTargets, scheduleImmediateSnapshotRefresh],
  );

  const saveTargets = useCallback(
    async (payload: {
      targets: FrameScopeTargetConfig[];
      dataRoot?: string;
      openReportOnComplete?: boolean;
    }) => {
      setTargetsSave({
        status: "loading",
        message: "正在保存目标设置。",
        error: "",
        requestId: "",
        updatedAt: new Date().toISOString(),
      });
      try {
        const dataPromise = adapter.request<TargetsPayload>(
          "targets.save",
          {
            targets: payload.targets,
            dataRoot: payload.dataRoot,
            openReportOnComplete: payload.openReportOnComplete,
          },
          { timeoutMs: 10000 },
        );
        const data = await withMinimumDelay(dataPromise, 180);
        setTargets({
          status: "success",
          data,
          message: "目标设置已保存并重新读取。",
          error: "",
          updatedAt: new Date().toISOString(),
        });
        setTargetsSave({
          status: "success",
          message: "目标设置已保存。",
          error: "",
          requestId: "",
          updatedAt: new Date().toISOString(),
        });
        void refreshConfig();
        scheduleImmediateSnapshotRefresh();
        return data;
      } catch (error) {
        setTargetsSave({
          status: "error",
          message: "目标设置保存失败。",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
        return null;
      }
    },
    [adapter, refreshConfig, scheduleImmediateSnapshotRefresh],
  );

  const startMonitor = useCallback(async () => {
    if (monitorAction.status === "loading") return;
    clearMonitorEventTimeout();
    setMonitorAction({
      status: "loading",
      message: "正在启动监控 worker。",
      error: "",
      requestId: "",
      updatedAt: new Date().toISOString(),
    });
    try {
      const accepted = await adapter.request<MonitorActionAcceptedPayload>("monitor.start", {}, { timeoutMs: 10000 });
      monitorRuntimeOverrideUntil.current = Date.now() + 15000;
      latestMonitorRequestId.current = accepted.requestId;
      if (completedMonitorRequestId.current === accepted.requestId) {
        scheduleImmediateSnapshotRefresh();
        return;
      }
      setMonitorAction({
        status: "loading",
        message: accepted.message,
        error: "",
        requestId: accepted.requestId,
        updatedAt: new Date().toISOString(),
      });
      scheduleImmediateSnapshotRefresh();
      monitorEventTimeout.current = setTimeout(() => {
        if (latestMonitorRequestId.current !== accepted.requestId) return;
        setMonitorAction({
          status: "error",
          message: "启动监控超时。",
          error: "等待监控 worker 启动状态超时。",
          requestId: accepted.requestId,
          updatedAt: new Date().toISOString(),
        });
      }, 20000);
    } catch (error) {
      setMonitorAction({
        status: "error",
        message: "启动监控失败。",
        error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
      });
      scheduleImmediateSnapshotRefresh();
    }
  }, [adapter, clearMonitorEventTimeout, monitorAction.status, scheduleImmediateSnapshotRefresh]);

  const stopMonitor = useCallback(async () => {
    if (monitorAction.status === "loading") return;
    clearMonitorEventTimeout();
    setMonitorAction({
      status: "loading",
      message: "正在停止监控 worker。",
      error: "",
      requestId: "",
      updatedAt: new Date().toISOString(),
    });
    try {
      const accepted = await adapter.request<MonitorActionAcceptedPayload>("monitor.stop", {}, { timeoutMs: 10000 });
      latestMonitorRequestId.current = accepted.requestId;
      if (completedMonitorRequestId.current === accepted.requestId) {
        scheduleImmediateSnapshotRefresh();
        return;
      }
      setMonitorAction({
        status: "loading",
        message: accepted.message,
        error: "",
        requestId: accepted.requestId,
        updatedAt: new Date().toISOString(),
      });
      scheduleImmediateSnapshotRefresh();
      monitorEventTimeout.current = setTimeout(() => {
        if (latestMonitorRequestId.current !== accepted.requestId) return;
        setMonitorAction({
          status: "error",
          message: "停止监控超时。",
          error: "等待监控 worker 停止状态超时。",
          requestId: accepted.requestId,
          updatedAt: new Date().toISOString(),
        });
      }, 20000);
    } catch (error) {
      setMonitorAction({
        status: "error",
        message: "停止监控失败。",
        error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
      });
      scheduleImmediateSnapshotRefresh();
    }
  }, [adapter, clearMonitorEventTimeout, monitorAction.status, scheduleImmediateSnapshotRefresh]);

  const openLogsDirectory = useCallback(async () => {
    if (logsOpenDirectory.status === "loading") return null;
    setLogsOpenDirectory({
      status: "loading",
      message: "正在打开日志目录。",
      error: "",
      requestId: "",
      updatedAt: new Date().toISOString(),
    });
    try {
      const data = await adapter.request<LogsOpenDirectoryPayload>("logs.openDirectory", {}, { timeoutMs: 10000 });
      setLogsOpenDirectory({
        status: "success",
        message: data.message || "日志目录已打开。",
        error: "",
        requestId: "",
        updatedAt: new Date().toISOString(),
      });
      scheduleImmediateSnapshotRefresh();
      return data;
    } catch (error) {
      setLogsOpenDirectory({
        status: "error",
        message: "日志目录打开失败。",
        error: getErrorMessage(error),
        requestId: error instanceof BridgeRequestError ? error.requestId : "",
        updatedAt: new Date().toISOString(),
      });
      scheduleImmediateSnapshotRefresh();
      return null;
    }
  }, [adapter, logsOpenDirectory.status, scheduleImmediateSnapshotRefresh]);

  const runImmediateReportAction = useCallback(
    async (kind: "open" | "openDirectory", requestType: "reports.open" | "reports.openDirectory", reportId: string) => {
      if (!reportId) return null;
      setReportOperation(kind, reportId, {
        status: "loading",
        message: requestType === "reports.open" ? "正在打开报告。" : "正在打开报告目录。",
        error: "",
        requestId: "",
        updatedAt: new Date().toISOString(),
      });
      try {
        const data = await adapter.request<ReportActionPayload>(requestType, { reportId }, { timeoutMs: 10000 });
        setReportOperation(kind, reportId, {
          status: "success",
          message: data.message || "报告操作已完成。",
          error: "",
          requestId: "",
          updatedAt: new Date().toISOString(),
        });
        void refreshReports();
        scheduleImmediateSnapshotRefresh();
        return data;
      } catch (error) {
        setReportOperation(kind, reportId, {
          status: "error",
          message: "报告操作失败。",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
        return null;
      }
    },
    [adapter, refreshReports, scheduleImmediateSnapshotRefresh, setReportOperation],
  );

  const openReport = useCallback(
    (reportId: string) => runImmediateReportAction("open", "reports.open", reportId),
    [runImmediateReportAction],
  );

  const openReportDirectory = useCallback(
    (reportId: string) => runImmediateReportAction("openDirectory", "reports.openDirectory", reportId),
    [runImmediateReportAction],
  );

  const regenerateReport = useCallback(
    async (reportId: string) => {
      if (!reportId) return;
      const key = createReportOperationKey("regenerate", reportId);
      if (reportOperations[key]?.status === "loading") return;
      setReportOperation("regenerate", reportId, {
        status: "loading",
        message: "正在请求重新生成报告。",
        error: "",
        requestId: "",
        updatedAt: new Date().toISOString(),
      });
      try {
        const accepted = await adapter.request<LongActionAcceptedPayload>(
          "reports.regenerate",
          { reportId },
          { timeoutMs: 10000 },
        );
        reportRequestMap.current.set(accepted.requestId, { kind: "regenerate", reportId });
        setReportOperation("regenerate", reportId, {
          status: "loading",
          message: accepted.message,
          error: "",
          requestId: accepted.requestId,
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
        reportEventTimeouts.current.set(
          accepted.requestId,
          setTimeout(() => {
            setReportOperation("regenerate", reportId, {
              status: "error",
              message: "重新生成报告超时。",
              error: "等待报告生成进度超时。",
              requestId: accepted.requestId,
              updatedAt: new Date().toISOString(),
            });
            reportRequestMap.current.delete(accepted.requestId);
          }, 30000),
        );
      } catch (error) {
        setReportOperation("regenerate", reportId, {
          status: "error",
          message: "重新生成报告失败。",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
      }
    },
    [adapter, reportOperations, scheduleImmediateSnapshotRefresh, setReportOperation],
  );

  const generateDiagnostics = useCallback(
    async (reportId?: string) => {
      if (diagnosticsGenerate.status === "loading") return;
      clearDiagnosticsEventTimeout();
      setDiagnosticsGenerate({
        status: "loading",
        message: "正在请求生成诊断。",
        error: "",
        requestId: "",
        updatedAt: new Date().toISOString(),
      });
      try {
        const payload = reportId ? { reportId } : {};
        const accepted = await adapter.request<LongActionAcceptedPayload>(
          "diagnostics.generate",
          payload,
          { timeoutMs: 10000 },
        );
        latestDiagnosticsRequestId.current = accepted.requestId;
        setDiagnosticsGenerate({
          status: "loading",
          message: accepted.message,
          error: "",
          requestId: accepted.requestId,
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
        diagnosticsEventTimeout.current = setTimeout(() => {
          if (latestDiagnosticsRequestId.current !== accepted.requestId) return;
          setDiagnosticsGenerate({
            status: "error",
            message: "生成诊断超时。",
            error: "等待诊断生成进度超时。",
            requestId: accepted.requestId,
            updatedAt: new Date().toISOString(),
          });
        }, 30000);
      } catch (error) {
        setDiagnosticsGenerate({
        status: "error",
          message: "诊断生成失败。",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
      }
    },
    [adapter, clearDiagnosticsEventTimeout, diagnosticsGenerate.status, scheduleImmediateSnapshotRefresh],
  );

  const getReportOperationState = useCallback(
    (kind: ReportOperationKind, reportId: string) => {
      return reportOperations[createReportOperationKey(kind, reportId)] ?? defaultReportOperationState;
    },
    [reportOperations],
  );

  useEffect(() => {
    let disposed = false;
    let timeout: ReturnType<typeof setTimeout> | null = null;
    const scheduleNextTick = () => {
      if (disposed) return;
      timeout = setTimeout(() => {
        void snapshotRefreshRunner.current(true).finally(scheduleNextTick);
      }, currentSnapshotIntervalMs.current);
    };

    scheduleNextTick();

    return () => {
      disposed = true;
      if (timeout) clearTimeout(timeout);
    };
  }, [adapter]);

  useEffect(() => {
    const unsubscribeProcesses = adapter.subscribe<ProcessesRefreshedEventPayload>(
      "event.processesRefreshed",
      (event) => {
        clearProcessEventTimeout();
        latestProcessRequestId.current = event.payload.requestId;
        setProcesses(event.payload.processes);
        setProcessRefresh({
          status: "success",
          message: `进程列表已刷新，共 ${event.payload.count} 个进程。`,
          error: "",
          requestId: event.payload.requestId,
          updatedAt: event.payload.refreshedAt || new Date().toISOString(),
        });
      },
    );
    const unsubscribeStatus = adapter.subscribe<StatusEventPayload>("event.status", (event) => {
      const payload = event.payload;
      setLastStatusEvent(payload);

      if (payload.status === "monitor.starting" || payload.status === "monitor.stopping" || payload.status === "monitor.in_flight") {
        setMonitorAction({
          status: "loading",
          message: payload.message || "监控操作正在执行。",
          error: "",
          requestId: payload.requestId || latestMonitorRequestId.current,
          updatedAt: new Date().toISOString(),
        });
      }

      if (payload.status === "monitor.started") {
        completedMonitorRequestId.current = payload.requestId || "";
        monitorRuntimeOverrideUntil.current = Date.now() + 15000;
        clearMonitorEventTimeout();
        latestMonitorRequestId.current = payload.requestId || latestMonitorRequestId.current;
        setMonitorAction({
          status: "success",
          message: payload.message || "监控 worker 已启动。",
          error: "",
          requestId: latestMonitorRequestId.current,
          updatedAt: new Date().toISOString(),
        });
        setMonitorRuntime({
          running: true,
          pid: readNumericPayload(payload, "pid"),
          message: payload.message || "监控 worker 已启动。",
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
      }

      if (payload.status === "monitor.stopped") {
        completedMonitorRequestId.current = payload.requestId || "";
        monitorRuntimeOverrideUntil.current = 0;
        clearMonitorEventTimeout();
        latestMonitorRequestId.current = payload.requestId || latestMonitorRequestId.current;
        setMonitorAction({
          status: "success",
          message: payload.message || "监控 worker 已停止。",
          error: "",
          requestId: latestMonitorRequestId.current,
          updatedAt: new Date().toISOString(),
        });
        setMonitorRuntime({
          running: false,
          pid: 0,
          message: payload.message || "监控 worker 已停止。",
          updatedAt: new Date().toISOString(),
        });
        scheduleImmediateSnapshotRefresh();
      }
    });
    const unsubscribeReportProgress = adapter.subscribe<ReportProgressEventPayload>(
      "event.reportProgress",
      (event) => {
        const payload = event.payload;
        const requestId = payload.requestId || "";

        if (requestId && requestId === latestDiagnosticsRequestId.current) {
          if (payload.status === "completed") {
            clearDiagnosticsEventTimeout();
            setDiagnosticsResult(payload);
            setDiagnosticsGenerate({
              status: "success",
              message: payload.message || "诊断已生成。",
              error: "",
              requestId,
              updatedAt: new Date().toISOString(),
            });
            scheduleImmediateSnapshotRefresh();
          } else if (payload.status === "error" || payload.ok === false) {
            clearDiagnosticsEventTimeout();
            setDiagnosticsGenerate({
              status: "error",
              message: payload.message || "诊断生成失败。",
              error: payload.message || "诊断生成失败。",
              requestId,
              updatedAt: new Date().toISOString(),
            });
            scheduleImmediateSnapshotRefresh();
          } else {
            setDiagnosticsGenerate({
              status: "loading",
              message: payload.message || "诊断正在生成。",
              error: "",
              requestId,
              updatedAt: new Date().toISOString(),
            });
          }
        }

        const reportAction = requestId ? reportRequestMap.current.get(requestId) : null;
        if (!reportAction) return;

        if (payload.status === "completed") {
          clearReportEventTimeout(requestId);
          setReportOperation(reportAction.kind, reportAction.reportId, {
            status: "success",
            message: payload.message || "报告操作已完成。",
            error: "",
            requestId,
            updatedAt: new Date().toISOString(),
          });
          reportRequestMap.current.delete(requestId);
          void refreshReports();
          scheduleImmediateSnapshotRefresh();
        } else if (payload.status === "error" || payload.ok === false) {
          clearReportEventTimeout(requestId);
          setReportOperation(reportAction.kind, reportAction.reportId, {
            status: "error",
            message: payload.message || "报告操作失败。",
            error: payload.message || "报告操作失败。",
            requestId,
            updatedAt: new Date().toISOString(),
          });
          reportRequestMap.current.delete(requestId);
          scheduleImmediateSnapshotRefresh();
        } else {
          setReportOperation(reportAction.kind, reportAction.reportId, {
            status: "loading",
            message: payload.message || "报告操作正在执行。",
            error: "",
            requestId,
            updatedAt: new Date().toISOString(),
          });
        }
      },
    );
    const unsubscribeReportsChanged = adapter.subscribe<ReportActionPayload>("event.reportsChanged", () => {
      void refreshReports();
      scheduleImmediateSnapshotRefresh();
    });
    const unsubscribeHostWindowChanged = adapter.subscribe<HostWindowChangedEventPayload>("event.hostWindowChanged", (event) => {
      const payload = event.payload;
      const hiddenOrTray = payload.visible === false || payload.inTray === true;
      currentSnapshotIntervalMs.current = hiddenOrTray
        ? HIDDEN_OR_TRAY_STATE_SNAPSHOT_INTERVAL_MS
        : VISIBLE_STATE_SNAPSHOT_INTERVAL_MS;
      if (!hiddenOrTray) scheduleImmediateSnapshotRefresh();
    });
    const unsubscribeError = adapter.subscribe<ErrorEventPayload>("event.error", (event) => {
      const payload = event.payload;
      setLastErrorEvent(payload);

      if (payload.requestId && payload.requestId === latestProcessRequestId.current) {
        clearProcessEventTimeout();
        setProcessRefresh({
          status: "error",
          message: "进程刷新失败。",
          error: payload.message,
          requestId: payload.requestId,
          updatedAt: new Date().toISOString(),
        });
      }

      if (payload.requestId && payload.requestId === latestMonitorRequestId.current) {
        clearMonitorEventTimeout();
        setMonitorAction({
          status: "error",
          message: "监控操作失败。",
          error: payload.message,
          requestId: payload.requestId,
          updatedAt: new Date().toISOString(),
        });
      }

      if (payload.requestId && payload.requestId === latestDiagnosticsRequestId.current) {
        clearDiagnosticsEventTimeout();
        setDiagnosticsGenerate({
          status: "error",
          message: "诊断生成失败。",
          error: payload.message,
          requestId: payload.requestId,
          updatedAt: new Date().toISOString(),
        });
      }

      const reportAction = payload.requestId ? reportRequestMap.current.get(payload.requestId) : null;
      if (reportAction && payload.requestId) {
        clearReportEventTimeout(payload.requestId);
        setReportOperation(reportAction.kind, reportAction.reportId, {
          status: "error",
          message: "报告操作失败。",
          error: payload.message,
          requestId: payload.requestId,
          updatedAt: new Date().toISOString(),
        });
        reportRequestMap.current.delete(payload.requestId);
      }
      scheduleImmediateSnapshotRefresh();
    });

    void refreshSnapshot();
    void refreshConfig();
    void refreshReports();
    void refreshTargets();

    return () => {
      clearProcessEventTimeout();
      clearMonitorEventTimeout();
      clearDiagnosticsEventTimeout();
      clearImmediateSnapshotRefreshTimeout();
      for (const timeout of reportEventTimeouts.current.values()) clearTimeout(timeout);
      reportEventTimeouts.current.clear();
      unsubscribeProcesses();
      unsubscribeStatus();
      unsubscribeReportProgress();
      unsubscribeReportsChanged();
      unsubscribeHostWindowChanged();
      unsubscribeError();
    };
  }, [
    adapter,
    clearDiagnosticsEventTimeout,
    clearImmediateSnapshotRefreshTimeout,
    clearMonitorEventTimeout,
    clearProcessEventTimeout,
    clearReportEventTimeout,
    refreshConfig,
    refreshReports,
    refreshSnapshot,
    refreshTargets,
    scheduleImmediateSnapshotRefresh,
    setReportOperation,
  ]);

  return {
    environment: adapter.environment,
    isMockPreview: adapter.environment === "mock",
    snapshot,
    config,
    reports,
    targets,
    processes,
    processRefresh,
    configSave,
    targetsSave,
    logsOpenDirectory,
    monitorAction,
    monitorRuntime,
    reportOperations,
    diagnosticsGenerate,
    diagnosticsResult,
    lastStatusEvent,
    lastErrorEvent,
    refreshSnapshot,
    refreshConfig,
    refreshReports,
    refreshTargets,
    refreshProcesses,
    saveConfig,
    saveTargets,
    openLogsDirectory,
    startMonitor,
    stopMonitor,
    openReport,
    openReportDirectory,
    regenerateReport,
    generateDiagnostics,
    getReportOperationState,
  };
}

function createReportOperationKey(kind: ReportOperationKind, reportId: string) {
  return `${kind}:${reportId}`;
}

function readNumericPayload(payload: StatusEventPayload, key: string) {
  const value = payload[key];
  return typeof value === "number" && Number.isFinite(value) ? value : 0;
}

function getErrorMessage(error: unknown) {
  if (error instanceof Error) return error.message;
  if (typeof error === "string") return error;
  return "未知错误。";
}

async function withMinimumDelay<TValue>(promise: Promise<TValue>, minimumMs: number) {
  const startedAt = Date.now();
  const value = await promise;
  const elapsedMs = Date.now() - startedAt;
  if (elapsedMs < minimumMs) {
    await new Promise((resolve) => globalThis.setTimeout(resolve, minimumMs - elapsedMs));
  }
  return value;
}
