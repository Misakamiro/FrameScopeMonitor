export type SensorReading = {
  hardware: string;
  hardwareType: string;
  subHardware?: string;
  name: string;
  sensorType: string;
  value: number;
  unit?: string;
};

export type SensorsResponse = {
  updatedAt: string;
  sensors: SensorReading[];
};

const DEFAULT_BASE = "http://127.0.0.1:58888";

export function getSensorBaseUrl(): string {
  if (typeof window === "undefined") return DEFAULT_BASE;
  return process.env.NEXT_PUBLIC_SENSOR_URL ?? DEFAULT_BASE;
}

export async function fetchSensors(signal?: AbortSignal): Promise<SensorsResponse> {
  const base = getSensorBaseUrl().replace(/\/$/, "");
  const res = await fetch(`${base}/api/sensors`, { signal, cache: "no-store" });
  if (!res.ok) {
    throw new Error(`传感器服务返回 ${res.status}`);
  }
  return res.json() as Promise<SensorsResponse>;
}
