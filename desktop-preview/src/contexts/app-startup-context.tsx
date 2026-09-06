import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from "react";
import { invoke } from "@tauri-apps/api/core";
import type { HardwareInfo } from "@/lib/hardware";
import { getPreviewState, subscribePreviewState, type OverlaySettings } from "@/preview/runtime";
import { defaultOverlay } from "@/preview/runtime/state";

interface StartupContext {
  isStartupComplete: boolean;
  startupProgress: number;
  startupMessage: string;
  hardwareInfo: HardwareInfo | null;
  tools: Array<{ id: string; name: string; category: string; description: string }>;
  overlaySettings: OverlaySettings;
  saveOverlaySettings: (settings: OverlaySettings) => Promise<void>;
  overlayHotkey: string;
  saveOverlayHotkey: (shortcut: string) => Promise<string | null>;
}
const missingProvider = async () => { throw new Error("Preview startup provider missing"); };
const Context = createContext<StartupContext>({
  isStartupComplete: false, startupProgress: 0, startupMessage: "正在加载预览", hardwareInfo: null,
  tools: [], overlaySettings: defaultOverlay, saveOverlaySettings: missingProvider,
  overlayHotkey: "Shift+F10", saveOverlayHotkey: missingProvider,
});
export function useAppStartup() { return useContext(Context); }
export function AppStartupProvider({ children }: { children: ReactNode }) {
  const [hardwareInfo, setHardwareInfo] = useState<HardwareInfo | null>(null);
  const [ready, setReady] = useState(false);
  const [overlaySettings, setSettings] = useState(getPreviewState().overlay.settings);
  const [overlayHotkey, setHotkey] = useState(getPreviewState().hotkey);
  const saveOverlaySettings = useCallback(async (settings: OverlaySettings) => {
    await invoke("update_overlay_settings", { settings });
    setSettings(getPreviewState().overlay.settings);
  }, []);
  const saveOverlayHotkey = useCallback(async (shortcut: string) => {
    try { await invoke("set_overlay_hotkey", { shortcut }); setHotkey(shortcut); return null; }
    catch (error) { return error instanceof Error ? error.message : String(error); }
  }, []);
  useEffect(() => {
    let active = true;
    let scenario = getPreviewState().scenario;
    const refresh = () => {
      void invoke<HardwareInfo>("get_hardware").then(data => { if (active) setHardwareInfo(data); })
        .catch(() => { if (active) setHardwareInfo(null); }).finally(() => { if (active) setReady(true); });
    };
    refresh();
    const unsubscribe = subscribePreviewState(state => {
      setSettings(current => JSON.stringify(current) === JSON.stringify(state.overlay.settings) ? current : state.overlay.settings);
      setHotkey(state.hotkey);
      if (scenario !== state.scenario) { scenario = state.scenario; refresh(); }
    });
    return () => { active = false; unsubscribe(); };
  }, []);
  return <Context.Provider value={{
    isStartupComplete: ready, startupProgress: ready ? 100 : 20, startupMessage: ready ? "准备就绪" : "正在加载预览",
    hardwareInfo, tools: [], overlaySettings, saveOverlaySettings, overlayHotkey, saveOverlayHotkey,
  }}>{children}</Context.Provider>;
}
