type Settings = { opacity: number; display_items: unknown[]; custom_items: unknown[] };
type Callback = (event: { payload: Settings }) => void;
let settingsListener: Callback | undefined;
let resolveInitial: (value: Settings) => void;
let resolveRegistration: () => void;
let release: (() => void) | undefined;
const probe = {
  order: [] as string[], unlistens: 0,
  register() { resolveRegistration?.(); },
  initial(opacity: number) { resolveInitial?.({ opacity, display_items: [], custom_items: [] }); },
  update(opacity: number) { settingsListener?.({ payload: { opacity, display_items: [], custom_items: [] } }); },
};
Object.assign(window, { verticalProbe: probe });
export function invoke(command: string) {
  probe.order.push(command);
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
