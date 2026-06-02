import { Edit3, Plus, RefreshCw, RotateCcw, Save, Search, Trash2 } from "lucide-react";
import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent, type UIEvent } from "react";
import type { FrameScopeTargetConfig } from "../bridge/contract";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import { readVisualFixtureMode } from "../data/mockPreview";
import type { AsyncStatus, FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
import { getVirtualListWindow } from "../utils/virtualListWindow";
import "./pages.css";

interface TargetsPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

const emptyTarget: FrameScopeTargetConfig = {
  Enabled: true,
  Name: "",
  ProcessName: "",
  SampleIntervalMs: 1000,
  ProcessSamplingMode: "normal",
  ProcessSampleIntervalMs: 1000,
  SlowSampleIntervalMs: 1000,
  OpenReportOnComplete: true,
};

const PROCESS_RESULT_WINDOW_THRESHOLD = 250;
const PROCESS_RESULT_ROW_HEIGHT = 62;
const PROCESS_RESULT_OVERSCAN = 6;
const PROCESS_RESULT_DEFAULT_VIEWPORT_HEIGHT = 360;

export function TargetsPage({ bridgeState }: TargetsPageProps) {
  const [query, setQuery] = useState("");
  const [draftTargets, setDraftTargets] = useState<FrameScopeTargetConfig[]>([]);
  const [draftDataRoot, setDraftDataRoot] = useState("");
  const [draftOpenReportOnComplete, setDraftOpenReportOnComplete] = useState(false);
  const [loadedSignature, setLoadedSignature] = useState("");
  const [editingTargetIndex, setEditingTargetIndex] = useState<number | null>(null);
  const [selectedProcessKey, setSelectedProcessKey] = useState("");
  const [lastProcessRefreshKey, setLastProcessRefreshKey] = useState("");
  const [settledProcessRefreshKey, setSettledProcessRefreshKey] = useState("");
  const [processListScrollTop, setProcessListScrollTop] = useState(0);
  const [processListViewportHeight, setProcessListViewportHeight] = useState(PROCESS_RESULT_DEFAULT_VIEWPORT_HEIGHT);
  const processListRef = useRef<HTMLDivElement | null>(null);
  const visualFixtureMode = readVisualFixtureMode();
  const loadedTargetsPayload = bridgeState.targets.data;
  const refreshBusy = bridgeState.processRefresh.status === "loading";
  const fixtureSaving = visualFixtureMode === "saving";
  const fixtureSaved = visualFixtureMode === "saved";
  const saveBusy = bridgeState.targetsSave.status === "loading" || fixtureSaving;
  const targetSaveStatus =
    visualFixtureMode === "failure"
      ? "error"
      : fixtureSaving
        ? "loading"
        : fixtureSaved
          ? "success"
          : bridgeState.targetsSave.status;
  const processRefreshStatus = visualFixtureMode === "failure" ? "error" : bridgeState.processRefresh.status;
  const visibleProcesses = useMemo(
    () =>
      visualFixtureMode === "many-results" && bridgeState.processes.length === 0
        ? buildFixtureProcesses()
        : bridgeState.processes,
    [bridgeState.processes, visualFixtureMode],
  );
  const targetSaveError =
    visualFixtureMode === "failure" ? "视觉状态夹具：目标保存失败，当前输入仍保留。" : bridgeState.targetsSave.error;
  const processSearchError =
    visualFixtureMode === "failure" ? "视觉状态夹具：进程查找失败，可直接重试。" : bridgeState.processRefresh.error;
  const targetSaveFailed = targetSaveStatus === "error";
  const processSearchFailed = processRefreshStatus === "error";
  const processLookupState = getProcessLookupState(
    processRefreshStatus,
    processSearchError,
    visibleProcesses.length,
  );
  const smokeProcessRefreshState = bridgeState.processRefresh.status === "success" ? "Process refresh completed" : "";
  const processResultSignature = useMemo(
    () => {
      const firstProcess = visibleProcesses[0];
      const lastProcess = visibleProcesses[visibleProcesses.length - 1];
      return [
        visibleProcesses.length,
        firstProcess ? `${firstProcess.processName}:${firstProcess.processId}` : "",
        lastProcess ? `${lastProcess.processName}:${lastProcess.processId}` : "",
        bridgeState.processRefresh.updatedAt,
      ].join("|");
    },
    [bridgeState.processRefresh.updatedAt, visibleProcesses],
  );
  const shouldWindowProcesses = visibleProcesses.length >= PROCESS_RESULT_WINDOW_THRESHOLD;
  const processWindow = useMemo(
    () =>
      getVirtualListWindow({
        rowCount: visibleProcesses.length,
        rowHeight: PROCESS_RESULT_ROW_HEIGHT,
        scrollTop: shouldWindowProcesses ? processListScrollTop : 0,
        viewportHeight: processListViewportHeight,
        overscan: PROCESS_RESULT_OVERSCAN,
      }),
    [processListScrollTop, processListViewportHeight, shouldWindowProcesses, visibleProcesses.length],
  );
  const renderedProcesses = useMemo(
    () =>
      shouldWindowProcesses
        ? visibleProcesses.slice(processWindow.startIndex, processWindow.endIndex)
        : visibleProcesses,
    [processWindow.endIndex, processWindow.startIndex, shouldWindowProcesses, visibleProcesses],
  );
  const handleProcessListScroll = useCallback(
    (event: UIEvent<HTMLDivElement>) => {
      if (!shouldWindowProcesses) return;
      setProcessListScrollTop(event.currentTarget.scrollTop);
    },
    [shouldWindowProcesses],
  );

  useEffect(() => {
    const list = processListRef.current;
    if (!list || !shouldWindowProcesses) {
      setProcessListViewportHeight(PROCESS_RESULT_DEFAULT_VIEWPORT_HEIGHT);
      return;
    }

    const updateViewportHeight = () => {
      setProcessListViewportHeight(Math.max(PROCESS_RESULT_ROW_HEIGHT, list.clientHeight || PROCESS_RESULT_DEFAULT_VIEWPORT_HEIGHT));
    };
    updateViewportHeight();

    if (typeof ResizeObserver === "undefined") {
      window.addEventListener("resize", updateViewportHeight);
      return () => window.removeEventListener("resize", updateViewportHeight);
    }

    const observer = new ResizeObserver(updateViewportHeight);
    observer.observe(list);
    return () => observer.disconnect();
  }, [shouldWindowProcesses, visibleProcesses.length]);

  useEffect(() => {
    setProcessListScrollTop(0);
    if (processListRef.current) processListRef.current.scrollTop = 0;
  }, [processResultSignature]);

  useEffect(() => {
    if (bridgeState.processRefresh.status !== "success" || !processResultSignature) return;
    const nextRefreshKey = `${bridgeState.processRefresh.updatedAt}:${processResultSignature}`;
    setLastProcessRefreshKey(nextRefreshKey);
    setSettledProcessRefreshKey("");
    const timeout = window.setTimeout(() => {
      setSettledProcessRefreshKey(nextRefreshKey);
    }, 240);
    return () => window.clearTimeout(timeout);
  }, [bridgeState.processRefresh.status, bridgeState.processRefresh.updatedAt, processResultSignature]);

  useEffect(() => {
    if (!loadedTargetsPayload) return;
    const nextTargets = cloneTargets(loadedTargetsPayload.targets);
    const nextDataRoot = loadedTargetsPayload.dataRoot;
    const nextOpenReport = loadedTargetsPayload.openReportOnComplete;
    setDraftTargets(nextTargets);
    setDraftDataRoot(nextDataRoot);
    setDraftOpenReportOnComplete(nextOpenReport);
    setLoadedSignature(serializeTargets(nextTargets, nextDataRoot, nextOpenReport));
    setEditingTargetIndex(null);
  }, [loadedTargetsPayload]);

  const dirty = useMemo(() => {
    if (!loadedSignature) return false;
    if (visualFixtureMode === "dirty" || visualFixtureMode === "saving") return true;
    return serializeTargets(draftTargets, draftDataRoot, draftOpenReportOnComplete) !== loadedSignature;
  }, [draftDataRoot, draftOpenReportOnComplete, draftTargets, loadedSignature, visualFixtureMode]);

  const updateTarget = <TKey extends keyof FrameScopeTargetConfig>(
    index: number,
    key: TKey,
    value: FrameScopeTargetConfig[TKey],
  ) => {
    setDraftTargets((current) =>
      current.map((target, targetIndex) => (targetIndex === index ? { ...target, [key]: value } : target)),
    );
  };

  const addTarget = () => {
    setDraftTargets((current) => [...current, { ...emptyTarget, Name: "新游戏", ProcessName: "" }]);
    setEditingTargetIndex(draftTargets.length);
  };

  const removeTarget = (index: number) => {
    setDraftTargets((current) => current.filter((_, targetIndex) => targetIndex !== index));
    setEditingTargetIndex(null);
  };

  const resetDraft = () => {
    if (!loadedTargetsPayload) return;
    const nextTargets = cloneTargets(loadedTargetsPayload.targets);
    setDraftTargets(nextTargets);
    setDraftDataRoot(loadedTargetsPayload.dataRoot);
    setDraftOpenReportOnComplete(loadedTargetsPayload.openReportOnComplete);
    setEditingTargetIndex(null);
  };

  const refreshProcessSearch = () => {
    if (refreshBusy) return;
    void bridgeState.refreshProcesses(query);
  };

  const handleProcessSearchKeyDown = (event: KeyboardEvent<HTMLInputElement>) => {
    if (event.key !== "Enter") return;
    event.preventDefault();
    refreshProcessSearch();
  };

  const saveDraft = async () => {
    if (!dirty || saveBusy) return;
    const saved = await bridgeState.saveTargets({
      targets: draftTargets,
      dataRoot: draftDataRoot,
      openReportOnComplete: draftOpenReportOnComplete,
    });
    if (!saved) return;
    const nextTargets = cloneTargets(saved.targets);
    setDraftTargets(nextTargets);
    setDraftDataRoot(saved.dataRoot);
    setDraftOpenReportOnComplete(saved.openReportOnComplete);
    setLoadedSignature(serializeTargets(nextTargets, saved.dataRoot, saved.openReportOnComplete));
    setEditingTargetIndex(null);
  };

  return (
    <section className="page targets-page" data-smoke-page="targets">
      <div className="page__header">
        <div>
          <h2>监控目标</h2>
          <p>管理要记录的游戏进程。</p>
        </div>
        <div className="page__actions">
          <Button
            icon={Plus}
            variant="secondary"
            disabled={saveBusy}
            data-smoke-action="add-target"
            onClick={addTarget}
          >
            添加目标
          </Button>
          <Button
            icon={Save}
            variant={dirty ? "primary" : "secondary"}
            disabled={!dirty || saveBusy}
            data-smoke-action="save-targets"
            onClick={() => void saveDraft()}
          >
            {saveBusy ? "正在保存" : "保存修改"}
          </Button>
          {targetSaveFailed ? (
            <div className="action-feedback action-feedback--danger target-save-error" role="alert">
              <span>{targetSaveMessage("error", targetSaveError, bridgeState.targetsSave.message)}</span>
              <button type="button" onClick={() => void saveDraft()} disabled={saveBusy || !dirty}>
                重试保存
              </button>
            </div>
          ) : null}
        </div>
      </div>

      <div className="split-grid targets-layout-grid">
        <GlassCard className="split-grid__main">
          <div className="section-title">
            <div>
              <h3>目标列表</h3>
              <p>默认浏览目标，编辑时再展开输入。</p>
            </div>
            <StatusPill tone={dirty ? "warning" : bridgeState.targets.status === "success" ? "success" : "diagnostics"}>
              {dirty ? "未保存" : targetStatusLabel(bridgeState.targets.status)}
            </StatusPill>
          </div>

          {bridgeState.targets.status === "error" ? (
            <InlineStatus
              tone="danger"
              title="目标加载失败"
              message={bridgeState.targets.error || "请重新加载目标；如果仍失败，请检查配置文件权限。"}
            />
          ) : bridgeState.targets.status === "loading" ? (
            <InlineStatus tone="diagnostics" title="正在加载目标" message="正在加载已保存的目标配置。" busy />
          ) : null}

          {draftTargets.length > 0 ? (
            <div className="target-list" role="table" aria-label="监控目标列表">
              <div className="target-list__head" role="row">
                <span>状态</span>
                <span>游戏</span>
                <span>进程名</span>
                <span>报告</span>
                <span>操作</span>
              </div>
              {draftTargets.map((target, index) => (
                <TargetListRow
                  key={`${target.Name}-${target.ProcessName}-${index}`}
                  target={target}
                  index={index}
                  editing={editingTargetIndex === index}
                  onEdit={() => setEditingTargetIndex(index)}
                  onDone={() => setEditingTargetIndex(null)}
                  onRemove={() => removeTarget(index)}
                  onUpdate={updateTarget}
                />
              ))}
            </div>
          ) : (
            <EmptyState
              icon={Plus}
              title="还没有监控目标"
              description="添加游戏进程后，才能开始记录帧表现。"
              actionLabel="添加目标"
              onAction={addTarget}
            />
          )}
        </GlassCard>

        <aside className="aux-panel-stack">
          <GlassCard>
            <InlineStatus
              tone={
                targetSaveStatus === "error"
                  ? "danger"
                  : targetSaveStatus === "success"
                    ? "success"
                    : dirty
                      ? "warning"
                      : "diagnostics"
              }
              title={
                targetSaveStatus === "loading"
                  ? "正在保存目标"
                  : targetSaveStatus === "success"
                    ? "目标已保存"
                    : targetSaveStatus === "error"
                      ? "保存失败"
                      : dirty
                        ? "有未保存修改"
                        : "目标已同步"
              }
              message={targetSaveMessage(targetSaveStatus, targetSaveError, bridgeState.targetsSave.message)}
              busy={saveBusy}
            />
            <div className="target-path-summary">
              <span>数据保存到</span>
              <strong title={draftDataRoot}>{formatPathTail(draftDataRoot)}</strong>
              <label className="compact-toggle">
                <input
                  type="checkbox"
                  checked={draftOpenReportOnComplete}
                  onChange={(event) => setDraftOpenReportOnComplete(event.target.checked)}
                  data-smoke-field="targets-open-report"
                />
                <span>报告完成后自动打开</span>
              </label>
            </div>
            <div className="side-action-row">
              <Button
                icon={RefreshCw}
                variant="secondary"
                disabled={bridgeState.targets.status === "loading" || saveBusy}
                data-smoke-action="refresh-targets"
                onClick={() => void bridgeState.refreshTargets()}
              >
                {bridgeState.targets.status === "loading" ? "正在加载" : "重新读取"}
              </Button>
              <Button icon={RotateCcw} variant="secondary" disabled={!dirty || saveBusy} onClick={resetDraft}>
                撤销
              </Button>
            </div>
          </GlassCard>

          <GlassCard>
            <div className="section-title section-title--compact">
              <div>
                <h3>查找进程</h3>
                <p>辅助确认游戏进程名。</p>
              </div>
              <span
                className={[
                  "process-search-status",
                  `process-search-status--${processLookupState.tone}`,
                ].join(" ")}
              >
                {processLookupState.shortMessage}
              </span>
            </div>
            <div className="process-refresh-panel process-search-toolbar">
              <label className="process-search-field">
                <span className="sr-only">游戏名或进程名</span>
                <Search aria-hidden="true" size={16} strokeWidth={2.2} />
                <input
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  onKeyDown={handleProcessSearchKeyDown}
                  placeholder="例如 TslGame 或 VALORANT"
                  aria-label="进程过滤关键字"
                />
              </label>
              <Button
                variant={query.trim() ? "tonal" : "secondary"}
                disabled={refreshBusy}
                data-smoke-action="refresh-processes"
                onClick={refreshProcessSearch}
              >
                {refreshBusy ? "正在查找" : "查找进程"}
              </Button>
              {processSearchFailed ? (
                <div className="action-feedback action-feedback--danger process-search-error" role="alert">
                  <span>{processRefreshMessage("error", processSearchError, bridgeState.processRefresh.message)}</span>
                  <button type="button" onClick={refreshProcessSearch} disabled={refreshBusy}>
                    重试查找
                  </button>
                </div>
              ) : null}
              {smokeProcessRefreshState ? (
                <span className="sr-only" data-smoke-state="process-refresh">
                  {smokeProcessRefreshState}
                </span>
              ) : null}
            </div>
            <p className="process-search-help">
              {processLookupState.help}
            </p>

            {visibleProcesses.length > 0 ? (
              <div>
                <div className="process-result-summary">
                  <strong>{formatProcessResultSummary(visibleProcesses.length)}</strong>
                  <span>{visibleProcesses.length > 80 ? "结果已限制在本面板内滚动，继续输入可缩小范围。" : "可选择一项核对进程名。"}</span>
                </div>
                <div
                  ref={processListRef}
                  className={[
                    "process-result-list",
                    refreshBusy ? "process-result-list--refreshing" : "",
                    shouldWindowProcesses ? "process-result-list--windowed" : "",
                  ].join(" ")}
                  role="table"
                  aria-rowcount={visibleProcesses.length}
                  data-windowed={shouldWindowProcesses ? "true" : "false"}
                  data-process-total={visibleProcesses.length}
                  data-rendered-row-count={renderedProcesses.length}
                  onScroll={handleProcessListScroll}
                  aria-label="刷新后的进程列表"
                >
                  <div className="process-result-list__head" role="row">
                    <span>进程</span>
                    <span>PID</span>
                    <span>窗口</span>
                  </div>
                  <span className="process-result-list__status" aria-live="polite">
                    {processLookupState.resultStatus}
                  </span>
                  {shouldWindowProcesses && processWindow.paddingTop > 0 ? (
                    <div
                      className="process-result-list__spacer"
                      style={{ height: processWindow.paddingTop }}
                      aria-hidden="true"
                    />
                  ) : null}
                  {renderedProcesses.map((process, renderedIndex) => {
                    const index = shouldWindowProcesses ? processWindow.startIndex + renderedIndex : renderedIndex;
                    const processKey = `${process.processName}-${process.processId}`;
                    const updated =
                      lastProcessRefreshKey.endsWith(processResultSignature) &&
                      settledProcessRefreshKey !== lastProcessRefreshKey;
                    return (
                      <button
                        type="button"
                        className={[
                          "process-result-row",
                          selectedProcessKey === processKey || (!selectedProcessKey && index === 0)
                            ? "process-result-row--selected"
                            : "",
                          updated ? "process-result-row--updated" : "",
                        ].join(" ")}
                        key={processKey}
                        role="row"
                        aria-pressed={selectedProcessKey === processKey}
                        onClick={() => setSelectedProcessKey(processKey)}
                      >
                        <strong title={process.processName}>{process.processName}</strong>
                        <span>PID {process.processId}</span>
                        <small title={process.windowTitle || process.displayText}>
                          {process.windowTitle || process.displayText || "无窗口标题"}
                        </small>
                      </button>
                    );
                  })}
                  {shouldWindowProcesses && processWindow.paddingBottom > 0 ? (
                    <div
                      className="process-result-list__spacer"
                      style={{ height: processWindow.paddingBottom }}
                      aria-hidden="true"
                    />
                  ) : null}
                </div>
              </div>
            ) : (
              <ProcessLookupEmptyState
                icon={Search}
                title={processLookupState.emptyTitle}
                description={processLookupState.emptyDescription}
                busy={processLookupState.kind === "loading"}
              />
            )}
          </GlassCard>
        </aside>
      </div>
    </section>
  );
}

function ProcessLookupEmptyState({
  icon: Icon,
  title,
  description,
  busy,
}: {
  icon: typeof Search;
  title: string;
  description: string;
  busy: boolean;
}) {
  return (
    <div
      className={["process-result-empty", busy ? "process-result-empty--busy" : ""].join(" ")}
      aria-live="polite"
    >
      <span className="process-result-empty__icon" aria-hidden="true">
        <Icon size={18} strokeWidth={2.2} />
      </span>
      <div>
        <strong>{title}</strong>
        <p>{description}</p>
      </div>
    </div>
  );
}

function TargetListRow({
  target,
  index,
  editing,
  onEdit,
  onDone,
  onRemove,
  onUpdate,
}: {
  target: FrameScopeTargetConfig;
  index: number;
  editing: boolean;
  onEdit: () => void;
  onDone: () => void;
  onRemove: () => void;
  onUpdate: <TKey extends keyof FrameScopeTargetConfig>(
    index: number,
    key: TKey,
    value: FrameScopeTargetConfig[TKey],
  ) => void;
}) {
  if (editing) {
    return (
      <div className="target-list__row target-list__row--editing" role="row">
        <label className="target-check">
          <input
            type="checkbox"
            checked={target.Enabled}
            onChange={(event) => onUpdate(index, "Enabled", event.target.checked)}
            data-smoke-field={`target-enabled-${index}`}
          />
          <span>{target.Enabled ? "启用" : "停用"}</span>
        </label>
        <input
          value={target.Name}
          onChange={(event) => onUpdate(index, "Name", event.target.value)}
          data-smoke-field={`target-name-${index}`}
          aria-label="游戏名称"
        />
        <input
          value={target.ProcessName}
          onChange={(event) => onUpdate(index, "ProcessName", event.target.value)}
          data-smoke-field={`target-process-${index}`}
          aria-label="进程名"
        />
        <label className="target-check">
          <input
            type="checkbox"
            checked={target.OpenReportOnComplete}
            onChange={(event) => onUpdate(index, "OpenReportOnComplete", event.target.checked)}
            data-smoke-field={`target-report-${index}`}
          />
          <span>{target.OpenReportOnComplete ? "自动" : "手动"}</span>
        </label>
        <div className="row-actions">
          <Button icon={Save} variant="secondary" data-smoke-action={`done-target-${index}`} onClick={onDone}>
            完成
          </Button>
          <Button icon={Trash2} variant="ghost" data-smoke-action={`delete-target-${index}`} onClick={onRemove}>
            删除
          </Button>
        </div>
      </div>
    );
  }

  return (
    <div className="target-list__row target-list__row--readonly" role="row">
      <StatusPill tone={target.Enabled ? "success" : "neutral"} className="target-list__status">
        {target.Enabled ? "启用" : "停用"}
      </StatusPill>
      <div className="target-list__identity">
        <strong title={target.Name}>{target.Name || "未命名游戏"}</strong>
      </div>
      <div className="target-list__process">
        <span className="compact-field-label">进程</span>
        <span title={target.ProcessName}>{target.ProcessName || "未填写进程"}</span>
      </div>
      <div className="target-list__meta-grid">
        <span>
          <span className="compact-field-label">报告</span>
          {target.OpenReportOnComplete ? "自动打开" : "手动打开"}
        </span>
      </div>
      <div className="row-actions">
        <Button icon={Edit3} variant="secondary" data-smoke-action={`edit-target-${index}`} onClick={onEdit}>
          开始编辑
        </Button>
      </div>
    </div>
  );
}

function cloneTargets(targets: FrameScopeTargetConfig[]): FrameScopeTargetConfig[] {
  return JSON.parse(JSON.stringify(targets)) as FrameScopeTargetConfig[];
}

function serializeTargets(
  targets: FrameScopeTargetConfig[],
  dataRoot: string,
  openReportOnComplete: boolean,
) {
  return JSON.stringify({ targets, dataRoot, openReportOnComplete });
}

function targetStatusLabel(status: AsyncStatus) {
  if (status === "loading") return "加载中";
  if (status === "success") return "已同步";
  if (status === "error") return "加载失败";
  return "未加载";
}

function targetSaveMessage(status: AsyncStatus, error: string, message: string) {
  if (status === "error") return error || "修改没有丢失。请检查配置文件权限后重试。";
  if (status === "loading") return "正在保存当前目标。";
  if (status === "success") return "修改已保存，下一次监控会使用新目标。";
  return message || "修改目标后点击保存修改。";
}

function processRefreshMessage(status: AsyncStatus, error: string, message: string) {
  if (status === "error") return error || "没有读取到进程。请确认游戏已启动后重试。";
  if (status === "loading") return "正在查找当前运行的进程。";
  if (status === "success") return "进程列表已刷新。";
  return message || "用于确认游戏进程名称是否填写正确。";
}

function getProcessLookupState(status: AsyncStatus, error: string, count: number) {
  if (status === "loading") {
    return {
      kind: "loading",
      tone: "busy",
      shortMessage: "正在查找...",
      help: "正在查找匹配进程，请保持游戏或启动器打开。",
      resultStatus: "正在查找匹配进程",
      emptyTitle: "正在查找匹配进程",
      emptyDescription: "查找结果会显示在这里，当前输入框和按钮保持在上方。",
    };
  }

  if (status === "error") {
    return {
      kind: "error",
      tone: "danger",
      shortMessage: "查找失败",
      help: error || "查找进程失败。请检查权限或稍后重试。",
      resultStatus: "查找进程失败",
      emptyTitle: "查找进程失败",
      emptyDescription: error || "没有读取到进程列表。输入内容已保留，可以直接重试。",
    };
  }

  if (status === "success") {
    if (count > 0) {
      return {
        kind: "results",
        tone: "success",
        shortMessage: `已找到 ${count} 项`,
        help: "找到匹配进程。点击一项可以核对进程名和窗口标题。",
        resultStatus: "找到匹配进程",
        emptyTitle: "找到匹配进程",
        emptyDescription: "点击一项可以核对进程名和窗口标题。",
      };
    }

    return {
      kind: "empty",
      tone: "neutral",
      shortMessage: "没有结果",
      help: "没有找到匹配进程。请确认游戏已启动，或换一个进程关键字。",
      resultStatus: "没有找到匹配进程",
      emptyTitle: "没有找到匹配进程",
      emptyDescription: "没有结果不是失败。请确认游戏已启动，或换一个进程关键字后再查找。",
    };
  }

  return {
    kind: "idle",
    tone: "neutral",
    shortMessage: "尚未查找",
    help: "输入关键字后点击查找进程，不会自动读取系统进程。",
    resultStatus: "尚未查找进程",
    emptyTitle: "尚未查找进程",
    emptyDescription: "输入游戏名或进程名，再点击上方的查找进程按钮。",
  };
}

function formatProcessResultSummary(count: number) {
  if (count >= 250) return `共 ${count} 项，显示在可滚动结果面板中`;
  return `共 ${count} 项结果`;
}

function buildFixtureProcesses() {
  return Array.from({ length: 250 }, (_, index) => ({
    processName: `FixtureProcess-${String(index + 1).padStart(3, "0")}.exe`,
    processId: 7000 + index,
    windowTitle: index % 6 === 0 ? `视觉夹具进程窗口 ${index + 1}` : "",
    displayText: `FixtureProcess-${index + 1}`,
  }));
}

function formatPathTail(value: string) {
  if (!value) return "-";
  const parts = value.split(/[\\/]/).filter(Boolean);
  return parts.slice(-2).join("\\") || value;
}
