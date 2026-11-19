import { useLayoutEffect, useRef, useState } from "react";
import { getMotionPreferences, playSurface, useMotionPreferences } from '../lib/motion-preferences';

export const reducedMotion = () =>
  getMotionPreferences().reduced || getMotionPreferences().mode === 'off';

// Keep a closing floating surface mounted only until its fade completes.
export function useReportPresence<T extends HTMLElement>(open: boolean) {
  const [present, setPresent] = useState(open);
  const ref = useRef<T>(null);
  const prefs = useMotionPreferences();
  useLayoutEffect(() => {
    if (open && !present) {
      setPresent(true);
      return;
    }
    const element = ref.current;
    if (!element || !present) return;
    let cancelled = false;
    const animation = playSurface(element, open, 'menu');
    const finish = () => { if (!cancelled && !open) setPresent(false); };
    if (animation) void animation.finished.then(finish, finish); else finish();
    return () => { cancelled = true; animation?.cancel(); };
  }, [open, present, prefs]);
  return { present, ref };
}
