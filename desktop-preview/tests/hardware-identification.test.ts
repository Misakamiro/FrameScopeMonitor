import { expect, test } from "vitest";
import { formatDiskType, formatMemorySpeed } from "../src/lib/hardware";

test("storage labels distinguish confirmed HDDs from unavailable classification", () => {
  expect(formatDiskType({ is_ssd: true })).toBe("SSD");
  expect(formatDiskType({ is_ssd: false })).toBe("机械硬盘");
  expect(formatDiskType({ is_ssd: null })).toBe("未知");
});

test("memory displays configured speed instead of module capability, in either direction", () => {
  expect(formatMemorySpeed({ speed_mhz: 5600, configured_clock_speed: 6200 })).toBe("6200 MT/s");
  expect(formatMemorySpeed({ speed_mhz: 6000, configured_clock_speed: 4800 })).toBe("4800 MT/s");
  expect(formatMemorySpeed({ speed_mhz: 3200, configured_clock_speed: 3200 })).toBe("3200 MT/s");
});

test("memory falls back only when configured speed is unavailable", () => {
  for (const configured_clock_speed of [null, 0, -1, NaN, Infinity]) {
    expect(formatMemorySpeed({ speed_mhz: 5600, configured_clock_speed })).toBe("5600 MT/s");
  }
  expect(formatMemorySpeed({ speed_mhz: 0, configured_clock_speed: null })).toBe("--");
  expect(formatMemorySpeed({ speed_mhz: NaN, configured_clock_speed: 0 })).toBe("--");
});
