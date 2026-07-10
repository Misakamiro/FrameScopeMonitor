import { FileText, FolderOpen, MoreHorizontal, RefreshCw, RotateCw, Stethoscope } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from "react";
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
  const [selectedReportId, setSelectedReportId] = useState("");
  const [openReportMenuId, setOpenReportMenuId] = useState("");
  const [closingReportMenuId, setClosingReportMenuId] = useState("");
  const reportMenuCloseTimer = useRef<number | null>(null);
  const reportSmokeIndexByIdRef = useRef<Map<string, number>>(new Map());
  const smokeIndexByReportId = useMemo(() => {
    const indexById = reportSmokeIndexByIdRef.current;
    for (const report of reports) {
      if (indexById.has(report.reportId)) continue;
      indexById.set(report.reportId, indexById.size);
    }
    return new Map(indexById);
  }, [reports]);
  const selectedReport = useMemo(
    () => reports.find((report) => report.reportId === selectedReportId) ?? reports[0] ?? null,
    [reports, selectedReportId],
  );

  useEffect(() => {
    if (selectedReportId && reports.some((report) => report.reportId === selectedReportId)) return;
    setSelectedReportId(reports[0]?.reportId ?? "");
  }, [reports, selectedReportId]);

  const clearReportMenuCloseTimer = useCallback(() => {
    if (reportMenuCloseTimer.current === null) return;
    window.clearTimeout(reportMenuCloseTimer.current);
    reportMenuCloseTimer.current = null;
  }, []);

  const closeReportMenu = useCallback(
    (restoreFocusId?: string) => {
      if (!openReportMenuId) return;
      const closingId = openReportMenuId;
      setOpenReportMenuId("");
      setClosingReportMenuId(closingId);
      clearReportMenuCloseTimer();
      reportMenuCloseTimer.current = window.setTimeout(() => {
        setClosingReportMenuId((current) => (current === closingId ? "" : current));
        reportMenuCloseTimer.current = null;
      }, 110);
      if (restoreFocusId) {
        window.setTimeout(() => document.getElementById(restoreFocusId)?.focus(), 0);
      }
    },
    [clearReportMenuCloseTimer, openReportMenuId],
  );

  useEffect(() => {
    return () => clearReportMenuCloseTimer();
  }, [clearReportMenuCloseTimer]);

  useEffect(() => {
    if (!openReportMenuId) return;
    if (reports.some((report) => report.reportId === openReportMenuId)) return;
    closeReportMenu();
  }, [closeReportMenu, openReportMenuId, reports]);

  useEffect(() => {
    if (!openReportMenuId) return;
    const handlePointerDown = (event: PointerEvent) => {
      const target = event.target instanceof Element ? event.target : null;
      const menuRoot = target?.closest("[data-report-menu-root]");
      if (menuRoot?.getAttribute("data-report-menu-root") === openReportMenuId) return;
      closeReportMenu();
    };
    document.addEventListener("pointerdown", handlePointerDown);
    return () => document.removeEventListener("pointerdown", handlePointerDown);
  }, [closeReportMenu, openReportMenuId]);

  return (
    <section className="page reports-page" data-smoke-page="reports">
      <div className="page__header">
        <div>
          <h2>报告</h2>
          <p>查找最近报告并打开查看。</p>
        </div>
        <div className="page__actions">
          <Button
            icon={RefreshCw}
            variant="secondary"
            disabled={reportsBusy}
            data-smoke-action="refresh-reports"
            onClick={() => void bridgeState.refreshReports()}
          >
            {reportsBusy ? "正在刷新" : "刷新列表"}
          </Button>
        </div>
      </div>

      <div className="split-grid split-grid--reports">
        <GlassCard className="split-grid__main">
          <div className="section-title">
            <div>
              <h3>报告列表</h3>
              <p>优先显示游戏、时间和报告状态。</p>
            </div>
            <StatusPill tone={bridgeState.reports.status === "success" ? "success" : "diagnostics"}>
              {loadStatusLabel(bridgeState.reports.status)}
            </StatusPill>
          </div>

          {bridgeState.reports.status === "error" ? (
            <InlineStatus
              tone="danger"
              title="报告读取失败"
              message={bridgeState.reports.error || "请刷新列表；如果仍失败，请检查数据目录是否存在。"}
            />
          ) : bridgeState.reports.status === "loading" ? (
            <InlineStatus tone="diagnostics" title="正在读取报告" message="正在加载可用报告。" busy />
          ) : null}

          {reports.length > 0 ? (
            <div className="report-list report-list--compact" aria-label="FrameScope report history">
              {reports.map((report, index) => (
                <ReportListRow
                  key={report.reportId}
                  report={report}
                  index={index}
                  smokeIndex={smokeIndexByReportId.get(report.reportId) ?? index}
                  selected={selectedReport?.reportId === report.reportId}
                  bridgeState={bridgeState}
                  diagnosticsBusy={diagnosticsBusy}
                  menuOpen={openReportMenuId === report.reportId}
                  menuClosing={closingReportMenuId === report.reportId}
                  onSelect={() => {
                    setSelectedReportId(report.reportId);
                    closeReportMenu();
                  }}
                  onToggleMenu={() => {
                    setSelectedReportId(report.reportId);
                    if (openReportMenuId === report.reportId) {
                      closeReportMenu();
                    } else {
                      clearReportMenuCloseTimer();
                      setClosingReportMenuId("");
                      setOpenReportMenuId(report.reportId);
                    }
                  }}
                  onCloseMenu={closeReportMenu}
                />
              ))}
            </div>
          ) : (
            <EmptyState
              icon={FolderOpen}
              title="还没有报告"
              description="完成一次监控后，报告会显示在这里。"
              actionLabel={hasEnabledTargets(bridgeState) ? "可从监控页启动" : "先添加目标后再监控"}
            />
          )}
        </GlassCard>

        <GlassCard>
          {selectedReport ? (
            <ReportDetail
              report={selectedReport}
              bridgeState={bridgeState}
              diagnosticsBusy={diagnosticsBusy}
            />
          ) : (
            <EmptyState
              icon={FileText}
              title="未选择报告"
              description="选择左侧报告后，可以打开文件夹、重新生成或生成诊断文件。"
            />
          )}
        </GlassCard>
      </div>
    </section>
  );
}

function ReportListRow({
  report,
  index,
  smokeIndex,
  selected,
  bridgeState,
  diagnosticsBusy,
  menuOpen,
  menuClosing,
  onSelect,
  onToggleMenu,
  onCloseMenu,
}: {
  report: ReportListItem;
  index: number;
  smokeIndex: number;
  selected: boolean;
  bridgeState: FrameScopeBridgeViewState;
  diagnosticsBusy: boolean;
  menuOpen: boolean;
  menuClosing: boolean;
  onSelect: () => void;
  onToggleMenu: () => void;
  onCloseMenu: (restoreFocusId?: string) => void;
}) {
  const openState = bridgeState.getReportOperationState("open", report.reportId);
  const directoryState = bridgeState.getReportOperationState("openDirectory", report.reportId);
  const regenerateState = bridgeState.getReportOperationState("regenerate", report.reportId);
  const menuButtonId = `report-more-button-${index}`;
  const menuId = `report-more-menu-${index}`;
  const busy =
    openState.status === "loading" ||
    directoryState.status === "loading" ||
    regenerateState.status === "loading";

  useEffect(() => {
    if (!menuOpen) return;
    window.setTimeout(() => {
      const firstMenuItem = document.querySelector<HTMLButtonElement>(
        `#${menuId} [role="menuitem"]:not(:disabled)`,
      );
      firstMenuItem?.focus();
    }, 0);
  }, [menuId, menuOpen]);

  const closeMenuAndFocusButton = () => {
    onCloseMenu(menuButtonId);
  };

  const runMenuAction = (action: () => void) => {
    onCloseMenu();
    action();
  };

  const handleReportMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      closeMenuAndFocusButton();
      return;
    }

    if (event.key === "Tab") {
      onCloseMenu();
      return;
    }

    if (event.key !== "ArrowDown" && event.key !== "ArrowUp" && event.key !== "Home" && event.key !== "End") return;
    const menuItems = Array.from(event.currentTarget.querySelectorAll<HTMLButtonElement>('[role="menuitem"]:not(:disabled)'));
    if (menuItems.length === 0) return;
    event.preventDefault();
    const activeIndex = menuItems.indexOf(document.activeElement as HTMLButtonElement);
    const direction = event.key === "ArrowDown" ? 1 : -1;
    const nextIndex =
      event.key === "Home"
        ? 0
        : event.key === "End"
          ? menuItems.length - 1
          : activeIndex < 0
            ? 0
            : (activeIndex + direction + menuItems.length) % menuItems.length;
    menuItems[nextIndex]?.focus();
  };

  return (
    <article
      className={["report-list-row report-list-row--table", selected ? "report-list-row--selected" : ""].join(" ")}
      data-report-menu-root={report.reportId}
    >
      <button type="button" className="report-list-row__select" onClick={onSelect} aria-label="查看报告详情">
        <div className="report-list-row__title">
          <strong>{report.game || report.processName || "FrameScope 报告"}</strong>
          <span>{formatReportTime(report.time || report.lastWriteTime)}</span>
        </div>
        <StatusPill tone={reportStatusTone(report)} className="report-list-row__status">
          {reportStatusLabel(report)}
        </StatusPill>
        <span className="report-list-row__metric nowrap-value">
          <span className="compact-field-label">帧数</span>
          {formatFrameCount(report)}
        </span>
        <span className="report-list-row__metric nowrap-value">
          <span className="compact-field-label">大小</span>
          {formatBytes(report.reportSizeBytes)}
        </span>
      </button>

      <div className="report-row-actions">
        <Button
          icon={FileText}
          variant="secondary"
          disabled={!report.canOpenReport || busy}
          data-smoke-action={`open-report-${smokeIndex}`}
          onClick={() => void bridgeState.openReport(report.reportId)}
        >
          {busy ? "正在打开" : "打开报告"}
        </Button>
        <Button
          id={menuButtonId}
          className="report-more-button report-more-button--icon"
          icon={MoreHorizontal}
          variant="ghost"
          aria-label="报告更多操作"
          aria-haspopup="menu"
          aria-expanded={menuOpen}
          aria-controls={menuOpen ? menuId : undefined}
          title="更多操作"
          onClick={onToggleMenu}
        >
          <span className="sr-only">更多操作</span>
        </Button>
        {menuOpen || menuClosing ? (
          <div
            id={menuId}
            className={["report-more-menu", menuClosing ? "report-more-menu--closing" : ""].join(" ")}
            role="menu"
            aria-label="报告更多操作"
            onKeyDown={handleReportMenuKeyDown}
          >
            <button
              type="button"
              role="menuitem"
              disabled={!report.canOpenDirectory || busy}
              onClick={() => runMenuAction(() => void bridgeState.openReportDirectory(report.reportId))}
            >
              <FolderOpen aria-hidden="true" size={16} />
              <span>打开文件夹</span>
            </button>
            <button
              type="button"
              role="menuitem"
              disabled={!report.canRegenerate || busy}
              onClick={() => runMenuAction(() => void bridgeState.regenerateReport(report.reportId))}
            >
              <RotateCw aria-hidden="true" size={16} />
              <span>重新生成报告</span>
            </button>
            <span className="report-more-menu__divider" aria-hidden="true" />
            <button
              type="button"
              role="menuitem"
              data-menu-danger-zone="diagnostics"
              disabled={diagnosticsBusy}
              onClick={() => runMenuAction(() => void bridgeState.generateDiagnostics(report.reportId))}
            >
              <Stethoscope aria-hidden="true" size={16} />
              <span>生成诊断文件</span>
            </button>
          </div>
        ) : null}
      </div>

      <button
        type="button"
        className="sr-only"
        aria-hidden="true"
        tabIndex={-1}
        disabled={!report.canOpenDirectory || busy}
        data-smoke-action={`open-directory-${smokeIndex}`}
        onClick={() => void bridgeState.openReportDirectory(report.reportId)}
      >
        打开文件夹
      </button>
      <button
        type="button"
        className="sr-only"
        aria-hidden="true"
        tabIndex={-1}
        disabled={!report.canRegenerate || busy}
        data-smoke-action={`regenerate-report-${smokeIndex}`}
        onClick={() => void bridgeState.regenerateReport(report.reportId)}
      >
        重新生成报告
      </button>

      <ReportOperationStatus kind="open" state={openState} />
      <ReportOperationStatus kind="openDirectory" state={directoryState} />
      <ReportOperationStatus kind="regenerate" state={regenerateState} />
    </article>
  );
}

function ReportDetail({
  report,
  bridgeState,
  diagnosticsBusy,
}: {
  report: ReportListItem;
  bridgeState: FrameScopeBridgeViewState;
  diagnosticsBusy: boolean;
}) {
  const openState = bridgeState.getReportOperationState("open", report.reportId);
  const directoryState = bridgeState.getReportOperationState("openDirectory", report.reportId);
  const regenerateState = bridgeState.getReportOperationState("regenerate", report.reportId);
  const anyReportActionBusy =
    openState.status === "loading" || directoryState.status === "loading" || regenerateState.status === "loading";

  return (
    <div className="report-detail-panel">
      <div className="section-title">
        <div>
          <h3>报告详情</h3>
          <p>{report.game || report.processName || "FrameScope 报告"}</p>
        </div>
        <StatusPill tone={reportStatusTone(report)}>{reportStatusLabel(report)}</StatusPill>
      </div>

      <dl className="detail-list">
        <DetailLine label="时间" value={formatReportTime(report.time || report.lastWriteTime)} />
        <DetailLine label="帧数" value={formatFrameCount(report)} />
        <DetailLine label="大小" value={formatBytes(report.reportSizeBytes)} />
        <DetailLine label="进程" value={report.processName || "-"} />
        <DetailLine label="位置" value={formatPathTail(report.runDir)} fullValue={report.runDir} />
      </dl>

      <div className="detail-actions">
        <Button
          icon={FolderOpen}
          variant="secondary"
          disabled={!report.canOpenDirectory || anyReportActionBusy}
          data-smoke-action="open-selected-directory"
          onClick={() => void bridgeState.openReportDirectory(report.reportId)}
        >
          {directoryState.status === "loading" ? "正在打开" : "打开文件夹"}
        </Button>
        <Button
          icon={RotateCw}
          variant="secondary"
          disabled={!report.canRegenerate || anyReportActionBusy}
          data-smoke-action="regenerate-selected-report"
          onClick={() => void bridgeState.regenerateReport(report.reportId)}
        >
          {regenerateState.status === "loading" ? "正在生成" : "重新生成报告"}
        </Button>
        <Button
          icon={Stethoscope}
          variant="secondary"
          disabled={diagnosticsBusy}
          data-smoke-action="generate-diagnostics"
          onClick={() => void bridgeState.generateDiagnostics(report.reportId)}
        >
          {diagnosticsBusy ? "正在生成" : "生成诊断文件"}
        </Button>
      </div>

      <ReportOperationStatus kind="open" state={openState} />
      <ReportOperationStatus kind="openDirectory" state={directoryState} />
      <ReportOperationStatus kind="regenerate" state={regenerateState} />
      <DiagnosticsStatus bridgeState={bridgeState} />
    </div>
  );
}

function ReportOperationStatus({ kind, state }: { kind: ReportOperationKind; state: OperationState }) {
  if (state.status === "idle") return null;
  return (
    <InlineStatus
      tone={state.status === "error" ? "danger" : state.status === "success" ? "success" : "diagnostics"}
      title={`${reportOperationLabel(kind)}${operationStatusLabel(state.status)}`}
      message={state.status === "error" ? state.error || "操作失败。请检查文件是否仍在原位置后重试。" : operationStatusMessage(kind, state.status)}
      busy={state.status === "loading"}
    />
  );
}

function DiagnosticsStatus({ bridgeState }: { bridgeState: FrameScopeBridgeViewState }) {
  if (bridgeState.diagnosticsGenerate.status === "idle") return null;
  return (
    <InlineStatus
      tone={
        bridgeState.diagnosticsGenerate.status === "error"
          ? "danger"
          : bridgeState.diagnosticsGenerate.status === "success"
            ? "success"
            : "diagnostics"
      }
      title={
        bridgeState.diagnosticsGenerate.status === "error"
          ? "诊断生成失败"
          : bridgeState.diagnosticsGenerate.status === "success"
            ? "诊断已生成"
            : "正在生成诊断"
      }
      message={diagnosticsMessage(
        bridgeState.diagnosticsGenerate.status,
        bridgeState.diagnosticsGenerate.error,
        bridgeState.diagnosticsGenerate.message,
      )}
      busy={bridgeState.diagnosticsGenerate.status === "loading"}
    />
  );
}

function DetailLine({ label, value, fullValue }: { label: string; value: string; fullValue?: string }) {
  return (
    <div>
      <dt>{label}</dt>
      <dd title={fullValue || value}>{value || "-"}</dd>
    </div>
  );
}

function hasEnabledTargets(bridgeState: FrameScopeBridgeViewState) {
  return Boolean(
    bridgeState.targets.data?.enabledTargetCount ||
      bridgeState.config.data?.enabledTargetCount ||
      bridgeState.snapshot.data?.config.enabledTargetCount,
  );
}

function loadStatusLabel(status: AsyncStatus) {
  if (status === "loading") return "读取中";
  if (status === "success") return "已加载";
  if (status === "error") return "读取失败";
  return "未读取";
}

function reportStatusTone(report: ReportListItem) {
  switch (report.reportKind) {
    case "full":
      return report.hasFrameData ? "success" : "warning";
    case "partial":
      return "warning";
    case "diagnostic":
      return "warning";
    case "error":
      return "danger";
    case "pending":
      return "warning";
    default:
      if (!report.reportExists) return "warning";
      if (report.canOpenReport && report.hasFrameData) return "success";
      if (report.monitorExitCode !== 0) return "danger";
      return "warning";
  }
}

function reportStatusLabel(report: ReportListItem) {
  switch (report.reportKind) {
    case "full":
      return report.hasFrameData ? "完整" : "可查看";
    case "partial":
      return "部分数据";
    case "diagnostic":
      return "诊断数据";
    case "error":
      return "失败";
    case "pending":
      return "生成中";
    default:
      if (!report.reportExists) return "缺失";
      if (report.canOpenReport && report.hasFrameData) return "完整";
      if (report.monitorExitCode !== 0) return "失败";
      return "可查看";
  }
}

function reportOperationLabel(kind: ReportOperationKind) {
  if (kind === "open") return "打开报告";
  if (kind === "openDirectory") return "打开文件夹";
  return "重新生成报告";
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
  if (status === "error") return error || "诊断生成失败。请确认数据目录可访问后重试。";
  if (status === "loading") return "正在收集报告相关信息。";
  if (status === "success") return "诊断文件已生成，可在报告目录中查看。";
  return message || "报告异常时再生成诊断文件。";
}

function formatReportTime(value: string) {
  if (!value) return "-";
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function formatFrameCount(report: ReportListItem) {
  return report.frameCount > 0 ? `${report.frameCount} 帧` : "-";
}

function formatBytes(value: number) {
  if (!Number.isFinite(value) || value <= 0) return "-";
  if (value < 1024) return `${value} B`;
  if (value < 1024 * 1024) return `${(value / 1024).toFixed(1)} KB`;
  return `${(value / 1024 / 1024).toFixed(1)} MB`;
}

function formatPathTail(value?: string) {
  if (!value) return "-";
  const parts = value.split(/[\\/]/).filter(Boolean);
  return parts.slice(-3).join("\\") || value;
}
