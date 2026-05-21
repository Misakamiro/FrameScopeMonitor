import { Clock3, Play, RefreshCw, ShieldAlert, Square, Target } from "lucide-react";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { MetricCard } from "../components/MetricCard";
import { StatusPill } from "../components/StatusPill";
import type { FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
import type { Metric, Tone } from "../types";
import "./pages.css";

interface OverviewPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

export function OverviewPage({ bridgeState }: OverviewPageProps) {
  const { snapshot, config, isMockPreview, refreshSnapshot } = bridgeState;
  const monitorBusy = bridgeState.monitorAction.status === "loading";
  const monitorRunning = bridgeState.monitorRuntime.running ?? snapshot.data?.watcher.running ?? false;
  const enabledTargets = getEnabledTargets(bridgeState);
  const targetSummary = enabledTargets.length > 0 ? enabledTargets.join("、") : "尚未启用目标";
  const metrics = buildOverviewMetrics(bridgeState, enabledTargets);

  return (
    <section className="page overview-page" data-smoke-page="overview">
      <div className="page__header page__header--focus">
        <div>
          <span className="mode-ribbon">{isMockPreview ? "界面预览" : "本机数据"}</span>
          <h2>当前监控</h2>
          <p>先看监控是否运行、正在关注哪些目标，再决定启动、停止或刷新状态。</p>
        </div>
        <div className="page__actions">
          <Button
            icon={Play}
            variant="primary"
            disabled={monitorBusy || monitorRunning}
            data-smoke-action="monitor-start"
            onClick={() => void bridgeState.startMonitor()}
          >
            {monitorBusy && !monitorRunning ? "启动中" : "启动监控"}
          </Button>
          <Button
            icon={Square}
            variant="secondary"
            disabled={monitorBusy || !monitorRunning}
            data-smoke-action="monitor-stop"
            onClick={() => void bridgeState.stopMonitor()}
          >
            {monitorBusy && monitorRunning ? "停止中" : "停止监控"}
          </Button>
          <Button
            icon={RefreshCw}
            variant="secondary"
            disabled={snapshot.status === "loading"}
            data-smoke-action="refresh-snapshot"
            onClick={() => void refreshSnapshot()}
          >
            {snapshot.status === "loading" ? "刷新中" : "刷新状态"}
          </Button>
        </div>
      </div>

      <div className="monitor-hero">
        <GlassCard className="monitor-hero__primary">
          <div className="monitor-status-block">
            <div className="monitor-status-block__icon">
              <Target aria-hidden="true" size={30} />
            </div>
            <div>
              <span>{monitorRunning ? "正在监控" : "当前未启动"}</span>
              <h3>{targetSummary}</h3>
              <p>{monitorRunning ? "可以随时停止监控，报告会按当前配置生成。" : "确认目标后，点击启动监控开始记录帧数据。"}</p>
            </div>
          </div>
        </GlassCard>

        <GlassCard className="monitor-hero__side">
          <InlineStatus
            tone={monitorStatusTone(bridgeState, monitorRunning)}
            title={monitorStatusTitle(bridgeState, monitorRunning)}
            message={monitorStatusMessage(bridgeState, monitorRunning)}
            busy={monitorBusy}
          />
        </GlassCard>
      </div>

      {snapshot.status === "error" ? (
        <InlineStatus tone="danger" title="状态读取失败" message={snapshot.error} />
      ) : snapshot.status === "loading" ? (
        <InlineStatus tone="diagnostics" title="正在读取状态" message="正在读取当前监控状态。" busy />
      ) : null}

      {snapshot.data ? (
        <>
          <div className="metric-grid">
            {metrics.map((metric, index) => (
              <MetricCard key={metric.label} metric={metric} index={index} />
            ))}
          </div>

          <div className="overview-grid">
            <GlassCard className="overview-grid__wide">
              <div className="section-title">
                <div>
                  <h3>会话详情</h3>
                  <p>这些信息用来确认当前监控对象、数据位置和最近报告。</p>
                </div>
                <StatusPill tone={monitorRunning ? "success" : "diagnostics"}>
                  {monitorRunning ? "运行中" : "未启动"}
                </StatusPill>
              </div>
              <div className="snapshot-grid">
                <SnapshotItem label="监控目标" value={targetSummary} />
                <SnapshotItem label="进程 ID" value={String(bridgeState.monitorRuntime.pid || snapshot.data.watcher.pid || "-")} />
                <SnapshotItem label="数据目录" value={snapshot.data.config.dataRoot || "-"} />
                <SnapshotItem label="最近报告" value={formatPathTail(snapshot.data.watcher.lastReport)} />
                <SnapshotItem label="历史记录" value={snapshot.data.reports.historyExists ? "已找到" : "暂无记录"} />
                <SnapshotItem label="更新时间" value={formatDateTime(snapshot.data.generatedAt)} />
              </div>
            </GlassCard>

            <GlassCard>
              <div className="section-title">
                <div>
                  <h3>下一步</h3>
                  <p>根据当前状态选择最直接的操作。</p>
                </div>
                <Clock3 aria-hidden="true" size={18} />
              </div>
              <InlineStatus
                tone={enabledTargets.length > 0 ? "success" : "warning"}
                title={enabledTargets.length > 0 ? "目标已准备" : "还没有启用目标"}
                message={
                  enabledTargets.length > 0
                    ? `已启用 ${enabledTargets.length} 个目标，可以开始监控。`
                    : "请先到目标页启用至少一个游戏进程。"
                }
              />
              <InlineStatus
                tone={config.status === "error" ? "danger" : config.data ? "success" : "diagnostics"}
                title={config.data ? "设置已加载" : "正在等待设置"}
                message={config.data ? `${config.data.targetCount} 个目标，${config.data.enabledTargetCount} 个启用。` : config.message}
                busy={config.status === "loading"}
              />
              {snapshot.data.watcher.lastError ? (
                <InlineStatus tone="danger" title="最近错误" message={snapshot.data.watcher.lastError} />
              ) : (
                <InlineStatus tone="success" title="未发现最近错误" message="当前状态没有报告新的监控错误。" />
              )}
            </GlassCard>
          </div>
        </>
      ) : snapshot.status !== "loading" ? (
        <EmptyState
          icon={ShieldAlert}
          title="暂无状态数据"
          description="点击刷新状态后，如果仍没有数据，请检查本机应用是否已经启动。"
          actionLabel="等待数据"
        />
      ) : null}
    </section>
  );
}

function buildOverviewMetrics(bridgeState: FrameScopeBridgeViewState, enabledTargets: string[]): Metric[] {
  const snapshot = bridgeState.snapshot.data;
  const config = bridgeState.config.data;
  const monitorRunning = bridgeState.monitorRuntime.running ?? snapshot?.watcher.running ?? false;
  return [
    {
      label: "监控状态",
      value: monitorRunning ? "运行中" : "未启动",
      detail: monitorRunning ? "正在记录性能数据" : "等待启动监控",
      tone: monitorRunning ? "success" : "neutral",
    },
    {
      label: "启用目标",
      value: String(enabledTargets.length || config?.enabledTargetCount || snapshot?.config.enabledTargetCount || "-"),
      detail: `${config?.targetCount ?? snapshot?.config.targetCount ?? 0} 个配置目标`,
      tone: "primary",
    },
    {
      label: "最近报告",
      value: formatPathTail(snapshot?.watcher.lastReport),
      detail: snapshot?.watcher.completedRuns ? `${snapshot.watcher.completedRuns} 个完成会话` : "暂无完成会话",
      tone: snapshot?.watcher.lastReport ? "success" : "warning",
    },
    {
      label: "历史记录",
      value: snapshot?.reports.historyExists ? "已找到" : "暂无",
      detail: snapshot?.reports.historyPath || "等待状态刷新",
      tone: snapshot?.reports.historyExists ? "success" : "warning",
    },
  ].map((metric) => ({ ...metric, tone: metric.tone as Tone }));
}

function getEnabledTargets(bridgeState: FrameScopeBridgeViewState) {
  const targets = bridgeState.config.data?.config.Targets ?? [];
  return targets.filter((target) => target.Enabled).map((target) => target.Name || target.ProcessName).filter(Boolean);
}

function monitorStatusTone(bridgeState: FrameScopeBridgeViewState, running: boolean): Tone {
  if (bridgeState.monitorAction.status === "error") return "danger";
  if (bridgeState.monitorAction.status === "loading") return "diagnostics";
  return running ? "success" : "diagnostics";
}

function monitorStatusTitle(bridgeState: FrameScopeBridgeViewState, running: boolean) {
  if (bridgeState.monitorAction.status === "error") return "监控操作失败";
  if (bridgeState.monitorAction.status === "loading") return "正在执行监控操作";
  return running ? "监控服务运行中" : "监控服务未启动";
}

function monitorStatusMessage(bridgeState: FrameScopeBridgeViewState, running: boolean) {
  if (bridgeState.monitorAction.status === "error") return bridgeState.monitorAction.error;
  if (bridgeState.monitorAction.status === "loading") return "请求已发送，正在等待本机应用返回状态。";
  if (running) return "正在记录已启用目标的性能数据。";
  return "启动前请确认目标页里的游戏进程名称和开关。";
}

function SnapshotItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="snapshot-item">
      <span>{label}</span>
      <strong>{value || "-"}</strong>
    </div>
  );
}

function formatPathTail(value?: string) {
  if (!value) return "-";
  const parts = value.split(/[\\/]/).filter(Boolean);
  return parts[parts.length - 1] ?? value;
}

function formatDateTime(value: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
