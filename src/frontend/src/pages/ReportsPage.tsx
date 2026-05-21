import { FileText, FolderOpen, RefreshCw, RotateCw, Stethoscope } from "lucide-react";
import type { ReportListItem } from "../bridge/contract";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import type {
  AsyncStatus,
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
          <span className="mode-ribbon">{bridgeState.isMockPreview ? "界面预览" : "本机数据"}</span>
          <h2>报告历史</h2>
          <p>查看最近生成的性能报告，打开报告或所在目录，也可以重新生成报告和诊断文件。</p>
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
              <p>按最近会话展示报告状态、帧数和文件大小。不可用的操作会保持禁用。</p>
            </div>
            <StatusPill tone={bridgeState.reports.status === "success" ? "success" : "diagnostics"}>
              {loadStatusLabel(bridgeState.reports.status)}
            </StatusPill>
          </div>

          {bridgeState.reports.status === "error" ? (
            <InlineStatus tone="danger" title="报告列表读取失败" message={bridgeState.reports.error} />
          ) : bridgeState.reports.status === "loading" ? (
            <InlineStatus tone="diagnostics" title="正在读取报告" message="正在加载可用报告。" busy />
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
              description={bridgeState.reports.error || "还没有可打开的报告。完成一次监控后，报告会出现在这里。"}
              actionLabel="等待报告"
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
            message={diagnosticsMessage(
              bridgeState.diagnosticsGenerate.status,
              bridgeState.diagnosticsGenerate.error,
              bridgeState.diagnosticsGenerate.message,
            )}
            busy={diagnosticsBusy}
          />

          <div className="report-detail">
            <h3>诊断文件</h3>
            <dl>
              <ReportDetailLine label="说明文件" value={stringPayload(bridgeState.diagnosticsResult, "markdownPath")} />
              <ReportDetailLine label="数据文件" value={stringPayload(bridgeState.diagnosticsResult, "jsonPath")} />
              <ReportDetailLine label="会话目录" value={stringPayload(bridgeState.diagnosticsResult, "runDir")} />
              <ReportDetailLine label="关联报告" value={stringPayload(bridgeState.diagnosticsResult, "reportHtml")} />
            </dl>
          </div>
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
          <strong>{report.game || report.processName || "FrameScope 报告"}</strong>
          <span>{formatReportTime(report.time || report.lastWriteTime)}</span>
        </div>
        <StatusPill tone={report.reportExists ? "success" : "warning"}>
          {report.reportExists ? reportKindLabel(report.reportKind) : "报告缺失"}
        </StatusPill>
      </div>

      <div className="report-list-row__meta">
        <SnapshotLine label="报告编号" value={report.reportId} />
        <SnapshotLine label="帧数" value={String(report.frameCount)} valueClassName="snapshot-item__value--nowrap" />
        <SnapshotLine label="大小" value={formatBytes(report.reportSizeBytes)} valueClassName="snapshot-item__value--nowrap" />
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
      title={`${reportOperationLabel(kind)}${operationStatusLabel(state.status)}`}
      message={state.status === "error" ? state.error || "操作失败，请稍后重试。" : operationStatusMessage(kind, state.status)}
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

function loadStatusLabel(status: AsyncStatus) {
  if (status === "loading") return "读取中";
  if (status === "success") return "已加载";
  if (status === "error") return "失败";
  return "未读取";
}

function reportKindLabel(kind: string) {
  if (kind === "full") return "完整报告";
  if (kind === "pending") return "处理中";
  return kind || "可打开";
}

function reportOperationLabel(kind: ReportOperationKind) {
  if (kind === "open") return "打开报告";
  if (kind === "openDirectory") return "打开目录";
  return "重新生成";
}

function operationStatusLabel(status: AsyncStatus) {
  if (status === "loading") return "中";
  if (status === "success") return "成功";
  if (status === "error") return "失败";
  return "";
}

function operationStatusMessage(kind: ReportOperationKind, status: AsyncStatus) {
  if (status === "loading") return `${reportOperationLabel(kind)}正在执行。`;
  if (status === "success") return `${reportOperationLabel(kind)}已完成。`;
  return "";
}

function diagnosticsMessage(status: AsyncStatus, error: string, message: string) {
  if (status === "error") return error || "诊断生成失败，请稍后重试。";
  if (status === "loading") return "正在收集当前会话信息。";
  if (status === "success") return "诊断文件已生成。";
  return message || "需要排查报告问题时，可以先生成诊断。";
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
