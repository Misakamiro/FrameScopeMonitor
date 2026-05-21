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
});
