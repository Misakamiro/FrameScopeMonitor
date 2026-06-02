import { describe, expect, it } from "vitest";
import glassCardSource from "./components/GlassCard.tsx?raw";
import overviewPageSource from "./pages/OverviewPage.tsx?raw";
import reportsPageSource from "./pages/ReportsPage.tsx?raw";
import settingsPageSource from "./pages/SettingsPage.tsx?raw";
import targetsPageSource from "./pages/TargetsPage.tsx?raw";
import bridgeStateSource from "./state/useFrameScopeBridgeState.ts?raw";
import bridgeContractSource from "./bridge/contract.ts?raw";
import mockPreviewSource from "./data/mockPreview.ts?raw";

describe("FrameScope UI interaction contract", () => {
  it("keeps WebView2 smoke state probes stable without visible English controls", () => {
    expect(targetsPageSource).toContain("Process refresh completed");
    expect(settingsPageSource).toContain("Saving FrameScope config.");
    expect(settingsPageSource).toContain("Config saved.");
    expect(settingsPageSource).toContain("data-smoke-state");
  });

  it("exposes Settings log-directory opening through a host-owned bridge action", () => {
    expect(bridgeContractSource).toContain('"logs.openDirectory"');
    expect(bridgeStateSource).toContain('adapter.request<LogsOpenDirectoryPayload>("logs.openDirectory", {}');
    expect(settingsPageSource).toContain("打开日志目录");
    expect(settingsPageSource).toContain('data-smoke-action="open-logs-directory"');
    expect(settingsPageSource).toContain("bridgeState.openLogsDirectory()");
    expect(settingsPageSource).not.toContain("openLogsDirectory(" + "draft.DataRoot");
    expect(mockPreviewSource).toContain('type === "logs.openDirectory"');
    expect(mockPreviewSource).toContain("payloadHasPathAuthority(payload)");
  });

  it("keeps the dirty Settings draft after save failure instead of reloading over user input", () => {
    expect(settingsPageSource).toContain("isSaveFailureState");
    expect(settingsPageSource).toContain("saveStatus === \"error\"");
    expect(settingsPageSource).toContain("if (isSaveFailureState && draft && serializeConfig(draft) !== nextSignature) return;");
  });

  it("exposes one global telemetry sampling interval in Settings", () => {
    expect(settingsPageSource).toContain('smokeField="global-telemetry-sample-interval"');
    expect(settingsPageSource).toContain("TelemetrySampleIntervalMs");
    expect(settingsPageSource).toContain("500");
    expect(settingsPageSource).toContain("5000");
    expect(settingsPageSource).not.toContain('smokeField="process-sample-interval"');
    expect(settingsPageSource).not.toContain('smokeField="slow-sample-interval"');
    expect(settingsPageSource).not.toContain('smokeField="cpu-core-sample-interval"');
    expect(settingsPageSource).not.toContain('smokeField="cpu-voltage-sample-interval"');
  });

  it("does not expose per-target sampling edits in Targets", () => {
    expect(targetsPageSource).not.toContain('data-smoke-field={`target-sample-${index}`}');
    expect(targetsPageSource).toContain('data-smoke-action="add-target"');
    expect(targetsPageSource).toContain("data-smoke-action={`edit-target-${index}`}");
    expect(targetsPageSource).toContain("data-smoke-action={`done-target-${index}`}");
    expect(targetsPageSource).toContain("data-smoke-action={`delete-target-${index}`}");
    expect(targetsPageSource).not.toContain("SampleIntervalMs}");
    expect(targetsPageSource).not.toContain("采样 100");
  });

  it("allows saving an empty Targets draft after deleting the last target", () => {
    expect(targetsPageSource).not.toContain("draftTargets.length === 0");
    expect(targetsPageSource).toMatch(/disabled=\{!dirty \|\| saveBusy\}/);
  });

  it("uses thresholded window rendering for large process results only", () => {
    expect(targetsPageSource).toContain("PROCESS_RESULT_WINDOW_THRESHOLD = 250");
    expect(targetsPageSource).toContain("getVirtualListWindow");
    expect(targetsPageSource).toContain('data-windowed={shouldWindowProcesses ? "true" : "false"}');
    expect(targetsPageSource).toContain("process-result-list__spacer");
  });

  it("uses internal state snapshot refresh cadence instead of a user setting", () => {
    expect(bridgeStateSource).toContain("VISIBLE_STATE_SNAPSHOT_INTERVAL_MS = 1000");
    expect(bridgeStateSource).toContain("HIDDEN_OR_TRAY_STATE_SNAPSHOT_INTERVAL_MS = 3000");
    expect(bridgeStateSource).toContain("IMMEDIATE_REFRESH_COALESCE_MS = 200");
    expect(bridgeStateSource).toContain("state.snapshot");
    expect(bridgeStateSource).toContain("scheduleImmediateSnapshotRefresh");
    expect(bridgeStateSource).toContain('adapter.subscribe<HostWindowChangedEventPayload>("event.hostWindowChanged"');
    expect(bridgeStateSource).toContain("payload.visible === false || payload.inTray === true");
    expect(bridgeStateSource).not.toContain("PollIntervalMs");
  });

  it("actively refreshes status after user operations and report completion", () => {
    expect(bridgeStateSource).toContain("scheduleImmediateSnapshotRefresh();");
    expect(bridgeStateSource).toMatch(/const startMonitor[\s\S]*scheduleImmediateSnapshotRefresh\(\);/);
    expect(bridgeStateSource).toMatch(/const stopMonitor[\s\S]*scheduleImmediateSnapshotRefresh\(\);/);
    expect(bridgeStateSource).toMatch(/const saveConfig[\s\S]*scheduleImmediateSnapshotRefresh\(\);/);
    expect(bridgeStateSource).toMatch(/payload\.status === "completed"[\s\S]*scheduleImmediateSnapshotRefresh\(\);/);
  });

  it("does not fade current primary page cards in on mount", () => {
    expect(glassCardSource).not.toContain("initial={{ opacity: 0");
    expect(overviewPageSource).toContain('className="monitor-panel monitor-panel--primary"');
    expect(overviewPageSource).toContain("overview-summary-grid");
    expect(overviewPageSource).not.toContain("initial={{ opacity: 0");
  });

  it("gives monitor start an immediate local pending state and prevents duplicate first-click sends", () => {
    expect(overviewPageSource).toContain("monitorStartFeedback");
    expect(overviewPageSource).toContain("setMonitorStartFeedback(\"pending\")");
    expect(overviewPageSource).toContain("monitorStartFeedback === \"pending\"");
    expect(overviewPageSource).toContain("data-smoke-state=\"monitor-start-feedback\"");
    expect(overviewPageSource).toContain("primaryAction.feedback");
    expect(overviewPageSource).toMatch(/if \(monitorStartFeedback === "pending"\) return/);
    expect(overviewPageSource).toMatch(/await bridgeState\.startMonitor\(\)/);
  });

  it("keeps report smoke action indexes stable after live report refreshes reorder the list", () => {
    expect(reportsPageSource).toContain("reportSmokeIndexByIdRef");
    expect(reportsPageSource).toContain("smokeIndexByReportId");
    expect(reportsPageSource).toContain("smokeIndex={smokeIndexByReportId.get(report.reportId) ?? index}");
    expect(reportsPageSource).toContain("data-smoke-action={`open-report-${smokeIndex}`}");
    expect(reportsPageSource).toContain("data-smoke-action={`open-directory-${smokeIndex}`}");
    expect(reportsPageSource).toContain("data-smoke-action={`regenerate-report-${smokeIndex}`}");
  });
});
