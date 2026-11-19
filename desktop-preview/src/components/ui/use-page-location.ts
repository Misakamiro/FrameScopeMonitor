import { useEffect, useRef, useState } from "react";
import { flushSync } from "react-dom";
import type { Location } from "react-router-dom";
import type { TransitionMode } from "./animated-page";
import { markMotionInteraction, playSurface, startContentTransition, useMotionPreferences } from '@/lib/motion-preferences';

export function navigationMotionKey(location: Pick<Location, 'pathname' | 'search'>) {
  const params = new URLSearchParams(location.search);
  return location.pathname + ['section','tab','board','conversation','page','block'].map(key => ':' + (params.get(key) || '')).join('');
}

export function usePageLocation(location: Location, mode: TransitionMode): Location {
  return useContentValue(location, navigationMotionKey(location), '[data-page-content]', mode);
}

export function useContentValue<T>(location: T, identity: string, selector: string, requestedMode?: TransitionMode): T {
  const prefs = useMotionPreferences();
  const mode = prefs.reduced ? 'off' : requestedMode ?? prefs.mode;
  const [displayed, setDisplayed] = useState(location);
  const committed = useRef(location);
  const previousIdentity = useRef(identity);
  const activeTransition = useRef<symbol | null>(null);

  useEffect(() => {
    if (committed.current === location) return;
    const changed = previousIdentity.current !== identity;
    if (changed) markMotionInteraction();
    const commit = () => {
      committed.current = location;
      previousIdentity.current = identity;
      setDisplayed(location);
    };
    if (mode === "off" || previousIdentity.current === identity ||
        window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      commit();
      if (changed) { const content = document.querySelector<HTMLElement>(selector); if(content) playSurface(content,true,'control'); }
      return;
    }

    if (mode === "slide") {
      const content = document.querySelector<HTMLElement>(selector);
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

    if (!document.startViewTransition) {
      commit();
      const content = document.querySelector<HTMLElement>(selector);
      const animation = content ? playSurface(content) : null;
      return () => animation?.cancel();
    }

    // Snapshot the fully composed wallpaper and glass. Fading a live ancestor
    // changes its backdrop root until opacity reaches 1 and makes blur jump.
    let cancelled = false;
    const owner = Symbol("page-transition");
    activeTransition.current = owner;
    document.documentElement.dataset.pageTransition = mode;
    const transition = startContentTransition(() => {
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
  }, [location, mode, identity, selector]);

  return displayed;
}
