import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

const native = vi.hoisted(() => ({ invoke: vi.fn() }));
vi.mock("@preview/native-core", () => ({ invoke: native.invoke }));

describe("report deletion through the real platform adapter", () => {
  beforeEach(() => {
    vi.resetModules();
    native.invoke.mockReset();
    vi.stubGlobal("window", { __TAURI_INTERNALS__: {} });
  });
  afterEach(() => vi.unstubAllGlobals());

  it("forwards report_delete through backendInvoke with only the requested id", async () => {
    native.invoke.mockResolvedValue({ status: "recycled", id: "run-1" });
    const { backendInvoke } = await import("../src/preview/runtime/platform");
    await expect(backendInvoke("report_delete", { id: "run-1" })).resolves.toEqual({ status: "recycled", id: "run-1" });
    expect(native.invoke).toHaveBeenCalledWith("preview_native", { command: "report_delete", args: { id: "run-1" } });
  });

  it("connects report.delete to deletion and refresh through the unmocked adapter", async () => {
    native.invoke.mockImplementation(async (_wrapper: string, request: { command: string }) => request.command === "report_delete"
      ? { status: "recycled", id: "sample-full" } : { reports: [] });
    const runtime = await import("../src/preview/runtime/state");
    await runtime.previewAction("report.delete", { id: "sample-full" });
    expect(native.invoke.mock.calls.map(call => call[1].command)).toEqual(["report_delete", "reports_list"]);
    expect(runtime.getPreviewState().reports).toEqual([]);
  });

  it("rejects native deletion from a browser preview", async () => {
    vi.stubGlobal("window", {});
    const { backendInvoke } = await import("../src/preview/runtime/platform");
    await expect(backendInvoke("report_delete", { id: "run-1" })).rejects.toThrow("Backend command requires desktop");
    expect(native.invoke).not.toHaveBeenCalled();
  });
});
