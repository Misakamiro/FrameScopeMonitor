import { afterEach, beforeEach, expect, test, vi } from "vitest";
const fixture = vi.hoisted(() => ({
  effects: [] as Array<() => (() => void) | void>,
  setters: [] as ReturnType<typeof vi.fn>[], invoke: vi.fn(),
}));
vi.mock("react", async original => ({
  ...await original<typeof import("react")>(),
  useEffect: (effect: () => void) => { fixture.effects.push(effect); },
  useState: (initial: unknown) => { const set = vi.fn(); fixture.setters.push(set); return [initial, set]; },
  useRef: (current: unknown) => ({ current }), useCallback: (callback: unknown) => callback,
  useMemo: (factory: () => unknown) => factory(),
}));
vi.mock("@tauri-apps/api/core", () => ({ invoke: fixture.invoke }));
vi.mock("@tauri-apps/api/event", () => ({ listen: async () => () => {} }));
vi.mock("@chakra-ui/react", () => Object.fromEntries([
  ...["Box", "Text", "VStack", "HStack", "Input", "InputGroup", "InputLeftElement", "Badge", "Spinner", "Center", "Flex", "Heading", "Tag", "TagLabel", "Tooltip", "IconButton", "Menu", "MenuButton", "MenuList", "MenuItem", "Button"].map(name => [name, name]),
  ["useColorModeValue", (first: unknown) => first],
]));
vi.mock("framer-motion", () => ({ motion: { div: "div" }, AnimatePresence: "div" }));
import SensorMonitorPage from "../src/pages/SensorMonitorPage";
const reply = { sensors: [{ hardware: "CPU", hardwareType: "CPU", identifier: "/cpu/load", name: "CPU Total", sensorType: "Load", value: 35, unit: "%" }], updatedAt: "fresh" };
let cleanup: Array<() => void> = [];
beforeEach(() => {
  vi.useFakeTimers(); fixture.effects.length = 0; fixture.setters.length = 0; fixture.invoke.mockReset();
  vi.stubGlobal("document", { hidden: false, body: { style: {} }, addEventListener: vi.fn(), removeEventListener: vi.fn() });
});
afterEach(() => { cleanup.forEach(stop => stop()); cleanup = []; vi.unstubAllGlobals(); vi.useRealTimers(); });
function mount() { SensorMonitorPage(); cleanup = fixture.effects.map(effect => effect()).filter((stop): stop is () => void => typeof stop === "function"); }

test("one missed refresh retains the fresh sensor list", async () => {
  fixture.invoke.mockResolvedValueOnce(reply).mockRejectedValueOnce("temporary read failure").mockResolvedValue(reply);
  mount(); await vi.advanceTimersByTimeAsync(1000);
  expect(fixture.setters[0]).toHaveBeenCalledWith(reply.sensors);
  expect(fixture.setters[0]).not.toHaveBeenCalledWith([]);
  expect(fixture.setters[3]).not.toHaveBeenCalledWith("temporary read failure");
});

test("a stalled request expires old values and cannot overlap new polls", async () => {
  fixture.invoke.mockResolvedValueOnce(reply).mockImplementation(() => new Promise(() => {}));
  mount(); await vi.advanceTimersByTimeAsync(2500);
  expect(fixture.invoke).toHaveBeenCalledTimes(2);
  expect(fixture.setters[0]).toHaveBeenLastCalledWith([]);
  expect(fixture.setters[3]).toHaveBeenLastCalledWith("传感器数据已过期，正在重新读取");
});

test("closing the sensor window cancels timers and ignores a late reply", async () => {
  let resolve: (value: unknown) => void = () => {};
  fixture.invoke.mockImplementation(() => new Promise(done => { resolve = done; }));
  mount(); cleanup.forEach(stop => stop()); cleanup = [];
  resolve(reply); await vi.advanceTimersByTimeAsync(5000);
  expect(fixture.setters[0]).not.toHaveBeenCalled();
  expect(vi.getTimerCount()).toBe(0);
});
