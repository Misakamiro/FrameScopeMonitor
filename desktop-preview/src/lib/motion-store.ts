import { store } from './store';
import { motionKeys, setMotionPreference, startMotionEnvironment } from './motion-preferences';

// The store broadcasts key changes to every native WebView, including reports.
export function startMotionStore() {
  let disposed = false;
  const stops: (() => void)[] = [startMotionEnvironment()];
  for (const key of motionKeys) {
    let changed = false;
    void store.onKeyChange<unknown>(key, value => { changed = true; if (!disposed) setMotionPreference(key, value); }).then(stop => {
      if (disposed) stop(); else stops.push(stop);
    }).catch(() => {});
    void store.get<unknown>(key).then(value => { if (!disposed && !changed && value !== undefined && value !== null) setMotionPreference(key, value); }).catch(() => {});
  }
  return () => { disposed = true; stops.forEach(stop => stop()); };
}
