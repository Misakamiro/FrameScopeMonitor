import { describe, expect, it } from "vitest";
import glassCardSource from "./components/GlassCard.tsx?raw";
import metricCardSource from "./components/MetricCard.tsx?raw";
import settingsPageSource from "./pages/SettingsPage.tsx?raw";
import targetsPageSource from "./pages/TargetsPage.tsx?raw";

describe("FrameScope UI interaction contract", () => {
  it("keeps WebView2 smoke state probes stable without visible English controls", () => {
    expect(targetsPageSource).toContain("Process refresh completed");
    expect(settingsPageSource).toContain("Saving FrameScope config.");
    expect(settingsPageSource).toContain("Config saved.");
    expect(settingsPageSource).toContain("data-smoke-state");
  });

  it("does not fade primary page cards in on mount", () => {
    expect(glassCardSource).not.toContain("initial={{ opacity: 0");
    expect(metricCardSource).not.toContain("initial={{ opacity: 0");
  });
});
