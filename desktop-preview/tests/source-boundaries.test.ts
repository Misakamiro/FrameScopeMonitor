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

  it("keeps frontend command tables consistent with the Rust host", () => {
    const core = read("src/preview/runtime/core.ts");
    const platform = read("src/preview/runtime/platform.ts");
    const rustLib = read("../desktop-preview/src-tauri/src/lib.rs");
    const hostCommands = [...rustLib.matchAll(/"([a-z_]+)"/g)].map(m => m[1]);
    const hostSet = new Set(hostCommands);

    // Native commands must be reachable in the preview switch AND whitelisted
    // in the host.
    const nativeCommands = [...core.matchAll(/"([a-z_]+)"/g)].map(m => m[1]);
    for (const cmd of ["open_sensor_monitor", "start_overlay_panel", "export_hardware_report",
      "check_pawnio_status", "install_pawnio_driver", "restart_monitor_process"]) {
      expect(nativeCommands).toContain(cmd);
      expect(hostSet.has(cmd)).toBe(true);
    }

    // Backend-only commands must exist in the host whitelist and may only be
    // sent via backendInvoke (the preview switch rejects them by design).
    for (const cmd of ["processes_refresh", "monitor_start", "monitor_stop", "reports_list",
      "report_open", "report_open_directory", "report_regenerate", "targets_save"]) {
      expect(hostSet.has(cmd)).toBe(true);
    }
    expect(platform).toContain("backendOnlyCommands");
    expect(platform).toContain("backendInvoke");
  });

  it("keeps hardware-samples.csv columns aligned with the report merge", () => {
    const client = read("../src/monitoring/FrameScopeHardwareServiceClient.cs");
    const systemData = read("../src/reporting/FrameScopeReportGenerator.SystemData.cs");
    // The recorder header must carry every column the report merge overlays
    // (same names as the system-samples.csv GPU block).
    for (const column of ["GpuUtilPct", "GpuTempC", "PowerW", "GpuClockMHz", "MemClockMHz", "VramUsedMiB", "VramTotalMiB"]) {
      expect(client).toContain(`"${column}"`);
    }
    // The merge reads these columns from hardware-samples.csv.
    for (const column of ["GpuUtilPct", "GpuTempC", "PowerW", "GpuClockMHz", "MemClockMHz", "VramUsedMiB", "VramTotalMiB"]) {
      expect(systemData).toContain(`Get(row, h, "${column}")`);
    }
  });
});
