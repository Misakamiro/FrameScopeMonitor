import { defaultOverlay } from "../src/preview/runtime/state";
import type { OverlaySettings } from "../src/preview/runtime/types";
export type { OverlaySettings };
let listener: ((state: ReturnType<typeof getPreviewState>) => void) | undefined;
let state = { overlay: { settings: defaultOverlay }, hotkey: "Shift+F10", scenario: "normal" };
const requests: { opacity: number; complete: () => void }[] = [];
export function invoke(command: string, args: { settings?: OverlaySettings } = {}) {
  if (command !== "update_overlay_settings") return Promise.resolve(null);
  const settings = args.settings!;
  return new Promise<void>(resolve => requests.push({ opacity: settings.opacity, complete: () => {
    state = { ...state, overlay: { settings } };
    listener?.(state);
    resolve();
  } }));
}
export function getPreviewState() { return state; }
export function subscribePreviewState(callback: typeof listener) {
  listener = callback;
  return () => { listener = undefined; };
}
Object.assign(window, { overlaySaveProbe: { requests } });
