import { beforeEach, describe, expect, it, vi } from "vitest";

const bridge = vi.hoisted(() => ({ desktop: true, invoke: vi.fn() }));
vi.mock("../src/preview/runtime/platform", () => ({
  isDesktop: () => bridge.desktop, isMainWindow: () => true,
  readPersistedState: () => undefined, persistState: () => {},
  connectStateBus: () => ({ send() {}, close() {} }),
  emitPreviewEvent: async () => {}, nativeOperation: async () => {},
  backendInvoke: (command: string, args: unknown) => bridge.invoke(command, args),
}));

describe("report deletion", () => {
  let runtime: typeof import("../src/preview/runtime/state");
  beforeEach(async () => {
    vi.resetModules();
    bridge.desktop = true;
    bridge.invoke.mockReset();
    runtime = await import("../src/preview/runtime/state");
  });

  it("recycles by id then refreshes shared reports and the home latest report", async () => {
    bridge.invoke.mockImplementation(async (command: string) => command === "report_delete"
      ? { status: "recycled", id: "sample-full" }
      : { reports: [{ id: "remaining", targetName: "Remaining game", kind: "partial" }] });
    await runtime.previewAction("report.delete", { id: "sample-full" });
    expect(bridge.invoke.mock.calls[0]).toEqual(["report_delete", { id: "sample-full" }]);
    expect(bridge.invoke.mock.calls[1][0]).toBe("reports_list");
    expect(runtime.getPreviewState().reports.map(r => r.id)).toEqual(["remaining"]);
    expect(runtime.getPreviewState().reports[0].targetName).toBe("Remaining game");
  });

  it("preserves all reports when recycling fails", async () => {
    const before = runtime.getPreviewState().reports;
    bridge.invoke.mockRejectedValue("回收站拒绝访问");
    await expect(runtime.previewAction("report.delete", { id: "sample-full" })).rejects.toBe("回收站拒绝访问");
    expect(runtime.getPreviewState().reports).toEqual(before);
    expect(bridge.invoke).toHaveBeenCalledTimes(1);
  });

  it("ignores a delayed pre-delete list response but permits restored reports on a later refresh", async () => {
    let finishOldList!: (value: unknown) => void;
    const oldList = new Promise(resolve => { finishOldList = resolve; });
    const recycled = { id: "sample-full", targetName: "Restored game", kind: "full" };
    bridge.invoke.mockImplementation(async (command: string) => {
      if (command === "report_delete") return { status: "recycled", id: "sample-full" };
      return { reports: [] };
    });
    bridge.invoke.mockImplementationOnce(() => oldList);
    const oldRefresh = runtime.refreshDesktopReports();
    await runtime.previewAction("report.delete", { id: "sample-full" });
    finishOldList({ reports: [recycled] });
    await oldRefresh;
    expect(runtime.getPreviewState().reports).toEqual([]);
    bridge.invoke.mockResolvedValueOnce({ reports: [recycled] });
    await runtime.refreshDesktopReports();
    expect(runtime.getPreviewState().reports[0]).toMatchObject({ id: "sample-full", targetName: "Restored game" });
  });

  it("rejects a result that does not confirm recycling the requested record", async () => {
    bridge.invoke.mockResolvedValue({ status: "recycled", id: "different" });
    await expect(runtime.previewAction("report.delete", { id: "sample-full" })).rejects.toThrow("未确认报告已移入回收站");
    expect(runtime.getPreviewState().reports).toHaveLength(4);
  });

  it.each(["regenerating", "diagnosing"] as const)("refuses deletion while %s", async operation => {
    runtime.mutatePreview(s => { s.reports[0].operation = operation; });
    await expect(runtime.previewAction("report.delete", { id: "sample-full" })).rejects.toThrow("报告正在处理，请完成后再删除");
    expect(bridge.invoke).not.toHaveBeenCalled();
  });

  it("refuses deletion of the active capture run", async () => {
    runtime.mutatePreview(s => { s.monitor.phase = "generating"; s.monitor.reportId = "sample-full"; });
    await expect(runtime.previewAction("report.delete", { id: "sample-full" })).rejects.toThrow("正在采集或生成的报告不能删除");
    expect(bridge.invoke).not.toHaveBeenCalled();
  });

  it("really removes the simulated record and clears its completed-run reference", async () => {
    bridge.desktop = false;
    runtime.mutatePreview(s => { s.monitor.reportId = "sample-full"; });
    await runtime.previewAction("report.delete", { id: "sample-full" });
    expect(runtime.getPreviewState().reports.map(r => r.id)).not.toContain("sample-full");
    expect(runtime.getPreviewState().monitor.reportId).toBeNull();
    expect(bridge.invoke).not.toHaveBeenCalled();
  });
});
