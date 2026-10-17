import React from "react";
import { renderToStaticMarkup } from "react-dom/server";
import { expect, test, vi } from "vitest";

const fixture = vi.hoisted(() => ({ state: {
  monitor: { phase: "waiting", activeTargets: [] as string[] },
  targets: [{ id: "wrong", name: "Not running", enabled: true }], selectedTargetId: "wrong", reports: [],
} }));
vi.mock("@chakra-ui/react", async () => {
  const { createElement } = await import("react");
  const component = ({ children }: any) => createElement("div", null, children);
  return Object.fromEntries(["Box","Text","Flex","HStack","VStack","Button","Badge","Progress","SimpleGrid"].map(name => [name, component]));
});
vi.mock("@/components/special/custom-select", () => ({ CustomSelect: () => { throw new Error("Home monitoring must not expose a game picker"); } }));
vi.mock("@/components/special/liquid-glass-card", async () => {
  const { createElement } = await import("react");
  return { LiquidGlassCard: ({ children }: any) => createElement("section", null, children) };
});
vi.mock("@/hooks/use-adaptive-text-color", () => ({ useAdaptiveTextColor: () => ({}) }));
vi.mock("@/hooks/use-floating-button-style", () => ({ useFloatingButtonStyle: () => ({}) }));
vi.mock("react-router-dom", () => ({ useNavigate: () => () => {} }));
vi.mock("@/contexts/app-startup-context", () => ({ useAppStartup: () => ({ hardwareInfo: null }) }));
vi.mock("@/preview/pages/shared", () => ({ usePreviewState: () => fixture.state, usePreviewAction: () => ({ run() {} }), phaseLabels: {}, ActionError: () => null }));
vi.mock("@/preview/runtime/state", () => ({ monitorBusy: () => true }));
import HomePage from "../src/pages/HomePage";

test("home shows actual captured games as read-only text, never configured selection", () => {
  fixture.state.monitor.activeTargets = [];
  expect(renderToStaticMarkup(<HomePage />)).toContain("未启动游戏");
  fixture.state.monitor.phase = "capturing";
  fixture.state.monitor.activeTargets = ["Counter-Strike 2", "Delta Force"];
  const html = renderToStaticMarkup(<HomePage />);
  expect(html).toContain("Counter-Strike 2");
  expect(html).toContain("Delta Force");
  expect(html).not.toContain("Not running");
});
