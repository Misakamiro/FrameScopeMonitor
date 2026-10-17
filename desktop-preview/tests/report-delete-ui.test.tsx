import { beforeEach, describe, expect, it, vi } from "vitest";
import { isValidElement, type ReactElement } from "react";

const ui = vi.hoisted(() => ({ slots: [] as unknown[], index: 0, reports: [] as any[], run: vi.fn(), error: "", pending: false }));
vi.mock("react", async importOriginal => ({
  ...await importOriginal<typeof import("react")>(),
  useEffect: () => {},
  useState: (initial: unknown) => {
    const index = ui.index++;
    if (!(index in ui.slots)) ui.slots[index] = initial;
    return [ui.slots[index], (value: unknown) => { ui.slots[index] = value; }];
  },
}));
vi.mock("react-router-dom", () => ({ useSearchParams: () => [new URLSearchParams("id=run-1")] }));
vi.mock("@chakra-ui/react", () => Object.fromEntries(["Box", "Heading", "Text", "HStack", "VStack", "Input", "Button", "Badge", "SimpleGrid", "Spinner"].map(name => [name, name])));
vi.mock("@/components/special/liquid-glass-card", () => ({ LiquidGlassCard: "Card" }));
vi.mock("@/components/special/custom-select", () => ({ CustomSelect: "Select" }));
vi.mock("@/hooks/use-adaptive-text-color", () => ({ useAdaptiveTextColor: () => ({}) }));
vi.mock("@/preview/runtime/platform", () => ({ isDesktop: () => false, backendInvoke: vi.fn() }));
vi.mock("@/preview/runtime/state", () => ({ refreshDesktopReports: vi.fn() }));
vi.mock("../src/preview/pages/shared", () => ({
  usePreviewState: () => ({ reports: ui.reports, monitor: { phase: "idle", reportId: null }, config: {} }),
  usePreviewAction: () => ({ run: ui.run, pending: ui.pending, error: ui.error, clearError: () => { ui.error = ""; } }),
  PreviewDialog: "Dialog", ActionError: "Error", formatMetric: () => "100", useFloatingFieldStyle: () => ({ sx: {} }),
}));
import ReportsPage from "../src/preview/pages/ReportsPage";

function elements(node: unknown): ReactElement<any>[] {
  if (Array.isArray(node)) return node.flatMap(elements);
  if (!isValidElement<{ children?: unknown }>(node)) return [];
  return [node, ...elements(node.props.children)];
}
function render() { ui.index = 0; return elements(ReportsPage()); }
function button(tree: ReactElement<any>[], text: string) { return tree.find(node => node.type === "Button" && node.props.children === text)!; }
function dialog(tree: ReactElement<any>[], title: string) { return tree.find(node => node.type === "Dialog" && node.props.title === title)!; }

describe("report deletion confirmation", () => {
  it("renders more than three reports without truncating the returned list", () => {
    const sample = ui.reports[0];
    ui.reports = Array.from({ length: 6 }, (_, index) => ({ ...sample, id: `run-${index + 1}` }));
    const tree = render();
    expect(tree.filter(node => node.type === "Button" && node.props.children === "查看详情")).toHaveLength(6);
  });
  beforeEach(() => {
    ui.slots = []; ui.error = ""; ui.pending = false; ui.run.mockReset();
    ui.reports = [{ id: "run-1", targetName: "Counter-Strike 2", kind: "full", createdAt: "2026-10-11T08:00:00Z", durationSeconds: 120 }];
  });

  it("cancel retains details and never calls the deletion action", () => {
    button(render(), "删除记录").props.onClick();
    const confirmation = dialog(render(), "删除报告记录");
    expect(confirmation.props.isOpen).toBe(true);
    button(render(), "取消").props.onClick();
    expect(dialog(render(), "删除报告记录").props.isOpen).toBe(false);
    expect(dialog(render(), "报告详情").props.isOpen).toBe(true);
    expect(ui.run).not.toHaveBeenCalled();
  });

  it("names the game and time and explains the whole-run recycle scope", () => {
    button(render(), "删除记录").props.onClick();
    const text = elements(dialog(render(), "删除报告记录")).flatMap(node => typeof node.props.children === "string" ? [node.props.children] : []).join(" ");
    expect(text).toContain("Counter-Strike 2");
    expect(text).toContain("2026");
    expect(text).toContain("原始采集数据");
    expect(text).toContain("诊断");
    expect(text).toContain("Windows 回收站");
    expect(text).not.toContain("run");
  });

  it("confirmed success closes both dialogs", async () => {
    ui.run.mockResolvedValue(true);
    button(render(), "删除记录").props.onClick();
    await button(render(), "移入回收站").props.onClick();
    expect(ui.run).toHaveBeenCalledWith("report.delete", { id: "run-1" });
    expect(dialog(render(), "报告详情").props.isOpen).toBe(false);
    expect(dialog(render(), "删除报告记录").props.isOpen).toBe(false);
  });

  it("failed deletion leaves details and the record visible with the error", async () => {
    ui.run.mockImplementation(async () => { ui.error = "回收站拒绝访问"; return false; });
    button(render(), "删除记录").props.onClick();
    await button(render(), "移入回收站").props.onClick();
    const tree = render();
    expect(dialog(tree, "报告详情").props.isOpen).toBe(true);
    expect(ui.reports).toHaveLength(1);
    expect(tree.some(node => node.type === "Error" && node.props.message === "回收站拒绝访问")).toBe(true);
  });
});
