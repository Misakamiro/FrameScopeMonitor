import { FolderOpen, Play, RefreshCw, ShieldAlert, Square } from "lucide-react";
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
  const { snapshot, config, environment, isMockPreview, refreshSnapshot } = bridgeState;
  const metrics = buildOverviewMetrics(bridgeState);
  const monitorBusy = bridgeState.monitorAction.status === "loading";
  const monitorRunning = bridgeState.monitorRuntime.running ?? snapshot.data?.watcher.running ?? false;

  return (
    <section className="page overview-page" data-smoke-page="overview">
      <div className="page__header">
        <div>
          <span className="mock-ribbon">{isMockPreview ? "浏览器 mock adapter preview" : "WebView2 bridge live"}</span>
          <h2>性能会话总览</h2>
          <p>
            本页读取 C# bridge 的 `state.snapshot`、`config.get`，并通过 `monitor.start` / `monitor.stop`
            控制 watcher。普通 Vite 预览只使用集中 mock adapter，不把 mock 数据当作真实监控结果。
          </p>
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
          <Button icon={FolderOpen} variant="secondary" disabled>
            打开数据目录待接入
          </Button>
        </div>
      </div>

      {snapshot.status === "error" ? (
        <InlineStatus tone="danger" title="Snapshot 加载失败" message={snapshot.error} />
      ) : snapshot.status === "loading" ? (
        <InlineStatus tone="diagnostics" title="正在读取状态" message="正在通过 bridge 请求 state.snapshot。" busy />
      ) : null}

      <InlineStatus
        tone={
          bridgeState.monitorAction.status === "error"
            ? "danger"
            : bridgeState.monitorAction.status === "success"
              ? "success"
              : bridgeState.monitorAction.status === "loading"
                ? "diagnostics"
                : monitorRunning
                  ? "success"
                  : "diagnostics"
        }
        title={
          bridgeState.monitorAction.status === "error"
            ? "Monitor 操作失败"
            : bridgeState.monitorAction.status === "loading"
              ? "Monitor 操作执行中"
              : monitorRunning
                ? "Watcher 运行中"
                : "Watcher 未运行"
        }
        message={
          bridgeState.monitorAction.error ||
          bridgeState.monitorAction.message ||
          bridgeState.monitorRuntime.message
        }
        busy={bridgeState.monitorAction.status === "loading"}
      />

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
                  <h3>Bridge snapshot</h3>
                  <p>真实 WebView2 环境中来自 C#；浏览器环境中来自集中 mock adapter。</p>
                </div>
                <StatusPill tone={environment === "webview2" ? "success" : "diagnostics"}>{environment}</StatusPill>
              </div>
              <div className="snapshot-grid">
                <SnapshotItem label="Watcher" value={snapshot.data.watcher.running ? "运行中" : "未运行"} />
                <SnapshotItem label="Watcher PID" value={String(snapshot.data.watcher.pid || "-")} />
                <SnapshotItem label="Config path" value={snapshot.data.config.path || "-"} />
                <SnapshotItem label="Data root" value={snapshot.data.config.dataRoot || "-"} />
                <SnapshotItem label="History" value={snapshot.data.reports.historyExists ? "存在" : "暂无历史"} />
                <SnapshotItem label="Generated" value={formatDateTime(snapshot.data.generatedAt)} />
              </div>
            </GlassCard>

            <GlassCard>
              <InlineStatus
                tone={config.status === "error" ? "danger" : config.data ? "success" : "diagnostics"}
                title={config.data ? "配置已加载" : "等待配置"}
                message={config.data ? `${config.data.targetCount} 个目标，${config.data.enabledTargetCount} 个启用。` : config.message}
                busy={config.status === "loading"}
              />
              {snapshot.data.watcher.lastError ? (
                <InlineStatus tone="danger" title="Watcher 错误" message={snapshot.data.watcher.lastError} />
              ) : (
                <InlineStatus tone="success" title="Watcher 错误状态" message="当前 snapshot 未报告 watcher 错误。" />
              )}
              <InlineStatus
                tone={monitorRunning ? "success" : "diagnostics"}
                title="Monitor bridge 状态"
                message={`event.status: ${bridgeState.lastStatusEvent?.status ?? "等待事件"}；PID ${
                  bridgeState.monitorRuntime.pid || snapshot.data.watcher.pid || "-"
                }`}
                busy={monitorBusy}
              />
            </GlassCard>
          </div>
        </>
      ) : snapshot.status !== "loading" ? (
        <EmptyState
          icon={ShieldAlert}
          title="暂无 snapshot 数据"
          description="state.snapshot 尚未返回可用数据。可以刷新状态；如果在普通浏览器预览中看到此状态，说明 mock adapter 没有正常初始化。"
          actionLabel="未启用后端操作"
        />
      ) : null}
    </section>
  );
}

function buildOverviewMetrics(bridgeState: FrameScopeBridgeViewState): Metric[] {
  const snapshot = bridgeState.snapshot.data;
  const config = bridgeState.config.data;
  return [
    {
      label: "Bridge",
      value: snapshot?.bridgeStatus ?? bridgeState.snapshot.status,
      detail: bridgeState.environment === "webview2" ? "C# WebView2 bridge" : "Vite mock adapter",
      tone: bridgeState.snapshot.status === "error" ? "danger" : "success",
    },
    {
      label: "启用目标",
      value: String(config?.enabledTargetCount ?? snapshot?.config.enabledTargetCount ?? "-"),
      detail: `${config?.targetCount ?? snapshot?.config.targetCount ?? 0} 个配置目标`,
      tone: "primary",
    },
    {
      label: "Watcher",
      value: snapshot?.watcher.running ? "运行中" : "未运行",
      detail: snapshot?.watcher.pid ? `PID ${snapshot.watcher.pid}` : "未检测到 watcher 进程",
      tone: snapshot?.watcher.running ? "success" : "neutral",
    },
    {
      label: "历史文件",
      value: snapshot?.reports.historyExists ? "存在" : "暂无",
      detail: snapshot?.reports.historyPath || "等待 snapshot",
      tone: snapshot?.reports.historyExists ? "success" : "warning",
    },
  ].map((metric) => ({ ...metric, tone: metric.tone as Tone }));
}

function SnapshotItem({ label, value }: { label: string; value: string }) {
  return (
    <div className="snapshot-item">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}

function formatDateTime(value: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}
