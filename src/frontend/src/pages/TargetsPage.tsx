import { Plus, RefreshCw, RotateCcw, Save } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import type { FrameScopeTargetConfig } from "../bridge/contract";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import type { FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
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
          <span className="mock-ribbon">
            {bridgeState.isMockPreview ? "mock targets adapter" : "real targets.get/save"}
          </span>
          <h2>监控目标</h2>
          <p>
            目标配置来自 `targets.get`。保存只发送 editable targets、dataRoot 和 openReportOnComplete；
            不向后端传 config path，失败时保留当前表单输入。
          </p>
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
          <Button icon={Plus} variant="secondary" disabled>
            新增目标待接入
          </Button>
        </div>
      </div>

      <div className="split-grid">
        <GlassCard className="split-grid__main">
          <div className="section-title">
            <div>
              <h3>真实目标配置</h3>
              <p>表单由 `targets.get` 返回值初始化；保存失败不会清空本地草稿。</p>
            </div>
            <StatusPill tone={dirty ? "warning" : bridgeState.targets.status === "success" ? "success" : "diagnostics"}>
              {dirty ? "dirty" : bridgeState.targets.status}
            </StatusPill>
          </div>

          {bridgeState.targets.status === "error" ? (
            <InlineStatus tone="danger" title="目标读取失败" message={bridgeState.targets.error} />
          ) : bridgeState.targets.status === "loading" ? (
            <InlineStatus tone="diagnostics" title="正在读取目标" message="等待 targets.get 返回。" busy />
          ) : null}

          {draftTargets.length > 0 ? (
            <>
              <div className="target-root-editor">
                <label>
                  <span>Data root</span>
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

              <div className="target-table target-table--editable" role="table" aria-label="FrameScope target config">
                <div className="target-table__head" role="row">
                  <span>启用</span>
                  <span>游戏</span>
                  <span>进程</span>
                  <span>采样</span>
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
              title="暂无配置目标"
              description={bridgeState.targets.error || "targets.get 尚未返回目标列表。"}
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
            message={bridgeState.targetsSave.error || bridgeState.targetsSave.message}
            busy={saveBusy}
          />

          <div className="process-refresh-panel">
            <label>
              <span>进程过滤</span>
              <input
                value={query}
                onChange={(event) => setQuery(event.target.value)}
                placeholder="例如 TslGame 或 VALORANT"
                aria-label="Process refresh query"
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
              message={bridgeState.processRefresh.error || bridgeState.processRefresh.message}
              busy={refreshBusy}
            />
          </div>

          {bridgeState.processes.length > 0 ? (
            <div className="process-result-list" aria-label="Refreshed process list">
              {bridgeState.processes.map((process) => (
                <div className="process-result-row" key={`${process.processName}-${process.processId}`}>
                  <div>
                    <strong>{process.processName}</strong>
                    <small>{process.windowTitle || process.displayText || "No window title"}</small>
                  </div>
                  <span>PID {process.processId}</span>
                </div>
              ))}
            </div>
          ) : (
            <EmptyState
              icon={RefreshCw}
              title="尚未返回进程列表"
              description="点击刷新进程后，这里会显示 event.processesRefreshed 推送的真实列表或合理空状态。"
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
