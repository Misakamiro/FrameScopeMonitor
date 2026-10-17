export interface GpuLiveSensor {
  name: string; hardware_type: string; temperature: number | null; usage: number | null;
  fan_speed: number | null; power: number | null; clock: number | null; memory_clock: number | null;
  vram_used: number | null; vram_total: number | null; voltage: number | null;
}
export interface MemoryStatus { total: number; available: number; used: number; usage_percent: number; }
export interface DiskStatus { name: string; total_gb: number; available_gb: number; used_gb: number; usage_percent: number; }
export interface LiveHardwareSnapshot {
  overlay: { cpu_usage: number | null; cpu_temp: number | null; memory_usage: number | null; gpu_sensors: GpuLiveSensor[]; active_gpu_index: number; };
  memory: MemoryStatus | null;
  disk: DiskStatus | null;
  observedAt: number;
}
export function startLiveHardwarePolling(read: (command: string) => Promise<unknown>,
  publish: (value: LiveHardwareSnapshot | null) => void): () => void {
  let stopped = false;
  let next: ReturnType<typeof setTimeout> | undefined;
  let expiry: ReturnType<typeof setTimeout> | undefined;
  const refresh = async () => {
    try {
      let observedAt = 0;
      const replies = await Promise.allSettled([
        read("get_overlay_hardware_data").then(value => { observedAt = Date.now(); return value; }),
        read("get_memory_status"), read("get_disk_status"),
      ]);
      if (stopped) return;
      if (replies.some(reply => reply.status === "rejected")) { publish(null); return; }
      const [overlay, memory, disk] = replies.map(reply => (reply as PromiseFulfilledResult<unknown>).value);
      if (expiry) clearTimeout(expiry);
      const remaining = 2000 - (Date.now() - observedAt);
      if (remaining <= 0) publish(null);
      else {
        publish({ overlay: overlay as LiveHardwareSnapshot["overlay"],
          memory: memory as MemoryStatus | null, disk: disk as DiskStatus | null, observedAt });
        expiry = setTimeout(() => { if (!stopped) publish(null); }, remaining);
      }
    } catch {
      if (!stopped) publish(null);
    } finally {
      if (!stopped) next = setTimeout(() => { void refresh(); }, 1000);
    }
  };
  void refresh();
  return () => { stopped = true; if (next) clearTimeout(next); if (expiry) clearTimeout(expiry); };
}
