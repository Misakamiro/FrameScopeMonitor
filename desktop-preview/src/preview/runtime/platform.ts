import type { PreviewState } from "./types";

export const STATE_KEY = "framescope.desktop-preview.runtime.v1";
export const STATE_EVENT = "framescope-preview-state";
export function isDesktop(): boolean {
  return typeof window !== "undefined" && "__TAURI_INTERNALS__" in window && !(window as any).__FRAMESCOPE_BROWSER_PREVIEW__;
}
export function isMainWindow(): boolean {
  if (typeof window === "undefined") return true;
  const metadata = (window as unknown as { __TAURI_INTERNALS__?: { metadata?: { currentWindow?: { label?: string } } } }).__TAURI_INTERNALS__?.metadata;
  if (metadata?.currentWindow?.label) return metadata.currentWindow.label === "main";
  return !/sensor-monitor|vertical-overlay|tray-menu/.test(window.location.pathname + window.location.hash + window.location.search);
}
export async function nativeOperation<T>(command: string, args: Record<string, unknown> = {}): Promise<T> {
  if (!isDesktop()) throw new Error(`Native window operation requires desktop: ${command}`);
  const native = await import("@preview/native-core");
  return native.invoke<T>("preview_native", { command, args });
}

// Commands that only exist on the real capture backend; browser previews must
// never send them (they have local equivalents in the preview switch).
const backendOnlyCommands = new Set([
  "targets_save", "processes_refresh", "monitor_start", "monitor_stop",
  "reports_list", "report_open", "report_open_directory", "report_regenerate", "report_delete",
  "config_save", "config_read",
]);

/** Backend-only commands: desktop forwards, browser previews reject. */
export async function backendInvoke<T = unknown>(command: string, args: Record<string, unknown> = {}): Promise<T> {
  if (!backendOnlyCommands.has(command)) throw new Error(`Unsupported backend command: ${command}`);
  if (!isDesktop()) throw new Error(`Backend command requires desktop: ${command}`);
  return nativeOperation<T>(command, args);
}
export async function emitPreviewEvent(event: string, payload: unknown): Promise<void> {
  if (isDesktop()) {
    const { emit } = await import("@tauri-apps/api/event");
    await emit(event, payload);
  }
}
export function readPersistedState(): PreviewState | undefined {
  try {
    const raw = globalThis.localStorage?.getItem(STATE_KEY);
    if (!raw) return;
    const state = JSON.parse(raw) as PreviewState;
    if (state.version === 1 && Number.isInteger(state.revision) && Array.isArray(state.targets)
      && Array.isArray(state.reports) && state.config && state.monitor && state.overlay
      && ["normal", "multi-gpu", "missing-sensors", "empty", "failure"].includes(state.scenario)) return state;
  } catch { /* A blocked storage API must not prevent an in-memory preview. */ }
}
export function persistState(state: PreviewState): void {
  try { globalThis.localStorage?.setItem(STATE_KEY, JSON.stringify(state)); }
  catch { /* Private browsing or storage quotas leave the session in memory. */ }
}
export function connectStateBus(receive: (state: PreviewState) => void): { send: (state: PreviewState) => void; close: () => void } {
  let closed = false;
  let unlisten: (() => void) | undefined;
  // Node's BroadcastChannel holds tests open; only webviews own a channel.
  const channel = typeof window !== "undefined" && typeof BroadcastChannel !== "undefined"
    ? new BroadcastChannel(STATE_EVENT) : undefined;
  if (channel) channel.onmessage = event => receive(event.data);
  const storageListener = (event: StorageEvent) => {
    if (event.key !== STATE_KEY || !event.newValue) return;
    try { receive(JSON.parse(event.newValue)); } catch { /* Ignore malformed external storage writes. */ }
  };
  if (typeof window !== "undefined") window.addEventListener("storage", storageListener);
  if (isDesktop()) {
    void import("@tauri-apps/api/event").then(({ listen }) =>
      listen<PreviewState>(STATE_EVENT, event => receive(event.payload)),
    ).then(fn => { if (closed) fn(); else unlisten = fn; }).catch(console.error);
  }
  return {
    send(state) {
      channel?.postMessage(state);
      void emitPreviewEvent(STATE_EVENT, state).catch(console.error);
    },
    close() {
      closed = true;
      channel?.close();
      unlisten?.();
      if (typeof window !== "undefined") window.removeEventListener("storage", storageListener);
    },
  };
}
