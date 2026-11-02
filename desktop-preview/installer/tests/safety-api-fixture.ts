let picked = "D:\\";
Object.assign(window, { installerProbe: { pick(path: string) { picked = path; } } });
export async function invoke<T>(command: string): Promise<T> {
  const values: Record<string, unknown> = {
    get_launch_mode: "install", get_app_version: "2.1.1-beta.2",
    get_state: { installed: false, version: "", install_path: "", legacy_nsis: false },
    get_default_install_path: "C:\\Program Files\\FrameScope", check_webview2: true,
  };
  if (!(command in values)) throw new Error("Unexpected native command in browser safety test: " + command);
  return values[command] as T;
}
export async function open() { return picked; }
export async function listen() { return () => {}; }
export function getCurrentWindow() { return { close: async () => {} }; }
