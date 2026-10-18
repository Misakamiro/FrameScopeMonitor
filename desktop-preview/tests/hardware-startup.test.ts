import { afterEach, beforeEach, expect, test, vi } from "vitest";
const fixture = vi.hoisted(() => ({ effects: [] as Array<() => (() => void) | void>, invoke: vi.fn(), setters: [] as ReturnType<typeof vi.fn>[] }));
vi.mock("react", async importOriginal => ({
  ...await importOriginal<typeof import("react")>(),
  useEffect: (effect: () => void) => { fixture.effects.push(effect); },
  useState: (initial: unknown) => { const setter = vi.fn(); fixture.setters.push(setter); return [initial, setter]; },
  useRef: (current: unknown) => ({ current }),
  useCallback: (callback: unknown) => callback,
}));
vi.mock("@tauri-apps/api/core", () => ({ invoke: fixture.invoke }));
vi.mock("@/preview/runtime", () => ({
  getPreviewState: () => ({ scenario: "normal", overlay: { settings: {} }, hotkey: "Shift+F10" }),
  subscribePreviewState: () => () => {},
}));
vi.mock("@/preview/runtime/state", () => ({ defaultOverlay: {} }));
import { AppStartupProvider } from "../src/contexts/app-startup-context";

let cleanup: (() => void) | void;
beforeEach(() => {
  vi.useFakeTimers(); fixture.effects.length = 0; fixture.setters.length = 0; fixture.invoke.mockReset();
});
afterEach(() => { cleanup?.(); vi.unstubAllGlobals(); vi.useRealTimers(); });
function mount() { AppStartupProvider({ children: null }); cleanup = fixture.effects[0](); }

test("sensor child windows do not request unrelated static hardware inventory", async () => {
  vi.stubGlobal("window", { location: { pathname: "/sensor-monitor", hash: "", search: "" } });
  fixture.invoke.mockResolvedValue({});
  mount(); await vi.advanceTimersByTimeAsync(0);
  expect(fixture.invoke.mock.calls.filter(args => args[0] === "get_hardware")).toHaveLength(0);
});

test("inventory readiness is retried promptly without overlapping native requests", async () => {
  let resolve: (value: unknown) => void = () => {};
  fixture.invoke.mockRejectedValueOnce("Hardware inventory is still loading; sensor sampling remains available.")
    .mockImplementationOnce(() => new Promise(done => { resolve = done; }));
  mount();
  await vi.advanceTimersByTimeAsync(500);
  expect(fixture.invoke).toHaveBeenCalledTimes(2);
  await vi.advanceTimersByTimeAsync(3000);
  expect(fixture.invoke).toHaveBeenCalledTimes(2);
  const hardware = { cpu: { name: "Ready CPU" } };
  resolve(hardware); await vi.advanceTimersByTimeAsync(0);
  expect(fixture.setters[0]).toHaveBeenCalledWith(hardware);
});

test("a brief hardware request contention retries promptly", async () => {
  fixture.invoke.mockRejectedValueOnce("hardware maintenance is active or requests have not drained").mockResolvedValueOnce({});
  mount(); await vi.advanceTimersByTimeAsync(500);
  expect(fixture.invoke).toHaveBeenCalledTimes(2);
});

test("a persistent provider failure keeps the existing backoff", async () => {
  fixture.invoke.mockRejectedValue("provider initialization failed");
  mount(); await vi.advanceTimersByTimeAsync(1000);
  expect(fixture.invoke).toHaveBeenCalledTimes(1);
});

test("unmount cancels a scheduled inventory retry", async () => {
  fixture.invoke.mockRejectedValue("Hardware inventory is still loading; sensor sampling remains available.");
  mount(); await vi.advanceTimersByTimeAsync(0); cleanup?.(); cleanup = undefined;
  expect(vi.getTimerCount()).toBe(0);
  await vi.advanceTimersByTimeAsync(9000);
  expect(fixture.invoke).toHaveBeenCalledTimes(1);
  expect(vi.getTimerCount()).toBe(0);
});

test("pending inventory does not hold the application on its splash screen", async () => {
  fixture.invoke.mockRejectedValue("Hardware inventory is still loading; sensor sampling remains available.");
  mount(); await vi.advanceTimersByTimeAsync(0);
  expect(fixture.setters[2]).toHaveBeenCalledWith(true);
  await vi.advanceTimersByTimeAsync(500);
  expect(fixture.invoke).toHaveBeenCalledTimes(2);
});

test("an inventory request that never settles cannot block the application shell", async () => {
  fixture.invoke.mockImplementation(() => new Promise(() => {}));
  mount(); await vi.advanceTimersByTimeAsync(2000);
  expect(fixture.setters[2]).toHaveBeenCalledWith(true);
  expect(fixture.invoke).toHaveBeenCalledTimes(1);
  cleanup?.(); cleanup = undefined;
  expect(vi.getTimerCount()).toBe(0);
});
