import { useLayoutEffect, useRef, useState } from 'react';
import { playSurface, useMotionPreferences, type SurfaceMotion } from '@/lib/motion-preferences';

// The positioning node stays outside this ref; only its inner visual surface moves.
export function useSurfacePresence<T extends HTMLElement>(open: boolean, kind: SurfaceMotion = 'menu', direction = 1) {
  const [present, setPresent] = useState(open);
  const ref = useRef<T>(null);
  const prefs = useMotionPreferences();
  useLayoutEffect(() => {
    if (open && !present) { setPresent(true); return; }
    if (!present || !ref.current) return;
    let cancelled = false;
    const animation = playSurface(ref.current, open, kind, direction);
    const finish = () => { if (!cancelled && !open) setPresent(false); };
    if (animation) void animation.finished.then(finish, finish); else finish();
    return () => { cancelled = true; animation?.cancel(); };
  }, [open, present, kind, direction, prefs]);
  return { present, ref };
}
