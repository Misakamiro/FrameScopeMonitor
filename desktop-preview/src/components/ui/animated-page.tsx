import { Transition, Variants } from "framer-motion";
import { ReactNode, useEffect } from "react";
import { store } from "@/lib/store";
import { useMotionPreferences, setMotionPreference } from '@/lib/motion-preferences';

interface AnimatedPageProps {
  children: ReactNode;
}

export type TransitionMode = "slide" | "fade" | "off";

const STORE_KEY = "nexbox_page_transition";
const OLD_STORAGE_KEY = "nexbox_page_transition_enabled";
const LS_KEY = "nexbox_page_transition";

export const slideVariants: Variants = {
  initial: { opacity: 0, x: 10 },
  enter: { opacity: 1, x: 0, transition: { duration: 0.18, ease: "easeOut" } },
  exit: { opacity: 0, x: -10, transition: { duration: 0.15, ease: "easeIn" } },
};

export const fadeVariants: Variants = {
  initial: { opacity: 0 },
  enter: { opacity: 1, transition: { duration: 0.18, ease: "easeOut" } },
  exit: { opacity: 0, transition: { duration: 0.18, ease: "easeIn" } },
};

export const slideTransition: Transition = {
  type: "tween",
  ease: "easeOut",
  duration: 0.18,
};

export const fadeTransition: Transition = {
  type: "tween",
  ease: "easeOut",
  duration: 0.18,
};

export function getVariants(mode: TransitionMode): Variants {
  switch (mode) {
    case 'off': return { initial: {}, enter: {}, exit: {} };
    case "fade":
      return fadeVariants;
    case "slide":
    default:
      return slideVariants;
  }
}

export function getTransitionConfig(mode: TransitionMode): Transition {
  switch (mode) {
    case 'off': return { duration: 0 };
    case "fade":
      return fadeTransition;
    case "slide":
    default:
      return slideTransition;
  }
}

export function readTransitionMode(): TransitionMode {
  // migrate old key
  const oldVal = localStorage.getItem(OLD_STORAGE_KEY);
  if (oldVal !== null) {
    localStorage.removeItem(OLD_STORAGE_KEY);
    const mode: TransitionMode = oldVal === "true" ? "slide" : "off";
    localStorage.setItem(LS_KEY, mode);
    store.set(STORE_KEY, mode).then(() => store.save());
    return mode;
  }
  const saved = localStorage.getItem(LS_KEY);
  return saved === "slide" || saved === "off" ? saved : "fade";
}

export function useTransitionMode(): TransitionMode {
  const prefs = useMotionPreferences();
  useEffect(() => {
    if (localStorage.getItem(OLD_STORAGE_KEY) !== null) setMotionPreference(STORE_KEY, readTransitionMode());
  }, []);
  return prefs.reduced ? 'off' : prefs.mode;
}

export function AnimatedPage({ children }: AnimatedPageProps) {
  return <div style={{ width: "100%", height: "100%" }}>{children}</div>;
}
