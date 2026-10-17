import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const bridge = vi.hoisted(() => ({
  status: { running: true, phase: "capturing", completedRuns: 0 } as Record<string, unknown>,
  stop: vi.fn(),
  statusError: null as Error | null,
  config: {} as Record<string, unknown>,
  backendCall: vi.fn(),
}));
vi.mock("../src/preview/runtime/platform", () => ({
  isDesktop: () => true,
  isMainWindow: () => true,
  readPersistedState: () => undefined,
  persistState: () => {},
  connectStateBus: () => ({ send() {}, close() {} }),
  emitPreviewEvent: async () => {},
  nativeOperation: async () => {},
  backendInvoke: async (command: string, args: unknown = {}) => {
    bridge.backendCall(command, args);
    return command === "monitor_stop" ? bridge.stop() : command === "reports_list" ? { reports: [] } : { config: bridge.config };
  },
}));
vi.mock("../src/preview/runtime/core", () => ({
  invoke: async (command: string) => {
    if (command !== "monitor_status") return { targets: [] };
    if (bridge.statusError) throw bridge.statusError;
    return structuredClone(bridge.status);
  },
}));

describe("desktop graceful stop", () => {
  let runtime: typeof import("../src/preview/runtime/state");
  let close: (() => void) | undefined;
  beforeEach(async () => {
    vi.resetModules();
    vi.useFakeTimers();
    bridge.status = { running: true, phase: "capturing", completedRuns: 0 };
    bridge.statusError = null;
    bridge.config = {}; bridge.backendCall.mockClear();
    bridge.stop.mockReset().mockResolvedValue({ status: "stopping", completedRuns: 0 });
    runtime = await import("../src/preview/runtime/state");
    runtime.mutatePreview(s => { s.monitor.phase = "capturing"; s.monitor.elapsedSeconds = 9; });
  });
  afterEach(() => { close?.(); close = undefined; vi.useRealTimers(); });

  it("loads and saves the startup setting without starting the current session", async () => {
    bridge.config = { autoStart: false };
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(0);
    expect(runtime.getPreviewState().config.autoStart).toBe(false);
    await runtime.previewAction("config.update", { autoStart: true });
    expect(bridge.backendCall).toHaveBeenCalledWith("config_save", { config: expect.objectContaining({ autoStart: true }) });
    expect(bridge.backendCall.mock.calls.some(([command]) => command === "monitor_start")).toBe(false);
  });

  it("loads and persists window startup options without toggling current windows", async () => {
    bridge.config = { autoStartOverlay: true, startMinimized: true };
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(0);
    expect(runtime.getPreviewState().config).toMatchObject({ autoStartOverlay: true, startMinimized: true });
    runtime.mutatePreview(state => { state.overlay.enabled = true; });
    await runtime.previewAction("config.update", { autoStartOverlay: false, startMinimized: false });
    expect(runtime.getPreviewState().overlay.enabled).toBe(true);
    expect(runtime.getPreviewState().monitor.phase).toBe("capturing");
    expect(bridge.backendCall).toHaveBeenCalledWith("config_save", { config: expect.objectContaining({ autoStartOverlay: false, startMinimized: false }) });
    expect(bridge.backendCall.mock.calls.some(([command]) => ["start_overlay_panel", "monitor_start", "minimize_to_tray"].includes(command))).toBe(false);
  });

  it("displays actual active games and clears them when no game is being captured", async () => {
    close = runtime.startPreviewRuntime();
    bridge.status = { running: true, phase: "capturing", targetNames: ["Counter-Strike 2", "Delta Force"] };
    await vi.advanceTimersByTimeAsync(1000);
    expect((runtime.getPreviewState().monitor as any).activeTargets).toEqual(["Counter-Strike 2", "Delta Force"]);
    bridge.status = { running: true, phase: "waiting", targetNames: [] };
    await vi.advanceTimersByTimeAsync(1000);
    expect((runtime.getPreviewState().monitor as any).activeTargets).toEqual([]);
  });

  it("starts monitoring without a selected UI target", async () => {
    runtime.mutatePreview(s => { s.monitor.phase = "idle"; s.selectedTargetId = null; s.targets = []; });
    await expect(runtime.previewAction("monitor.start")).resolves.toBeUndefined();
    expect(runtime.getPreviewState().monitor.phase).toBe("waiting");
  });

  it("keeps stopping after the request and preserves the captured state", async () => {
    await runtime.previewAction("monitor.stop");
    expect(runtime.getPreviewState().monitor).toMatchObject({ phase: "stopping", elapsedSeconds: 9 });
  });

  it("waits through worker drain and report generation before completing", async () => {
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(1000);
    await runtime.previewAction("monitor.stop");
    bridge.status = { running: true, phase: "stopping", completedRuns: 0 };
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor.phase).toBe("stopping");
    bridge.status = { running: true, phase: "generating", reportPercent: 42, completedRuns: 0 };
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor).toMatchObject({ phase: "generating", reportPercent: 42 });
    bridge.status = { running: false, phase: "stopped", completedRuns: 1 };
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor.phase).toBe("completed");
    await vi.advanceTimersByTimeAsync(2000);
    expect(runtime.getPreviewState().monitor.phase).toBe("completed");
  });

  it("does not regress to capturing while the stop request is pending", async () => {
    await runtime.previewAction("monitor.stop");
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor.phase).toBe("stopping");
  });

  it("shows a clean idle stop without fabricating a report", async () => {
    runtime.mutatePreview(s => { s.monitor.phase = "waiting"; s.monitor.elapsedSeconds = 0; });
    await runtime.previewAction("monitor.stop");
    bridge.status = { running: false, phase: "stopped", completedRuns: 0 };
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor).toMatchObject({ phase: "idle", error: null });
  });

  it("keeps the current capture when ownership validation rejects the request", async () => {
    bridge.stop.mockRejectedValue(new Error("watcher ownership mismatch"));
    await expect(runtime.previewAction("monitor.stop")).rejects.toThrow("ownership mismatch");
    expect(runtime.getPreviewState().monitor.phase).toBe("capturing");
  });

  it("reports finalization failure instead of claiming completed", async () => {
    await runtime.previewAction("monitor.stop");
    bridge.status = { running: false, phase: "stopped", completedRuns: 1, error: "report failed" };
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor).toMatchObject({ phase: "error", error: "report failed" });
  });

  it("does not treat a watcher crash during drain as a clean stop", async () => {
    await runtime.previewAction("monitor.stop");
    bridge.status = { running: false, phase: "idle", completedRuns: 0 };
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor.phase).toBe("error");
  });

  it.each(["idle", "stopped"])("reconciles a late completed count in %s after an earlier idle snapshot", async (phase) => {
    runtime.mutatePreview(s => { s.monitor.phase = "waiting"; });
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(1000);
    bridge.status = { running: false, phase: "idle", completedRuns: 0 };
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor.phase).toBe("error");
    bridge.status = { running: false, phase, completedRuns: 1 };
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor).toMatchObject({ phase: "completed", error: null });
  });

  it("preserves capture and generation across failed status reads", async () => {
    runtime.mutatePreview(s => { s.monitor.phase = "waiting"; });
    close = runtime.startPreviewRuntime();
    await vi.advanceTimersByTimeAsync(1000);
    for (const phase of ["capturing", "generating"]) {
      bridge.status = { running: true, phase, completedRuns: 0 };
      await vi.advanceTimersByTimeAsync(1000);
      bridge.statusError = new Error("读取监控状态失败");
      await vi.advanceTimersByTimeAsync(1000);
      expect(runtime.getPreviewState().monitor).toMatchObject({ phase, error: null });
      bridge.statusError = null;
    }
    bridge.status = { running: false, phase: "stopped", completedRuns: 1 };
    await vi.advanceTimersByTimeAsync(1000);
    expect(runtime.getPreviewState().monitor).toMatchObject({ phase: "completed", error: null });
  });
});
