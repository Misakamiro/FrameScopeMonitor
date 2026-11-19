export interface SensorBinding { category: string; device_id: string; sensor_id: string }
export interface OverlayItemOptions {
  binding?: SensorBinding | null;
  font_color?: string | null;
  inherit_font_color?: boolean | null;
  font_size?: number | null;
  font_bold?: boolean | null;
  metric?: string | null;
}
export interface OverlaySensor extends SensorBinding {
  device_name: string; name: string; unit: string; value: number | null; available: boolean;
}
export interface ResolvedOverlayItem {
  core_rows?: Array<{ label: string; cells: ResolvedOverlayItem[] }>;
  id: string; metric: string; label: string; value: string;
  color: string | null; font_size: number; font_bold: boolean;
}
export const OVERLAY_METRICS: Record<string, { label: string; unit: string }> = {
  cpu_clock: { label: 'CPU 频率', unit: 'MHz' }, cpu_voltage: { label: 'CPU 电压（Vcore / VID）', unit: 'V' },
  cpu_usage: { label: 'CPU 占用', unit: '%' }, cpu_temp: { label: 'CPU 温度', unit: '°C' },
  cpu_power: { label: 'CPU 功耗', unit: 'W' }, cpu_fan_speed: { label: 'CPU 风扇', unit: 'RPM' },
  gpu_clock: { label: 'GPU 核心频率', unit: 'MHz' }, gpu_memory_clock: { label: 'GPU 显存频率', unit: 'MHz' },
  gpu_voltage: { label: 'GPU 电压', unit: 'V' }, gpu_usage: { label: 'GPU 占用', unit: '%' },
  gpu_temp: { label: 'GPU 温度', unit: '°C' }, gpu_power: { label: 'GPU 功耗', unit: 'W' },
  gpu_fan_speed: { label: 'GPU 风扇', unit: 'RPM' }, gpu_vram: { label: 'GPU 显存用量', unit: 'GB' },
  memory_usage: { label: '内存占用', unit: '%' }, ssd_temp: { label: '硬盘温度', unit: '°C' },
};
