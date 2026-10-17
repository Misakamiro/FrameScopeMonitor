import { createContext, useContext, useEffect, useState, useCallback, useRef, type ReactNode } from "react";
import { invoke } from "@tauri-apps/api/core";
import type { HardwareInfo } from "@/lib/hardware";
import { getPreviewState, subscribePreviewState, type OverlaySettings } from "@/preview/runtime";
import { defaultOverlay } from "@/preview/runtime/state";
import { isMainWindow } from "@/preview/runtime/platform";
import { startLiveHardwarePolling, type LiveHardwareSnapshot } from "@/lib/live-hardware";

interface StartupContext {
  isStartupComplete: boolean;
  startupProgress: number;
  startupMessage: string;
  hardwareInfo: HardwareInfo | null;
  liveHardware: LiveHardwareSnapshot | null;
  tools: Array<{ id: string; name: string; category: string; description: string }>;
  overlaySettings: OverlaySettings;
  saveOverlaySettings: (settings: OverlaySettings) => Promise<void>;
  overlayHotkey: string;
  saveOverlayHotkey: (shortcut: string) => Promise<string | null>;
}
const missingProvider = async () => { throw new Error("Preview startup provider missing"); };
const Context = createContext<StartupContext>({
  isStartupComplete: false, startupProgress: 0, startupMessage: "正在加载预览", hardwareInfo: null,
  liveHardware: null, tools: [], overlaySettings: defaultOverlay, saveOverlaySettings: missingProvider,
  overlayHotkey: "Shift+F10", saveOverlayHotkey: missingProvider,
});
export function useAppStartup() { return useContext(Context); }
export function AppStartupProvider({ children }: { children: ReactNode }) {
  const [hardwareInfo, setHardwareInfo] = useState<HardwareInfo | null>(null);
  const [liveHardware, setLiveHardware] = useState<LiveHardwareSnapshot | null>(null);
  const [ready, setReady] = useState(false);
  const [overlaySettings, setSettings] = useState(getPreviewState().overlay.settings);
  const [overlayHotkey, setHotkey] = useState(getPreviewState().hotkey);
  const overlaySaveQueue = useRef<Promise<void>>(Promise.resolve());
  const latestOverlaySave = useRef(0);
  const overlaySavePending = useRef(false);
  const saveOverlaySettings = useCallback((settings: OverlaySettings) => {
    const version = ++latestOverlaySave.current;
    overlaySavePending.current = true;
    setSettings(settings);
    const save = overlaySaveQueue.current.then(async () => {
      try {
        await invoke("update_overlay_settings", { settings });
      } finally {
        if (version === latestOverlaySave.current) {
          overlaySavePending.current = false;
          setSettings(getPreviewState().overlay.settings);
        }
      }
    });
    // 一次失败不会堵塞之后的保存；调用者仍收到本次失败。
    overlaySaveQueue.current = save.catch(() => {});
    return save;
  }, []);
  const saveOverlayHotkey = useCallback(async (shortcut: string) => {
    try { await invoke("set_overlay_hotkey", { shortcut }); setHotkey(shortcut); return null; }
    catch (error) { return error instanceof Error ? error.message : String(error); }
  }, []);
  useEffect(() => {
    let active = true;
    let scenario = getPreviewState().scenario;
    const mainWindow = isMainWindow();
    let retryTimer: ReturnType<typeof setTimeout> | null = null;
    const refresh = () => {
      void invoke<HardwareInfo>("get_hardware").then(data => { if (active) { setHardwareInfo(data); setReady(true); } })
        .catch(error => {
          if (!active) return;
          setHardwareInfo(null);
          // Retry instead of pinning the page on "loading" forever - the
          // backend may simply not be up yet.
          if (retryTimer) clearTimeout(retryTimer);
          // Inventory is built in the background; a pending query or a short
          // collision with the sensor poller must not add eight seconds.
          const pending = /Hardware inventory is still loading|hardware maintenance is active or requests have not drained/.test(String(error));
          if (!pending) setReady(true);
          retryTimer = setTimeout(() => { if (active) refresh(); }, pending ? 500 : 8000);
        });
    };
    if (mainWindow) refresh(); else setReady(true);
    if (typeof window !== "undefined" && "__TAURI_INTERNALS__" in window) {
      void invoke<string>("preview_native", { command: "get_overlay_hotkey", args: {} })
        .then(value => { if (active && value) setHotkey(value); })
        .catch(() => { /* The persisted preview default remains visible until native startup is ready. */ });
    }
    const unsubscribe = subscribePreviewState(state => {
      if (!overlaySavePending.current) {
        setSettings(current => JSON.stringify(current) === JSON.stringify(state.overlay.settings) ? current : state.overlay.settings);
      }
      setHotkey(state.hotkey);
      if (scenario !== state.scenario) { scenario = state.scenario; if (mainWindow) refresh(); }
    });
    return () => { active = false; if (retryTimer) clearTimeout(retryTimer); unsubscribe(); };
  }, []);
  useEffect(() => {
    if (!isMainWindow()) return;
    return startLiveHardwarePolling(command => invoke(command), setLiveHardware);
  }, []);
  return <Context.Provider value={{
    isStartupComplete: ready, startupProgress: ready ? 100 : 20, startupMessage: ready ? "准备就绪" : "正在加载预览",
    hardwareInfo, liveHardware, tools: [], overlaySettings, saveOverlaySettings, overlayHotkey, saveOverlayHotkey,
  }}>{children}</Context.Provider>;
}
