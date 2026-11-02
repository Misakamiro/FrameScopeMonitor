type Settings = { opacity: number; display_items: unknown[]; custom_items: unknown[]; item_width: number };
type Callback = (event: { payload: Settings }) => void;
let settingsListener: Callback | undefined;
let resolveInitial: (value: Settings) => void;
let resolveRegistration: () => void;
let release: (() => void) | undefined;
let resolveInput: (() => void) | undefined;
const probe = {
  order: [] as string[], unlistens: 0, failures: [] as string[],
  register() { resolveRegistration?.(); },
  initial(opacity: number) { resolveInitial?.({ opacity, display_items: [], custom_items: [], item_width: 220 }); },
  update(opacity: number) { settingsListener?.({ payload: { opacity, display_items: [], custom_items: [], item_width: 220 } }); },
  inputReady() { resolveInput?.(); },
};
Object.assign(window, { verticalProbe: probe });
export function invoke(command: string, args?: { error?: string }) {
  probe.order.push(command);
  if (command === "vertical_overlay_ready" && args?.error) probe.failures.push(args.error);
  if (command === "set_vertical_overlay_click_through" && new URLSearchParams(location.search).has("input-failure")) return Promise.reject(new Error("input styles unavailable"));
  if (command === "set_vertical_overlay_click_through" && new URLSearchParams(location.search).has("input-pending")) return new Promise<void>(resolve => { resolveInput = resolve; });
  if (command === "get_overlay_current_settings") return new Promise<Settings>(resolve => { resolveInitial = resolve; });
  return Promise.resolve();
}
export type UnlistenFn = () => void;
export function listen(event: string, callback: Callback) {
  if (event !== "vertical-overlay-settings") return Promise.resolve(() => {});
  probe.order.push("listen-start");
  return new Promise<UnlistenFn>(resolve => {
    resolveRegistration = () => {
      settingsListener = callback;
      probe.order.push("listen-registered");
      release = () => { probe.unlistens++; settingsListener = undefined; };
      resolve(release);
    };
  });
}
const win = { onMoved: () => Promise.resolve(() => {}), startDragging: () => Promise.resolve() };
export function getCurrentWindow() { return win; }
