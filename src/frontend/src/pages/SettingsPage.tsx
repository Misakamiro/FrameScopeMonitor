import { RotateCcw, Save } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import type { FrameScopeConfig } from "../bridge/contract";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import type { AsyncStatus, FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
import "./pages.css";

interface SettingsPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

export function SettingsPage({ bridgeState }: SettingsPageProps) {
  const loadedConfig = bridgeState.config.data?.config ?? null;
  const [draft, setDraft] = useState<FrameScopeConfig | null>(null);
  const [loadedSignature, setLoadedSignature] = useState("");
  const saveBusy = bridgeState.configSave.status === "loading";

  useEffect(() => {
    if (!loadedConfig) return;
    const nextSignature = serializeConfig(loadedConfig);
    setDraft(cloneConfig(loadedConfig));
    setLoadedSignature(nextSignature);
  }, [loadedConfig]);

  const dirty = useMemo(() => {
    return draft ? serializeConfig(draft) !== loadedSignature : false;
  }, [draft, loadedSignature]);
  const smokeConfigState = getSmokeConfigState(dirty, bridgeState.configSave.status);

  const updateDraft = (patch: Partial<FrameScopeConfig>) => {
    setDraft((current) => (current ? { ...current, ...patch } : current));
  };

  const resetDraft = () => {
    if (!loadedConfig) return;
    setDraft(cloneConfig(loadedConfig));
  };

  const saveDraft = async () => {
    if (!draft || !dirty || saveBusy) return;
    const saved = await bridgeState.saveConfig(draft);
    if (saved) {
      setLoadedSignature(serializeConfig(saved.config));
      setDraft(cloneConfig(saved.config));
    }
  };

  return (
    <section className="page settings-page" data-smoke-page="settings">
      <div className="page__header">
        <div>
          <span className="mode-ribbon">{bridgeState.isMockPreview ? "界面预览" : "本机数据"}</span>
          <h2>应用设置</h2>
          <p>调整数据保存位置、刷新间隔、日志保留和诊断开关。修改后需要点击保存设置才会生效。</p>
        </div>
        <div className="page__actions">
          <Button icon={RotateCcw} variant="secondary" disabled={!dirty || saveBusy} onClick={resetDraft}>
            撤销本地修改
          </Button>
          <Button
            icon={Save}
            variant="primary"
            disabled={!dirty || saveBusy || !draft}
            data-smoke-action="save-config"
            onClick={() => void saveDraft()}
          >
            {saveBusy ? "保存中" : dirty ? "保存设置" : "无修改"}
          </Button>
        </div>
      </div>

      <div className="settings-grid">
        <GlassCard>
          <div className="section-title">
            <div>
              <h3>基础配置</h3>
              <p>这些设置会影响后续监控和报告生成，不会改变已经生成的历史报告。</p>
            </div>
            <StatusPill tone={dirty ? "warning" : "success"}>{dirty ? "未保存" : "已同步"}</StatusPill>
          </div>

          {bridgeState.config.status === "error" ? (
            <InlineStatus tone="danger" title="配置读取失败" message={bridgeState.config.error} />
          ) : bridgeState.config.status === "loading" ? (
            <InlineStatus tone="diagnostics" title="正在读取设置" message="正在加载已保存的应用设置。" busy />
          ) : null}

          {draft ? (
            <div className="settings-form">
              <label className="settings-control settings-control--wide">
                <span>数据目录</span>
                <input
                  data-smoke-field="data-root"
                  value={draft.DataRoot}
                  onChange={(event) => updateDraft({ DataRoot: event.target.value })}
                />
              </label>
              <label className="settings-control">
                <span>状态刷新间隔(ms)</span>
                <input
                  type="number"
                  min={100}
                  data-smoke-field="poll-interval"
                  value={draft.PollIntervalMs}
                  onChange={(event) => updateDraft({ PollIntervalMs: normalizeNumber(event.target.value, 1000) })}
                />
              </label>
              <label className="settings-control">
                <span>日志保留天数</span>
                <input
                  type="number"
                  min={1}
                  max={365}
                  value={draft.LogRetentionDays}
                  onChange={(event) => updateDraft({ LogRetentionDays: normalizeNumber(event.target.value, 14) })}
                />
              </label>
              <label className="settings-control">
                <span>最大日志大小(MB)</span>
                <input
                  type="number"
                  min={1}
                  max={2048}
                  value={draft.MaxLogDiskMb}
                  onChange={(event) => updateDraft({ MaxLogDiskMb: normalizeNumber(event.target.value, 100) })}
                />
              </label>
              <ToggleRow
                label="报告完成后自动打开"
                checked={draft.OpenReportOnComplete}
                onChange={(checked) => updateDraft({ OpenReportOnComplete: checked })}
              />
              <ToggleRow
                label="详细日志"
                checked={draft.EnableVerboseLogs}
                onChange={(checked) => updateDraft({ EnableVerboseLogs: checked })}
              />
              <ToggleRow
                label="记录性能诊断日志"
                checked={draft.EnablePerformanceDiagnosticsLogs}
                onChange={(checked) => updateDraft({ EnablePerformanceDiagnosticsLogs: checked })}
              />
              <ToggleRow
                label="自动生成诊断报告"
                checked={draft.AutoGenerateDiagnosticReport}
                onChange={(checked) => updateDraft({ AutoGenerateDiagnosticReport: checked })}
              />
            </div>
          ) : (
            <EmptyState
              icon={Save}
              title="暂无可编辑配置"
              description="还没有读取到设置，或读取设置失败。"
              actionLabel="等待设置"
            />
          )}
        </GlassCard>

        <GlassCard>
          <InlineStatus
            tone={
              bridgeState.configSave.status === "error"
                ? "danger"
                : bridgeState.configSave.status === "success"
                  ? "success"
                  : dirty
                    ? "warning"
                    : "diagnostics"
            }
            title={
              bridgeState.configSave.status === "loading"
                ? "正在保存"
                : bridgeState.configSave.status === "success"
                  ? "保存成功"
                  : bridgeState.configSave.status === "error"
                    ? "保存失败"
                    : dirty
                      ? "有未保存修改"
                      : "配置已同步"
            }
            message={settingsSaveMessage(bridgeState.configSave.status, bridgeState.configSave.error, bridgeState.configSave.message)}
            busy={saveBusy}
          />
          {smokeConfigState ? (
            <span className="sr-only" data-smoke-state="settings-config">
              {smokeConfigState}
            </span>
          ) : null}

          <div className="settings-note">
            <h3>当前设置摘要</h3>
            <div className="snapshot-grid">
              <SnapshotLine label="保存位置" value={bridgeState.config.data?.resolvedDataRoot ?? "-"} />
              <SnapshotLine label="目标总数" value={String(bridgeState.config.data?.targetCount ?? 0)} />
              <SnapshotLine label="启用目标" value={String(bridgeState.config.data?.enabledTargetCount ?? 0)} />
              <SnapshotLine label="自动打开报告" value={draft?.OpenReportOnComplete ? "开启" : "关闭"} />
            </div>
          </div>
        </GlassCard>
      </div>
    </section>
  );
}

function ToggleRow({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label className="settings-control settings-control--toggle">
      <span>{label}</span>
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
    </label>
  );
}

function SnapshotLine({ label, value }: { label: string; value: string }) {
  const valueClassName = value.length > 28 ? "snapshot-item__value--nowrap" : undefined;

  return (
    <div className="snapshot-item">
      <span>{label}</span>
      <strong className={valueClassName} title={value}>
        {value}
      </strong>
    </div>
  );
}

function normalizeNumber(value: string, fallback: number) {
  const next = Number.parseInt(value, 10);
  return Number.isFinite(next) ? next : fallback;
}

function cloneConfig(config: FrameScopeConfig): FrameScopeConfig {
  return JSON.parse(JSON.stringify(config)) as FrameScopeConfig;
}

function serializeConfig(config: FrameScopeConfig) {
  return JSON.stringify(config);
}

function settingsSaveMessage(status: AsyncStatus, error: string, message: string) {
  if (status === "error") return error || "保存失败，请检查设置后重试。";
  if (status === "loading") return "正在保存当前设置。";
  if (status === "success") return "设置已保存。";
  return message || "修改字段后，点击保存设置写入配置。";
}

function getSmokeConfigState(dirty: boolean, saveStatus: AsyncStatus) {
  if (saveStatus === "loading") return "Saving FrameScope config.";
  if (saveStatus === "success") return "Config saved.";
  return dirty ? "dirty" : "";
}
