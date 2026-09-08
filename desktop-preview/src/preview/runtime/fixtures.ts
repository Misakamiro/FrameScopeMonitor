import type { HardwareInfo, GpuInfo } from "../../lib/hardware";
import type { SensorsResponse, SensorReading } from "../../lib/sensors";
import type { GpuSensorData, OverlayHardwareData, PreviewState } from "./types";

export const FIXTURE_EPOCH = Date.parse("2026-09-06T12:00:00.000Z");

export function overlayFixture(state: PreviewState): OverlayHardwareData {
  const empty = state.scenario === "empty" || state.scenario === "failure";
  const missing = state.scenario === "missing-sensors";
  const wave = [0, 2, 4, 1, -2, -3, 0, 3][state.tick % 8];
  const gpu: GpuSensorData = {
    name: "NVIDIA GeForce RTX 4070 SUPER", hardware_type: "GpuNvidia",
    temperature: missing ? null : 62 + wave, usage: 72 + wave,
    fan_speed: missing ? null : 1420 + wave * 10, power: missing ? null : 168 + wave,
    clock: 2475, memory_clock: 10501, vram_used: 6.0, vram_total: 12.0,
    voltage: missing ? null : 1.05,
  };
  const gpuSensors = empty ? [] : [gpu];
  if (state.scenario === "multi-gpu") {
    gpuSensors.push({
      name: "AMD Radeon Graphics", hardware_type: "GpuAmd", temperature: 45 + wave,
      usage: 8 + wave, fan_speed: null, power: 12, clock: 600, memory_clock: null,
      vram_used: null, vram_total: null, voltage: null,
    });
  }
  const active = gpuSensors[state.activeGpuIndex] ?? gpuSensors[0];
  const capturing = state.monitor.phase === "capturing";
  return {
    fps: empty || !capturing ? null : 144 + wave,
    fps_1low: empty || !capturing ? null : 112 + wave,
    fps_01low: empty || !capturing ? null : 94 + wave,
    cpu_usage: empty ? null : 36 + wave,
    cpu_temp: empty || missing ? null : 58 + wave,
    cpu_clock: empty ? null : 4850 + wave * 10,
    cpu_voltage: empty || missing ? null : 1.18,
    cpu_power: empty || missing ? null : 68 + wave,
    cpu_fan_speed: empty || missing ? null : 1120 + wave * 10,
    gpu_temp: active?.temperature ?? null,
    gpu_usage: active?.usage ?? null,
    gpu_fan_speed: active?.fan_speed ?? null,
    gpu_power: active?.power ?? null,
    gpu_clock: active?.clock ?? null,
    gpu_voltage: active?.voltage ?? null,
    gpu_memory_clock: active?.memory_clock ?? null,
    memory_usage: empty ? null : 42 + wave / 10,
    ssd_temp: empty || missing ? null : 41,
    delta_password: null,
    game_ping: null,
    gpu_vram_used: active?.vram_used ?? null,
    gpu_vram_total: active?.vram_total ?? null,
    net_time_offset_ms: null,
    gpu_sensors: gpuSensors,
    active_gpu_index: active ? state.activeGpuIndex : 0,
  };
}

export function hardwareFixture(state: PreviewState): HardwareInfo {
  const live = overlayFixture(state);
  const gpu: GpuInfo[] = live.gpu_sensors.map((item, index) => ({
    name: item.name, vendor: (index ? "AMD" : "NVIDIA") as GpuInfo["vendor"],
    memory_gb: item.vram_total === null ? null : item.vram_total / 1024,
    driver_version: "Preview 1.0", temperature: item.temperature, usage: item.usage,
    video_processor: item.name, adapter_compatibility: index ? "AMD" : "NVIDIA",
    driver_date: "2026-09-06", installed_drivers: "Simulated", video_mode: "2560 x 1440",
    resolution_width: 2560, resolution_height: 1440, refresh_rate: 165,
    device_id: `PREVIEW_GPU_${index}`, pnp_device_id: `PREVIEW\\GPU\\${index}`,
    status: "OK", inf_filename: "preview.inf", video_architecture: null, video_memory_type: "GDDR6X",
  }));
  const empty = state.scenario === "empty";
  return {
    cpu: {
      name: empty ? "Unavailable (Preview)" : "AMD Ryzen 7 7800X3D",
      manufacturer: "AuthenticAMD", cores: 8, threads: 16, max_clock_speed: 5000,
      l2_cache_size: 8192, l3_cache_size: 98304, load_percentage: live.cpu_usage,
      architecture: "x64", socket: "AM5", l2_cache_speed: null, l3_cache_speed: null,
      current_clock_speed: live.cpu_clock, ext_clock: 100, processor_id: "PREVIEW-CPU",
      family: 25, stepping: "2", revision: "Preview", enabled_cores: 8, voltage_caps: null,
    },
    gpu,
    memory: empty ? [] : [0, 1].map(index => ({
      manufacturer: "Kingston", part_number: "PREVIEW-DDR5-6000", capacity_gb: 16,
      speed_mhz: 6000, bank_label: `DIMM ${index + 1}`, form_factor: "DIMM", memory_type: "DDR5",
      configured_clock_speed: 6000, configured_voltage: 1350, data_width: 64, total_width: 64,
      serial_number: `PREVIEW-RAM-${index}`, type_detail: "Synchronous",
    })),
    motherboard: {
      product: "B650 Preview", manufacturer: "Preview", serial_number: "PREVIEW-BOARD",
      version: "1.0", bios_vendor: "Preview", bios_version: "Preview 1.0",
      bios_release_date: "2026-09-06", system_manufacturer: "FrameScope",
      system_model: "Simulated Desktop", system_type: "x64-based PC", chassis_type: "Desktop",
    },
    disk: empty ? [] : [{
      model: "Preview NVMe SSD 1TB", size_gb: 1000, interface_type: "NVMe",
      serial_number: "PREVIEW-DISK", firmware_revision: "1.0", media_type: "SSD",
      bytes_per_sector: 512, partitions: 3, status: "OK", is_ssd: true,
    }],
    sound_card: empty ? [] : [{
      name: "Preview Audio", manufacturer: "Preview", status: "OK",
      device_id: "PREVIEW-AUDIO", pnp_device_id: "PREVIEW\\AUDIO",
    }],
    network_card: empty ? [] : [{
      name: "Preview 2.5GbE", manufacturer: "Preview", adapter_type: "Ethernet",
      mac_address: "02:00:00:00:00:01", speed_mbps: 2500, connection_name: "Preview Ethernet",
      service_name: "preview", index: 0, max_speed: 2500, guid: "PREVIEW-NETWORK",
    }],
    monitor: empty ? [] : [{
      name: "Preview 27-inch", manufacturer: "Preview", screen_width: 2560, screen_height: 1440,
      refresh_rate: 165, pnp_device_id: "PREVIEW\\DISPLAY", status: "OK", availability: 3,
    }],
  };
}

export function sensorsFixture(state: PreviewState): SensorsResponse {
  const data = overlayFixture(state);
  const sensors: SensorReading[] = [];
  const add = (hardware: string, hardwareType: string, name: string, sensorType: string, value: number | null, unit: string) => {
    if (value !== null) sensors.push({ hardware, hardwareType, name, sensorType, value, unit });
  };
  add("AMD Ryzen 7 7800X3D", "Cpu", "CPU Total", "Load", data.cpu_usage, "%");
  add("AMD Ryzen 7 7800X3D", "Cpu", "CPU Package", "Temperature", data.cpu_temp, "\u00b0C");
  add("AMD Ryzen 7 7800X3D", "Cpu", "CPU Package", "Power", data.cpu_power, "W");
  add("AMD Ryzen 7 7800X3D", "Cpu", "Core Clock", "Clock", data.cpu_clock, "MHz");
  add("AMD Ryzen 7 7800X3D", "Cpu", "CPU Core", "Voltage", data.cpu_voltage, "V");
  add("B650 Preview", "Motherboard", "CPU Fan", "Fan", data.cpu_fan_speed, "RPM");
  for (const gpu of data.gpu_sensors) {
    add(gpu.name, gpu.hardware_type, "GPU Core", "Temperature", gpu.temperature, "\u00b0C");
    add(gpu.name, gpu.hardware_type, "GPU Core", "Load", gpu.usage, "%");
    add(gpu.name, gpu.hardware_type, "GPU Package", "Power", gpu.power, "W");
    add(gpu.name, gpu.hardware_type, "GPU Fan", "Fan", gpu.fan_speed, "RPM");
    add(gpu.name, gpu.hardware_type, "GPU Core", "Clock", gpu.clock, "MHz");
    add(gpu.name, gpu.hardware_type, "GPU Memory Used", "SmallData", gpu.vram_used, "MB");
  }
  add("DDR5 Memory", "Memory", "Memory Used", "Load", data.memory_usage, "%");
  add("Preview NVMe SSD", "Storage", "Composite", "Temperature", data.ssd_temp, "\u00b0C");
  return { updatedAt: new Date(FIXTURE_EPOCH + state.tick * 1000).toISOString(), sensors };
}
