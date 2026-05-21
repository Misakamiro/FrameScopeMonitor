import { FileText, FolderOpen, RefreshCw, RotateCw, Stethoscope } from "lucide-react";
import type { ReportListItem } from "../bridge/contract";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import type {
  FrameScopeBridgeViewState,
  OperationState,
  ReportOperationKind,
} from "../state/useFrameScopeBridgeState";
import "./pages.css";

interface ReportsPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

export function ReportsPage({ bridgeState }: ReportsPageProps) {
  const reports = bridgeState.reports.data?.reports ?? [];
  const reportsBusy = bridgeState.reports.status === "loading";
  const diagnosticsBusy = bridgeState.diagnosticsGenerate.status === "loading";

  return (
    <section className="page reports-page" data-smoke-page="reports">
      <div className="page__header">
        <div>
          <span className="mock-ribbon">{bridgeState.isMockPreview ? "mock reports adapter" : "real reports bridge"}</span>
          <h2>报告历史</h2>
          <p>
            本页通过 `reports.list` 读取报告。打开报告、打开目录、重新生成和诊断生成都只使用后端返回的
            `reportId`，不在前端拼接路径。
          </p>
        </div>
        <div className="page__actions">
          <Button
            icon={RefreshCw}
            variant="secondary"
            disabled={reportsBusy}
            data-smoke-action="refresh-reports"
            onClick={() => void bridgeState.refreshReports()}
          >
            {reportsBusy ? "刷新中" : "刷新列表"}
          </Button>
          <Button
            icon={Stethoscope}
            variant="secondary"
            disabled={diagnosticsBusy}
            data-smoke-action="generate-diagnostics"
            onClick={() => void bridgeState.generateDiagnostics()}
          >
            {diagnosticsBusy ? "诊断中" : "生成诊断"}
          </Button>
        </div>
      </div>

      <div className="split-grid split-grid--reports">
        <GlassCard className="split-grid__main">
          <div className="section-title">
            <div>
              <h3>报告列表</h3>
              <p>列表内容来自 `reports.list`。如果后端返回 unsupported/error，这里显示真实失败。</p>
            </div>
            <StatusPill tone={bridgeState.reports.status === "success" ? "success" : "diagnostics"}>
              {bridgeState.reports.status}
            </StatusPill>
          </div>

          {bridgeState.reports.status === "error" ? (
            <InlineStatus tone="danger" title="报告列表读取失败" message={bridgeState.reports.error} />
          ) : bridgeState.reports.status === "loading" ? (
            <InlineStatus tone="diagnostics" title="正在读取报告" message="等待 reports.list 返回。" busy />
          ) : null}

          {reports.length > 0 ? (
            <div className="report-list" aria-label="FrameScope report history">
              {reports.map((report, index) => (
                <ReportListRow
                  key={report.reportId}
                  report={report}
                  index={index}
                  bridgeState={bridgeState}
                  diagnosticsBusy={diagnosticsBusy}
                />
              ))}
            </div>
          ) : (
            <EmptyState
              icon={FolderOpen}
              title="暂无报告"
              description={bridgeState.reports.error || "reports.list 尚未返回可操作的报告记录。"}
              actionLabel="等待 reports.list"
            />
          )}
        </GlassCard>

        <GlassCard>
          <InlineStatus
            tone={
              bridgeState.diagnosticsGenerate.status === "error"
                ? "danger"
                : bridgeState.diagnosticsGenerate.status === "success"
                  ? "success"
                  : bridgeState.diagnosticsGenerate.status === "loading"
                    ? "diagnostics"
                    : "warning"
            }
            title={
              bridgeState.diagnosticsGenerate.status === "loading"
                ? "正在生成诊断"
                : bridgeState.diagnosticsGenerate.status === "success"
                  ? "诊断生成成功"
                  : bridgeState.diagnosticsGenerate.status === "error"
                    ? "诊断生成失败"
                    : "诊断待生成"
            }
            message={bridgeState.diagnosticsGenerate.error || bridgeState.diagnosticsGenerate.message}
            busy={diagnosticsBusy}
          />

          <div className="report-detail">
            <h3>诊断输出</h3>
            <dl>
              <ReportDetailLine label="Markdown" value={stringPayload(bridgeState.diagnosticsResult, "markdownPath")} />
              <ReportDetailLine label="JSON" value={stringPayload(bridgeState.diagnosticsResult, "jsonPath")} />
              <ReportDetailLine label="Run dir" value={stringPayload(bridgeState.diagnosticsResult, "runDir")} />
              <ReportDetailLine label="Report" value={stringPayload(bridgeState.diagnosticsResult, "reportHtml")} />
            </dl>
          </div>

          <InlineStatus
            tone="diagnostics"
            title="Mock/live 边界"
            message={
              bridgeState.isMockPreview
                ? "当前为浏览器 mock adapter，用于预览 UI 状态，不代表真实报告已生成。"
                : "当前为 WebView2 live bridge；失败会按后端返回错误显示，不回落到 mock 成功。"
            }
          />
        </GlassCard>
      </div>
    </section>
  );
}

function ReportListRow({
  report,
  index,
  bridgeState,
  diagnosticsBusy,
}: {
  report: ReportListItem;
  index: number;
  bridgeState: FrameScopeBridgeViewState;
  diagnosticsBusy: boolean;
}) {
  const openState = bridgeState.getReportOperationState("open", report.reportId);
  const directoryState = bridgeState.getReportOperationState("openDirectory", report.reportId);
  const regenerateState = bridgeState.getReportOperationState("regenerate", report.reportId);
  const anyReportActionBusy =
    openState.status === "loading" || directoryState.status === "loading" || regenerateState.status === "loading";

  return (
    <article className="report-list-row">
      <div className="report-list-row__head">
        <div className="report-row__icon">
          <FileText aria-hidden="true" size={18} />
        </div>
        <div className="report-list-row__title">
          <strong>{report.game || report.processName || "FrameScope report"}</strong>
          <span>{formatReportTime(report.time || report.lastWriteTime)}</span>
        </div>
        <StatusPill tone={report.reportExists ? "success" : "warning"}>
          {report.reportExists ? report.reportKind || "ready" : "missing html"}
        </StatusPill>
      </div>

      <div className="report-list-row__meta">
        <SnapshotLine label="Report ID" value={report.reportId} />
        <SnapshotLine label="Frames" value={String(report.frameCount)} valueClassName="snapshot-item__value--nowrap" />
        <SnapshotLine label="Size" value={formatBytes(report.reportSizeBytes)} valueClassName="snapshot-item__value--nowrap" />
      </div>

      <div className="report-list-row__actions">
        <Button
          icon={FileText}
          variant="secondary"
          disabled={!report.canOpenReport || anyReportActionBusy}
          data-smoke-action={`open-report-${index}`}
          onClick={() => void bridgeState.openReport(report.reportId)}
        >
          {openState.status === "loading" ? "打开中" : "打开报告"}
        </Button>
        <Button
          icon={FolderOpen}
          variant="secondary"
          disabled={!report.canOpenDirectory || anyReportActionBusy}
          data-smoke-action={`open-directory-${index}`}
          onClick={() => void bridgeState.openReportDirectory(report.reportId)}
        >
          {directoryState.status === "loading" ? "打开中" : "打开目录"}
        </Button>
        <Button
          icon={RotateCw}
          variant="primary"
          disabled={!report.canRegenerate || anyReportActionBusy}
          data-smoke-action={`regenerate-report-${index}`}
          onClick={() => void bridgeState.regenerateReport(report.reportId)}
        >
          {regenerateState.status === "loading" ? "生成中" : "重新生成"}
        </Button>
        <Button
          icon={Stethoscope}
          variant="secondary"
          disabled={diagnosticsBusy}
          data-smoke-action={`diagnostics-report-${index}`}
          onClick={() => void bridgeState.generateDiagnostics(report.reportId)}
        >
          {diagnosticsBusy ? "诊断中" : "诊断"}
        </Button>
      </div>

      <ReportOperationStatus kind="open" state={openState} />
      <ReportOperationStatus kind="openDirectory" state={directoryState} />
      <ReportOperationStatus kind="regenerate" state={regenerateState} />
    </article>
  );
}

function ReportOperationStatus({ kind, state }: { kind: ReportOperationKind; state: OperationState }) {
  if (state.status === "idle") return null;
  return (
    <InlineStatus
      tone={state.status === "error" ? "danger" : state.status === "success" ? "success" : "diagnostics"}
      title={`${kind} ${state.status}`}
      message={state.error || state.message}
      busy={state.status === "loading"}
    />
  );
}

function ReportDetailLine({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd>{value || "-"}</dd>
    </div>
  );
}

function SnapshotLine({ label, value, valueClassName }: { label: string; value: string; valueClassName?: string }) {
  return (
    <div className="snapshot-item">
      <span>{label}</span>
      <strong className={valueClassName}>{value}</strong>
    </div>
  );
}

function stringPayload(payload: Record<string, unknown> | null, key: string) {
  const value = payload?.[key];
  return typeof value === "string" ? value : "";
}

function formatReportTime(value: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return "-";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}
