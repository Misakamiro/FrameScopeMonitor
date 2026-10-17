import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { getPreviewState, previewAction, startPreviewRuntime, subscribePreviewState } from "./runtime";
import { invoke } from "./runtime/core";

describe("isolated preview runtime", () => {
  let stop: (() => void) | undefined;
  beforeEach(async () => {
    vi.useFakeTimers();
    await previewAction("preview.reset");
  });
  afterEach(() => { stop?.(); stop = undefined; vi.useRealTimers(); });

  it("rejects unsupported commands and never delegates hardware reads", async () => {
    // install_pawnio_driver became a real desktop command (M4); a command
    // that is still unknown to both layers must keep failing loudly.
    await expect(invoke("install_pawnio_driver")).rejects.toThrow(
      "Native window operation requires desktop",
    );
    await expect(invoke("definitely_not_a_command")).rejects.toThrow(
      "Unsupported preview command",
    );
    expect(await invoke("get_system_locale")).toBe("zh-CN");
    expect(await invoke("is_autostart_mode")).toBe(false);
  });

  it("matches original hardware, memory, disk and sensor schemas", async () => {
    const hardware = await invoke<any>("get_hardware");
    expect(hardware.cpu.cores).toBe(8);
    expect(hardware.gpu[0].vendor).toBe("NVIDIA");
    expect(hardware.memory[0].capacity_gb).toBe(16);
    expect(await invoke("get_memory_status")).toMatchObject({ total: 32768, usage_percent: 42 });
    expect(await invoke("get_disk_status")).toMatchObject({ total_gb: 1000 });
    expect(await invoke("get_all_sensors")).toMatchObject({
      sensors: expect.arrayContaining([expect.objectContaining({ hardwareType: "Cpu", sensorType: "Load", value: 36 })]),
    });
  });

  it("keeps reads deterministic and selects the second GPU", async () => {
    await previewAction("scenario.set", "multi-gpu");
    const first = await invoke<any>("get_overlay_hardware_data");
    expect(await invoke("get_overlay_hardware_data")).toEqual(first);
    expect(first.gpu_sensors).toHaveLength(2);
    await invoke("set_active_gpu_index", { index: 1 });
    const second = await invoke<any>("get_overlay_hardware_data");
    expect(second.active_gpu_index).toBe(1);
    expect(second.gpu_usage).toBe(second.gpu_sensors[1].usage);
    await expect(invoke("set_active_gpu_index", { index: 4 })).rejects.toThrow();
  });


  it("represents unavailable metrics as null and omits unavailable raw sensor rows", async () => {
    await previewAction("scenario.set", "missing-sensors");
    expect(await invoke("get_overlay_hardware_data")).toMatchObject({ cpu_temp: null, gpu_power: null });
    const response = await invoke<any>("get_all_sensors");
    expect(response.sensors.every((sensor: any) => Number.isFinite(sensor.value))).toBe(true);
    await previewAction("scenario.set", "empty");
    expect(await invoke("get_all_sensors")).toMatchObject({ sensors: [] });
    expect(await invoke("get_overlay_hardware_data")).toMatchObject({ fps: null, cpu_usage: null, gpu_sensors: [] });
    await previewAction("scenario.set", "failure");
    await expect(invoke("get_hardware")).rejects.toThrow("Simulated");
  });

  it("keeps overlay switches independent of idle and waiting monitoring", async () => {
    for (const phase of ["idle", "waiting"] as const) {
      if (phase === "waiting") await previewAction("monitor.start");
      await invoke("start_overlay_panel");
      expect(getPreviewState().monitor.phase).toBe(phase);
      await invoke("stop_overlay_panel");
      expect(getPreviewState().monitor.phase).toBe(phase);
    }
  });

  it("runs one main timer through all monitor phases and generates a simulated report", async () => {
    await previewAction("config.update", { captureDurationSeconds: 3 });
    stop = startPreviewRuntime();
    const duplicateStop = startPreviewRuntime();
    await previewAction("monitor.start");
    expect(getPreviewState().monitor.phase).toBe("waiting");
    await vi.advanceTimersByTimeAsync(1000);
    expect(getPreviewState().tick).toBe(1);
    expect(getPreviewState().monitor.phase).toBe("capturing");
    await vi.advanceTimersByTimeAsync(3000);
    expect(getPreviewState().monitor.phase).toBe("stopping");
    await vi.advanceTimersByTimeAsync(1000);
    expect(getPreviewState().monitor.phase).toBe("generating");
    await vi.advanceTimersByTimeAsync(1000);
    expect(getPreviewState().monitor.phase).toBe("completed");
    expect(getPreviewState().reports[0]).toMatchObject({ simulated: true, durationSeconds: 3 });
    duplicateStop();
  });

  it("updates targets/config, notifies subscribers, and validates mutations", async () => {
    const listener = vi.fn();
    const unsubscribe = subscribePreviewState(listener);
    await previewAction("target.add", { name: "Test", executable: "test.exe" });
    const target = getPreviewState().targets.at(-1)!;
    await previewAction("target.select", { id: target.id });
    await previewAction("target.update", { id: target.id, enabled: false });
    expect(getPreviewState().selectedTargetId).toBe(target.id);
    await previewAction("target.remove", { id: target.id });
    await previewAction("monitor.start");
    expect(getPreviewState().monitor.phase).toBe("waiting");
    await expect(previewAction("config.update", { captureDurationSeconds: -1 })).rejects.toThrow();
    expect(listener).toHaveBeenCalled();
    unsubscribe();
  });

  it("allows target management while monitoring waits for a game", async () => {
    await previewAction("monitor.start");
    await previewAction("target.add", { name: "Naraka", executable: "NarakaBladepoint.exe" });
    const target = getPreviewState().targets.at(-1)!;
    await previewAction("target.update", { id: target.id, enabled: false });
    expect(getPreviewState().targets.at(-1)?.enabled).toBe(false);
    await previewAction("target.remove", { id: target.id });
    expect(getPreviewState().targets.some(item => item.id === target.id)).toBe(false);
    expect(getPreviewState().monitor.phase).toBe("waiting");
  });

  it("supports early stop and explicit failure without manufacturing a report", async () => {
    stop = startPreviewRuntime();
    await previewAction("monitor.start");
    await previewAction("monitor.stop");
    expect(getPreviewState().monitor.phase).toBe("stopping");
    await vi.advanceTimersByTimeAsync(2000);
    expect(getPreviewState().monitor.phase).toBe("completed");
    await previewAction("monitor.reset");
    await previewAction("scenario.set", "failure");
    await previewAction("monitor.start");
    await vi.advanceTimersByTimeAsync(1000);
    expect(getPreviewState().monitor).toMatchObject({ phase: "error", error: expect.stringContaining("Simulated") });
  });

  it("update_overlay_settings appends missing defaults and keeps the password map selection", async () => {
    // Pre-fix normalization (M5/R-C2): filtering down to the preview default
    // list dropped the native-only rows and force-cleared delta_password_maps
    // on the first save. Simulate a stale 20-item saved state that predates
    // the native-only rows.
    const stale = getPreviewState().overlay.settings;
    const withoutNativeRows = stale.display_items.filter(
      item => item.id !== "game_ping" && item.id !== "delta_password",
    );
    await invoke("update_overlay_settings", {
      settings: {
        ...stale,
        display_items: withoutNativeRows,
        delta_password_maps: ["潮汐监狱"],
      },
    });
    const saved = getPreviewState().overlay.settings;
    const ids = saved.display_items.map(item => item.id);
    expect(ids).toContain("game_ping");
    expect(ids).toContain("delta_password");
    // The caller's items and their ordering survive; defaults append after.
    expect(ids.slice(0, withoutNativeRows.length)).toEqual(withoutNativeRows.map(item => item.id));
    expect(saved.delta_password_maps).toEqual(["潮汐监狱"]);
  });
});
