import { describe, expect, it } from "vitest";
import {
  chartSeries,
  mockPreviewLabel,
  navigationItems,
  overviewMetrics,
  reportPreview,
  settingsPreview,
  targetPreview,
} from "./mockPreview";
import mockPreviewSource from "./mockPreview.ts?raw";

describe("mock preview data boundaries", () => {
  it("marks the frontend data as a static mock preview", () => {
    expect(mockPreviewLabel.toLowerCase()).toContain("mock preview");
  });

  it("covers every required page and core visual section", () => {
    expect(navigationItems.map((item) => item.id)).toEqual([
      "overview",
      "targets",
      "reports",
      "settings",
      "about",
    ]);
    expect(overviewMetrics.length).toBeGreaterThanOrEqual(4);
    expect(targetPreview.length).toBeGreaterThanOrEqual(3);
    expect(reportPreview.length).toBeGreaterThanOrEqual(3);
    expect(settingsPreview.length).toBeGreaterThanOrEqual(4);
  });

  it("keeps chart samples numeric for deterministic SVG rendering", () => {
    for (const series of chartSeries) {
      expect(series.points.length).toBeGreaterThan(1);
      expect(series.points.every((point) => Number.isFinite(point))).toBe(true);
    }
  });

  it("keeps mock config aligned with persisted theme, window, tray, and cpu telemetry fields", () => {
    expect(mockPreviewSource).toContain('ThemeMode: "system"');
    expect(mockPreviewSource).toContain('CloseWindowBehavior: "minimize-to-tray"');
    expect(mockPreviewSource).toContain("TrayEnabled: true");
    expect(mockPreviewSource).toContain("TelemetrySampleIntervalMs: 1000");
    expect(mockPreviewSource).toContain("CpuTelemetry:");
    expect(mockPreviewSource).toContain("CollectPerCoreFrequency: true");
    expect(mockPreviewSource).toContain("CollectCpuVoltage: true");
    expect(mockPreviewSource).toContain("PerCoreSampleIntervalMs: 1000");
    expect(mockPreviewSource).toContain("PerCoreVoltageSampleIntervalMs: 1000");
    expect(mockPreviewSource).toContain('VoltageProvider: "auto"');
  });

  it("keeps mock targets aligned with the process sampling profile contract", () => {
    expect(mockPreviewSource).toContain('ProcessSamplingMode: "normal"');
    expect(mockPreviewSource).toContain("ProcessSampleIntervalMs: 1000");
    expect(mockPreviewSource).toContain("SlowSampleIntervalMs: 1000");
  });
});
