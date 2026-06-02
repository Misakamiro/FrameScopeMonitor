import { describe, expect, it } from "vitest";
import { getVirtualListWindow } from "./virtualListWindow";

describe("getVirtualListWindow", () => {
  it("keeps small lists fully rendered", () => {
    const windowState = getVirtualListWindow({
      rowCount: 12,
      rowHeight: 62,
      scrollTop: 0,
      viewportHeight: 360,
      overscan: 6,
    });

    expect(windowState.startIndex).toBe(0);
    expect(windowState.endIndex).toBe(12);
    expect(windowState.paddingTop).toBe(0);
    expect(windowState.paddingBottom).toBe(0);
  });

  it("renders only the visible middle window with overscan", () => {
    const windowState = getVirtualListWindow({
      rowCount: 250,
      rowHeight: 62,
      scrollTop: 62 * 100,
      viewportHeight: 360,
      overscan: 6,
    });

    expect(windowState.startIndex).toBe(94);
    expect(windowState.endIndex).toBe(113);
    expect(windowState.renderedCount).toBe(19);
    expect(windowState.paddingTop).toBe(94 * 62);
    expect(windowState.paddingBottom).toBe((250 - 113) * 62);
  });

  it("clamps near the end of the list without exceeding row count", () => {
    const windowState = getVirtualListWindow({
      rowCount: 250,
      rowHeight: 62,
      scrollTop: 62 * 248,
      viewportHeight: 360,
      overscan: 6,
    });

    expect(windowState.startIndex).toBe(242);
    expect(windowState.endIndex).toBe(250);
    expect(windowState.renderedCount).toBe(8);
    expect(windowState.paddingBottom).toBe(0);
  });
});
