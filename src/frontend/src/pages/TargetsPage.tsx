import { RefreshCw, RotateCcw, Save } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import type { FrameScopeTargetConfig } from "../bridge/contract";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import type { AsyncStatus, FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
import "./pages.css";

interface TargetsPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

export function TargetsPage({ bridgeState }: TargetsPageProps) {
  const [query, setQuery] = useState("");
  const [draftTargets, setDraftTargets] = useState<FrameScopeTargetConfig[]>([]);
  const [draftDataRoot, setDraftDataRoot] = useState("");
  const [draftOpenReportOnComplete, setDraftOpenReportOnComplete] = useState(false);
  const [loadedSignature, setLoadedSignature] = useState("");
  const loadedTargetsPayload = bridgeState.targets.data;
  const refreshBusy = bridgeState.processRefresh.status === "loading";
  const saveBusy = bridgeState.targetsSave.status === "loading";
  const smokeProcessRefreshState = bridgeState.processRefresh.status === "success" ? "Process refresh completed" : "";

  useEffect(() => {
    if (!loadedTargetsPayload) return;
    const nextTargets = cloneTargets(loadedTargetsPayload.targets);
    const nextDataRoot = loadedTargetsPayload.dataRoot;
    const nextOpenReport = loadedTargetsPayload.openReportOnComplete;
    setDraftTargets(nextTargets);
    setDraftDataRoot(nextDataRoot);
    setDraftOpenReportOnComplete(nextOpenReport);
    setLoadedSignature(serializeTargets(nextTargets, nextDataRoot, nextOpenReport));
  }, [loadedTargetsPayload]);

  const dirty = useMemo(() => {
    if (!loadedSignature) return false;
    return serializeTargets(draftTargets, draftDataRoot, draftOpenReportOnComplete) !== loadedSignature;
  }, [draftDataRoot, draftOpenReportOnComplete, draftTargets, loadedSignature]);

  const updateTarget = <TKey extends keyof FrameScopeTargetConfig>(
    index: number,
    key: TKey,
    value: FrameScopeTargetConfig[TKey],
  ) => {
    setDraftTargets((current) =>
      current.map((target, targetIndex) =>
        targetIndex === index
          ? {
              ...target,
              [key]: value,
            }
          : target,
      ),
    );
  };

  const resetDraft = () => {
    if (!loadedTargetsPayload) return;
    const nextTargets = cloneTargets(loadedTargetsPayload.targets);
    setDraftTargets(nextTargets);
    setDraftDataRoot(loadedTargetsPayload.dataRoot);
    setDraftOpenReportOnComplete(loadedTargetsPayload.openReportOnComplete);
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
  };

  return (
    <section className="page targets-page" data-smoke-page="targets">
      <div className="page__header">
        <div>
          <span className="mode-ribbon">{bridgeState.isMockPreview ? "界面预览" : "本机数据"}</span>
          <h2>监控目标</h2>
          <p>选择需要监控的游戏进程，设置采样间隔和报告打开方式。保存失败时，当前输入会保留在页面上。</p>
        </div>
        <div className="page__actions">
          <Button
            icon={RefreshCw}
            variant="secondary"
            disabled={bridgeState.targets.status === "loading" || saveBusy}
            data-smoke-action="refresh-targets"
            onClick={() => void bridgeState.refreshTargets()}
          >
            {bridgeState.targets.status === "loading" ? "读取中" : "读取目标"}
          </Button>
          <Button icon={RotateCcw} variant="secondary" disabled={!dirty || saveBusy} onClick={resetDraft}>
            撤销修改
          </Button>
          <Button
            icon={Save}
            variant="primary"
            disabled={!dirty || saveBusy || draftTargets.length === 0}
            data-smoke-action="save-targets"
            onClick={() => void saveDraft()}
          >
            {saveBusy ? "保存中" : dirty ? "保存目标" : "无修改"}
          </Button>
        </div>
      </div>

      <div className="split-grid">
        <GlassCard className="split-grid__main">
          <div className="section-title">
            <div>
              <h3>目标列表</h3>
              <p>启用开关决定启动监控时是否采样；进程名需要和系统里的进程一致。</p>
            </div>
            <StatusPill tone={dirty ? "warning" : bridgeState.targets.status === "success" ? "success" : "diagnostics"}>
              {dirty ? "未保存" : targetStatusLabel(bridgeState.targets.status)}
            </StatusPill>
          </div>

          {bridgeState.targets.status === "error" ? (
            <InlineStatus tone="danger" title="目标读取失败" message={bridgeState.targets.error} />
          ) : bridgeState.targets.status === "loading" ? (
            <InlineStatus tone="diagnostics" title="正在读取目标" message="正在加载已保存的目标配置。" busy />
          ) : null}

          {draftTargets.length > 0 ? (
            <>
              <div className="target-root-editor">
                <label>
                  <span>数据保存位置</span>
                  <input
                    value={draftDataRoot}
                    onChange={(event) => setDraftDataRoot(event.target.value)}
                    data-smoke-field="targets-data-root"
                  />
                </label>
                <label className="settings-control--toggle">
                  <span>报告完成后自动打开</span>
                  <input
                    type="checkbox"
                    checked={draftOpenReportOnComplete}
                    onChange={(event) => setDraftOpenReportOnComplete(event.target.checked)}
                    data-smoke-field="targets-open-report"
                  />
                </label>
              </div>

              <div className="target-table target-table--editable" role="table" aria-label="监控目标配置">
                <div className="target-table__head" role="row">
                  <span>启用</span>
                  <span>游戏</span>
                  <span>进程</span>
                  <span>间隔(ms)</span>
                  <span>报告</span>
                </div>
                {draftTargets.map((target, index) => (
                  <div className="target-table__row" role="row" key={`${target.Name}-${target.ProcessName}-${index}`}>
                    <label className="target-check">
                      <input
                        type="checkbox"
                        checked={target.Enabled}
                        onChange={(event) => updateTarget(index, "Enabled", event.target.checked)}
                        data-smoke-field={`target-enabled-${index}`}
                      />
                      <span>{target.Enabled ? "启用" : "停用"}</span>
                    </label>
                    <input
                      value={target.Name}
                      onChange={(event) => updateTarget(index, "Name", event.target.value)}
                      data-smoke-field={`target-name-${index}`}
                    />
                    <input
                      value={target.ProcessName}
                      onChange={(event) => updateTarget(index, "ProcessName", event.target.value)}
                      data-smoke-field={`target-process-${index}`}
                    />
                    <input
                      type="number"
                      min={50}
                      value={target.SampleIntervalMs}
                      onChange={(event) => {
                        const next = normalizeNumber(event.target.value, target.SampleIntervalMs);
                        updateTarget(index, "SampleIntervalMs", next);
                        updateTarget(index, "ProcessSampleIntervalMs", Math.max(100, next));
                      }}
                      data-smoke-field={`target-sample-${index}`}
                    />
                    <label className="target-check">
                      <input
                        type="checkbox"
                        checked={target.OpenReportOnComplete}
                        onChange={(event) => updateTarget(index, "OpenReportOnComplete", event.target.checked)}
                        data-smoke-field={`target-report-${index}`}
                      />
                      <span>{target.OpenReportOnComplete ? "自动" : "手动"}</span>
                    </label>
                  </div>
                ))}
              </div>
            </>
          ) : (
            <EmptyState
              icon={Save}
              title="暂无目标"
              description={bridgeState.targets.error || "点击重新加载后，这里会显示当前保存的监控目标。"}
              actionLabel="等待目标"
            />
          )}
        </GlassCard>

        <GlassCard>
          <InlineStatus
            tone={
              bridgeState.targetsSave.status === "error"
                ? "danger"
                : bridgeState.targetsSave.status === "success"
                  ? "success"
                  : dirty
                    ? "warning"
                    : "diagnostics"
            }
            title={
              bridgeState.targetsSave.status === "loading"
                ? "正在保存目标"
                : bridgeState.targetsSave.status === "success"
                  ? "目标保存成功"
                  : bridgeState.targetsSave.status === "error"
                    ? "目标保存失败"
                    : dirty
                      ? "有未保存目标修改"
                      : "目标配置已同步"
            }
            message={targetSaveMessage(bridgeState.targetsSave.status, bridgeState.targetsSave.error, bridgeState.targetsSave.message)}
            busy={saveBusy}
          />

          <div className="process-refresh-panel">
            <label>
              <span>进程过滤</span>
              <input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="例如 TslGame 或 VALORANT"
                aria-label="进程过滤关键字"
              />
            </label>
            <Button
              icon={RefreshCw}
              variant="secondary"
              disabled={refreshBusy}
              data-smoke-action="refresh-processes"
              onClick={() => void bridgeState.refreshProcesses(query)}
            >
              {refreshBusy ? "刷新中" : "刷新进程"}
            </Button>
            <InlineStatus
              tone={
                bridgeState.processRefresh.status === "error"
                  ? "danger"
                  : bridgeState.processRefresh.status === "success"
                    ? "success"
                    : "diagnostics"
              }
              title="刷新进程状态"
              message={processRefreshMessage(
                bridgeState.processRefresh.status,
                bridgeState.processRefresh.error,
                bridgeState.processRefresh.message,
              )}
              busy={refreshBusy}
            />
            {smokeProcessRefreshState ? (
              <span className="sr-only" data-smoke-state="process-refresh">
                {smokeProcessRefreshState}
              </span>
            ) : null}
          </div>

          {bridgeState.processes.length > 0 ? (
            <div className="process-result-list" aria-label="刷新后的进程列表">
              {bridgeState.processes.map((process) => (
                <div className="process-result-row" key={`${process.processName}-${process.processId}`}>
                  <div>
                    <strong>{process.processName}</strong>
                    <small>{process.windowTitle || process.displayText || "无窗口标题"}</small>
                  </div>
                  <span>PID {process.processId}</span>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState
              icon={RefreshCw}
              title="尚未返回进程列表"
              description="点击刷新进程后，这里会显示匹配到的进程。空列表表示暂时没有找到。"
              actionLabel="非阻塞刷新"
            />
          )}
        </GlassCard>
      </div>
    </section>
  );
}

function normalizeNumber(value: string, fallback: number) {
  const next = Number.parseInt(value, 10);
  return Number.isFinite(next) ? next : fallback;
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
  if (status === "loading") return "读取中";
  if (status === "success") return "已同步";
  if (status === "error") return "读取失败";
  return "未读取";
}

function targetSaveMessage(status: AsyncStatus, error: string, message: string) {
  if (status === "error") return error || "保存失败，请检查目标配置后重试。";
  if (status === "loading") return "正在保存当前目标设置。";
  if (status === "success") return "目标设置已保存。";
  return message || "修改目标后，点击保存目标写入配置。";
}

function processRefreshMessage(status: AsyncStatus, error: string, message: string) {
  if (status === "error") return error || "进程刷新失败，请稍后重试。";
  if (status === "loading") return "正在读取当前运行的进程。";
  if (status === "success") return "进程列表已刷新。";
  return message || "用于确认游戏进程名称是否填写正确。";
}
