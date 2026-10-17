import { useEffect, useRef, useState } from "react";
import { flushSync } from "react-dom";
import type { Location } from "react-router-dom";
import type { TransitionMode } from "./animated-page";

export function usePageLocation(location: Location, mode: TransitionMode): Location {
  const [displayed, setDisplayed] = useState(location);
  const committed = useRef(location);
  const activeTransition = useRef<symbol | null>(null);

  useEffect(() => {
    if (committed.current === location) return;
    const commit = () => {
      committed.current = location;
      setDisplayed(location);
    };
    if (mode === "off" || committed.current.pathname === location.pathname ||
        window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      commit();
      return;
    }

    if (mode === "slide") {
      const content = document.querySelector<HTMLElement>("[data-page-content]");
      if (!content?.animate) { commit(); return; }
      let cancelled = false;
      const owner = Symbol("page-slide");
      activeTransition.current = owner;
      document.documentElement.dataset.pageTransition = mode;
      let animation = content.animate(
        [{ transform: "translateX(0)" }, { transform: "translateX(-10px)" }],
        { duration: 150, easing: "ease-in", fill: "forwards" },
      );
      const clear = () => {
        animation.cancel();
        if (activeTransition.current === owner) {
          activeTransition.current = null;
          delete document.documentElement.dataset.pageTransition;
        }
      };
      void animation.finished.then(() => {
        if (cancelled) return;
        flushSync(commit);
        animation.cancel();
        animation = content.animate(
          [{ transform: "translateX(10px)" }, { transform: "translateX(0)" }],
          { duration: 180, easing: "ease-out", fill: "forwards" },
        );
        void animation.finished.then(clear, clear);
      }, clear);
      return () => { cancelled = true; clear(); };
    }

    if (!document.startViewTransition) { commit(); return; }

    // Snapshot the fully composed wallpaper and glass. Fading a live ancestor
    // changes its backdrop root until opacity reaches 1 and makes blur jump.
    let cancelled = false;
    const owner = Symbol("page-transition");
    activeTransition.current = owner;
    document.documentElement.dataset.pageTransition = mode;
    const transition = document.startViewTransition(() => {
      if (!cancelled) flushSync(commit);
    });
    const removeListener = () => {
      if (activeTransition.current === owner) {
        activeTransition.current = null;
        delete document.documentElement.dataset.pageTransition;
      }
    };
    void transition.ready.catch(() => undefined);
    void transition.finished.then(removeListener, removeListener);
    return () => {
      cancelled = true;
      removeListener();
      transition.skipTransition();
    };
  }, [location, mode]);

  return displayed;
}
