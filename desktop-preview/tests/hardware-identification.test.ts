import { expect, test } from "vitest";
import { formatDiskType } from "../src/lib/hardware";

test("storage labels distinguish confirmed HDDs from unavailable classification", () => {
  expect(formatDiskType({ is_ssd: true })).toBe("SSD");
  expect(formatDiskType({ is_ssd: false })).toBe("机械硬盘");
  expect(formatDiskType({ is_ssd: null })).toBe("未知");
});
