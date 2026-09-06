import { mockIPC, mockWindows, mockConvertFileSrc } from "@tauri-apps/api/mocks";

if (!("__TAURI_INTERNALS__" in window)) {
  (window as any).__FRAMESCOPE_BROWSER_PREVIEW__ = true;
  mockWindows("main");
  mockConvertFileSrc("windows");
  mockIPC((command) => {
    if (command === "plugin:window|is_maximized") return false;
    if (command === "plugin:window|is_visible") return true;
    if (command === "plugin:window|scale_factor") return devicePixelRatio;
    if (command === "plugin:window|outer_position") return { x: 0, y: 0 };
    if (command === "plugin:window|inner_size") return { width: innerWidth, height: innerHeight };
    throw new Error(`Desktop-only API used in browser: ${command}`);
  }, { shouldMockEvents: true });
}
