import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const native = vi.hoisted(() => ({
  targets: [{ id: "saved", name: "Saved", executable: "saved.exe", enabled: true }],
  save: vi.fn(),
  get: vi.fn(),
}));

vi.mock("../src/preview/runtime/platform", () => ({
  isDesktop: () => true, isMainWindow: () => true,
  readPersistedState: () => undefined, persistState: () => {},
  connectStateBus: () => ({ send() {}, close() {} }), emitPreviewEvent: async () => {},
  nativeOperation: async () => {},
  backendInvoke: async (command: string) => {
    if (command === "targets_get") return { targets: native.get() };
    if (command === "targets_save") return native.save();
    return {};
  },
}));
vi.mock("../src/preview/runtime/core", () => ({
  invoke: async (command: string) => command === "targets_get" ? { targets: native.get() } : {},
}));

describe("native target persistence failures", () => {
  let runtime: typeof import("../src/preview/runtime/state");
  let close: (() => void) | undefined;
  beforeEach(async () => {
    vi.resetModules();
    native.save.mockReset();
    native.get.mockReset().mockReturnValue(native.targets);
    native.save.mockRejectedValue(new Error("controlled targets_save failure"));
    runtime = await import("../src/preview/runtime/state");
    close = runtime.startPreviewRuntime();
    await Promise.resolve();
    await Promise.resolve();
  });
  afterEach(() => close?.());

  it("does not accept a missing target array as a successful empty-table sync", async () => {
    close?.();
    const before = structuredClone(runtime.getPreviewState().targets);
    native.get.mockReturnValue(undefined);
    runtime.resetTargetSyncForTests();
    close = runtime.startPreviewRuntime();
    await Promise.resolve();
    await Promise.resolve();
    expect(runtime.isTargetsSynced()).toBe(false);
    expect(runtime.getPreviewState().targets).toEqual(before);
  });

  it("restores target add and edit after a real desktop backend rejection", async () => {
    const before = runtime.getPreviewState().targets.map(target => ({ ...target }));
    await expect(runtime.previewAction("target.add", { name: "New", executable: "new.exe" })).rejects.toThrow("controlled targets_save failure");
    expect(runtime.getPreviewState().targets).toEqual(before);
    const saved = before[0];
    await expect(runtime.previewAction("target.update", { id: saved.id, name: "Changed", executable: "changed.exe" })).rejects.toThrow("controlled targets_save failure");
    expect(runtime.getPreviewState().targets[0]).toEqual(saved);
  });

  it("restores target removal after a real desktop backend rejection", async () => {
    const before = runtime.getPreviewState().targets.map(target => ({ ...target }));
    await expect(runtime.previewAction("target.remove", { id: before[0].id })).rejects.toThrow("controlled targets_save failure");
    expect(runtime.getPreviewState().targets).toEqual(before);
  });

  it("persists targets during an automatic waiting session without stopping it", async () => {
    native.save.mockResolvedValue({});
    runtime.mutatePreview(state => { state.monitor.phase = "waiting"; });
    await runtime.previewAction("target.add", { name: "Naraka", executable: "NarakaBladepoint.exe" });
    expect(native.save).toHaveBeenCalledOnce();
    expect(runtime.getPreviewState().targets.at(-1)?.executable).toBe("NarakaBladepoint.exe");
    expect(runtime.getPreviewState().monitor.phase).toBe("waiting");
  });

  it.each(["capturing", "stopping", "generating"] as const)("still rejects target changes while %s", async phase => {
    runtime.mutatePreview(state => { state.monitor.phase = phase; });
    await expect(runtime.previewAction("target.add", { name: "Naraka", executable: "NarakaBladepoint.exe" })).rejects.toThrow("不能修改目标");
    expect(native.save).not.toHaveBeenCalled();
  });
});
