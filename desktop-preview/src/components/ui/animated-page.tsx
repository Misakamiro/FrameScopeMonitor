import { motion, Transition, Variants } from "framer-motion";
import { ReactNode, useEffect, useState } from "react";
import { store } from "@/lib/store";

interface AnimatedPageProps {
  children: ReactNode;
}

export type TransitionMode = "slide" | "fade" | "off";

const STORE_KEY = "nexbox_page_transition";
const OLD_STORAGE_KEY = "nexbox_page_transition_enabled";
const LS_KEY = "nexbox_page_transition";
const EVENT_NAME = "page-transition-setting-changed";

export const slideVariants: Variants = {
  initial: { opacity: 0, x: 10 },
  enter: { opacity: 1, x: 0, transition: { duration: 0.18, ease: "easeOut" } },
  exit: { opacity: 0, x: -10, transition: { duration: 0.15, ease: "easeIn" } },
};

export const fadeVariants: Variants = {
  initial: { opacity: 0 },
  enter: { opacity: 1, transition: { duration: 0.18, ease: "easeOut" } },
  exit: { opacity: 0, transition: { duration: 0.15, ease: "easeIn" } },
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
    case "fade":
      return fadeVariants;
    case "slide":
    default:
      return slideVariants;
  }
}

export function getTransitionConfig(mode: TransitionMode): Transition {
  switch (mode) {
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
  return (localStorage.getItem(LS_KEY) as TransitionMode) || "fade";
}

export function useTransitionMode(): TransitionMode {
  const [mode, setMode] = useState<TransitionMode>(readTransitionMode);

  useEffect(() => {
    (async () => {
      let saved = await store.get<string>(STORE_KEY) as TransitionMode | null;
      if (saved === "slide" || saved === "fade" || saved === "off") {
        setMode(saved);
      }
    })();
  }, []);

  useEffect(() => {
    const handler = async () => {
      setMode(readTransitionMode());
    };
    window.addEventListener(EVENT_NAME, handler);
    return () => window.removeEventListener(EVENT_NAME, handler);
  }, []);

  return mode;
}

export function AnimatedPage({ children }: AnimatedPageProps) {
  const mode = useTransitionMode();
  const [animating, setAnimating] = useState(false);

  if (mode === "off") {
    return <div style={{ width: "100%", height: "100%" }}>{children}</div>;
  }

  return (
    <motion.div
      initial="initial"
      animate="enter"
      exit="exit"
      variants={getVariants(mode)}
      onAnimationStart={() => setAnimating(true)}
      onAnimationComplete={() => setAnimating(false)}
      className={animating ? "page-animating" : undefined}
      style={{ width: "100%", height: "100%" }}
    >
      {children}
    </motion.div>
  );
}
