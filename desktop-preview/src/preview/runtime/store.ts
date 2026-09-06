type ChangeCallback<T> = (value: T | undefined) => void;
const PREFIX = "framescope.preview.store.";
export class LazyStore {
  constructor(private path: string) {}
  private read(): Record<string, unknown> {
    try { return JSON.parse(localStorage.getItem(PREFIX + this.path) || "{}"); } catch { return {}; }
  }
  async get<T>(key: string): Promise<T | undefined> { return this.read()[key] as T | undefined; }
  async set(key: string, value: unknown): Promise<void> {
    const values = this.read(); values[key] = value;
    localStorage.setItem(PREFIX + this.path, JSON.stringify(values));
    window.dispatchEvent(new CustomEvent(PREFIX + this.path, { detail: { key, value } }));
  }
  async has(key: string) { return Object.prototype.hasOwnProperty.call(this.read(), key); }
  async delete(key: string) { const values = this.read(); delete values[key]; localStorage.setItem(PREFIX + this.path, JSON.stringify(values)); return true; }
  async clear() { localStorage.removeItem(PREFIX + this.path); }
  async keys() { return Object.keys(this.read()); }
  async values() { return Object.values(this.read()); }
  async entries() { return Object.entries(this.read()); }
  async length() { return Object.keys(this.read()).length; }
  async save() { /* Mutations persist atomically in the isolated webview profile. */ }
  async reload() { this.read(); }
  async onKeyChange<T>(key: string, callback: ChangeCallback<T>) {
    const listener = (event: Event) => { const value = (event as CustomEvent).detail; if (value.key === key) callback(value.value); };
    window.addEventListener(PREFIX + this.path, listener);
    return () => window.removeEventListener(PREFIX + this.path, listener);
  }
}
export class Store extends LazyStore {
  static async load(path: string) { return new Store(path); }
}
export async function load(path: string) { return new Store(path); }
