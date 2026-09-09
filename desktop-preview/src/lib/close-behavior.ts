import { store } from "@/lib/store";
import { isDesktop, backendInvoke } from "@/preview/runtime/platform";

// Close-behavior contract: the UI speaks ask/close/minimize, the backend
// config (framescope-config.json CloseWindowBehavior) speaks ask/exit/
// minimize-to-tray. On desktop the config is the source of truth so the host
// can honor Alt+F4/system closes the same way the title-bar X button does;
// browser previews keep the nexbox_close_behavior storage key.
export type CloseBehavior = "ask" | "close" | "minimize";
export const CLOSE_BEHAVIOR_EVENT = "close-behavior-changed";

const toConfigValue: Record<CloseBehavior, string> = { ask: "ask", close: "exit", minimize: "minimize-to-tray" };
const fromConfigValue: Record<string, CloseBehavior> = { ask: "ask", exit: "close", "minimize-to-tray": "minimize" };

export function mapConfigCloseBehavior(value: string | undefined | null): CloseBehavior | null {
  return (value && fromConfigValue[value]) || null;
}

export async function loadCloseBehavior(): Promise<CloseBehavior> {
  if (isDesktop()) {
    try {
      const payload = await backendInvoke<{ config: { closeWindowBehavior?: string } }>("config_read");
      return mapConfigCloseBehavior(payload.config?.closeWindowBehavior) ?? "minimize";
    } catch (error) {
      console.error("config_read failed", error);
    }
  }
  try {
    const stored = await store.get<string>("nexbox_close_behavior");
    if (stored === "ask" || stored === "close" || stored === "minimize") return stored;
  } catch { /* storage blocked */ }
  return "ask";
}

export async function persistCloseBehavior(behavior: CloseBehavior): Promise<void> {
  try {
    localStorage.setItem("nexbox_close_behavior", behavior);
    await store.set("nexbox_close_behavior", behavior);
    await store.save();
  } catch { /* storage blocked; the in-session value still applies */ }
  if (isDesktop()) {
    // Mirror into the real config so the host's Alt+F4 path follows the same
    // choice the UI just saved.
    await backendInvoke("config_save", { config: { closeWindowBehavior: toConfigValue[behavior] } })
      .catch(error => console.error("config_save failed", error));
  }
  window.dispatchEvent(new CustomEvent(CLOSE_BEHAVIOR_EVENT, { detail: behavior }));
}
