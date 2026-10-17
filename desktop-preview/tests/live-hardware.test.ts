import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { startLiveHardwarePolling, type LiveHardwareSnapshot } from "../src/lib/live-hardware";
beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());
const overlay = { cpu_usage: 35, cpu_temp: 55, memory_usage: 40, gpu_sensors: [], active_gpu_index: 0 };
test("application polling retains a real snapshot across page subscriptions", async () => {
  const read = vi.fn(async (command: string) => command === "get_overlay_hardware_data" ? overlay : null);
  let latest: LiveHardwareSnapshot | null = null;
  const stop = startLiveHardwarePolling(read, value => { latest = value; });
  await vi.advanceTimersByTimeAsync(0);
  expect(latest?.overlay.cpu_usage).toBe(35);
  // Navigating does not own or restart the application sampler.
  expect(read).toHaveBeenCalledTimes(3);
  await vi.advanceTimersByTimeAsync(1000);
  expect(latest?.overlay.cpu_usage).toBe(35);
  stop(); expect(vi.getTimerCount()).toBe(0);
});
test("an in-flight refresh never overlaps and a stalled refresh expires old numbers", async () => {
  const publish = vi.fn(); let calls = 0;
  const read = vi.fn((command: string) => ++calls <= 3 ? Promise.resolve(command === "get_overlay_hardware_data" ? overlay : null) : new Promise(() => {}));
  const stop = startLiveHardwarePolling(read, publish);
  await vi.advanceTimersByTimeAsync(0);
  expect(publish.mock.calls[0]?.[0]?.overlay.cpu_usage).toBe(35);
  await vi.advanceTimersByTimeAsync(3000);
  expect(read).toHaveBeenCalledTimes(6);
  expect(publish).toHaveBeenLastCalledWith(null);
  stop(); expect(vi.getTimerCount()).toBe(0);
});
test("unmount ignores late replies", async () => {
  let complete: (value: unknown) => void = () => {};
  const read = vi.fn((command: string) => command === "get_overlay_hardware_data" ? new Promise(done => { complete = done; }) : Promise.resolve(null));
  const publish = vi.fn(); const stop = startLiveHardwarePolling(read, publish);
  stop(); complete(overlay); await vi.advanceTimersByTimeAsync(5000);
  expect(publish).not.toHaveBeenCalled(); expect(vi.getTimerCount()).toBe(0);
});

test("one failed subrequest cannot start another cycle while its siblings are pending", async () => {
  let calls = 0;
  const read = vi.fn((command: string) => {
    if (++calls <= 3) return Promise.resolve(command === "get_overlay_hardware_data" ? overlay : null);
    return command === "get_overlay_hardware_data" ? Promise.reject("failed") : new Promise(() => {});
  });
  const publish = vi.fn(); const stop = startLiveHardwarePolling(read, publish);
  await vi.advanceTimersByTimeAsync(4000);
  expect(read).toHaveBeenCalledTimes(6);
  expect(publish).toHaveBeenLastCalledWith(null);
  stop();
});
