import { beforeEach, expect, it, vi } from "vitest";
const bridge = vi.hoisted(() => ({ invoke: vi.fn() }));
vi.mock("../src/preview/runtime/platform", () => ({
  isDesktop: () => true, isMainWindow: () => true,
  readPersistedState: () => undefined, persistState: () => {},
  connectStateBus: () => ({ send() {}, close() {} }), emitPreviewEvent: async () => {},
  nativeOperation: async () => {}, backendInvoke: (command: string) => bridge.invoke(command),
}));
beforeEach(() => { vi.resetModules(); bridge.invoke.mockReset(); });
it("keeps report capabilities closed until explicitly confirmed and preserves artifact explanations", async () => {
  bridge.invoke.mockResolvedValue({ reports: [{ id: "old", kind: "full", artifactStatus: "legacy-readable",
    artifactError: "Historical inputs need regeneration", durationSeconds: null }] });
  const runtime = await import("../src/preview/runtime/state");
  await runtime.refreshDesktopReports();
  expect(runtime.getPreviewState().reports[0]).toMatchObject({
    durationSeconds: null, canOpenReport: false, canRegenerate: false,
    artifactStatus: "legacy-readable", artifactError: "Historical inputs need regeneration",
  });
});
