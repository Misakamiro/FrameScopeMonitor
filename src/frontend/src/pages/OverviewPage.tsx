import { FileText, Play, RefreshCw, ShieldAlert, Square, Target } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import type { ReportListItem } from "../bridge/contract";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import type { FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
import type { Tone } from "../types";
import "./pages.css";

interface OverviewPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

export function OverviewPage({ bridgeState }: OverviewPageProps) {
  const { snapshot, config, reports, refreshSnapshot } = bridgeState;
  const [monitorStartFeedback, setMonitorStartFeedback] = useState<"idle" | "pending" | "settled">("idle");
  const monitorStartInFlightRef = useRef(false);
  const monitorBusy = bridgeState.monitorAction.status === "loading" || monitorStartFeedback === "pending";
  const monitorRunning = bridgeState.monitorRuntime.running ?? snapshot.data?.watcher.running ?? false;
  const enabledTargets = getEnabledTargets(bridgeState);
  const latestReport = reports.data?.reports.find((report) => report.canOpenReport) ?? reports.data?.reports[0] ?? null;
  const handleStartMonitor = async () => {
    if (monitorStartFeedback === "pending") return;
    if (monitorStartInFlightRef.current) return;
    monitorStartInFlightRef.current = true;
    setMonitorStartFeedback("pending");

    try {
      await bridgeState.startMonitor();
    } finally {
      monitorStartInFlightRef.current = false;
      setMonitorStartFeedback("settled");
    }
  };
  const primaryAction = getPrimaryAction({
    monitorRunning,
    monitorBusy,
    enabledTargetCount: enabledTargets.length || snapshot.data?.config.enabledTargetCount || 0,
    latestReport,
    bridgeState,
    monitorStartFeedback,
    onStartMonitor: handleStartMonitor,
  });

  useEffect(() => {
    if (bridgeState.monitorAction.status === "loading") {
      setMonitorStartFeedback("pending");
      return;
    }
    if (bridgeState.monitorAction.status === "success" || bridgeState.monitorAction.status === "error") {
      monitorStartInFlightRef.current = false;
      setMonitorStartFeedback("settled");
    }
  }, [bridgeState.monitorAction.status]);

  useEffect(() => {
    if (!monitorRunning) return;
    monitorStartInFlightRef.current = false;
    setMonitorStartFeedback("settled");
  }, [monitorRunning]);

  return (
    <section className="page overview-page" data-smoke-page="overview">
      <div className="page__header page__header--focus">
        <div>
          <h2>当前监控</h2>
          <p>确认状态、目标和下一步操作。</p>
        </div>
        <div className="page__actions">
          <Button
            icon={RefreshCw}
            variant="secondary"
            disabled={snapshot.status === "loading"}
            data-smoke-action="refresh-snapshot"
            onClick={() => void refreshSnapshot()}
          >
            {snapshot.status === "loading" ? "正在刷新" : "刷新状态"}
          </Button>
        </div>
      </div>

      <div className="current-monitor-grid">
        <GlassCard className="monitor-panel monitor-panel--primary">
          <div className="monitor-panel__status">
            <span className={["monitor-panel__dot", monitorRunning ? "monitor-panel__dot--running" : ""].join(" ")} />
            <div>
              <span className="monitor-panel__eyebrow">当前状态</span>
              <h3>{monitorStatusTitle(bridgeState, monitorRunning)}</h3>
              <p>{monitorStatusCopy(bridgeState, monitorRunning, enabledTargets.length)}</p>
            </div>
          </div>

          <div className="monitor-panel__summary" aria-label="已启用目标">
            <span>已启用目标</span>
            <strong>{formatEnabledTargets(enabledTargets, snapshot.data?.config.enabledTargetCount ?? 0)}</strong>
          </div>

          {(monitorRunning || bridgeState.monitorAction.status === "loading") ? (
            <p
              className="monitor-panel__worker-note"
              title="任务管理器中可能显示一个 FrameScopeMonitor.exe 子进程，这是监控 worker，不是重复打开软件。"
            >
              任务管理器中可能显示一个 FrameScopeMonitor.exe 子进程；这是监控 worker，不是重复打开软件。
            </p>
          ) : null}

          <div className="monitor-panel__actions">
            <Button
              icon={primaryAction.icon}
              variant={primaryAction.variant}
              disabled={primaryAction.disabled}
              data-smoke-action={primaryAction.smokeAction}
              onClick={primaryAction.onClick}
            >
              {primaryAction.label}
            </Button>
            <span>{primaryAction.helper}</span>
            {primaryAction.feedback ? (
              <span
                className="monitor-panel__feedback"
                aria-live="polite"
                data-smoke-state="monitor-start-feedback"
              >
                {primaryAction.feedback}
              </span>
            ) : null}
          </div>
        </GlassCard>

        <GlassCard className="next-step-panel">
          <div className="section-title">
            <div>
              <h3>下一步</h3>
              <p>{nextStepLead(monitorRunning, enabledTargets.length, latestReport)}</p>
            </div>
            <StatusPill tone={enabledTargets.length > 0 ? "success" : "warning"}>
              {enabledTargets.length > 0 ? "目标可用" : "需要目标"}
            </StatusPill>
          </div>
          <ol className="step-list">
            {buildNextSteps(monitorRunning, enabledTargets.length, latestReport).map((step) => (
              <li key={step}>{step}</li>
            ))}
          </ol>
        </GlassCard>
      </div>

      {snapshot.status === "error" ? (
        <InlineStatus
          tone="danger"
          title="状态读取失败"
          message={snapshot.error || "请确认本机程序仍在运行，然后重新刷新状态。"}
        />
      ) : snapshot.status === "loading" ? (
        <InlineStatus tone="diagnostics" title="正在读取状态" message="正在读取当前监控状态。" busy />
      ) : null}

      <div className="overview-summary-grid">
        <GlassCard density="compact">
          <SummaryBlock
            title="目标摘要"
            tone={enabledTargets.length > 0 ? "success" : "warning"}
            value={enabledTargets.length > 0 ? `${enabledTargets.length} 个目标可监控` : "还没有启用目标"}
            detail={
              enabledTargets.length > 0
                ? formatEnabledTargets(enabledTargets, enabledTargets.length)
                : "先到目标页启用至少一个游戏。"
            }
          />
        </GlassCard>
        <GlassCard density="compact">
          <SummaryBlock
            title="最新报告"
            tone={latestReport?.canOpenReport ? "success" : "warning"}
            value={latestReport ? latestReport.game || "FrameScope 报告" : "还没有报告"}
            detail={latestReport ? `${formatReportTime(latestReport.time || latestReport.lastWriteTime)} · 报告页可查看` : "完成一次监控后会出现在报告页。"}
          />
          {latestReport?.canOpenReport ? (
            <Button
              icon={FileText}
              variant="secondary"
              disabled={bridgeState.getReportOperationState("open", latestReport.reportId).status === "loading"}
              data-smoke-action="open-latest-report"
              onClick={() => void bridgeState.openReport(latestReport.reportId)}
            >
              打开最新报告
            </Button>
          ) : null}
        </GlassCard>
        <GlassCard density="compact">
          <SummaryBlock
            title="数据保存"
            tone={config.status === "error" ? "danger" : "neutral"}
            value={formatPathTail(snapshot.data?.config.dataRoot || config.data?.resolvedDataRoot || "")}
            detail="完整位置可在设置页查看。"
          />
        </GlassCard>
      </div>

      {!snapshot.data && snapshot.status !== "loading" ? (
        <EmptyState
          icon={ShieldAlert}
          title="还没有状态"
          description="点击刷新状态读取本机状态。如果仍失败，请重新打开 FrameScope Monitor。"
          actionLabel="刷新状态"
        />
      ) : null}
    </section>
  );
}

function getEnabledTargets(bridgeState: FrameScopeBridgeViewState) {
  const targets = bridgeState.config.data?.config.Targets ?? bridgeState.targets.data?.targets ?? [];
  return targets.filter((target) => target.Enabled).map((target) => target.Name || target.ProcessName).filter(Boolean);
}

function getPrimaryAction({
  monitorRunning,
  monitorBusy,
  enabledTargetCount,
  latestReport,
  bridgeState,
  monitorStartFeedback,
  onStartMonitor,
}: {
  monitorRunning: boolean;
  monitorBusy: boolean;
  enabledTargetCount: number;
  latestReport: ReportListItem | null;
  bridgeState: FrameScopeBridgeViewState;
  monitorStartFeedback: "idle" | "pending" | "settled";
  onStartMonitor: () => Promise<void>;
}) {
  if (monitorRunning) {
    return {
      label: monitorBusy ? "正在停止" : "停止监控",
      helper: "停止后按当前设置生成报告。",
      icon: Square,
      variant: "danger" as const,
      disabled: monitorBusy,
      smokeAction: "monitor-stop",
      onClick: () => void bridgeState.stopMonitor(),
      feedback: "",
    };
  }

  if (enabledTargetCount > 0) {
    const startPending = monitorStartFeedback === "pending" || monitorBusy;
    return {
      label: startPending || monitorBusy ? "正在启动" : "启动监控",
      helper: startPending ? "启动请求已发送，正在等待本机程序确认。" : "启动后保持软件运行，然后进入游戏。",
      feedback: startPending ? "已收到点击，正在启动监控..." : "",
      icon: Play,
      variant: "primary" as const,
      disabled: startPending || monitorBusy,
      smokeAction: "monitor-start",
      onClick: () => void onStartMonitor(),
    };
  }

  if (latestReport?.canOpenReport) {
    return {
      label: "打开最新报告",
      helper: "可以先查看最近一次结果。",
      icon: FileText,
      variant: "primary" as const,
      disabled: bridgeState.getReportOperationState("open", latestReport.reportId).status === "loading",
      smokeAction: "open-latest-report",
      onClick: () => void bridgeState.openReport(latestReport.reportId),
      feedback: "",
    };
  }

  return {
    label: "去设置目标",
    helper: "先启用至少一个游戏进程。",
    icon: Target,
    variant: "primary" as const,
    disabled: true,
    smokeAction: "go-targets-disabled",
    onClick: undefined,
    feedback: "",
  };
}

function monitorStatusTitle(bridgeState: FrameScopeBridgeViewState, running: boolean) {
  if (bridgeState.monitorAction.status === "error") return "需要处理";
  return running ? "正在监控" : "未启动";
}

function monitorStatusCopy(bridgeState: FrameScopeBridgeViewState, running: boolean, enabledTargetCount: number) {
  if (bridgeState.monitorAction.status === "error") {
    return bridgeState.monitorAction.error || "监控操作失败。请检查权限或目标进程后重试。";
  }
  if (bridgeState.monitorAction.status === "loading") return "操作已发送，正在等待本机程序确认。";
  if (running) return "监控 worker 正在记录帧表现和系统占用。";
  if (enabledTargetCount > 0) return `已启用 ${enabledTargetCount} 个目标，可以开始监控。`;
  return "先启用至少一个游戏进程，才能开始记录。";
}

function formatEnabledTargets(targets: string[], fallbackCount: number) {
  if (targets.length === 0 && fallbackCount > 0) return `${fallbackCount} 个目标已启用`;
  if (targets.length === 0) return "没有启用目标";
  const visible = targets.slice(0, 3).join("、");
  return targets.length > 3 ? `${visible} 等 ${targets.length} 个` : visible;
}

function buildNextSteps(running: boolean, enabledTargetCount: number, latestReport: ReportListItem | null) {
  if (running) return ["进入游戏复现卡顿", "结束后停止监控", "到报告页查看结果"];
  if (enabledTargetCount === 0) return ["打开目标页", "启用或添加游戏进程", "回到这里启动监控"];
  if (latestReport?.canOpenReport) return ["启动监控记录新会话", "或打开最新报告", "报告页可查看历史结果"];
  return ["启动监控", "进入游戏复现问题", "回到报告页查看结果"];
}

function nextStepLead(running: boolean, enabledTargetCount: number, latestReport: ReportListItem | null) {
  if (running) return "正在记录，结束后看报告。";
  if (enabledTargetCount === 0) return "先添加或启用目标。";
  if (latestReport?.canOpenReport) return "可以监控，也可以看报告。";
  return "目标已准备，可以开始。";
}

function SummaryBlock({
  title,
  tone,
  value,
  detail,
}: {
  title: string;
  tone: Tone;
  value: string;
  detail: string;
}) {
  return (
    <div className="summary-block">
      <div>
        <span>{title}</span>
        <strong title={value}>{value || "-"}</strong>
      </div>
      <StatusPill tone={tone}>{statusLabel(tone)}</StatusPill>
      <p title={detail}>{detail}</p>
    </div>
  );
}

function statusLabel(tone: Tone) {
  if (tone === "success") return "正常";
  if (tone === "warning") return "注意";
  if (tone === "danger") return "失败";
  return "信息";
}

function formatPathTail(value?: string) {
  if (!value) return "-";
  const parts = value.split(/[\\/]/).filter(Boolean);
  return parts.slice(-2).join("\\") || value;
}

function formatReportTime(value: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
