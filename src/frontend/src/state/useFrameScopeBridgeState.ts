import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import type {
  BridgeEnvironment,
  ConfigPayload,
  ErrorEventPayload,
  FrameScopeBridgeAdapter,
  FrameScopeConfig,
  FrameScopeTargetConfig,
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

const defaultReportOperationState = emptyOperationState("No report action has been requested.");

export function useFrameScopeBridgeState(adapterOverride?: FrameScopeBridgeAdapter): FrameScopeBridgeViewState {
  const adapter = useMemo(() => adapterOverride ?? createFrameScopeBridgeAdapter(), [adapterOverride]);
  const [snapshot, setSnapshot] = useState<LoadState<StateSnapshotPayload>>(
    emptyLoadState("Snapshot has not been requested yet."),
  );
  const [config, setConfig] = useState<LoadState<ConfigPayload>>(
    emptyLoadState("Config has not been requested yet."),
  );
  const [reports, setReports] = useState<LoadState<ReportListPayload>>(
    emptyLoadState("Reports have not been requested yet."),
  );
  const [targets, setTargets] = useState<LoadState<TargetsPayload>>(
    emptyLoadState("Targets have not been requested yet."),
  );
  const [processes, setProcesses] = useState<ProcessInfo[]>([]);
  const [processRefresh, setProcessRefresh] = useState<OperationState>(
    emptyOperationState("Process refresh has not been requested yet."),
  );
  const [configSave, setConfigSave] = useState<OperationState>(
    emptyOperationState("No config changes have been saved in this session."),
  );
  const [targetsSave, setTargetsSave] = useState<OperationState>(
    emptyOperationState("No target changes have been saved in this session."),
  );
  const [monitorAction, setMonitorAction] = useState<OperationState>(
    emptyOperationState("Monitor start/stop has not been requested yet."),
  );
  const [monitorRuntime, setMonitorRuntime] = useState<MonitorRuntimeState>({
    running: null,
    pid: 0,
    message: "Waiting for state.snapshot or event.status.",
    updatedAt: "",
  });
  const [reportOperations, setReportOperations] = useState<Record<string, OperationState>>({});
  const [diagnosticsGenerate, setDiagnosticsGenerate] = useState<OperationState>(
    emptyOperationState("Diagnostics generation has not been requested yet."),
  );
  const [diagnosticsResult, setDiagnosticsResult] = useState<ReportProgressEventPayload | null>(null);
  const [lastStatusEvent, setLastStatusEvent] = useState<StatusEventPayload | null>(null);
  const [lastErrorEvent, setLastErrorEvent] = useState<ErrorEventPayload | null>(null);

  const latestProcessRequestId = useRef("");
  const latestMonitorRequestId = useRef("");
  const latestDiagnosticsRequestId = useRef("");
  const reportRequestMap = useRef(new Map<string, { kind: ReportOperationKind; reportId: string }>());
  const processEventTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
  const monitorEventTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
  const diagnosticsEventTimeout = useRef<ReturnType<typeof setTimeout> | null>(null);
  const reportEventTimeouts = useRef(new Map<string, ReturnType<typeof setTimeout>>());

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

  const setReportOperation = useCallback(
    (kind: ReportOperationKind, reportId: string, next: OperationState) => {
      setReportOperations((current) => ({
        ...current,
        [createReportOperationKey(kind, reportId)]: next,
      }));
    },
    [],
  );

  const refreshSnapshot = useCallback(async () => {
    setSnapshot((current) => ({
      ...current,
      status: "loading",
      message: "Loading bridge snapshot.",
      error: "",
    }));
    try {
      const data = await adapter.request<StateSnapshotPayload>("state.snapshot", {}, { timeoutMs: 8000 });
      setSnapshot({
        status: "success",
        data,
        message: "Snapshot loaded from bridge.",
        error: "",
        updatedAt: new Date().toISOString(),
      });
      setMonitorRuntime({
        running: data.watcher.running,
        pid: data.watcher.pid,
        message: data.watcher.running ? "Watcher is running according to state.snapshot." : "Watcher is stopped according to state.snapshot.",
        updatedAt: data.generatedAt || new Date().toISOString(),
      });
    } catch (error) {
      setSnapshot((current) => ({
        ...current,
        status: "error",
        message: "Snapshot load failed.",
        error: getErrorMessage(error),
        updatedAt: new Date().toISOString(),
      }));
    }
  }, [adapter]);

  const refreshConfig = useCallback(async () => {
    setConfig((current) => ({
      ...current,
      status: "loading",
      message: "Loading FrameScope config.",
      error: "",
    }));
    try {
      const data = await adapter.request<ConfigPayload>("config.get", {}, { timeoutMs: 10000 });
      setConfig({
        status: "success",
        data,
        message: "Config loaded from bridge.",
        error: "",
        updatedAt: new Date().toISOString(),
      });
    } catch (error) {
      setConfig((current) => ({
        ...current,
        status: "error",
        message: "Config load failed.",
        error: getErrorMessage(error),
        updatedAt: new Date().toISOString(),
      }));
    }
  }, [adapter]);

  const refreshReports = useCallback(async () => {
    setReports((current) => ({
      ...current,
      status: "loading",
      message: "Loading reports from bridge.",
      error: "",
    }));
    try {
      const data = await adapter.request<ReportListPayload>("reports.list", {}, { timeoutMs: 10000 });
      setReports({
        status: "success",
        data,
        message: `Reports loaded. ${data.count} entries returned.`,
        error: "",
        updatedAt: new Date().toISOString(),
      });
    } catch (error) {
      setReports((current) => ({
        ...current,
        status: "error",
        message: "Reports load failed.",
        error: getErrorMessage(error),
        updatedAt: new Date().toISOString(),
      }));
    }
  }, [adapter]);

  const refreshTargets = useCallback(async () => {
    setTargets((current) => ({
      ...current,
      status: "loading",
      message: "Loading targets from bridge.",
      error: "",
    }));
    try {
      const data = await adapter.request<TargetsPayload>("targets.get", {}, { timeoutMs: 10000 });
      setTargets({
        status: "success",
        data,
        message: `Targets loaded. ${data.targetCount} entries returned.`,
        error: "",
        updatedAt: new Date().toISOString(),
      });
    } catch (error) {
      setTargets((current) => ({
        ...current,
        status: "error",
        message: "Targets load failed.",
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
        message: "Requesting process refresh.",
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
            message: "Process refresh accepted but no result event arrived.",
            error: "Timed out waiting for event.processesRefreshed.",
            requestId: accepted.requestId,
            updatedAt: new Date().toISOString(),
          });
        }, 15000);
      } catch (error) {
        setProcessRefresh({
          status: "error",
          message: "Process refresh failed.",
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
        message: "Saving FrameScope config.",
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
          message: "Config saved and reloaded from bridge.",
          error: "",
          updatedAt: new Date().toISOString(),
        });
        setConfigSave({
          status: "success",
          message: "Config saved.",
          error: "",
          requestId: "",
          updatedAt: new Date().toISOString(),
        });
        void refreshSnapshot();
        void refreshTargets();
        return data;
      } catch (error) {
        setConfigSave({
          status: "error",
          message: "Config save failed.",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
        return null;
      }
    },
    [adapter, refreshSnapshot, refreshTargets],
  );

  const saveTargets = useCallback(
    async (payload: {
      targets: FrameScopeTargetConfig[];
      dataRoot?: string;
      openReportOnComplete?: boolean;
    }) => {
      setTargetsSave({
        status: "loading",
        message: "Saving target configuration.",
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
          message: "Targets saved and reloaded from bridge.",
          error: "",
          updatedAt: new Date().toISOString(),
        });
        setTargetsSave({
          status: "success",
          message: "Targets saved.",
          error: "",
          requestId: "",
          updatedAt: new Date().toISOString(),
        });
        void refreshConfig();
        void refreshSnapshot();
        return data;
      } catch (error) {
        setTargetsSave({
          status: "error",
          message: "Target save failed.",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
        return null;
      }
    },
    [adapter, refreshConfig, refreshSnapshot],
  );

  const startMonitor = useCallback(async () => {
    if (monitorAction.status === "loading") return;
    clearMonitorEventTimeout();
    setMonitorAction({
      status: "loading",
      message: "Requesting monitor.start.",
      error: "",
      requestId: "",
      updatedAt: new Date().toISOString(),
    });
    try {
      const accepted = await adapter.request<MonitorActionAcceptedPayload>("monitor.start", {}, { timeoutMs: 10000 });
      latestMonitorRequestId.current = accepted.requestId;
      setMonitorAction({
        status: "loading",
        message: accepted.message,
        error: "",
        requestId: accepted.requestId,
        updatedAt: new Date().toISOString(),
      });
      monitorEventTimeout.current = setTimeout(() => {
        if (latestMonitorRequestId.current !== accepted.requestId) return;
        setMonitorAction({
          status: "error",
          message: "monitor.start accepted but no status event arrived.",
          error: "Timed out waiting for event.status.",
          requestId: accepted.requestId,
          updatedAt: new Date().toISOString(),
        });
      }, 20000);
    } catch (error) {
      setMonitorAction({
        status: "error",
        message: "monitor.start failed.",
        error: getErrorMessage(error),
        requestId: error instanceof BridgeRequestError ? error.requestId : "",
        updatedAt: new Date().toISOString(),
      });
    }
  }, [adapter, clearMonitorEventTimeout, monitorAction.status]);

  const stopMonitor = useCallback(async () => {
    if (monitorAction.status === "loading") return;
    clearMonitorEventTimeout();
    setMonitorAction({
      status: "loading",
      message: "Requesting monitor.stop.",
      error: "",
      requestId: "",
      updatedAt: new Date().toISOString(),
    });
    try {
      const accepted = await adapter.request<MonitorActionAcceptedPayload>("monitor.stop", {}, { timeoutMs: 10000 });
      latestMonitorRequestId.current = accepted.requestId;
      setMonitorAction({
        status: "loading",
        message: accepted.message,
        error: "",
        requestId: accepted.requestId,
        updatedAt: new Date().toISOString(),
      });
      monitorEventTimeout.current = setTimeout(() => {
        if (latestMonitorRequestId.current !== accepted.requestId) return;
        setMonitorAction({
          status: "error",
          message: "monitor.stop accepted but no status event arrived.",
          error: "Timed out waiting for event.status.",
          requestId: accepted.requestId,
          updatedAt: new Date().toISOString(),
        });
      }, 20000);
    } catch (error) {
      setMonitorAction({
        status: "error",
        message: "monitor.stop failed.",
        error: getErrorMessage(error),
        requestId: error instanceof BridgeRequestError ? error.requestId : "",
        updatedAt: new Date().toISOString(),
      });
    }
  }, [adapter, clearMonitorEventTimeout, monitorAction.status]);

  const runImmediateReportAction = useCallback(
    async (kind: "open" | "openDirectory", requestType: "reports.open" | "reports.openDirectory", reportId: string) => {
      if (!reportId) return null;
      setReportOperation(kind, reportId, {
        status: "loading",
        message: requestType === "reports.open" ? "Opening report through host." : "Opening run directory through host.",
        error: "",
        requestId: "",
        updatedAt: new Date().toISOString(),
      });
      try {
        const data = await adapter.request<ReportActionPayload>(requestType, { reportId }, { timeoutMs: 10000 });
        setReportOperation(kind, reportId, {
          status: "success",
          message: data.message || "Report action completed.",
          error: "",
          requestId: "",
          updatedAt: new Date().toISOString(),
        });
        void refreshReports();
        return data;
      } catch (error) {
        setReportOperation(kind, reportId, {
          status: "error",
          message: "Report action failed.",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
        return null;
      }
    },
    [adapter, refreshReports, setReportOperation],
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
        message: "Requesting reports.regenerate.",
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
        reportEventTimeouts.current.set(
          accepted.requestId,
          setTimeout(() => {
            setReportOperation("regenerate", reportId, {
              status: "error",
              message: "reports.regenerate accepted but no progress event arrived.",
              error: "Timed out waiting for event.reportProgress.",
              requestId: accepted.requestId,
              updatedAt: new Date().toISOString(),
            });
            reportRequestMap.current.delete(accepted.requestId);
          }, 30000),
        );
      } catch (error) {
        setReportOperation("regenerate", reportId, {
          status: "error",
          message: "reports.regenerate failed.",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
      }
    },
    [adapter, reportOperations, setReportOperation],
  );

  const generateDiagnostics = useCallback(
    async (reportId?: string) => {
      if (diagnosticsGenerate.status === "loading") return;
      clearDiagnosticsEventTimeout();
      setDiagnosticsGenerate({
        status: "loading",
        message: "Requesting diagnostics.generate.",
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
        diagnosticsEventTimeout.current = setTimeout(() => {
          if (latestDiagnosticsRequestId.current !== accepted.requestId) return;
          setDiagnosticsGenerate({
            status: "error",
            message: "diagnostics.generate accepted but no progress event arrived.",
            error: "Timed out waiting for event.reportProgress.",
            requestId: accepted.requestId,
            updatedAt: new Date().toISOString(),
          });
        }, 30000);
      } catch (error) {
        setDiagnosticsGenerate({
          status: "error",
          message: "diagnostics.generate failed.",
          error: getErrorMessage(error),
          requestId: error instanceof BridgeRequestError ? error.requestId : "",
          updatedAt: new Date().toISOString(),
        });
      }
    },
    [adapter, clearDiagnosticsEventTimeout, diagnosticsGenerate.status],
  );

  const getReportOperationState = useCallback(
    (kind: ReportOperationKind, reportId: string) => {
      return reportOperations[createReportOperationKey(kind, reportId)] ?? defaultReportOperationState;
    },
    [reportOperations],
  );

  useEffect(() => {
    const unsubscribeProcesses = adapter.subscribe<ProcessesRefreshedEventPayload>(
      "event.processesRefreshed",
      (event) => {
        clearProcessEventTimeout();
        latestProcessRequestId.current = event.payload.requestId;
        setProcesses(event.payload.processes);
        setProcessRefresh({
          status: "success",
          message: `Process refresh completed. ${event.payload.count} processes returned.`,
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
          message: payload.message || `${payload.status}.`,
          error: "",
          requestId: payload.requestId || latestMonitorRequestId.current,
          updatedAt: new Date().toISOString(),
        });
      }

      if (payload.status === "monitor.started") {
        clearMonitorEventTimeout();
        latestMonitorRequestId.current = payload.requestId || latestMonitorRequestId.current;
        setMonitorAction({
          status: "success",
          message: payload.message || "Monitor started.",
          error: "",
          requestId: latestMonitorRequestId.current,
          updatedAt: new Date().toISOString(),
        });
        setMonitorRuntime({
          running: true,
          pid: readNumericPayload(payload, "pid"),
          message: payload.message || "Monitor started.",
          updatedAt: new Date().toISOString(),
        });
        void refreshSnapshot();
      }

      if (payload.status === "monitor.stopped") {
        clearMonitorEventTimeout();
        latestMonitorRequestId.current = payload.requestId || latestMonitorRequestId.current;
        setMonitorAction({
          status: "success",
          message: payload.message || "Monitor stopped.",
          error: "",
          requestId: latestMonitorRequestId.current,
          updatedAt: new Date().toISOString(),
        });
        setMonitorRuntime({
          running: false,
          pid: 0,
          message: payload.message || "Monitor stopped.",
          updatedAt: new Date().toISOString(),
        });
        void refreshSnapshot();
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
              message: payload.message || "Diagnostics generated.",
              error: "",
              requestId,
              updatedAt: new Date().toISOString(),
            });
          } else if (payload.status === "error" || payload.ok === false) {
            clearDiagnosticsEventTimeout();
            setDiagnosticsGenerate({
              status: "error",
              message: payload.message || "Diagnostics generation failed.",
              error: payload.message || "diagnostics.generate failed.",
              requestId,
              updatedAt: new Date().toISOString(),
            });
          } else {
            setDiagnosticsGenerate({
              status: "loading",
              message: payload.message || "Diagnostics generation is running.",
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
            message: payload.message || "Report action completed.",
            error: "",
            requestId,
            updatedAt: new Date().toISOString(),
          });
          reportRequestMap.current.delete(requestId);
          void refreshReports();
        } else if (payload.status === "error" || payload.ok === false) {
          clearReportEventTimeout(requestId);
          setReportOperation(reportAction.kind, reportAction.reportId, {
            status: "error",
            message: payload.message || "Report action failed.",
            error: payload.message || "Report action failed.",
            requestId,
            updatedAt: new Date().toISOString(),
          });
          reportRequestMap.current.delete(requestId);
        } else {
          setReportOperation(reportAction.kind, reportAction.reportId, {
            status: "loading",
            message: payload.message || "Report action is running.",
            error: "",
            requestId,
            updatedAt: new Date().toISOString(),
          });
        }
      },
    );
    const unsubscribeReportsChanged = adapter.subscribe<ReportActionPayload>("event.reportsChanged", () => {
      void refreshReports();
    });
    const unsubscribeError = adapter.subscribe<ErrorEventPayload>("event.error", (event) => {
      const payload = event.payload;
      setLastErrorEvent(payload);

      if (payload.requestId && payload.requestId === latestProcessRequestId.current) {
        clearProcessEventTimeout();
        setProcessRefresh({
          status: "error",
          message: "Process refresh failed.",
          error: payload.message,
          requestId: payload.requestId,
          updatedAt: new Date().toISOString(),
        });
      }

      if (payload.requestId && payload.requestId === latestMonitorRequestId.current) {
        clearMonitorEventTimeout();
        setMonitorAction({
          status: "error",
          message: "Monitor action failed.",
          error: payload.message,
          requestId: payload.requestId,
          updatedAt: new Date().toISOString(),
        });
      }

      if (payload.requestId && payload.requestId === latestDiagnosticsRequestId.current) {
        clearDiagnosticsEventTimeout();
        setDiagnosticsGenerate({
          status: "error",
          message: "Diagnostics generation failed.",
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
          message: "Report action failed.",
          error: payload.message,
          requestId: payload.requestId,
          updatedAt: new Date().toISOString(),
        });
        reportRequestMap.current.delete(payload.requestId);
      }
    });

    void refreshSnapshot();
    void refreshConfig();
    void refreshReports();
    void refreshTargets();

    return () => {
      clearProcessEventTimeout();
      clearMonitorEventTimeout();
      clearDiagnosticsEventTimeout();
      for (const timeout of reportEventTimeouts.current.values()) clearTimeout(timeout);
      reportEventTimeouts.current.clear();
      unsubscribeProcesses();
      unsubscribeStatus();
      unsubscribeReportProgress();
      unsubscribeReportsChanged();
      unsubscribeError();
    };
  }, [
    adapter,
    clearDiagnosticsEventTimeout,
    clearMonitorEventTimeout,
    clearProcessEventTimeout,
    clearReportEventTimeout,
    refreshConfig,
    refreshReports,
    refreshSnapshot,
    refreshTargets,
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
  return "Unknown bridge error.";
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
