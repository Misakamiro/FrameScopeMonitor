import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";

const read = (path: string) => readFileSync(new URL(`../${path}`, import.meta.url), "utf8");

describe("preview source boundaries", () => {
  it("uses the upstream shell without unrelated providers or routes", () => {
    const app = read("src/App.tsx");
    expect(app).toContain('from "./components/ui/main-layout"');
    for (const removed of ["MusicProvider", "StartupAdHost", "UpdateModal", "/optimization", "/music", "/steam"]) {
      expect(app).not.toContain(removed);
    }
    for (const route of ["/hardware", "/targets", "/reports", "/overlay-panel", "/settings"]) {
      expect(app).toContain(route);
    }
  });

  it("retains original window controls without the unrelated game mode", () => {
    const source = read("src/components/ui/title-bar.tsx");
    expect(source).toContain("getCurrentWindow");
    expect(source).toContain("<GlobalSearch");
    expect(source).not.toContain("GameModeSwitch");
  });

  it("removes unsupported navigation entries", () => {
    const source = read("src/components/ui/sidebar.tsx");
    for (const route of ["/music", "/tools", "/optimization", "/steam", "/epic-free", "/delta-force"]) {
      expect(source).not.toContain(`path: "${route}"`);
    }
  });

  it("keeps liquid glass enabled by default (approved deviation 2026-09-07)", () => {
    const source = read("src/contexts/background-context.tsx");
    expect(source).toContain("liquidGlassEnabled: true");
    expect(source).toContain("useState(true)");
    expect(source).toContain('store.set("liquid-glass-enabled", true)');
  });
});
