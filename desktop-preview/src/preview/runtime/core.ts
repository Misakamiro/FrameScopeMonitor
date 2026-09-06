import { hardwareFixture, overlayFixture, sensorsFixture } from "./fixtures";
import { getPreviewState, mutatePreview, defaultOverlay } from "./state";
import { isDesktop, nativeOperation, emitPreviewEvent } from "./platform";
import type { OverlaySettings } from "./types";

const nativeCommands = new Set([
  "open_sensor_monitor", "start_overlay_panel", "stop_overlay_panel", "toggle_overlay_panel",
  "get_overlay_panel_status", "set_overlay_drag_mode", "reset_overlay_position", "reset_vertical_overlay_position",
  "set_vertical_overlay_click_through", "save_vertical_overlay_position", "resize_vertical_overlay",
  "vertical_overlay_ready", "open_vertical_overlay", "stop_vertical_overlay", "minimize_to_tray", "exit_app",
  "export_hardware_report",
  "open_report_sample",
]);
export async function invoke<T = unknown>(command: string, args: Record<string, any> = {}): Promise<T> {
  const state = getPreviewState();
  let result: unknown;
  switch (command) {
    case "get_system_locale": result = "zh-CN"; break;
    case "get_system_username": result = "FrameScope UI Preview"; break;
    case "is_autostart_mode": result = false; break;
    case "get_hardware":
    case "get_hardware_info":
      if (state.scenario === "failure") throw new Error("Simulated：硬件信息读取失败");
      result = hardwareFixture(state); break;
    case "get_all_sensors":
      if (state.scenario === "failure") throw new Error("Simulated：传感器读取失败");
      result = sensorsFixture(state); break;
    case "get_overlay_hardware_data": result = overlayFixture(state); break;
    case "get_memory_status": {
      const usage = overlayFixture(state).memory_usage;
      result = usage === null ? null : { total: 32768, used: 32768 * usage / 100, available: 32768 * (1 - usage / 100), usage_percent: usage };
      break;
    }
    case "get_disk_status": result = state.scenario === "empty" ? null : { name: "Preview NVMe SSD", total_gb: 1000, used_gb: 420, available_gb: 580, usage_percent: 42 }; break;
    case "is_nvidia_gpu": result = true; break;
    case "set_active_gpu_index":
      if (!Number.isInteger(args.index) || args.index < 0 || args.index >= overlayFixture(state).gpu_sensors.length) throw new Error("GPU 索引超出范围");
      mutatePreview(next => { next.activeGpuIndex = args.index; });
      result = null; break;
    case "clear_hardware_data": mutatePreview(next => { next.tick = 0; }); result = null; break;
    case "get_overlay_current_settings": result = state.overlay.settings; break;
    case "update_overlay_settings": {
      const settings = args.settings as OverlaySettings;
      if (!settings || !Array.isArray(settings.display_items) || !Number.isFinite(settings.opacity) || settings.opacity < 0 || settings.opacity > 255) throw new Error("无效悬浮窗设置");
      const supported = new Set(defaultOverlay.display_items.map(item => item.id));
      const normalized = { ...settings, display_items: settings.display_items.filter(item => supported.has(item.id)), delta_password_maps: [] };
      mutatePreview(next => { next.overlay.settings = normalized; });
      await emitPreviewEvent("vertical-overlay-settings", normalized);
      if (isDesktop()) await nativeOperation("update_overlay_settings", { settings: normalized });
      result = { success: true, message: "预览设置已保存" }; break;
    }
    case "get_overlay_hotkey": result = state.hotkey; break;
    case "set_overlay_hotkey": {
      const shortcut = String(args.shortcut ?? "");
      if (!shortcut.trim()) throw new Error("快捷键不能为空");
      if (isDesktop()) await nativeOperation("set_overlay_hotkey", { shortcut });
      mutatePreview(next => { next.hotkey = shortcut; });
      result = null; break;
    }
    case "get_hotkeys_enabled_cmd": result = state.hotkeysEnabled; break;
    case "get_overlay_hotkey_enabled": result = state.overlayHotkeyEnabled; break;
    default:
      if (!nativeCommands.has(command)) throw new Error(`Unsupported preview command: ${command}`);
      if (command === "start_overlay_panel" && args.settings) await invoke("update_overlay_settings", args);
      if (command === "export_hardware_report") args = { ...args, content: JSON.stringify(hardwareFixture(state), null, 2) };
      if (isDesktop()) result = await nativeOperation(command, args);
      else if (command === "get_overlay_panel_status") result = state.overlay.enabled;
      else if (command === "open_report_sample") { window.open("/samples/charts/framescope-interactive-report.html", "report-sample"); result = true; }
      else if (command === "open_sensor_monitor" || command === "open_vertical_overlay") {
        window.open(command === "open_sensor_monitor" ? "/sensor-monitor" : "/vertical-overlay", command, "width=1000,height=700");
        result = { success: true, message: "预览窗口已打开" };
      } else if (["start_overlay_panel", "stop_overlay_panel", "toggle_overlay_panel", "set_overlay_drag_mode"].includes(command)) {
        result = { success: true, message: "浏览器预览状态已更新；原生窗口仅在桌面版显示" };
      } else throw new Error(`Native window operation requires desktop: ${command}`);
      if (command === "start_overlay_panel") mutatePreview(next => { next.overlay.enabled = true; });
      if (command === "stop_overlay_panel") mutatePreview(next => { next.overlay.enabled = false; });
      if (command === "toggle_overlay_panel") mutatePreview(next => { next.overlay.enabled = !next.overlay.enabled; });
      if (command === "set_overlay_drag_mode") mutatePreview(next => { next.overlay.dragMode = !!args.enabled; });
      break;
  }
  return structuredClone(result) as T;
}
export function convertFileSrc(path: string, protocol = "asset") {
  if (!isDesktop() || /^(data:|blob:|https?:|\/)/.test(path)) return path;
  return `http://${protocol}.localhost/${encodeURIComponent(path)}`;
}
