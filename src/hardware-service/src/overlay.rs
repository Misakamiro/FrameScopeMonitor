//! Overlay aggregation: builds the frontend `OverlayHardwareData` snapshot
//! (snake_case contract shared with the NexBox-derived UI) from one provider
//! snapshot, and carries forward the previous snapshot's non-null fields so a
//! transiently missing sensor never blinks the overlay to "--".

use serde::Serialize;
use serde_json::Value;
use std::collections::HashMap;
use std::time::{Duration, Instant};

#[derive(Debug, Clone, Default, PartialEq, Serialize)]
pub struct GpuSensorData {
    pub name: String,
    pub identifier: String,
    pub hardware_type: String,
    pub temperature: Option<f64>,
    pub usage: Option<f64>,
    pub fan_speed: Option<f64>,
    pub power: Option<f64>,
    pub clock: Option<f64>,
    pub memory_clock: Option<f64>,
    pub vram_used: Option<f64>,
    pub vram_total: Option<f64>,
    pub voltage: Option<f64>,
}

#[derive(Debug, Clone, Default, PartialEq, Serialize)]
pub struct OverlayData {
    pub fps: Option<f64>,
    pub fps_1low: Option<f64>,
    pub fps_01low: Option<f64>,
    pub cpu_usage: Option<f64>,
    pub cpu_temp: Option<f64>,
    pub cpu_clock: Option<f64>,
    pub cpu_voltage: Option<f64>,
    pub cpu_power: Option<f64>,
    pub cpu_fan_speed: Option<f64>,
    pub gpu_temp: Option<f64>,
    pub gpu_usage: Option<f64>,
    pub gpu_fan_speed: Option<f64>,
    pub gpu_power: Option<f64>,
    pub gpu_clock: Option<f64>,
    pub gpu_voltage: Option<f64>,
    pub gpu_memory_clock: Option<f64>,
    pub memory_usage: Option<f64>,
    pub ssd_temp: Option<f64>,
    pub delta_password: Option<String>,
    pub game_ping: Option<u32>,
    pub gpu_vram_used: Option<f64>,
    pub gpu_vram_total: Option<f64>,
    pub net_time_offset_ms: Option<f64>,
    /// Per-core CPU clocks (MHz, index = core number - 1). LibreHardwareMonitor
    /// names them "Core #N"; cores the platform does not expose stay null.
    pub cpu_core_clocks: Vec<Option<f64>>,
    /// Per-core CPU voltages (V, "Core #N" VIDs where the platform has them).
    pub cpu_core_voltages: Vec<Option<f64>>,
    pub gpu_sensors: Vec<GpuSensorData>,
    pub active_gpu_index: usize,
    /// 目标进程最近呈现模式（问题 6 方案 E，ETW 事件 ID 推断）：
    /// "composited" / "independent-flip" / "legacy-exclusive"；None = 无活跃信号
    pub present_mode: Option<String>,
}

#[derive(Debug, Clone, Copy)]
struct SensorRow<'a> {
    hardware: &'a str,
    identifier: &'a str,
    hardware_type: &'a str,
    name: &'a str,
    sensor_type: &'a str,
    value: f64,
}

/// LibreHardwareMonitor values carry f32 precision noise (11.279999732971191);
/// the UI renders them directly, so keep one decimal for percent/temperature
/// and two for clocks/power/voltages/VRAM.
fn r1(value: Option<f64>) -> Option<f64> {
    value.map(|v| (v * 10.0).round() / 10.0)
}

fn r2(value: Option<f64>) -> Option<f64> {
    value.map(|v| (v * 100.0).round() / 100.0)
}

fn contains(haystack: &str, needle: &str) -> bool {
    haystack.to_ascii_lowercase().contains(&needle.to_ascii_lowercase())
}

fn parse_sensors(snapshot: &Value) -> Vec<SensorRow<'_>> {
    snapshot
        .get("sensors")
        .and_then(Value::as_array)
        .map(|rows| {
            rows.iter()
                .filter_map(|row| {
                    let value = row.get("value").and_then(Value::as_f64)?;
                    Some(SensorRow {
                        hardware: row.get("hardware").and_then(Value::as_str).unwrap_or(""),
                        identifier: row.get("identifier").and_then(Value::as_str).unwrap_or(""),
                        hardware_type: row.get("hardwareType").and_then(Value::as_str).unwrap_or(""),
                        name: row.get("name").and_then(Value::as_str).unwrap_or(""),
                        sensor_type: row.get("sensorType").and_then(Value::as_str).unwrap_or(""),
                        value,
                    })
                })
                .collect()
        })
        .unwrap_or_default()
}

/// Reads `dynamic.<key>.value` from the provider payload.
fn dynamic_value(snapshot: &Value, key: &str) -> Option<f64> {
    snapshot
        .get("dynamic")
        .and_then(|dynamic| dynamic.get(key))
        .and_then(|metric| metric.get("value"))
        .and_then(Value::as_f64)
}

fn disk_temperature(rows: &[SensorRow<'_>]) -> Option<f64> {
    // Select the drive's primary/composite reading before comparing drives.
    // Numbered, controller, NAND, hot-spot and threshold channels are distinct
    // measurements and must not replace the temperature advertised by SMART.
    let mut drives: HashMap<&str, (u8, f64)> = HashMap::new();
    for row in rows.iter().filter(|r| contains(r.hardware_type, "storage") && r.sensor_type.eq_ignore_ascii_case("Temperature")) {
        let priority = match row.name.trim().to_ascii_lowercase().as_str() {
            "composite temperature" | "temperature (composite)" | "drive temperature (composite)" | "composite" => 0,
            "temperature" | "drive temperature" => 1,
            "hdd temperature" | "ssd temperature" => 2,
            _ => continue,
        };
        if !row.value.is_finite() { continue; }
        let key=if row.identifier.is_empty() { row.hardware } else { row.identifier };
        drives.entry(key).and_modify(|current| {
            if priority < current.0 || (priority == current.0 && row.value > current.1) { *current=(priority,row.value); }
        }).or_insert((priority,row.value));
    }
    drives.values().map(|(_,value)|*value).max_by(f64::total_cmp)
}

fn find_sensor<'a>(
    rows: &'a [SensorRow<'a>],
    sensor_type: &str,
    hardware_type: &str,
    names: &[&str],
) -> Option<&'a SensorRow<'a>> {
    // Strict matching: the sensor type, hardware class and one of the name
    // hints must all agree. Deliberately NO "first sensor of that type"
    // fallback — a loose fallback used to match chassis fans as the CPU fan
    // and GPU memory temperature as the GPU temperature.
    //
    // Hint order is priority: candidates matching an earlier hint win ("GPU
    // Core" beats the bare "GPU" hint), so a broad last-resort hint can
    // never let GPU memory temperature or SOC power masquerade as the core
    // reading just because of row order (R-A5).
    let mut best: Option<(usize, &'a SensorRow<'a>)> = None;
    for row in rows {
        if !contains(row.sensor_type, sensor_type) {
            continue;
        }
        if !hardware_type.is_empty() && !contains(row.hardware_type, hardware_type) {
            continue;
        }
        let combined = format!("{} {}", row.hardware, row.name);
        for (priority, name) in names.iter().enumerate() {
            if contains(&combined, name) {
                if best.map_or(true, |(best_priority, _)| priority < best_priority) {
                    best = Some((priority, row));
                }
                break;
            }
        }
    }
    best.map(|(_, row)| row)
}

fn sensor_value(
    rows: &[SensorRow<'_>],
    sensor_type: &str,
    hardware_type: &str,
    names: &[&str],
) -> Option<f64> {
    find_sensor(rows, sensor_type, hardware_type, names).map(|row| row.value)
}

/// "Core #3" -> 2; "Core #3 VID" -> 2 when `allow_vid_suffix` (AMD Zen
/// exposes per-core VIDs under that exact name). Strict otherwise: only a
/// single optional "vid" suffix is allowed, so "Core #2 Max" style rows
/// never leak into the per-core clock/voltage series.
fn core_index(name: &str, allow_vid_suffix: bool) -> Option<usize> {
    let lowered = name.trim().to_ascii_lowercase();
    let rest = lowered.strip_prefix("core #")?;
    let mut parts = rest.split_whitespace();
    let index = parts.next()?.parse::<usize>().ok()?;
    match parts.next() {
        None => Some(index),
        Some(suffix) if allow_vid_suffix && suffix == "vid" => Some(index),
        Some(_) => None,
    }
    .and_then(|n| n.checked_sub(1))
}

/// Collects a per-core CPU series in core order; holes stay null. Capped at
/// 64 slots so a nonsense sensor name cannot balloon the payload.
fn collect_core_series(rows: &[SensorRow<'_>], sensor_type: &str, allow_vid: bool) -> Vec<Option<f64>> {
    const MAX_CORES: usize = 64;
    let mut indexed: Vec<(usize, f64)> = rows
        .iter()
        .filter(|row| {
            contains(row.sensor_type, sensor_type)
                && contains(row.hardware_type, "cpu")
                && core_index(row.name, allow_vid).is_some_and(|index| index < MAX_CORES)
        })
        .map(|row| (core_index(row.name, allow_vid).unwrap_or(0), row.value))
        .collect();
    if indexed.is_empty() {
        return Vec::new();
    }
    indexed.sort_by_key(|(index, _)| *index);
    let len = indexed[indexed.len() - 1].0 + 1;
    let mut series = vec![None; len];
    for (index, value) in indexed {
        series[index] = Some(value);
    }
    series
}

pub fn build_overlay_data(
    snapshot: &Value,
    fps: (Option<f64>, Option<f64>, Option<f64>),
    ping: Option<u32>,
    requested_gpu_index: usize,
) -> OverlayData {
    let rows = parse_sensors(snapshot);

    // One GpuSensorData per distinct GPU hardware, in first-seen order.
    let mut gpu_names: Vec<(&str, &str)> = Vec::new();
    for row in &rows {
        if contains(row.hardware_type, "gpu") && !gpu_names.iter().any(|(name, id)| {
            if !row.identifier.is_empty() && !id.is_empty() { id.eq_ignore_ascii_case(row.identifier) }
            else { row.identifier.is_empty() && id.is_empty() && name.eq_ignore_ascii_case(row.hardware) }
        }) {
            gpu_names.push((row.hardware, row.identifier));
        }
    }

    let gpu_sensors: Vec<GpuSensorData> = gpu_names
        .iter()
        .map(|(gpu, identifier)| {
            let per_gpu: Vec<SensorRow<'_>> = rows
                .iter()
                .filter(|row| if !identifier.is_empty() { row.identifier.eq_ignore_ascii_case(identifier) } else { row.identifier.is_empty() && row.hardware.eq_ignore_ascii_case(gpu) })
                .copied()
                .collect();
            GpuSensorData {
                name: (*gpu).to_string(),
                identifier: (*identifier).to_string(),
                hardware_type: per_gpu.first().map(|row| row.hardware_type).unwrap_or("").to_string(),
                temperature: r1(sensor_value(&per_gpu, "Temperature", "", &["GPU Core", "GPU Hot Spot", "GPU"])),
                usage: r1(sensor_value(&per_gpu, "Load", "", &["GPU Core", "GPU"])),
                fan_speed: sensor_value(&per_gpu, "Fan", "", &["GPU"]).map(|v| v.round()),
                power: r2(sensor_value(&per_gpu, "Power", "", &["GPU Power", "GPU"])),
                clock: sensor_value(&per_gpu, "Clock", "", &["GPU Core", "GPU"]).map(|v| v.round()),
                memory_clock: sensor_value(&per_gpu, "Clock", "", &["GPU Memory"]).map(|v| v.round()),
                vram_used: r2(sensor_value(&per_gpu, "SmallData", "", &["GPU Memory Used"]).map(|mb| mb / 1024.0)),
                vram_total: r2(sensor_value(&per_gpu, "SmallData", "", &["GPU Memory Total"]).map(|mb| mb / 1024.0)),
                voltage: r2(sensor_value(&per_gpu, "Voltage", "", &["GPU VDDC", "GPU Voltage", "GPU Core"])),
            }
        })
        .collect();

    let active_gpu_index = if gpu_sensors.is_empty() {
        0
    } else {
        requested_gpu_index.min(gpu_sensors.len() - 1)
    };
    let active = gpu_sensors.get(active_gpu_index);

    // Memory usage percent from the provider's WMI-backed totals.
    let memory_used_gb = dynamic_value(snapshot, "memoryUsedGb");
    let memory_total_gb = dynamic_value(snapshot, "memoryTotalGb");
    let memory_usage = match (memory_used_gb, memory_total_gb) {
        (Some(used), Some(total)) if total > 0.0 => Some((used / total * 100.0).clamp(0.0, 100.0)),
        _ => None,
    };
    let ssd_temp = disk_temperature(&rows);

    // Actual core-rail voltage as measured by the board's SuperIO sensor.
    // LibreHardwareMonitor's per-core AMD "VID" sensors are scaled with the
    // old SVI2 formula while Zen4 uses SVI3, so they read systematically low
    // (~0.6V vs ~1.0V real) — the board measurement is the trustworthy one.
    let board_vcore = sensor_value(&rows, "Voltage", "superio", &["Vcore"])
        .or_else(|| sensor_value(&rows, "Voltage", "", &["CPU Vcore"]));

    let mut cpu_core_clocks = collect_core_series(&rows, "Clock", false);
    for value in cpu_core_clocks.iter_mut() {
        *value = value.map(|v| v.round());
    }
    let mut cpu_core_voltages = collect_core_series(&rows, "Voltage", true);
    for value in cpu_core_voltages.iter_mut() {
        *value = r2(*value);
    }
    // AMD exposes a single shared VDDCR voltage domain, so the honest
    // per-core voltage is the measured rail (same value on every core) —
    // the scaled per-core VID request is not. Intel keeps true per-core VIDs.
    let is_amd = rows
        .iter()
        .any(|row| contains(row.hardware_type, "cpu") && contains(row.hardware, "amd"));
    if is_amd {
        if let Some(vcore) = r2(board_vcore) {
            if cpu_core_voltages.is_empty() {
                let cores = cpu_core_clocks.len();
                cpu_core_voltages = vec![Some(vcore); cores];
            } else {
                for value in cpu_core_voltages.iter_mut() {
                    *value = Some(vcore);
                }
            }
        }
    }

    OverlayData {
        fps: fps.0.map(|v| (v * 10.0).round() / 10.0),
        fps_1low: r1(fps.1),
        fps_01low: r1(fps.2),
        cpu_usage: r1(dynamic_value(snapshot, "cpuUsage")),
        cpu_temp: r1(dynamic_value(snapshot, "cpuTemperature")),
        cpu_clock: dynamic_value(snapshot, "cpuFrequency").map(|v| v.round()),
        cpu_power: r2(dynamic_value(snapshot, "cpuPower")),
        cpu_voltage: r2(
            // Prefer the board's measured rail over any LHM VID-derived value
            // (AMD Zen4 VID scaling reads low, see the per-core note above).
            board_vcore.or_else(|| sensor_value(&rows, "Voltage", "cpu", &["Vcore", "VBcore", "CPU Vcore"])),
        ),
        cpu_fan_speed: sensor_value(&rows, "Fan", "", &["CPU"]).map(|v| v.round()),
        gpu_temp: active.and_then(|gpu| gpu.temperature),
        gpu_usage: active.and_then(|gpu| gpu.usage),
        gpu_fan_speed: active.and_then(|gpu| gpu.fan_speed),
        gpu_power: active.and_then(|gpu| gpu.power),
        gpu_clock: active.and_then(|gpu| gpu.clock),
        gpu_voltage: active.and_then(|gpu| gpu.voltage),
        gpu_memory_clock: active.and_then(|gpu| gpu.memory_clock),
        memory_usage: r1(memory_usage),
        ssd_temp: r1(ssd_temp),
        delta_password: None,
        game_ping: ping,
        gpu_vram_used: active.and_then(|gpu| gpu.vram_used),
        gpu_vram_total: active.and_then(|gpu| gpu.vram_total),
        net_time_offset_ms: None,
        cpu_core_clocks,
        cpu_core_voltages,
        gpu_sensors,
        active_gpu_index,
        present_mode: crate::game_fps::get_cached_present_mode().map(str::to_string),
    }
}

macro_rules! carry_forward {
    ($new:expr, $old:expr) => {
        if $new.is_none() {
            $new = $old.clone();
        }
    };
}

/// Carry hardware values forward; FPS nulls explicitly invalidate a stream.
pub fn merge_overlay(new: &mut OverlayData, old: &OverlayData) {
    carry_forward!(new.cpu_usage, old.cpu_usage);
    carry_forward!(new.cpu_temp, old.cpu_temp);
    carry_forward!(new.cpu_clock, old.cpu_clock);
    carry_forward!(new.cpu_voltage, old.cpu_voltage);
    carry_forward!(new.cpu_power, old.cpu_power);
    carry_forward!(new.cpu_fan_speed, old.cpu_fan_speed);
    carry_forward!(new.gpu_temp, old.gpu_temp);
    carry_forward!(new.gpu_usage, old.gpu_usage);
    carry_forward!(new.gpu_fan_speed, old.gpu_fan_speed);
    carry_forward!(new.gpu_power, old.gpu_power);
    carry_forward!(new.gpu_clock, old.gpu_clock);
    carry_forward!(new.gpu_voltage, old.gpu_voltage);
    carry_forward!(new.gpu_memory_clock, old.gpu_memory_clock);
    carry_forward!(new.memory_usage, old.memory_usage);
    carry_forward!(new.ssd_temp, old.ssd_temp);
    carry_forward!(new.delta_password, old.delta_password);
    carry_forward!(new.game_ping, old.game_ping);
    carry_forward!(new.gpu_vram_used, old.gpu_vram_used);
    carry_forward!(new.gpu_vram_total, old.gpu_vram_total);
    carry_forward!(new.net_time_offset_ms, old.net_time_offset_ms);
    // Per-core series: a whole series disappearing inherits the old one
    // (service hiccup), and per-element carry-forward covers platforms that
    // only expose part of the cores on a degraded read.
    if new.cpu_core_clocks.is_empty() {
        new.cpu_core_clocks = old.cpu_core_clocks.clone();
    }
    if new.cpu_core_voltages.is_empty() {
        new.cpu_core_voltages = old.cpu_core_voltages.clone();
    }
    for (value, old_value) in new.cpu_core_clocks.iter_mut().zip(old.cpu_core_clocks.iter()) {
        carry_forward!(*value, old_value);
    }
    for (value, old_value) in new.cpu_core_voltages.iter_mut().zip(old.cpu_core_voltages.iter()) {
        carry_forward!(*value, old_value);
    }
    for gpu in new.gpu_sensors.iter_mut() {
        let Some(old_gpu) = old.gpu_sensors.iter().find(|candidate| {
            !gpu.identifier.is_empty() && !candidate.identifier.is_empty() && gpu.identifier.eq_ignore_ascii_case(&candidate.identifier)
                || gpu.identifier.is_empty() && candidate.identifier.is_empty() && gpu.name.eq_ignore_ascii_case(&candidate.name)
        }) else { continue; };
        carry_forward!(gpu.temperature, old_gpu.temperature);
        carry_forward!(gpu.usage, old_gpu.usage);
        carry_forward!(gpu.fan_speed, old_gpu.fan_speed);
        carry_forward!(gpu.power, old_gpu.power);
        carry_forward!(gpu.clock, old_gpu.clock);
        carry_forward!(gpu.memory_clock, old_gpu.memory_clock);
        carry_forward!(gpu.vram_used, old_gpu.vram_used);
        carry_forward!(gpu.vram_total, old_gpu.vram_total);
        carry_forward!(gpu.voltage, old_gpu.voltage);
    }
}

pub fn clear_hardware_values(data: &mut OverlayData) {
    macro_rules! clear { ($($field:ident),+) => { $(data.$field = None;)+ }; }
    clear!(cpu_usage, cpu_temp, cpu_clock, cpu_voltage, cpu_power, cpu_fan_speed,
        gpu_temp, gpu_usage, gpu_fan_speed, gpu_power, gpu_clock, gpu_voltage,
        gpu_memory_clock, gpu_vram_used, gpu_vram_total, memory_usage, ssd_temp);
    data.cpu_core_clocks.fill(None);
    data.cpu_core_voltages.fill(None);
    for gpu in &mut data.gpu_sensors {
        gpu.temperature = None; gpu.usage = None; gpu.fan_speed = None; gpu.power = None;
        gpu.clock = None; gpu.voltage = None; gpu.memory_clock = None; gpu.vram_used = None; gpu.vram_total = None;
    }
}

pub fn record_observed_fields(data: &OverlayData, now: Instant, times: &mut HashMap<String, Instant>) {
    macro_rules! seen { ($name:expr, $value:expr) => { if $value.is_some() { times.insert($name.to_string(), now); } }; }
    seen!("cpu_usage", data.cpu_usage); seen!("cpu_temp", data.cpu_temp); seen!("cpu_clock", data.cpu_clock);
    seen!("cpu_voltage", data.cpu_voltage); seen!("cpu_power", data.cpu_power); seen!("cpu_fan_speed", data.cpu_fan_speed);
    seen!("memory_usage", data.memory_usage); seen!("ssd_temp", data.ssd_temp); seen!("game_ping", data.game_ping);
    for (index, value) in data.cpu_core_clocks.iter().enumerate() { seen!(&format!("core_clock:{index}"), *value); }
    for (index, value) in data.cpu_core_voltages.iter().enumerate() { seen!(&format!("core_voltage:{index}"), *value); }
    for gpu in &data.gpu_sensors {
        let key = if gpu.identifier.is_empty() { format!("gpu:name:{}", gpu.name.to_ascii_lowercase()) } else { format!("gpu:id:{}", gpu.identifier.to_ascii_lowercase()) };
        seen!(&format!("{key}:temp"), gpu.temperature); seen!(&format!("{key}:usage"), gpu.usage);
        seen!(&format!("{key}:fan"), gpu.fan_speed); seen!(&format!("{key}:power"), gpu.power);
        seen!(&format!("{key}:clock"), gpu.clock); seen!(&format!("{key}:voltage"), gpu.voltage);
        seen!(&format!("{key}:memclock"), gpu.memory_clock); seen!(&format!("{key}:vramused"), gpu.vram_used); seen!(&format!("{key}:vramtotal"), gpu.vram_total);
    }
}

pub fn merge_overlay_with_ttl(new: &mut OverlayData, old: &OverlayData, now: Instant, times: &HashMap<String, Instant>, ttl: Duration) {

    macro_rules! carry { ($name:expr, $field:expr, $old:expr) => { if $field.is_none() && times.get($name).map(|t| now.duration_since(*t) <= ttl).unwrap_or(false) { $field = $old; } }; }
    carry!("cpu_usage", new.cpu_usage, old.cpu_usage); carry!("cpu_temp", new.cpu_temp, old.cpu_temp); carry!("cpu_clock", new.cpu_clock, old.cpu_clock);
    carry!("cpu_voltage", new.cpu_voltage, old.cpu_voltage); carry!("cpu_power", new.cpu_power, old.cpu_power); carry!("cpu_fan_speed", new.cpu_fan_speed, old.cpu_fan_speed);
    carry!("memory_usage", new.memory_usage, old.memory_usage); carry!("ssd_temp", new.ssd_temp, old.ssd_temp); carry!("game_ping", new.game_ping, old.game_ping);
    if new.cpu_core_clocks.len() < old.cpu_core_clocks.len() { new.cpu_core_clocks.resize(old.cpu_core_clocks.len(), None); }
    if new.cpu_core_voltages.len() < old.cpu_core_voltages.len() { new.cpu_core_voltages.resize(old.cpu_core_voltages.len(), None); }
    for i in 0..new.cpu_core_clocks.len() { carry!(&format!("core_clock:{i}"), new.cpu_core_clocks[i], old.cpu_core_clocks.get(i).cloned().unwrap_or(None)); }
    for i in 0..new.cpu_core_voltages.len() { carry!(&format!("core_voltage:{i}"), new.cpu_core_voltages[i], old.cpu_core_voltages.get(i).cloned().unwrap_or(None)); }
    for gpu in &mut new.gpu_sensors {
        let old_gpu = old.gpu_sensors.iter().find(|candidate| {
            (!gpu.identifier.is_empty() && gpu.identifier.eq_ignore_ascii_case(&candidate.identifier)) ||
            (gpu.identifier.is_empty() && candidate.identifier.is_empty() && gpu.name.eq_ignore_ascii_case(&candidate.name))
        });
        let Some(old_gpu) = old_gpu else { continue; };
        let key = if gpu.identifier.is_empty() { format!("gpu:name:{}", gpu.name.to_ascii_lowercase()) } else { format!("gpu:id:{}", gpu.identifier.to_ascii_lowercase()) };
        carry!(&format!("{key}:temp"), gpu.temperature, old_gpu.temperature); carry!(&format!("{key}:usage"), gpu.usage, old_gpu.usage);
        carry!(&format!("{key}:fan"), gpu.fan_speed, old_gpu.fan_speed); carry!(&format!("{key}:power"), gpu.power, old_gpu.power);
        carry!(&format!("{key}:clock"), gpu.clock, old_gpu.clock); carry!(&format!("{key}:voltage"), gpu.voltage, old_gpu.voltage);
        carry!(&format!("{key}:memclock"), gpu.memory_clock, old_gpu.memory_clock); carry!(&format!("{key}:vramused"), gpu.vram_used, old_gpu.vram_used); carry!(&format!("{key}:vramtotal"), gpu.vram_total, old_gpu.vram_total);
    }
    // Top-level GPU fields are a projection of the selected identity's independently aged values.
    // No timestamp or number from the previously selected GPU can enter this projection.
    if let Some(gpu) = new.gpu_sensors.get(new.active_gpu_index) {
        new.gpu_temp = gpu.temperature; new.gpu_usage = gpu.usage; new.gpu_fan_speed = gpu.fan_speed;
        new.gpu_power = gpu.power; new.gpu_clock = gpu.clock; new.gpu_voltage = gpu.voltage;
        new.gpu_memory_clock = gpu.memory_clock; new.gpu_vram_used = gpu.vram_used; new.gpu_vram_total = gpu.vram_total;
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    fn sample_snapshot() -> Value {
        json!({
            "availability": "available",
            "dynamic": {
                "cpuUsage": {"value": 42.0},
                "cpuTemperature": {"value": 61.5},
                "cpuFrequency": {"value": 4850.0},
                "cpuPower": {"value": 65.0},
                "memoryUsedGb": {"value": 12.8},
                "memoryTotalGb": {"value": 32.0}
            },
            "sensors": [
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #1", "sensorType": "Load", "value": 40.0},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #1", "sensorType": "Clock", "value": 4825.5},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #2", "sensorType": "Clock", "value": 4870.0},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #3", "sensorType": "Clock", "value": 4910.25},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #2 Max", "sensorType": "Clock", "value": 5000.0},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core (Vcore)", "sensorType": "Voltage", "value": 1.24},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #1", "sensorType": "Voltage", "value": 1.236},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #2 VID", "sensorType": "Voltage", "value": 0.5875},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #3 Max", "sensorType": "Voltage", "value": 9.99},
                {"hardware": "NVIDIA GeForce RTX 3080", "hardwareType": "Gpu", "name": "GPU Core", "sensorType": "Load", "value": 97.0},
                {"hardware": "NVIDIA GeForce RTX 3080", "hardwareType": "Gpu", "name": "GPU Core", "sensorType": "Temperature", "value": 71.0},
                {"hardware": "NVIDIA GeForce RTX 3080", "hardwareType": "Gpu", "name": "GPU Memory Used", "sensorType": "SmallData", "value": 8192.0},
                {"hardware": "NVIDIA GeForce RTX 3080", "hardwareType": "Gpu", "name": "GPU Memory Total", "sensorType": "SmallData", "value": 10240.0},
                {"hardware": "Samsung SSD 980 PRO", "hardwareType": "Storage", "name": "Drive Temperature", "sensorType": "Temperature", "value": 44.0}
            ]
        })
    }

    #[test]
    fn builds_overlay_data_from_snapshot() {
        let data = build_overlay_data(&sample_snapshot(), (None, None, None), None, 0);
        assert_eq!(data.cpu_usage, Some(42.0));
        assert_eq!(data.cpu_temp, Some(61.5));
        assert_eq!(data.cpu_voltage, Some(1.24));
        assert_eq!(data.cpu_core_clocks, vec![Some(4826.0), Some(4870.0), Some(4910.0)]);
        // "Core #1" (Intel style) and "Core #2 VID" (AMD style) both land in
        // the series; "Core #3 Max" is rejected, so there is no third slot.
        assert_eq!(data.cpu_core_voltages, vec![Some(1.24), Some(0.59)]);
        assert_eq!(data.memory_usage, Some(40.0));
        assert_eq!(data.ssd_temp, Some(44.0));
        assert_eq!(data.gpu_sensors.len(), 1);
        let gpu = &data.gpu_sensors[0];
        assert_eq!(gpu.name, "NVIDIA GeForce RTX 3080");
        assert_eq!(gpu.usage, Some(97.0));
        assert_eq!(gpu.temperature, Some(71.0));
        assert_eq!(gpu.vram_used, Some(8.0));
        assert_eq!(gpu.vram_total, Some(10.0));
        assert_eq!(data.active_gpu_index, 0);
        assert_eq!(data.gpu_temp, Some(71.0));
        assert_eq!(data.gpu_vram_used, Some(8.0));
    }

    #[test]
    fn disk_temperature_uses_composite_instead_of_hotspot_or_secondary_sensors() {
        let snapshot=serde_json::json!({"sensors":[
            {"hardware":"NVMe A","identifier":"/nvme/0","hardwareType":"Storage","name":"Temperature","sensorType":"Temperature","value":43.0},
            {"hardware":"NVMe A","identifier":"/nvme/0","hardwareType":"Storage","name":"Temperature 1","sensorType":"Temperature","value":89.0},
            {"hardware":"NVMe A","identifier":"/nvme/0","hardwareType":"Storage","name":"Controller Hot Spot","sensorType":"Temperature","value":97.0},
            {"hardware":"NVMe B","identifier":"/nvme/1","hardwareType":"Storage","name":"Composite Temperature","sensorType":"Temperature","value":48.0},
            {"hardware":"NVMe B","identifier":"/nvme/1","hardwareType":"Storage","name":"Temperature 2","sensorType":"Temperature","value":92.0}
        ]});
        assert_eq!(build_overlay_data(&snapshot,(None,None,None),None,0).ssd_temp,Some(48.0));
    }

    #[test]
    fn disk_secondary_or_threshold_temperature_is_not_a_primary_temperature() {
        for name in ["Temperature 1","NAND Temperature","Controller Temperature","Hot Spot","Critical Temperature","Warning Composite Temperature"] {
            let snapshot=serde_json::json!({"sensors":[{"hardware":"NVMe","hardwareType":"Storage","name":name,"sensorType":"Temperature","value":89.0}]});
            assert_eq!(build_overlay_data(&snapshot,(None,None,None),None,0).ssd_temp,None,"{name}");
        }
    }

    #[test]
    fn requested_gpu_index_is_clamped() {
        let data = build_overlay_data(&sample_snapshot(), (None, None, None), None, 9);
        assert_eq!(data.active_gpu_index, 0);
    }

    #[test]
    fn low_windows_are_not_clamped_to_unrelated_instantaneous_fps() {
        let data = build_overlay_data(
            &sample_snapshot(), (Some(60.0), Some(180.0), Some(120.0)), None, 0,
        );
        assert_eq!(data.fps_1low, Some(180.0));
        assert_eq!(data.fps_01low, Some(120.0));
    }

    #[test]
    fn merge_does_not_resurrect_invalidated_fps_or_low_samples() {
        let old = build_overlay_data(
            &sample_snapshot(), (Some(300.0), Some(250.0), Some(200.0)), None, 0,
        );
        let mut fresh = build_overlay_data(&sample_snapshot(), (None, None, None), None, 0);
        for _ in 0..30 {
            let previous = fresh.clone();
            merge_overlay(&mut fresh, &old);
            merge_overlay(&mut fresh, &previous);
        }
        assert_eq!(fresh.fps, None);
        assert_eq!(fresh.fps_1low, None);
        assert_eq!(fresh.fps_01low, None);
    }

    #[test]
    fn merge_carries_previous_non_null_fields() {
        let mut stale = build_overlay_data(&sample_snapshot(), (None, None, None), None, 0);
        stale.cpu_temp = Some(61.5);
        stale.game_ping = Some(12);

        let degraded = build_overlay_data(
            &json!({"availability": "partial", "dynamic": {}, "sensors": []}),
            (None, None, None),
            None,
            0,
        );
        let mut merged = degraded;
        merge_overlay(&mut merged, &stale);
        assert_eq!(merged.cpu_temp, Some(61.5));
        assert_eq!(merged.game_ping, Some(12));
        assert_eq!(merged.gpu_sensors.len(), 0);
    }

    #[test]
    fn amd_per_core_voltage_prefers_measured_rail() {
        // Real-world layout: ITE SuperIO Vcore sense + LHM per-core VIDs.
        // The board measurement (matches GamePP's CPU voltage) must win over
        // the low-reading SVI2-scaled VID series.
        let snapshot = json!({
            "availability": "available",
            "dynamic": {},
            "sensors": [
                {"hardware": "ITE IT8689E", "hardwareType": "SuperIO", "name": "Vcore", "sensorType": "Voltage", "value": 1.02},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #1", "sensorType": "Clock", "value": 4950.0},
                {"hardware": "AMD Ryzen 7 7800X3D", "hardwareType": "Cpu", "name": "Core #1 VID", "sensorType": "Voltage", "value": 0.575}
            ]
        });
        let data = build_overlay_data(&snapshot, (None, None, None), None, 0);
        assert_eq!(data.cpu_voltage, Some(1.02));
        assert_eq!(data.cpu_core_voltages, vec![Some(1.02)]);
    }

    #[test]
    fn serializes_with_frontend_field_names() {
        let data = build_overlay_data(&sample_snapshot(), (Some(60.0), Some(45.0), Some(30.0)), Some(9), 0);
        let json = serde_json::to_value(&data).unwrap();
        for key in [
            "fps", "fps_1low", "fps_01low", "cpu_usage", "cpu_temp", "cpu_clock", "cpu_voltage",
            "cpu_power", "cpu_fan_speed", "gpu_temp", "gpu_usage", "gpu_fan_speed", "gpu_power",
            "gpu_clock", "gpu_voltage", "gpu_memory_clock", "memory_usage", "ssd_temp",
            "delta_password", "game_ping", "gpu_vram_used", "gpu_vram_total", "net_time_offset_ms",
            "cpu_core_clocks", "cpu_core_voltages",
            "gpu_sensors", "active_gpu_index",
        ] {
            assert!(json.get(key).is_some(), "missing field {key}");
        }
        let gpu = &json["gpu_sensors"][0];
        for key in [
            "name", "hardware_type", "temperature", "usage", "fan_speed", "power", "clock",
            "memory_clock", "vram_used", "vram_total", "voltage",
        ] {
            assert!(gpu.get(key).is_some(), "missing gpu field {key}");
        }
    }
}
