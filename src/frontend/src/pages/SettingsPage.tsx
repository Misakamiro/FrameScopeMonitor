import { FolderOpen, RotateCcw, Save } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import type { FrameScopeCloseWindowBehavior, FrameScopeConfig, FrameScopeCpuTelemetryConfig, FrameScopeThemeMode } from "../bridge/contract";
import { Button } from "../components/Button";
import { EmptyState } from "../components/EmptyState";
import { GlassCard } from "../components/GlassCard";
import { InlineStatus } from "../components/InlineStatus";
import { StatusPill } from "../components/StatusPill";
import { readVisualFixtureMode } from "../data/mockPreview";
import type { AsyncStatus, FrameScopeBridgeViewState } from "../state/useFrameScopeBridgeState";
import "./pages.css";

interface SettingsPageProps {
  bridgeState: FrameScopeBridgeViewState;
}

export function SettingsPage({ bridgeState }: SettingsPageProps) {
  const loadedConfig = bridgeState.config.data?.config ?? null;
  const [draft, setDraft] = useState<FrameScopeConfig | null>(null);
  const [loadedSignature, setLoadedSignature] = useState("");
  const visualFixtureMode = readVisualFixtureMode();
  const fixtureSaving = visualFixtureMode === "saving";
  const fixtureSaved = visualFixtureMode === "saved";
  const saveStatus =
    visualFixtureMode === "failure"
      ? "error"
      : fixtureSaving
        ? "loading"
        : fixtureSaved
          ? "success"
          : bridgeState.configSave.status;
  const saveError =
    visualFixtureMode === "failure" ? "视觉状态夹具：设置保存失败，当前输入仍保留。" : bridgeState.configSave.error;
  const saveBusy = bridgeState.configSave.status === "loading" || fixtureSaving;
  const logsOpenBusy = bridgeState.logsOpenDirectory.status === "loading";
  const isSaveFailureState = saveStatus === "error";

  useEffect(() => {
    if (!loadedConfig) return;
    const nextSignature = serializeConfig(loadedConfig);
    if (isSaveFailureState && draft && serializeConfig(draft) !== nextSignature) return;
    setDraft(cloneConfig(loadedConfig));
    setLoadedSignature(nextSignature);
  }, [isSaveFailureState, loadedConfig]);

  const dirty = useMemo(() => {
    return draft ? visualFixtureMode === "dirty" || visualFixtureMode === "saving" || serializeConfig(draft) !== loadedSignature : false;
  }, [draft, loadedSignature, visualFixtureMode]);
  const smokeConfigState = getSmokeConfigState(dirty, saveStatus);

  const updateDraft = (patch: Partial<FrameScopeConfig>) => {
    setDraft((current) => (current ? { ...current, ...patch } : current));
  };

  const updateCpuTelemetry = (patch: Partial<FrameScopeCpuTelemetryConfig>) => {
    setDraft((current) =>
          current
            ? {
                ...current,
                CpuTelemetry: {
                  ...(current.CpuTelemetry ?? {}),
                  ...defaultCpuTelemetry(current.CpuTelemetry),
                  ...patch,
                } as FrameScopeCpuTelemetryConfig,
              }
            : current,
    );
  };

  const resetDraft = () => {
    if (!loadedConfig) return;
    setDraft(cloneConfig(loadedConfig));
  };

  const saveDraft = async () => {
    if (!draft || !dirty || saveBusy) return;
    const saved = await bridgeState.saveConfig(draft);
    if (saved) {
      const savedSignature = serializeConfig(saved.config);
      setLoadedSignature(savedSignature);
      setDraft(cloneConfig(saved.config));
    }
  };

  return (
    <section className="page settings-page" data-smoke-page="settings">
      <div className="page__header">
        <div>
          <h2>应用设置</h2>
          <p>调整后续监控和报告行为。</p>
        </div>
        <div className="page__actions">
          <Button
            icon={Save}
            variant={dirty ? "primary" : "secondary"}
            disabled={!dirty || saveBusy || !draft}
            data-smoke-action="save-config"
            onClick={() => void saveDraft()}
          >
            {saveBusy ? "正在保存" : "保存修改"}
          </Button>
          {saveStatus === "error" ? (
            <div className="action-feedback action-feedback--danger settings-save-error" role="alert">
              <span>{settingsSaveMessage("error", saveError, bridgeState.configSave.message)}</span>
              <button type="button" onClick={() => void saveDraft()} disabled={!dirty || saveBusy || !draft}>
                重试保存
              </button>
            </div>
          ) : null}
        </div>
      </div>

      {bridgeState.config.status === "error" ? (
        <InlineStatus
          tone="danger"
          title="设置读取失败"
          message={bridgeState.config.error || "请重新打开应用或检查配置文件权限。"}
        />
      ) : bridgeState.config.status === "loading" ? (
        <InlineStatus tone="diagnostics" title="正在读取设置" message="正在加载已保存的应用设置。" busy />
      ) : null}

      {draft ? (
        <div className="settings-page-grid">
          <div className="settings-groups">
            <SettingsGroup
              title="外观与窗口行为"
              description="控制界面主题和主窗口关闭时的处理方式。"
              status={themeModeLabel(draft.ThemeMode)}
              tone="neutral"
            >
              <SegmentedControl<FrameScopeThemeMode>
                label="主题"
                value={draft.ThemeMode}
                options={[
                  { value: "light", label: "浅色" },
                  { value: "dark", label: "深色" },
                  { value: "system", label: "跟随系统" },
                ]}
                onChange={(ThemeMode) => updateDraft({ ThemeMode })}
              />
              <SegmentedControl<FrameScopeCloseWindowBehavior>
                label="关闭窗口"
                value={draft.CloseWindowBehavior}
                options={[
                  { value: "exit", label: "直接退出" },
                  { value: "minimize-to-tray", label: "退出到托盘" },
                ]}
                onChange={(CloseWindowBehavior) => updateDraft({ CloseWindowBehavior })}
              />
              <ToggleRow
                label="托盘入口"
                helper="本轮只保存配置，不改变主程序托盘行为。"
                checked={draft.TrayEnabled}
                onChange={(TrayEnabled) => updateDraft({ TrayEnabled })}
              />
            </SettingsGroup>

            <SettingsGroup
              title="数据与报告"
              description="决定报告保存和打开方式。"
              status={dirty ? "有未保存修改" : "没有未保存修改"}
              tone={dirty ? "warning" : "success"}
            >
              <PathControl
                label="数据保存位置"
                value={draft.DataRoot}
                onChange={(value) => updateDraft({ DataRoot: value })}
              />
              <ToggleRow
                label="报告生成后自动打开"
                helper="完成监控后直接打开 HTML 报告。"
                checked={draft.OpenReportOnComplete}
                onChange={(checked) => updateDraft({ OpenReportOnComplete: checked })}
              />
              <div className="settings-static-row">
                <div>
                  <strong>文件夹入口</strong>
                  <small>具体报告的文件夹操作在报告详情里执行。</small>
                </div>
                <span>报告页</span>
              </div>
            </SettingsGroup>

            <SettingsGroup
              title="日志与诊断"
              description="排查问题时再提高记录量。"
              status={draft.EnableVerboseLogs || draft.EnablePerformanceDiagnosticsLogs ? "诊断增强" : "常规记录"}
              tone={draft.EnableVerboseLogs || draft.EnablePerformanceDiagnosticsLogs ? "warning" : "neutral"}
            >
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
              <label className="settings-control settings-control--with-unit">
                <span>最大日志大小</span>
                <div>
                  <input
                    type="number"
                    min={1}
                    max={2048}
                    value={draft.MaxLogDiskMb}
                    onChange={(event) => updateDraft({ MaxLogDiskMb: normalizeNumber(event.target.value, 100) })}
                  />
                  <em>MB</em>
                </div>
              </label>
              <ToggleRow
                label="详细日志"
                helper="只有排查问题时建议开启。"
                checked={draft.EnableVerboseLogs}
                onChange={(checked) => updateDraft({ EnableVerboseLogs: checked })}
              />
              <ToggleRow
                label="性能诊断日志"
                helper="记录更多诊断信息。"
                checked={draft.EnablePerformanceDiagnosticsLogs}
                onChange={(checked) => updateDraft({ EnablePerformanceDiagnosticsLogs: checked })}
              />
              <ToggleRow
                label="自动生成诊断文件"
                helper="报告异常时保留更多线索。"
                checked={draft.AutoGenerateDiagnosticReport}
                onChange={(checked) => updateDraft({ AutoGenerateDiagnosticReport: checked })}
              />
              <div className="settings-inline-action">
                <div>
                  <strong>日志目录</strong>
                  <small>由主程序解析当前日志位置，不从前端接收路径。</small>
                </div>
                <Button
                  icon={FolderOpen}
                  variant="secondary"
                  disabled={logsOpenBusy}
                  data-smoke-action="open-logs-directory"
                  onClick={() => void bridgeState.openLogsDirectory()}
                >
                  {logsOpenBusy ? "正在打开" : "打开日志目录"}
                </Button>
              </div>
              {bridgeState.logsOpenDirectory.status === "success" ? (
                <InlineStatus
                  tone="success"
                  title="日志目录已打开"
                  message={bridgeState.logsOpenDirectory.message}
                />
              ) : bridgeState.logsOpenDirectory.status === "error" ? (
                <InlineStatus
                  tone="danger"
                  title="日志目录打开失败"
                  message={bridgeState.logsOpenDirectory.error || bridgeState.logsOpenDirectory.message}
                />
              ) : null}
            </SettingsGroup>

            <SettingsGroup
              title="采样间隔"
              description="控制后台进程、系统和硬件 telemetry 的记录间隔；FPS 原始帧统计继续按真实帧数据计算。"
              status={`${samplingInterval(draft)} ms`}
              tone="neutral"
            >
              <IntervalControl
                label="全局采样间隔"
                value={samplingInterval(draft)}
                min={500}
                max={5000}
                smokeField="global-telemetry-sample-interval"
                onChange={(TelemetrySampleIntervalMs) => updateDraft({ TelemetrySampleIntervalMs })}
              />
              <ToggleRow
                label="CPU 核心频率采集"
                helper="记录每个逻辑处理器的 Actual Frequency。"
                checked={defaultCpuTelemetry(draft.CpuTelemetry).CollectPerCoreFrequency}
                onChange={(CollectPerCoreFrequency) => updateCpuTelemetry({ CollectPerCoreFrequency })}
              />
              <div className="settings-static-row">
                <div>
                  <strong>CPU Core VID</strong>
                  <small>使用全局采样间隔记录 CPU 请求/目标电压；不是真实主板实测 Vcore。</small>
                </div>
                <span>自动记录</span>
              </div>
            </SettingsGroup>
          </div>

          <aside className="settings-side-panel">
            <GlassCard>
              <InlineStatus
                tone={
                  saveStatus === "error"
                    ? "danger"
                    : saveStatus === "success"
                      ? "success"
                      : dirty
                        ? "warning"
                        : "diagnostics"
                }
                title={
                  saveStatus === "loading"
                    ? "正在保存"
                    : saveStatus === "success"
                      ? "保存成功"
                      : saveStatus === "error"
                        ? "保存失败"
                        : dirty
                          ? "有未保存修改"
                          : "没有未保存修改"
                }
                message={settingsSaveMessage(saveStatus, saveError, bridgeState.configSave.message)}
                busy={saveBusy}
              />
              <div className="side-action-row">
                <Button icon={RotateCcw} variant="secondary" disabled={!dirty || saveBusy} onClick={resetDraft}>
                  撤销
                </Button>
              </div>
              {smokeConfigState ? (
                <span className="sr-only" data-smoke-state="settings-config">
                  {smokeConfigState}
                </span>
              ) : null}

              <div className="settings-note">
                <h3>当前摘要</h3>
                <div className="snapshot-grid">
                  <SnapshotLine label="保存位置" value={bridgeState.config.data?.resolvedDataRoot ?? draft.DataRoot} />
                  <SnapshotLine label="目标总数" value={String(bridgeState.config.data?.targetCount ?? 0)} />
                  <SnapshotLine label="启用目标" value={String(bridgeState.config.data?.enabledTargetCount ?? 0)} />
                  <SnapshotLine label="自动打开报告" value={draft.OpenReportOnComplete ? "开启" : "关闭"} />
                  <SnapshotLine label="主题" value={themeModeLabel(draft.ThemeMode)} />
                  <SnapshotLine label="关闭窗口" value={closeWindowBehaviorLabel(draft.CloseWindowBehavior)} />
                </div>
              </div>
            </GlassCard>
          </aside>
        </div>
      ) : (
        <EmptyState
          icon={Save}
          title="还没有设置"
          description="设置读取完成后才能修改。读取失败时，请先检查配置文件权限。"
          actionLabel="等待设置读取"
        />
      )}
    </section>
  );
}

function PathControl({
  label,
  value,
  onChange,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
}) {
  const preview = formatPathPreview(value);

  return (
    <label className="settings-control settings-control--wide path-control">
      <span>{label}</span>
      <div className="path-control__preview" title={value}>
        <strong>{preview.root}</strong>
        <span>{preview.tail}</span>
      </div>
      <input
        data-smoke-field="data-root"
        value={value}
        title={value}
        spellCheck={false}
        onChange={(event) => onChange(event.target.value)}
      />
      <small title={value}>聚焦输入框可横向查看和编辑完整路径。</small>
    </label>
  );
}

function IntervalControl({
  label,
  value,
  min,
  max,
  smokeField,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  smokeField: string;
  onChange: (value: number) => void;
}) {
  return (
    <label className="settings-control settings-control--with-unit">
      <span>{label}</span>
      <div>
        <input
          type="number"
          min={min}
          max={max}
          step={100}
          value={value}
          data-smoke-field={smokeField}
          onChange={(event) => onChange(normalizeNumber(event.target.value, 1000))}
        />
        <em>ms</em>
      </div>
      <small>默认 1000 ms；可设置范围 500-5000 ms。数值越小刷新越密集，占用越高。</small>
    </label>
  );
}

function SettingsGroup({
  title,
  description,
  status,
  tone,
  children,
}: {
  title: string;
  description: string;
  status: string;
  tone: "neutral" | "success" | "warning";
  children: React.ReactNode;
}) {
  return (
    <GlassCard className="settings-group">
      <div className="section-title">
        <div>
          <h3>{title}</h3>
          <p>{description}</p>
        </div>
        <StatusPill tone={tone}>{status}</StatusPill>
      </div>
      <div className="settings-form settings-form--grouped">{children}</div>
    </GlassCard>
  );
}

function ToggleRow({
  label,
  helper,
  checked,
  onChange,
}: {
  label: string;
  helper: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label className="settings-control settings-control--toggle">
      <span>{label}</span>
      <small>{helper}</small>
      <input type="checkbox" checked={checked} onChange={(event) => onChange(event.target.checked)} />
    </label>
  );
}

function SegmentedControl<TValue extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: TValue;
  options: Array<{ value: TValue; label: string }>;
  onChange: (value: TValue) => void;
}) {
  return (
    <div className="settings-control settings-control--wide">
      <span>{label}</span>
      <div className="segmented-control" role="radiogroup" aria-label={label}>
        {options.map((option) => (
          <button
            key={option.value}
            type="button"
            role="radio"
            aria-checked={value === option.value}
            className={value === option.value ? "segmented-control__item segmented-control__item--active" : "segmented-control__item"}
            onClick={() => onChange(option.value)}
          >
            {option.label}
          </button>
        ))}
      </div>
    </div>
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

function defaultCpuTelemetry(value: FrameScopeCpuTelemetryConfig | null | undefined) {
  return {
    CollectPerCoreFrequency: value?.CollectPerCoreFrequency ?? true,
    PerCoreSampleIntervalMs: value?.PerCoreSampleIntervalMs ?? 1000,
  };
}

function samplingInterval(config: FrameScopeConfig) {
  return config.TelemetrySampleIntervalMs ?? 1000;
}

function cloneConfig(config: FrameScopeConfig): FrameScopeConfig {
  return JSON.parse(JSON.stringify(config)) as FrameScopeConfig;
}

function serializeConfig(config: FrameScopeConfig) {
  return JSON.stringify(config);
}

function settingsSaveMessage(status: AsyncStatus, error: string, message: string) {
  if (status === "error") return error || "修改没有丢失。请检查路径或权限后重试。";
  if (status === "loading") return "正在保存当前设置。";
  if (status === "success") return "修改已保存，下一次监控会使用新设置。";
  return message || "修改字段后点击保存修改。";
}

function themeModeLabel(themeMode: FrameScopeThemeMode) {
  if (themeMode === "light") return "浅色";
  if (themeMode === "dark") return "深色";
  return "跟随系统";
}

function closeWindowBehaviorLabel(closeWindowBehavior: FrameScopeCloseWindowBehavior) {
  return closeWindowBehavior === "exit" ? "直接退出" : "退出到托盘";
}

function getSmokeConfigState(dirty: boolean, saveStatus: AsyncStatus) {
  if (saveStatus === "loading") return "Saving FrameScope config.";
  if (saveStatus === "success") return "Config saved.";
  return dirty ? "dirty" : "";
}

function formatPathTail(value: string) {
  if (!value) return "-";
  const parts = value.split(/[\\/]/).filter(Boolean);
  return parts.slice(-3).join("\\") || value;
}

function formatPathPreview(value: string) {
  if (!value) return { root: "-", tail: "未设置" };
  const normalized = value.replace(/\//g, "\\");
  const parts = normalized.split("\\").filter(Boolean);
  if (parts.length <= 3) return { root: parts[0] || normalized, tail: parts.slice(1).join("\\") || normalized };
  return {
    root: parts[0],
    tail: `...\\${parts.slice(-3).join("\\")}`,
  };
}
