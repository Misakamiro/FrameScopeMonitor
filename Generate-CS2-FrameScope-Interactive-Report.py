#!/usr/bin/env python3
"""
Generate a single-page interactive FrameScope report.

The HTML stays small. Full monitor CSVs are processed into one external
JavaScript data file, then the page renders charts with canvas.
"""

from __future__ import annotations

import argparse
import csv
import ctypes
import html
import json
import math
import os
import platform
import shutil
import subprocess
from collections import defaultdict
from datetime import datetime, timedelta
from pathlib import Path

try:
    import winreg
except Exception:  # pragma: no cover - Windows-only optional import
    winreg = None


BRAND = "FrameScope"
COLORS = [
    "#28f3ff", "#a7ff2f", "#ffd247", "#ff5e7e", "#66a6ff", "#d77cff",
    "#45ff9a", "#ff9f43", "#70e1f5", "#f8f871", "#38ef7d", "#ff6bcb",
    "#a4b0be", "#ffa502", "#2ed573", "#ff7675", "#7bed9f", "#5352ed",
    "#ff6b81", "#1e90ff", "#feca57", "#48dbfb", "#ff9ff3", "#54a0ff",
]


def parse_float(value: object, default: float | None = None) -> float | None:
    if value is None:
        return default
    text = str(value).strip()
    if not text or text.upper() == "NA":
        return default
    try:
        value = float(text)
        if not math.isfinite(value):
            return default
        return value
    except ValueError:
        return default


def parse_presentmon_time(value: str) -> datetime | None:
    text = (value or "").strip()
    if not text or text.upper() == "NA":
        return None
    try:
        date_part, time_part = text.split(" ", 1)
        year, month, day = [int(part) for part in date_part.split("-")]
        hour_s, minute_s, second_part = time_part.split(":")
        if "." in second_part:
            second_s, frac_s = second_part.split(".", 1)
            frac = "".join(ch for ch in frac_s if ch.isdigit())[:6].ljust(6, "0")
            microsecond = int(frac)
        else:
            second_s = second_part
            microsecond = 0
        return datetime(year, month, day, int(hour_s), int(minute_s), int(second_s), microsecond)
    except Exception:
        return None


def parse_iso_time(value: str) -> datetime | None:
    text = (value or "").strip()
    if not text:
        return None
    try:
        return datetime.fromisoformat(text).replace(tzinfo=None)
    except ValueError:
        return None


def percentile_high(values: list[float], quantile: float) -> float | None:
    if not values:
        return None
    sorted_values = sorted(values)
    index = max(0, min(len(sorted_values) - 1, math.ceil(quantile * len(sorted_values)) - 1))
    return sorted_values[index]


def avg(values: list[float]) -> float | None:
    return sum(values) / len(values) if values else None


def rounded(value: float | None, digits: int = 2) -> float | None:
    if value is None:
        return None
    return round(float(value), digits)


def find_latest_run(base_dir: Path) -> Path:
    runs_dir = base_dir / "cs2-monitor-runs"
    candidates = [
        item for item in runs_dir.iterdir() if item.is_dir() and (item / "presentmon.csv").exists()
    ]
    if not candidates:
        raise FileNotFoundError(f"No CS2 monitor runs found in {runs_dir}")
    return max(candidates, key=lambda item: item.stat().st_mtime)


def read_presentmon(path: Path) -> list[tuple[datetime, float]]:
    frames: list[tuple[datetime, float]] = []
    if not path.exists():
        return frames
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        for row in reader:
            t = parse_presentmon_time(row.get("TimeInDateTime", ""))
            frame_ms = parse_float(row.get("MsBetweenPresents"))
            if t is not None and frame_ms is not None and 0 < frame_ms < 10000:
                frames.append((t, frame_ms))
    return frames


def read_system(path: Path) -> list[dict]:
    rows: list[dict] = []
    if not path.exists():
        return rows
    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        for row in reader:
            t = parse_iso_time(row.get("Time", ""))
            if t is None:
                continue
            rows.append({
                "time": t,
                "cpu": parse_float(row.get("TotalCpuPct")),
                "cpu_freq": parse_float(row.get("CpuFrequencyMHz")),
                "cpu_perf": parse_float(row.get("CpuPerformancePct")),
                "available_mb": parse_float(row.get("AvailableMB")),
                "disk_latency_ms": (parse_float(row.get("DiskAvgSecPerTransfer")) or 0) * 1000,
                "disk_mbps": (parse_float(row.get("DiskBytesPerSec")) or 0) / 1048576,
                "net_mbps": (parse_float(row.get("NetBytesPerSec")) or 0) / 1048576,
                "gpu": parse_float(row.get("GpuUtilPct")),
                "gpu_mem": parse_float(row.get("GpuMemUtilPct")),
                "gpu_temp": parse_float(row.get("GpuTempC")),
                "gpu_clock": parse_float(row.get("GpuClockMHz")),
                "mem_clock": parse_float(row.get("MemClockMHz")),
                "power": parse_float(row.get("PowerW")),
                "vram_used_gb": (parse_float(row.get("VramUsedMiB")) or 0) / 1024,
                "vram_total_gb": (parse_float(row.get("VramTotalMiB")) or 0) / 1024,
            })
    return rows


def align_presentmon_time(
    frames: list[tuple[datetime, float]], system_rows: list[dict]
) -> tuple[list[tuple[datetime, float]], int]:
    if not frames or not system_rows:
        return frames, 0
    diff_hours = (frames[0][0] - system_rows[0]["time"]).total_seconds() / 3600
    rounded_hours = round(diff_hours)
    if abs(diff_hours - rounded_hours) <= 0.1 and abs(rounded_hours) >= 1:
        return [(t - timedelta(hours=rounded_hours), ms) for t, ms in frames], -rounded_hours
    return frames, 0


def bucket_fps(frames: list[tuple[datetime, float]], seconds: float = 0.1) -> dict:
    if not frames:
        return {"bucketMs": int(seconds * 1000), "t": [], "avg": [], "low1": [], "low01": [], "min": []}

    start = frames[0][0]
    buckets: dict[int, list[float]] = defaultdict(list)
    for t, ms in frames:
        elapsed = max(0.0, (t - start).total_seconds())
        buckets[int(math.floor(elapsed / seconds))].append(ms)
    times, avg_fps, low1, low01, min_fps = [], [], [], [], []
    for bucket in sorted(buckets):
        values = buckets[bucket]
        mean_ms = avg(values)
        p99 = percentile_high(values, 0.99)
        p999 = percentile_high(values, 0.999)
        max_ms = max(values)
        times.append(rounded(bucket * seconds, 3))
        avg_fps.append(rounded(1000 / mean_ms if mean_ms else None, 2))
        low1.append(rounded(1000 / p99 if p99 else None, 2))
        low01.append(rounded(1000 / p999 if p999 else None, 2))
        min_fps.append(rounded(1000 / max_ms if max_ms else None, 3))
    return {"bucketMs": int(seconds * 1000), "t": times, "avg": avg_fps, "low1": low1, "low01": low01, "min": min_fps}


def load_hardware() -> dict:
    data: dict[str, object] = {
        "CpuThreads": os.cpu_count(),
        "OsCaption": platform.platform(),
        "OsVersion": platform.version(),
        "OsArch": platform.machine(),
    }

    if winreg is not None:
        def reg_value(root, path: str, name: str) -> object | None:
            try:
                with winreg.OpenKey(root, path) as key:
                    return winreg.QueryValueEx(key, name)[0]
            except Exception:
                return None

        cpu_name = reg_value(
            winreg.HKEY_LOCAL_MACHINE,
            r"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "ProcessorNameString",
        )
        cpu_mhz = reg_value(
            winreg.HKEY_LOCAL_MACHINE,
            r"HARDWARE\DESCRIPTION\System\CentralProcessor\0",
            "~MHz",
        )
        product_name = reg_value(
            winreg.HKEY_LOCAL_MACHINE,
            r"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "ProductName",
        )
        display_version = reg_value(
            winreg.HKEY_LOCAL_MACHINE,
            r"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "DisplayVersion",
        )
        build = reg_value(
            winreg.HKEY_LOCAL_MACHINE,
            r"SOFTWARE\Microsoft\Windows NT\CurrentVersion",
            "CurrentBuildNumber",
        )

        if cpu_name:
            data["CpuName"] = str(cpu_name).strip()
        if cpu_mhz:
            data["CpuMaxClockMHz"] = cpu_mhz
        if product_name:
            suffix = f" {display_version}" if display_version else ""
            data["OsCaption"] = f"{product_name}{suffix}".strip()
        if build:
            data["OsVersion"] = str(build)

    try:
        class MemoryStatusEx(ctypes.Structure):
            _fields_ = [
                ("dwLength", ctypes.c_ulong),
                ("dwMemoryLoad", ctypes.c_ulong),
                ("ullTotalPhys", ctypes.c_ulonglong),
                ("ullAvailPhys", ctypes.c_ulonglong),
                ("ullTotalPageFile", ctypes.c_ulonglong),
                ("ullAvailPageFile", ctypes.c_ulonglong),
                ("ullTotalVirtual", ctypes.c_ulonglong),
                ("ullAvailVirtual", ctypes.c_ulonglong),
                ("sullAvailExtendedVirtual", ctypes.c_ulonglong),
            ]

        memory = MemoryStatusEx()
        memory.dwLength = ctypes.sizeof(MemoryStatusEx)
        if ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(memory)):
            data["TotalMemoryMB"] = round(memory.ullTotalPhys / 1024 / 1024)
    except Exception:
        pass

    nvidia_smi = shutil.which("nvidia-smi.exe") or r"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe"
    if nvidia_smi and Path(nvidia_smi).exists():
        try:
            completed = subprocess.run(
                [
                    nvidia_smi,
                    "--query-gpu=name,driver_version",
                    "--format=csv,noheader,nounits",
                ],
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=5,
                check=False,
            )
            line = (completed.stdout or "").splitlines()[0].strip() if completed.stdout else ""
            if completed.returncode == 0 and line:
                parts = [part.strip() for part in line.split(",", 1)]
                if parts:
                    data["GpuName"] = parts[0]
                if len(parts) > 1:
                    data["GpuDriver"] = parts[1]
        except Exception:
            pass

    return {key: value for key, value in data.items() if value not in (None, "")}


def load_run_metadata(run_dir: Path) -> dict:
    for name in ("status.json", "summary.json"):
        path = run_dir / name
        if not path.exists():
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8-sig"))
            if isinstance(data, dict):
                target = data.get("TargetProcess") or data.get("TargetProcessName")
                if target:
                    return {"targetProcess": str(target)}
        except Exception:
            continue
    return {"targetProcess": "cs2.exe"}


def fmt_duration(seconds: int) -> str:
    return f"{seconds // 60}分{seconds % 60}秒"


def read_process_matrix(
    path: Path, start: datetime, target_process: str | None = None
) -> tuple[list[float], list[str], list[list[float | None]], list[list[float | None]], list[dict]]:
    sample_times: list[float] = []
    sample_index_to_pos: dict[str, int] = {}
    stats: dict[str, dict] = {}
    target_base = Path(target_process or "").stem.lower()

    if not path.exists():
        return [], [], [], [], []

    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        for row in reader:
            name = (row.get("ProcessName") or "").strip()
            if not name:
                continue
            if name.lower() == "cs2" or (target_base and name.lower() == target_base):
                continue
            idx = row.get("SampleIndex", "")
            t = parse_iso_time(row.get("Time", ""))
            if t is None:
                continue
            if idx not in sample_index_to_pos:
                sample_index_to_pos[idx] = len(sample_times)
                sample_times.append(round((t - start).total_seconds(), 3))
            pos = sample_index_to_pos[idx]
            cpu = parse_float(row.get("CpuPct"))
            mem = parse_float(row.get("WorkingSetMB"))

            item = stats.setdefault(name, {"name": name, "maxCpu": 0.0, "avgCpuSum": 0.0, "cpuSamples": 0, "maxMem": 0.0, "samples": 0})
            item["samples"] += 1
            if cpu is not None:
                item["maxCpu"] = max(item["maxCpu"], cpu)
                item["avgCpuSum"] += cpu
                item["cpuSamples"] += 1
            if mem is not None:
                item["maxMem"] = max(item["maxMem"], mem)

    process_stats = []
    for item in stats.values():
        process_stats.append({
            "name": item["name"],
            "maxCpu": rounded(item["maxCpu"], 2),
            "avgCpu": rounded(item["avgCpuSum"] / item["cpuSamples"] if item["cpuSamples"] else 0, 2),
            "maxMem": rounded(item["maxMem"], 1),
            "samples": item["samples"],
        })
    process_stats.sort(key=lambda row: (row["maxCpu"], row["maxMem"], row["samples"]), reverse=True)
    names = [row["name"] for row in process_stats]
    name_to_index = {name: i for i, name in enumerate(names)}
    n_times = len(sample_times)
    cpu_matrix: list[list[float | None]] = [[None] * n_times for _ in names]
    mem_matrix: list[list[float | None]] = [[None] * n_times for _ in names]

    with path.open("r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        for row in reader:
            name = (row.get("ProcessName") or "").strip()
            if name not in name_to_index:
                continue
            idx = row.get("SampleIndex", "")
            pos = sample_index_to_pos.get(idx)
            if pos is None:
                continue
            cpu = parse_float(row.get("CpuPct"))
            mem = parse_float(row.get("WorkingSetMB"))
            i = name_to_index[name]
            if cpu is not None:
                cpu_matrix[i][pos] = rounded(cpu, 2)
            if mem is not None:
                mem_matrix[i][pos] = rounded(mem, 1)
    return sample_times, names, cpu_matrix, mem_matrix, process_stats


def series_from_system(system_rows: list[dict], start: datetime, total_memory_mb: float | None) -> dict:
    times, cpu, gpu, gpu_mem, mem_used, vram_used, cpu_freq, gpu_clock, mem_clock = [], [], [], [], [], [], [], [], []
    disk, net, disk_latency, power, temp = [], [], [], [], []
    for row in system_rows:
        sec = round((row["time"] - start).total_seconds(), 3)
        times.append(sec)
        cpu.append(rounded(row.get("cpu"), 2))
        gpu.append(rounded(row.get("gpu"), 2))
        gpu_mem.append(rounded(row.get("gpu_mem"), 2))
        mem_pct = None
        if total_memory_mb and row.get("available_mb") is not None:
            mem_pct = max(0, min(100, (total_memory_mb - row["available_mb"]) / total_memory_mb * 100))
        mem_used.append(rounded(mem_pct, 2))
        vram_pct = None
        if row.get("vram_total_gb"):
            vram_pct = row["vram_used_gb"] / row["vram_total_gb"] * 100
        vram_used.append(rounded(vram_pct, 2))
        cpu_freq.append(rounded(row.get("cpu_freq"), 0))
        gpu_clock.append(rounded(row.get("gpu_clock"), 0))
        mem_clock.append(rounded(row.get("mem_clock"), 0))
        disk.append(rounded(row.get("disk_mbps"), 3))
        net.append(rounded(row.get("net_mbps"), 3))
        disk_latency.append(rounded(row.get("disk_latency_ms"), 3))
        power.append(rounded(row.get("power"), 2))
        temp.append(rounded(row.get("gpu_temp"), 1))
    return {
        "t": times,
        "usage": {"cpu": cpu, "gpu": gpu, "gpuMem": gpu_mem, "mem": mem_used, "vram": vram_used},
        "perf": {"cpuFreq": cpu_freq, "gpuClock": gpu_clock, "memClock": mem_clock},
        "io": {"disk": disk, "net": net, "diskLatency": disk_latency, "power": power, "temp": temp},
    }


def make_html_legacy_unused() -> str:
    return r"""<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>FrameScope - CS2 性能报告</title>
  <style>
    :root { --bg:#0d141c; --panel:#151f2b; --panel2:#1c2a39; --line:#34546a; --text:#eff7ff; --muted:#9fb6c8; --cyan:#28f3ff; --green:#a7ff2f; --yellow:#ffd247; --red:#ff5e7e; }
    * { box-sizing: border-box; }
    body { margin: 0; background: radial-gradient(circle at 80% 0%, rgba(40,243,255,.11), transparent 34%), #0d141c; color: var(--text); font-family: "Segoe UI","Microsoft YaHei",Arial,sans-serif; }
    .shell { width: min(1720px, calc(100vw - 24px)); margin: 12px auto; display: grid; grid-template-columns: 330px 1fr; gap: 14px; }
    .left,.main { border: 1px solid rgba(87,132,159,.42); background: rgba(21,31,43,.96); border-radius: 10px; }
    .left { padding: 18px; }
    .main { padding: 18px 22px 22px; }
    .brand { font-size: 29px; font-weight: 900; color: #ffd27a; margin-bottom: 4px; }
    .brand-sub { color: var(--cyan); font-size: 14px; margin-bottom: 16px; }
    .block { border-top: 1px solid rgba(143,185,210,.18); padding: 13px 0; }
    .block h3 { margin: 0 0 8px; color: var(--cyan); font-size: 15px; }
    .line { display: flex; justify-content: space-between; gap: 12px; line-height: 1.65; font-size: 14px; }
    .line b { color: #fff2d8; text-align: right; }
    .topbar { display: flex; justify-content: space-between; align-items: center; gap: 16px; border-bottom: 1px solid rgba(143,185,210,.18); padding-bottom: 12px; }
    .game { display:flex; align-items:center; gap:12px; min-width:0; }
    .game-icon { width:42px; height:42px; display:grid; place-items:center; border-radius:8px; background:linear-gradient(145deg,#ee263a,#9e1128); font-weight:900; font-size:22px; }
    .game-name { font-size:22px; font-weight:900; }
    .meta { display:flex; flex-wrap:wrap; gap:14px; color:var(--muted); font-size:13px; margin-top:4px; }
    .tabs { display:flex; gap:10px; flex-wrap:wrap; justify-content:flex-end; }
    .tab { border:1px solid rgba(75,213,236,.36); background:#223246; color:#d9eef7; height:34px; padding:0 13px; border-radius:5px; font-weight:800; cursor:pointer; }
    .tab.active { color:#06141a; background:var(--cyan); box-shadow:0 0 18px rgba(40,243,255,.35); }
    .title { display:flex; align-items:baseline; gap:14px; margin:16px 0 8px; }
    .title h2 { margin:0; color:var(--cyan); font-size:22px; }
    .title span { color:var(--muted); font-size:13px; }
    .gauges { display:grid; grid-template-columns:170px repeat(6,minmax(120px,1fr)); gap:16px; align-items:start; margin: 8px 0 14px; }
    .gauge { text-align:center; min-width:0; }
    .gauge h3 { margin:0 0 8px; font-size:20px; color:#ffeccc; }
    .ring { --p:0; --c:var(--cyan); width:104px; height:104px; margin:auto; border-radius:50%; display:grid; place-items:center; background:radial-gradient(circle at center,#142232 0 54%,transparent 56%), conic-gradient(var(--c) calc(var(--p)*1%), rgba(85,122,145,.35) 0); box-shadow: inset 0 0 0 2px rgba(138,181,207,.25); }
    .ring.big { width:126px; height:126px; }
    .ring b { font-size:28px; line-height:1; text-shadow:0 0 12px rgba(40,243,255,.42); }
    .ring.big b { font-size:38px; color:var(--cyan); }
    .ring small { display:block; color:#d8e6ef; font-size:12px; margin-top:5px; }
    .gauge .foot { color:var(--yellow); font-size:12px; margin-top:8px; }
    .toolbar { display:flex; align-items:center; justify-content:space-between; gap:12px; margin:8px 0; flex-wrap:wrap; }
    .left-tools,.right-tools { display:flex; align-items:center; gap:10px; flex-wrap:wrap; }
    select,input { background:#26384c; color:#fff; border:1px solid rgba(75,213,236,.45); height:38px; border-radius:5px; padding:0 10px; font-weight:800; outline:none; }
    input { min-width:220px; }
    .chartbox { position:relative; height:560px; border:1px solid rgba(96,147,179,.48); background:linear-gradient(rgba(54,88,111,.35) 1px,transparent 1px),linear-gradient(90deg,rgba(54,88,111,.28) 1px,transparent 1px),linear-gradient(180deg,rgba(40,243,255,.07),rgba(40,243,255,.02)); background-size:100% 48px,22px 100%,auto; overflow:hidden; }
    canvas { width:100%; height:100%; display:block; }
    .tooltip { position:absolute; pointer-events:none; opacity:0; min-width:260px; max-width:430px; background:rgba(25,34,47,.94); border:1px solid rgba(196,226,241,.22); box-shadow:0 12px 36px rgba(0,0,0,.38); border-radius:5px; padding:10px 12px; font-size:13px; line-height:1.55; white-space:normal; z-index:5; }
    .legend { display:flex; gap:12px; flex-wrap:wrap; max-height:74px; overflow:auto; color:#dceef7; font-size:12px; }
    .dot { width:12px; height:12px; border-radius:3px; display:inline-block; margin-right:5px; vertical-align:-2px; }
    .panelgrid { margin-top:14px; display:grid; grid-template-columns:1fr 1fr; gap:14px; }
    .card { border:1px solid rgba(87,132,159,.42); background:rgba(28,42,57,.86); border-radius:8px; padding:12px 14px; }
    .card h3 { margin:0 0 10px; color:var(--yellow); }
    .rows { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:7px 18px; font-size:13px; }
    .row { display:grid; grid-template-columns:minmax(90px,1fr) 70px 90px; gap:8px; align-items:center; }
    .row span:first-child { overflow:hidden; white-space:nowrap; text-overflow:ellipsis; }
    .bar { height:5px; border-radius:99px; background:#2e455b; overflow:hidden; }
    .bar i { display:block; height:100%; background:linear-gradient(90deg,var(--cyan),var(--green)); }
    .note { color:var(--muted); font-size:13px; }
    @media (max-width:1200px){ .shell{grid-template-columns:1fr}.gauges{grid-template-columns:repeat(2,minmax(0,1fr))}.panelgrid{grid-template-columns:1fr} }
  </style>
</head>
<body>
<div class="shell">
  <aside class="left">
    <div class="brand">FrameScope</div>
    <div class="brand-sub">CS2 性能监测报告</div>
    <div class="block"><h3>处理器</h3><div class="line"><span id="hwCpu">-</span></div><div class="line"><span>核心/线程</span><b id="hwCore">-</b></div><div class="line"><span>最大频率</span><b id="hwCpuClock">-</b></div></div>
    <div class="block"><h3>显卡</h3><div class="line"><span id="hwGpu">-</span></div><div class="line"><span>驱动版本</span><b id="hwDriver">-</b></div><div class="line"><span>记录显存</span><b id="hwVram">-</b></div></div>
    <div class="block"><h3>系统</h3><div class="line"><span id="hwOs">-</span></div><div class="line"><span>内存</span><b id="hwMem">-</b></div><div class="line"><span>采样点</span><b id="hwSamples">-</b></div></div>
    <div class="block"><h3>说明</h3><div class="note">后台 CPU/内存都在同一个页面中切换；鼠标悬停图表可看当前时间点的实时值。</div></div>
  </aside>
  <main class="main">
    <div class="topbar">
      <div class="game"><div class="game-icon">CS</div><div><div class="game-name">cs2.exe</div><div class="meta"><span id="runStart">-</span><span id="runEnd">-</span><span id="runDuration">-</span></div></div></div>
      <div class="tabs"><button class="tab active" data-view="fps">性能报告</button><button class="tab" data-view="perf">性能图表</button><button class="tab" data-view="system">系统占用</button><button class="tab" data-view="process">后台进程</button><button class="tab" data-view="io">IO/温度</button></div>
    </div>
    <div class="title"><h2>硬件状态</h2><span>图表值来自本次完整 CSV 记录，页面使用离线处理后的轻量数据。</span></div>
    <div class="gauges" id="gauges"></div>
    <div class="toolbar">
      <div class="left-tools"><select id="metricSelect"></select><input id="processSearch" placeholder="搜索进程，留空显示全部"></div>
      <div class="right-tools"><div class="legend" id="legend"></div></div>
    </div>
    <div class="chartbox" id="chartBox"><canvas id="chart"></canvas><div class="tooltip" id="tooltip"></div></div>
    <div class="panelgrid"><div class="card"><h3>后台进程峰值</h3><div class="rows" id="processRows"></div></div><div class="card"><h3>本次帧率摘要</h3><div class="rows" id="summaryRows"></div></div></div>
  </main>
</div>
<script src="framescope-interactive-data.js"></script>
<script>
const DATA = window.FRAMESCOPE_DATA;
const COLORS = DATA.colors;
let view = "fps";
let processMetric = "cpu";
const canvas = document.getElementById("chart");
const ctx = canvas.getContext("2d");
const tooltip = document.getElementById("tooltip");
const metricSelect = document.getElementById("metricSelect");
const processSearch = document.getElementById("processSearch");
const chartBox = document.getElementById("chartBox");
const legend = document.getElementById("legend");
const PAD = {l:58,r:22,t:28,b:42};
let currentSeries = [];
let currentTimes = [];
let currentUnit = "";
let currentYMax = null;

function n(v,d=1){ return v===null || v===undefined || Number.isNaN(Number(v)) ? "N/A" : Number(v).toFixed(d); }
function mmss(sec){ sec=Math.max(0,Math.round(sec||0)); return `${Math.floor(sec/60)}:${String(sec%60).padStart(2,"0")}`; }
function setText(id,v){ const e=document.getElementById(id); if(e) e.textContent=v; }
function pct(v){ return Math.max(0,Math.min(100,Number(v)||0)); }
function ring(title,value,sub,p,color,foot){ return `<div class="gauge"><h3>${title}</h3><div class="ring ${title==="FPS"?"big":""}" style="--p:${pct(p)};--c:${color}"><div><b>${value}</b><small>${sub}</small></div></div><div class="foot">${foot||""}</div></div>`; }
function initStatic(){
  const h=DATA.hardware, hd=DATA.hardwareDerived, fs=DATA.frameStats, ss=DATA.systemStats;
  setText("hwCpu", h.CpuName || "N/A"); setText("hwCore", h.CpuCores ? `${h.CpuCores} / ${h.CpuThreads}` : "N/A"); setText("hwCpuClock", h.CpuMaxClockMHz ? `${h.CpuMaxClockMHz} MHz` : "N/A");
  setText("hwGpu", h.GpuName || "N/A"); setText("hwDriver", h.GpuDriver || "N/A"); setText("hwVram", hd.vramTotalGb ? `${n(hd.vramTotalGb,1)} GB` : "N/A");
  setText("hwOs", `${h.OsCaption||"Windows"} ${h.OsArch||""}`.trim()); setText("hwMem", hd.totalMemoryGb ? `${n(hd.totalMemoryGb,1)} GB` : "N/A"); setText("hwSamples", `${DATA.counts.frames} 帧 / ${DATA.counts.processSamples} 次进程采样`);
  setText("runStart", `开始时间：${DATA.run.startLabel}`); setText("runEnd", `结束时间：${DATA.run.endLabel}`); setText("runDuration", `记录时长：${DATA.run.durationLabel}`);
  document.getElementById("gauges").innerHTML = [
    ring("FPS", n(fs.average,0), "平均帧", 100, "#28f3ff", ""),
    ring("处理器", `${n(ss.cpuAvg,0)}%`, "占用率", ss.cpuAvg, "#a7ff2f", `峰值 ≈ ${n(ss.cpuMax,0)}%`),
    ring("GPU", `${n(ss.gpuAvg,0)}%`, "占用率", ss.gpuAvg, "#28f3ff", `功耗 ≈ ${n(ss.powerAvg,0)}W`),
    ring("显卡温度", `${n(ss.gpuTempAvg,0)}°C`, "温度", ss.gpuTempAvg, "#ffd247", `GPU频率 ≈ ${n(ss.gpuClockAvg,0)}MHz`),
    ring("显存", `${n(ss.vramUsedPctAvg,0)}%`, `${n(ss.vramUsedAvg,2)}/${n(hd.vramTotalGb,1)} GB`, ss.vramUsedPctAvg, "#a7ff2f", ""),
    ring("内存", `${n(ss.memUsedPctAvg,0)}%`, `${n(ss.memUsedAvgGb,1)}/${n(hd.totalMemoryGb,1)} GB`, ss.memUsedPctAvg, "#ffd247", ""),
    ring("卡顿", String(fs.framesOver33), ">33ms", Math.min(100,fs.framesOver33*2), "#ff5e7e", `最大帧时 ${n(fs.maxFrameMs,1)}ms`)
  ].join("");
  const top=DATA.process.stats.slice(0,16); const max=Math.max(...top.map(p=>p.maxCpu),1);
  document.getElementById("processRows").innerHTML = top.map(p=>`<div class="row"><span title="${p.name}">${p.name}</span><span>${n(p.maxCpu,1)}%</span><div class="bar"><i style="width:${Math.max(3,p.maxCpu/max*100)}%"></i></div></div>`).join("");
  const rows=[["平均 FPS",n(fs.average,2)],["1% Low",n(fs.low1,2)],["0.1% Low",n(fs.low01,2)],["最低瞬时 FPS",n(fs.minInstant,3)],[">20ms 帧",fs.framesOver20],[">33ms 帧",fs.framesOver33],[">100ms 帧",fs.framesOver100],["最大帧时间",`${n(fs.maxFrameMs,3)} ms`]];
  document.getElementById("summaryRows").innerHTML = rows.map(r=>`<div class="row"><span>${r[0]}</span><span>${r[1]}</span><div class="bar"><i style="width:64%"></i></div></div>`).join("");
}
function resizeCanvas(){ const r=canvas.getBoundingClientRect(); const d=window.devicePixelRatio||1; canvas.width=Math.round(r.width*d); canvas.height=Math.round(r.height*d); ctx.setTransform(d,0,0,d,0,0); draw(); }
function chartDims(){ const r=canvas.getBoundingClientRect(); return {w:r.width,h:r.height,pw:r.width-PAD.l-PAD.r,ph:r.height-PAD.t-PAD.b}; }
function visibleProcessIndexes(){
  const q=processSearch.value.trim().toLowerCase();
  const names=DATA.process.names;
  const indexes=[];
  for(let i=0;i<names.length;i++){ if(!q || names[i].toLowerCase().includes(q)) indexes.push(i); }
  return indexes;
}
function setOptions(){
  processSearch.style.display = view==="process" ? "" : "none";
  if(view==="process"){ metricSelect.innerHTML = `<option value="cpu">后台进程 CPU</option><option value="mem">后台进程内存</option>`; metricSelect.value=processMetric; return; }
  if(view==="fps") metricSelect.innerHTML = `<option>FPS</option>`;
  if(view==="perf") metricSelect.innerHTML = `<option>CPU/GPU 频率</option>`;
  if(view==="system") metricSelect.innerHTML = `<option>系统占用率</option>`;
  if(view==="io") metricSelect.innerHTML = `<option>IO / 功耗 / 温度</option>`;
  if(view==="fps" && !DATA.notes.frameDataCaptured){
    viewTitle.textContent="帧率数据未捕获";
    viewNote.textContent="PresentMon 本次没有写入帧数据；本页只保留系统和后台进程诊断数据，不会作为正常帧率报告自动弹出。";
  }
}
function buildSeries(){
  currentYMax=null;
  if(view==="fps"){
    currentTimes=DATA.fps.t; currentUnit="FPS";
    currentSeries=[
      {name:"平均 FPS",color:"#28f3ff",data:DATA.fps.avg,fill:true},
      {name:"1% Low",color:"#a7ff2f",data:DATA.fps.low1},
      {name:"0.1% Low",color:"#ffd247",data:DATA.fps.low01},
      {name:"最低瞬时 FPS",color:"#ff5e7e",data:DATA.fps.min},
    ]; return;
  }
  if(view==="system"){
    currentTimes=DATA.system.t; currentUnit="%"; currentYMax=100;
    currentSeries=[
      {name:"处理器占用",color:"#28f3ff",data:DATA.system.usage.cpu},
      {name:"显卡占用",color:"#a7ff2f",data:DATA.system.usage.gpu},
      {name:"显存控制器",color:"#ffd247",data:DATA.system.usage.gpuMem},
      {name:"内存占用",color:"#ff5e7e",data:DATA.system.usage.mem},
      {name:"显存占用",color:"#66a6ff",data:DATA.system.usage.vram},
    ]; return;
  }
  if(view==="perf"){
    currentTimes=DATA.system.t; currentUnit="MHz";
    currentSeries=[
      {name:"CPU 频率",color:"#28f3ff",data:DATA.system.perf.cpuFreq},
      {name:"GPU 频率",color:"#a7ff2f",data:DATA.system.perf.gpuClock},
      {name:"显存频率",color:"#ffd247",data:DATA.system.perf.memClock},
    ]; return;
  }
  if(view==="io"){
    currentTimes=DATA.system.t; currentUnit="混合单位";
    currentSeries=[
      {name:"磁盘 MB/s",color:"#28f3ff",data:DATA.system.io.disk},
      {name:"网络 MB/s",color:"#a7ff2f",data:DATA.system.io.net},
      {name:"磁盘延迟 ms",color:"#ffd247",data:DATA.system.io.diskLatency},
      {name:"GPU 功耗 W",color:"#ff5e7e",data:DATA.system.io.power},
      {name:"GPU 温度 °C",color:"#66a6ff",data:DATA.system.io.temp},
    ]; return;
  }
  const idxs=visibleProcessIndexes();
  currentTimes=DATA.process.t; currentUnit=processMetric==="cpu" ? "CPU %" : "MB";
  const matrix=processMetric==="cpu" ? DATA.process.cpu : DATA.process.mem;
  currentSeries=idxs.map((idx,n)=>({name:DATA.process.names[idx],color:COLORS[n%COLORS.length],data:matrix[idx]}));
}
function draw(){
  if(!DATA) return; buildSeries();
  const {w,h,pw,ph}=chartDims(); ctx.clearRect(0,0,w,h);
  ctx.fillStyle="#111821"; ctx.fillRect(0,0,w,h);
  const vals=[]; for(const s of currentSeries){ for(const v of s.data){ if(v!==null && Number.isFinite(Number(v))) vals.push(Number(v)); } }
  const maxT=Math.max(...currentTimes,1); let maxY=currentYMax || Math.max(...vals,1); if(!currentYMax) maxY=maxY>100?Math.ceil(maxY/100)*100:Math.ceil(maxY/10)*10;
  function x(sec){ return PAD.l + (sec/maxT)*pw; } function y(v){ return PAD.t + ph - (Number(v)/maxY)*ph; }
  ctx.strokeStyle="rgba(155,205,235,.25)"; ctx.lineWidth=1; ctx.fillStyle="#9fb6c8"; ctx.font="12px Segoe UI";
  for(let i=0;i<=6;i++){ const yy=PAD.t+ph/6*i; ctx.beginPath(); ctx.moveTo(PAD.l,yy); ctx.lineTo(PAD.l+pw,yy); ctx.stroke(); const val=maxY-(maxY/6)*i; ctx.fillText(maxY>100?val.toFixed(0):val.toFixed(1),8,yy+4); }
  for(let i=0;i<=8;i++){ const xx=PAD.l+pw/8*i; ctx.beginPath(); ctx.moveTo(xx,PAD.t); ctx.lineTo(xx,PAD.t+ph); ctx.stroke(); ctx.fillText(mmss(maxT/8*i),xx-14,h-16); }
  for(let si=0;si<currentSeries.length;si++){
    const s=currentSeries[si]; const alpha=view==="process" ? (si<18?.72:.28) : .95;
    ctx.strokeStyle=s.color; ctx.globalAlpha=alpha; ctx.lineWidth=view==="process" ? 1.1 : (si===0?3:2);
    ctx.beginPath(); let started=false;
    for(let i=0;i<currentTimes.length;i++){ const v=s.data[i]; if(v===null || !Number.isFinite(Number(v))) { started=false; continue; } const xx=x(currentTimes[i]); const yy=y(v); if(!started){ ctx.moveTo(xx,yy); started=true; } else ctx.lineTo(xx,yy); }
    ctx.stroke(); ctx.globalAlpha=1;
  }
  ctx.strokeStyle="rgba(96,147,179,.75)"; ctx.strokeRect(PAD.l,PAD.t,pw,ph);
  legend.innerHTML=currentSeries.slice(0,60).map(s=>`<span><i class="dot" style="background:${s.color}"></i>${s.name}</span>`).join("");
}
function nearestIndex(times, sec){ let best=0,bd=Infinity; for(let i=0;i<times.length;i++){ const d=Math.abs(times[i]-sec); if(d<bd){bd=d;best=i;} } return best; }
function hover(evt){
  const {w,h,pw,ph}=chartDims(); const rect=canvas.getBoundingClientRect(); const mx=evt.clientX-rect.left; const my=evt.clientY-rect.top;
  if(mx<PAD.l||mx>PAD.l+pw||my<PAD.t||my>PAD.t+ph){ tooltip.style.opacity=0; draw(); return; }
  const maxT=Math.max(...currentTimes,1); const sec=(mx-PAD.l)/pw*maxT; const idx=nearestIndex(currentTimes,sec);
  draw(); ctx.strokeStyle="rgba(255,255,255,.6)"; ctx.beginPath(); ctx.moveTo(mx,PAD.t); ctx.lineTo(mx,PAD.t+ph); ctx.stroke();
  let rows=[];
  if(view==="process"){
    for(const s of currentSeries){ const v=s.data[idx]; if(v!==null && Number.isFinite(Number(v))) rows.push({name:s.name,value:v,color:s.color}); }
    rows.sort((a,b)=>Number(b.value)-Number(a.value)); rows=rows.slice(0,24);
  } else {
    rows=currentSeries.map(s=>({name:s.name,value:s.data[idx],color:s.color})).filter(r=>r.value!==null);
  }
  tooltip.innerHTML=`<b>${mmss(currentTimes[idx])}</b><br>`+rows.map(r=>`<span style="color:${r.color}">■</span> ${r.name}: ${n(r.value, view==="process"&&processMetric==="mem"?1:2)} ${currentUnit}`).join("<br>");
  tooltip.style.left=Math.min(rect.width-450,Math.max(8,mx+14))+"px"; tooltip.style.top=Math.max(8,my+14)+"px"; tooltip.style.opacity=1;
}
document.querySelectorAll(".tab").forEach(btn=>btn.addEventListener("click",()=>{document.querySelectorAll(".tab").forEach(b=>b.classList.remove("active")); btn.classList.add("active"); view=btn.dataset.view; setOptions(); draw();}));
metricSelect.addEventListener("change",()=>{ if(view==="process") processMetric=metricSelect.value; draw(); });
processSearch.addEventListener("input",draw);
canvas.addEventListener("mousemove",hover); canvas.addEventListener("mouseleave",()=>{tooltip.style.opacity=0; draw();});
window.addEventListener("resize",resizeCanvas);
initStatic(); setOptions(); resizeCanvas();
</script>
</body>
</html>"""


def make_html() -> str:
    return r"""<!doctype html>
<html lang="zh-CN">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>FrameScope - CS2 性能监测报告</title>
  <style>
    :root {
      --bg:#0c1118; --panel:#141d28; --panel-2:#1b2836; --line:#385467;
      --text:#eff7ff; --muted:#9fb4c4; --cyan:#29e6ff; --green:#a9ff47;
      --yellow:#ffd35b; --red:#ff5d7d; --blue:#65a7ff;
    }
    * { box-sizing: border-box; }
    body {
      margin: 0; color: var(--text); background: #0c1118;
      font-family: "Segoe UI", "Microsoft YaHei", Arial, sans-serif;
    }
    .shell {
      width: min(1760px, calc(100vw - 22px)); margin: 11px auto;
      display: grid; grid-template-columns: 330px minmax(0,1fr); gap: 12px;
    }
    .left, .main {
      border: 1px solid rgba(94,137,160,.45); background: rgba(20,29,40,.98);
      border-radius: 8px;
    }
    .left { padding: 16px; }
    .main { padding: 16px 18px 18px; min-width: 0; }
    .brand { color: var(--yellow); font-size: 30px; font-weight: 900; letter-spacing: 0; }
    .brand-sub { color: var(--cyan); font-size: 14px; margin: 4px 0 14px; }
    .block { border-top: 1px solid rgba(143,185,210,.18); padding: 12px 0; }
    .block h3 { margin: 0 0 8px; color: var(--cyan); font-size: 15px; }
    .line { display: flex; justify-content: space-between; gap: 12px; line-height: 1.65; font-size: 13px; }
    .line span:first-child { color: var(--muted); }
    .line b { color: #fff1d0; text-align: right; overflow-wrap: anywhere; }
    .note { color: var(--muted); font-size: 13px; line-height: 1.55; }
    .topbar { display:flex; align-items:center; justify-content:space-between; gap:14px; border-bottom:1px solid rgba(143,185,210,.18); padding-bottom:12px; }
    .game { display:flex; align-items:center; gap:12px; min-width:0; }
    .game-icon { width:42px; height:42px; display:grid; place-items:center; border-radius:8px; background:#b51e2d; color:white; font-weight:900; font-size:22px; }
    .game-name { font-size:22px; font-weight:900; }
    .meta { display:flex; flex-wrap:wrap; gap:14px; color:var(--muted); font-size:13px; margin-top:4px; }
    .tabs { display:flex; gap:8px; flex-wrap:wrap; justify-content:flex-end; }
    .tab { height:34px; padding:0 12px; border-radius:5px; border:1px solid rgba(75,213,236,.42); background:#213145; color:#d9eef7; font-weight:800; cursor:pointer; transition: background .18s ease, color .18s ease, border-color .18s ease, transform .18s ease, box-shadow .18s ease; }
    .tab:hover { transform: translateY(-1px); border-color: rgba(41,230,255,.72); }
    .tab.active { color:#06141a; background:var(--cyan); box-shadow:0 0 16px rgba(41,230,255,.32); }
    .title { display:flex; align-items:baseline; justify-content:space-between; gap:12px; margin:14px 0 8px; }
    .title h2 { margin:0; color:var(--cyan); font-size:21px; }
    .title span { color:var(--muted); font-size:13px; text-align:right; }
    .gauges { display:grid; grid-template-columns: 170px repeat(6,minmax(112px,1fr)); gap: 14px; align-items:start; margin: 8px 0 12px; }
    .gauge { text-align:center; min-width:0; }
    .gauge h3 { margin:0 0 8px; font-size:18px; color:#ffe8bd; }
    .ring { --p:0; --c:var(--cyan); width:104px; height:104px; margin:auto; border-radius:50%; display:grid; place-items:center; background:radial-gradient(circle at center,#132032 0 54%,transparent 56%), conic-gradient(var(--c) calc(var(--p)*1%), rgba(85,122,145,.35) 0); box-shadow: inset 0 0 0 2px rgba(138,181,207,.25); }
    .ring.big { width:126px; height:126px; }
    .ring b { font-size:27px; line-height:1; text-shadow:0 0 12px rgba(41,230,255,.36); }
    .ring.big b { font-size:38px; color:var(--cyan); }
    .ring small { display:block; color:#d8e6ef; font-size:12px; margin-top:5px; }
    .gauge .foot { color:var(--yellow); font-size:12px; margin-top:7px; min-height: 16px; }
    .toolbar { display:flex; align-items:center; justify-content:space-between; gap:12px; margin:8px 0; flex-wrap:wrap; }
    .left-tools,.right-tools { display:flex; align-items:center; gap:10px; flex-wrap:wrap; }
    select,input { background:#26384b; color:#fff; border:1px solid rgba(75,213,236,.45); height:38px; border-radius:5px; padding:0 10px; font-weight:800; outline:none; transition: border-color .18s ease, box-shadow .18s ease, background .18s ease; }
    select:hover,input:hover,select:focus,input:focus { border-color: rgba(41,230,255,.78); box-shadow: 0 0 0 2px rgba(41,230,255,.1); }
    input { min-width:240px; }
    .chartbox { position:relative; height:560px; border:1px solid rgba(96,147,179,.52); background:#111a24; overflow:hidden; transition: border-color .2s ease, box-shadow .2s ease; }
    .chartbox.switching { border-color: rgba(41,230,255,.82); box-shadow: inset 0 0 18px rgba(41,230,255,.08); }
    canvas { position:absolute; inset:0; width:100%; height:100%; display:block; opacity:1; transform:translateY(0); transition: opacity .16s ease, transform .16s ease; }
    #overlay { pointer-events:none; }
    .chartbox.switching canvas { opacity:.46; transform:translateY(5px); }
    .tooltip { position:absolute; pointer-events:none; opacity:0; min-width:260px; max-width:460px; background:rgba(24,33,46,.96); border:1px solid rgba(196,226,241,.25); box-shadow:0 12px 36px rgba(0,0,0,.38); border-radius:5px; padding:10px 12px; font-size:13px; line-height:1.55; white-space:normal; z-index:5; transition: opacity .1s ease, transform .1s ease; }
    .legend { display:flex; gap:10px 12px; flex-wrap:wrap; max-height:76px; overflow:auto; color:#dceef7; font-size:12px; }
    .dot { width:12px; height:12px; border-radius:3px; display:inline-block; margin-right:5px; vertical-align:-2px; }
    .panelgrid { margin-top:13px; display:grid; grid-template-columns:1fr 1fr; gap:12px; }
    .card { border:1px solid rgba(87,132,159,.42); background:rgba(27,40,54,.9); border-radius:8px; padding:12px 14px; min-width:0; }
    .card h3 { margin:0 0 10px; color:var(--yellow); font-size:15px; }
    .rows { display:grid; grid-template-columns:repeat(2,minmax(0,1fr)); gap:7px 16px; font-size:13px; }
    .row { display:grid; grid-template-columns:minmax(90px,1fr) 78px 88px; gap:8px; align-items:center; min-width:0; }
    .row span:first-child { overflow:hidden; white-space:nowrap; text-overflow:ellipsis; }
    .bar { height:5px; border-radius:99px; background:#2e455b; overflow:hidden; }
    .bar i { display:block; height:100%; background:linear-gradient(90deg,var(--cyan),var(--green)); }
    @media (max-width:1200px){
      .shell{grid-template-columns:1fr}.gauges{grid-template-columns:repeat(2,minmax(0,1fr))}.panelgrid{grid-template-columns:1fr}
      .title{display:block}.title span{text-align:left;display:block;margin-top:5px}
    }
  </style>
</head>
<body>
<div class="shell">
  <aside class="left">
    <div class="brand">FrameScope</div>
    <div class="brand-sub">单页性能监测报告</div>
    <div class="block"><h3>处理器</h3><div class="line"><span id="hwCpu">-</span></div><div class="line"><span>核心/线程</span><b id="hwCore">-</b></div><div class="line"><span>标称最大频率</span><b id="hwCpuClock">-</b></div></div>
    <div class="block"><h3>显卡</h3><div class="line"><span id="hwGpu">-</span></div><div class="line"><span>驱动版本</span><b id="hwDriver">-</b></div><div class="line"><span>记录显存</span><b id="hwVram">-</b></div></div>
    <div class="block"><h3>系统</h3><div class="line"><span id="hwOs">-</span></div><div class="line"><span>内存</span><b id="hwMem">-</b></div><div class="line"><span>采样点</span><b id="hwSamples">-</b></div></div>
    <div class="block"><h3>交互</h3><div class="note">所有监测结果集中在这一页。上方按钮切换图表，后台进程 CPU 和内存在同一视图内切换，鼠标悬停可查看对应时间点的实时占用。</div></div>
  </aside>
  <main class="main">
    <div class="topbar">
      <div class="game"><div class="game-icon" id="gameIcon">FS</div><div><div class="game-name" id="gameName">-</div><div class="meta"><span id="runStart">-</span><span id="runEnd">-</span><span id="runDuration">-</span></div></div></div>
      <div class="tabs">
        <button class="tab active" data-view="fps">帧率</button>
        <button class="tab" data-view="perf">性能图表</button>
        <button class="tab" data-view="system">系统占用</button>
        <button class="tab" data-view="process">后台进程</button>
        <button class="tab" data-view="io">IO/温度</button>
      </div>
    </div>
    <div class="title"><h2 id="viewTitle">帧率波动</h2><span id="viewNote">完整数据保留在本地 data.js，图表按屏幕像素自适应绘制。</span></div>
    <div class="gauges" id="gauges"></div>
    <div class="toolbar">
      <div class="left-tools"><select id="metricSelect"></select><input id="processSearch" placeholder="搜索进程，留空显示全部后台进程"></div>
      <div class="right-tools"><div class="legend" id="legend"></div></div>
    </div>
    <div class="chartbox" id="chartBox"><canvas id="chart"></canvas><canvas id="overlay"></canvas><div class="tooltip" id="tooltip"></div></div>
    <div class="panelgrid">
      <div class="card"><h3>后台进程峰值</h3><div class="rows" id="processRows"></div></div>
      <div class="card"><h3>本次帧率摘要</h3><div class="rows" id="summaryRows"></div></div>
    </div>
  </main>
</div>
<script src="framescope-interactive-data.js"></script>
<script>
const DATA = window.FRAMESCOPE_DATA;
const COLORS = (DATA && DATA.colors) || ["#29e6ff","#a9ff47","#ffd35b","#ff5d7d","#65a7ff"];
let view = "fps";
let fpsMetric = "all";
let perfMetric = "all";
let systemMetric = "all";
let ioMetric = "all";
let processMetric = "cpu";
const canvas = document.getElementById("chart");
const ctx = canvas.getContext("2d");
const overlay = document.getElementById("overlay");
const octx = overlay.getContext("2d");
const tooltip = document.getElementById("tooltip");
const metricSelect = document.getElementById("metricSelect");
const processSearch = document.getElementById("processSearch");
const legend = document.getElementById("legend");
const chartBox = document.getElementById("chartBox");
const viewTitle = document.getElementById("viewTitle");
const viewNote = document.getElementById("viewNote");
const PAD = {l:58,r:22,t:28,b:42};
let currentSeries = [];
let currentTimes = [];
let currentUnit = "";
let currentYMax = null;
const renderCache = new Map();
const yMaxCache = new Map();
let legendKey = "";
let hoverFrame = 0;
let pendingHoverEvent = null;

function esc(v){ return String(v ?? "").replace(/[&<>"']/g, ch => ({"&":"&amp;","<":"&lt;",">":"&gt;","\"":"&quot;","'":"&#39;"}[ch])); }
function n(v,d=1){ const num=Number(v); return v===null || v===undefined || !Number.isFinite(num) ? "N/A" : num.toFixed(d); }
function mmss(sec){ sec=Math.max(0,Math.round(Number(sec)||0)); return `${Math.floor(sec/60)}:${String(sec%60).padStart(2,"0")}`; }
function setText(id,v){ const e=document.getElementById(id); if(e) e.textContent=v; }
function pct(v){ return Math.max(0,Math.min(100,Number(v)||0)); }
function maxFinite(arr, fallback=1){ let m=fallback; for(let i=0;i<arr.length;i++){ const v=Number(arr[i]); if(Number.isFinite(v) && v>m) m=v; } return m; }
function seriesMaxValue(s, fallback=1){
  const d=s.data||[];
  const key=`${s.key||s.name}|${d.length}|max`;
  if(yMaxCache.has(key)) return yMaxCache.get(key);
  let m=fallback;
  for(let i=0;i<d.length;i++){ const v=Number(d[i]); if(Number.isFinite(v) && v>m) m=v; }
  yMaxCache.set(key,m);
  return m;
}
function maxSeriesValue(series, fallback=1){ let m=fallback; for(const s of series){ m=Math.max(m, seriesMaxValue(s, fallback)); } return m; }
function hasAnyValue(arr){ for(let i=0;i<arr.length;i++){ if(Number.isFinite(Number(arr[i]))) return true; } return false; }
function clearOverlay(){ octx.clearRect(0,0,overlay.width,overlay.height); }
function getRenderablePoints(series, pixelWidth){
  const data=series.data||[];
  const len=Math.min(currentTimes.length, data.length);
  if(len<=0) return {t:[], y:[]};
  const widthBucket=Math.max(1,Math.round(pixelWidth));
  const bucketCount=Math.max(260,Math.ceil(widthBucket*(view==="process" ? .75 : 1.6)));
  const cacheKey=`${series.key||series.name}|${len}|${widthBucket}|${view}`;
  if(renderCache.has(cacheKey)) return renderCache.get(cacheKey);
  if(len<=bucketCount*4){
    const raw={t:currentTimes, y:data};
    renderCache.set(cacheKey, raw);
    return raw;
  }
  const t=[], y=[];
  const step=len/bucketCount;
  for(let b=0;b<bucketCount;b++){
    const start=Math.floor(b*step);
    const end=Math.min(len, Math.max(start+1, Math.floor((b+1)*step)));
    let first=-1,last=-1,minI=-1,maxI=-1,minV=Infinity,maxV=-Infinity;
    for(let i=start;i<end;i++){
      const v=Number(data[i]);
      if(!Number.isFinite(v)) continue;
      if(first<0) first=i;
      last=i;
      if(v<minV){ minV=v; minI=i; }
      if(v>maxV){ maxV=v; maxI=i; }
    }
    if(first<0){
      const gap=Math.min(len-1, Math.floor((start+end)/2));
      t.push(Number(currentTimes[gap])||0); y.push(null);
      continue;
    }
    const indexes=[first,minI,maxI,last].filter((v,i,a)=>v>=0 && a.indexOf(v)===i).sort((a,b)=>a-b);
    for(const idx of indexes){ t.push(currentTimes[idx]); y.push(data[idx]); }
  }
  const result={t,y};
  renderCache.set(cacheKey,result);
  return result;
}
function updateLegend(){
  const key=`${view}|${currentUnit}|${currentSeries.map(s=>s.key||s.name).join(";")}`;
  if(key===legendKey) return;
  legendKey=key;
  const shown=currentSeries.slice(0,80).map(s=>`<span><i class="dot" style="background:${s.color}"></i>${esc(s.name)}</span>`).join("");
  legend.innerHTML=shown + (currentSeries.length>80 ? `<span>另外 ${currentSeries.length-80} 条曲线已绘制</span>` : "");
}
function ring(title,value,sub,p,color,foot){
  return `<div class="gauge"><h3>${esc(title)}</h3><div class="ring ${title==="FPS"?"big":""}" style="--p:${pct(p)};--c:${color}"><div><b>${esc(value)}</b><small>${esc(sub)}</small></div></div><div class="foot">${esc(foot||"")}</div></div>`;
}
function initStatic(){
  const h=DATA.hardware || {}, hd=DATA.hardwareDerived || {}, fs=DATA.frameStats || {}, ss=DATA.systemStats || {};
  const targetName=(DATA.target && (DATA.target.displayName || DATA.target.processName)) || "cs2.exe";
  setText("gameName", targetName);
  setText("gameIcon", targetName.replace(/\.exe$/i,"").slice(0,2).toUpperCase() || "FS");
  document.title = `FrameScope - ${targetName} 性能监测报告`;
  setText("hwCpu", h.CpuName || "N/A");
  setText("hwCore", h.CpuCores ? `${h.CpuCores} / ${h.CpuThreads}` : "N/A");
  setText("hwCpuClock", h.CpuMaxClockMHz ? `${h.CpuMaxClockMHz} MHz` : "N/A");
  setText("hwGpu", h.GpuName || "N/A");
  setText("hwDriver", h.GpuDriver || "N/A");
  setText("hwVram", hd.vramTotalGb ? `${n(hd.vramTotalGb,1)} GB` : "N/A");
  setText("hwOs", `${h.OsCaption || "Windows"} ${h.OsArch || ""}`.trim());
  setText("hwMem", hd.totalMemoryGb ? `${n(hd.totalMemoryGb,1)} GB` : "N/A");
  setText("hwSamples", `${DATA.counts.frames} 帧 / ${DATA.counts.processSamples} 次进程采样`);
  setText("runStart", `开始时间：${DATA.run.startLabel}`);
  setText("runEnd", `结束时间：${DATA.run.endLabel}`);
  setText("runDuration", `记录时长：${DATA.run.durationLabel}`);
  document.getElementById("gauges").innerHTML = [
    ring("FPS", n(fs.average,0), "平均帧", 100, "#29e6ff", ""),
    ring("处理器", `${n(ss.cpuAvg,0)}%`, "占用率", ss.cpuAvg, "#a9ff47", `峰值 ${n(ss.cpuMax,0)}%`),
    ring("GPU", `${n(ss.gpuAvg,0)}%`, "占用率", ss.gpuAvg, "#29e6ff", `功耗 ${n(ss.powerAvg,0)}W`),
    ring("显卡温度", `${n(ss.gpuTempAvg,0)}°C`, "温度", ss.gpuTempAvg, "#ffd35b", `GPU频率 ${n(ss.gpuClockAvg,0)}MHz`),
    ring("显存", `${n(ss.vramUsedPctAvg,0)}%`, `${n(ss.vramUsedAvg,2)}/${n(hd.vramTotalGb,1)} GB`, ss.vramUsedPctAvg, "#a9ff47", ""),
    ring("内存", `${n(ss.memUsedPctAvg,0)}%`, `${n(ss.memUsedAvgGb,1)}/${n(hd.totalMemoryGb,1)} GB`, ss.memUsedPctAvg, "#ffd35b", ""),
    ring("卡顿帧", String(fs.framesOver33 ?? 0), ">33ms", Math.min(100,(fs.framesOver33||0)*2), "#ff5d7d", `最大帧时 ${n(fs.maxFrameMs,1)}ms`)
  ].join("");
  const top=(DATA.process.stats || []).slice(0,16);
  const max=maxFinite(top.map(p=>p.maxCpu),1);
  document.getElementById("processRows").innerHTML = top.map(p=>`<div class="row"><span title="${esc(p.name)}">${esc(p.name)}</span><span>${n(p.maxCpu,1)}%</span><div class="bar"><i style="width:${Math.max(3,Number(p.maxCpu||0)/max*100)}%"></i></div></div>`).join("");
  const rows=[["平均 FPS",n(fs.average,2)],["1% Low",n(fs.low1,2)],["0.1% Low",n(fs.low01,2)],["最低瞬时 FPS",n(fs.minInstant,3)],[">20ms 帧",fs.framesOver20 ?? 0],[">33ms 帧",fs.framesOver33 ?? 0],[">100ms 帧",fs.framesOver100 ?? 0],["最大帧时间",`${n(fs.maxFrameMs,3)} ms`]];
  document.getElementById("summaryRows").innerHTML = rows.map(r=>`<div class="row"><span>${esc(r[0])}</span><span>${esc(r[1])}</span><div class="bar"><i style="width:64%"></i></div></div>`).join("");
}
function resizeCanvas(){
  const r=chartBox.getBoundingClientRect();
  const d=window.devicePixelRatio || 1;
  canvas.width=overlay.width=Math.max(1,Math.round(r.width*d));
  canvas.height=overlay.height=Math.max(1,Math.round(r.height*d));
  ctx.setTransform(d,0,0,d,0,0);
  octx.setTransform(d,0,0,d,0,0);
  renderCache.clear();
  draw();
}
function chartDims(){ const r=chartBox.getBoundingClientRect(); return {w:r.width,h:r.height,pw:r.width-PAD.l-PAD.r,ph:r.height-PAD.t-PAD.b}; }
function visibleProcessIndexes(){
  const q=processSearch.value.trim().toLowerCase();
  const names=DATA.process.names || [];
  const indexes=[];
  for(let i=0;i<names.length;i++){ if(!q || String(names[i]).toLowerCase().includes(q)) indexes.push(i); }
  return indexes;
}
function setOptions(){
  processSearch.style.display = view==="process" ? "" : "none";
  metricSelect.disabled = false;
  if(view==="process"){
    metricSelect.innerHTML = `<option value="cpu">后台进程 CPU</option><option value="mem">后台进程内存</option>`;
    metricSelect.value=processMetric;
  } else if(view==="fps") {
    metricSelect.innerHTML = `<option value="all">平均 FPS / 1% Low / 0.1% Low</option><option value="avg">只看平均 FPS</option><option value="low1">只看 1% Low</option><option value="low01">只看 0.1% Low</option><option value="min">只看最低瞬时 FPS</option>`;
    metricSelect.value=fpsMetric;
  } else if(view==="perf") {
    metricSelect.innerHTML = `<option value="all">CPU / GPU / 显存频率</option><option value="cpu">CPU 频率</option><option value="gpu">GPU 频率</option><option value="mem">显存频率</option>`;
    metricSelect.value=perfMetric;
  } else if(view==="system") {
    metricSelect.innerHTML = `<option value="all">CPU / GPU / 内存 / 显存占用</option><option value="cpu">CPU 占用</option><option value="gpu">GPU 占用</option><option value="gpuMem">显存控制器</option><option value="mem">内存占用</option><option value="vram">显存占用</option>`;
    metricSelect.value=systemMetric;
  } else if(view==="io") {
    metricSelect.innerHTML = `<option value="all">磁盘 / 网络 / 功耗 / 温度</option><option value="diskNet">磁盘 + 网络</option><option value="diskLatency">磁盘延迟</option><option value="powerTemp">GPU 功耗 + 温度</option>`;
    metricSelect.value=ioMetric;
  }
}
function setTitle(){
  const countText = `${DATA.counts.processes} 个后台进程，${DATA.counts.processSamples} 次进程采样`;
  if(view==="fps"){ viewTitle.textContent="帧率波动"; viewNote.textContent=`FPS 使用 ${DATA.fps.bucketMs || 100}ms 采样桶，坐标点更密，能更接近实时帧率变化。`; }
  if(view==="perf"){ viewTitle.textContent="性能图表"; viewNote.textContent=DATA.notes.cpuFrequencyCaptured ? "每个时间点的 CPU 频率、GPU 频率和显存频率。" : "本次旧记录未采集 CPU 频率；GPU/显存频率可用。下一次记录会包含 CPU 频率。"; }
  if(view==="system"){ viewTitle.textContent="系统占用"; viewNote.textContent="CPU、GPU、显存控制器、内存和显存占用率。"; }
  if(view==="process"){ viewTitle.textContent="后台进程监测"; viewNote.textContent=`${countText}。留空搜索框会绘制全部进程，悬停显示该时间点占用最高的进程。`; }
  if(view==="io"){ viewTitle.textContent="IO / 温度"; viewNote.textContent="磁盘、网络、磁盘延迟、GPU 功耗和温度。"; }
  if(view==="fps" && !DATA.notes.frameDataCaptured){
    viewTitle.textContent="帧率数据未捕获";
    viewNote.textContent="PresentMon 本次没有写入帧数据；本页只保留系统和后台进程诊断数据，不会作为正常帧率报告自动弹出。";
  }
}
function buildSeries(){
  currentYMax=null;
  if(view==="fps"){
    currentTimes=DATA.fps.t || []; currentUnit="FPS";
    const all=[
      {key:"fps:avg",name:"平均 FPS",color:"#29e6ff",data:DATA.fps.avg || []},
      {key:"fps:low1",name:"1% Low",color:"#a9ff47",data:DATA.fps.low1 || []},
      {key:"fps:low01",name:"0.1% Low",color:"#ffd35b",data:DATA.fps.low01 || []},
      {key:"fps:min",name:"最低瞬时 FPS",color:"#ff5d7d",data:DATA.fps.min || []},
    ];
    const map={avg:0,low1:1,low01:2,min:3};
    currentSeries=fpsMetric==="all" ? all : [all[map[fpsMetric] || 0]];
    return;
  }
  if(view==="perf"){
    currentTimes=DATA.system.t || []; currentUnit="MHz";
    const all=[
      {key:"perf:cpu",name:"CPU 频率",color:"#29e6ff",data:DATA.system.perf.cpuFreq || []},
      {key:"perf:gpu",name:"GPU 频率",color:"#a9ff47",data:DATA.system.perf.gpuClock || []},
      {key:"perf:mem",name:"显存频率",color:"#ffd35b",data:DATA.system.perf.memClock || []},
    ];
    const map={cpu:0,gpu:1,mem:2};
    currentSeries=perfMetric==="all" ? all : [all[map[perfMetric] || 0]];
    return;
  }
  if(view==="system"){
    currentTimes=DATA.system.t || []; currentUnit="%"; currentYMax=100;
    const all=[
      {key:"system:cpu",name:"CPU 占用",color:"#29e6ff",data:DATA.system.usage.cpu || []},
      {key:"system:gpu",name:"GPU 占用",color:"#a9ff47",data:DATA.system.usage.gpu || []},
      {key:"system:gpuMem",name:"显存控制器",color:"#ffd35b",data:DATA.system.usage.gpuMem || []},
      {key:"system:mem",name:"内存占用",color:"#ff5d7d",data:DATA.system.usage.mem || []},
      {key:"system:vram",name:"显存占用",color:"#65a7ff",data:DATA.system.usage.vram || []},
    ];
    const map={cpu:0,gpu:1,gpuMem:2,mem:3,vram:4};
    currentSeries=systemMetric==="all" ? all : [all[map[systemMetric] || 0]];
    return;
  }
  if(view==="io"){
    currentTimes=DATA.system.t || []; currentUnit="混合单位";
    const all=[
      {key:"io:disk",name:"磁盘 MB/s",color:"#29e6ff",data:DATA.system.io.disk || []},
      {key:"io:net",name:"网络 MB/s",color:"#a9ff47",data:DATA.system.io.net || []},
      {key:"io:latency",name:"磁盘延迟 ms",color:"#ffd35b",data:DATA.system.io.diskLatency || []},
      {key:"io:power",name:"GPU 功耗 W",color:"#ff5d7d",data:DATA.system.io.power || []},
      {key:"io:temp",name:"GPU 温度 °C",color:"#65a7ff",data:DATA.system.io.temp || []},
    ];
    if(ioMetric==="diskNet") currentSeries=[all[0],all[1]];
    else if(ioMetric==="diskLatency") currentSeries=[all[2]];
    else if(ioMetric==="powerTemp") currentSeries=[all[3],all[4]];
    else currentSeries=all;
    return;
  }
  const idxs=visibleProcessIndexes();
  currentTimes=DATA.process.t || []; currentUnit=processMetric==="cpu" ? "CPU %" : "MB";
  const matrix=processMetric==="cpu" ? (DATA.process.cpu || []) : (DATA.process.mem || []);
  currentSeries=idxs.map((idx,n)=>({key:`process:${processMetric}:${idx}`,name:DATA.process.names[idx],color:COLORS[n%COLORS.length],data:matrix[idx] || []}));
}
function draw(){
  if(!DATA) return;
  setTitle();
  buildSeries();
  const {w,h,pw,ph}=chartDims();
  clearOverlay();
  ctx.clearRect(0,0,w,h);
  ctx.fillStyle="#111a24"; ctx.fillRect(0,0,w,h);
  if(!currentTimes.length || !currentSeries.length){
    ctx.fillStyle="#9fb4c4"; ctx.font="15px Segoe UI"; ctx.fillText("本视图没有可绘制的数据。", PAD.l, PAD.t + 24);
    legend.innerHTML="";
    return;
  }
  const maxT=maxFinite(currentTimes,1);
  let maxY=currentYMax || maxSeriesValue(currentSeries,1);
  if(!currentYMax) maxY=maxY>100?Math.ceil(maxY/100)*100:Math.ceil(maxY/10)*10;
  maxY=Math.max(maxY,1);
  function x(sec){ return PAD.l + (Number(sec)/maxT)*pw; }
  function y(v){ return PAD.t + ph - (Number(v)/maxY)*ph; }
  ctx.strokeStyle="rgba(155,205,235,.24)";
  ctx.lineWidth=1; ctx.fillStyle="#9fb4c4"; ctx.font="12px Segoe UI";
  for(let i=0;i<=6;i++){
    const yy=PAD.t+ph/6*i;
    ctx.beginPath(); ctx.moveTo(PAD.l,yy); ctx.lineTo(PAD.l+pw,yy); ctx.stroke();
    const val=maxY-(maxY/6)*i; ctx.fillText(maxY>100?val.toFixed(0):val.toFixed(1),8,yy+4);
  }
  for(let i=0;i<=8;i++){
    const xx=PAD.l+pw/8*i;
    ctx.beginPath(); ctx.moveTo(xx,PAD.t); ctx.lineTo(xx,PAD.t+ph); ctx.stroke();
    ctx.fillText(mmss(maxT/8*i),xx-14,h-16);
  }
  for(let si=0;si<currentSeries.length;si++){
    const s=currentSeries[si];
    if(seriesMaxValue(s, -Infinity) === -Infinity) continue;
    const alpha=view==="process" ? (si < 18 ? .72 : .25) : .95;
    ctx.strokeStyle=s.color; ctx.globalAlpha=alpha; ctx.lineWidth=view==="process" ? 1.05 : (si===0?2.8:2);
    ctx.beginPath(); let started=false;
    const points=getRenderablePoints(s,pw);
    const len=Math.min(points.t.length, points.y.length);
    for(let i=0;i<len;i++){
      const v=points.y[i];
      if(v===null || !Number.isFinite(Number(v))) { started=false; continue; }
      const xx=x(points.t[i]); const yy=y(v);
      if(!started){ ctx.moveTo(xx,yy); started=true; } else ctx.lineTo(xx,yy);
    }
    ctx.stroke(); ctx.globalAlpha=1;
  }
  ctx.strokeStyle="rgba(96,147,179,.78)"; ctx.strokeRect(PAD.l,PAD.t,pw,ph);
  updateLegend();
}
function redrawWithTransition(){
  tooltip.style.opacity=0;
  chartBox.classList.add("switching");
  window.setTimeout(()=>{
    draw();
    window.requestAnimationFrame(()=>chartBox.classList.remove("switching"));
  }, 90);
}
function nearestIndex(times, sec){
  if(!times.length) return 0;
  if(sec<=Number(times[0])) return 0;
  let lo=0, hi=times.length-1;
  if(sec>=Number(times[hi])) return hi;
  while(hi-lo>1){
    const mid=(lo+hi)>>1;
    if(Number(times[mid])<sec) lo=mid; else hi=mid;
  }
  return Math.abs(Number(times[lo])-sec)<=Math.abs(Number(times[hi])-sec) ? lo : hi;
}
function drawHoverLine(mx){
  const {ph}=chartDims();
  clearOverlay();
  octx.strokeStyle="rgba(255,255,255,.62)";
  octx.lineWidth=1;
  octx.beginPath(); octx.moveTo(mx,PAD.t); octx.lineTo(mx,PAD.t+ph); octx.stroke();
}
function hover(evt){
  const {pw,ph}=chartDims();
  const rect=chartBox.getBoundingClientRect();
  const mx=evt.clientX-rect.left; const my=evt.clientY-rect.top;
  if(mx<PAD.l||mx>PAD.l+pw||my<PAD.t||my>PAD.t+ph){ tooltip.style.opacity=0; clearOverlay(); return; }
  const maxT=maxFinite(currentTimes,1);
  const sec=(mx-PAD.l)/pw*maxT;
  const idx=nearestIndex(currentTimes,sec);
  drawHoverLine(mx);
  let rows=[];
  if(view==="process"){
    for(const s of currentSeries){ const v=s.data[idx]; if(v!==null && Number.isFinite(Number(v))) rows.push({name:s.name,value:v,color:s.color}); }
    rows.sort((a,b)=>Number(b.value)-Number(a.value));
    rows=rows.slice(0,26);
  } else {
    rows=currentSeries.map(s=>({name:s.name,value:s.data[idx],color:s.color})).filter(r=>r.value!==null && Number.isFinite(Number(r.value)));
  }
  tooltip.innerHTML=`<b>${mmss(currentTimes[idx])}</b><br>`+rows.map(r=>`<span style="color:${r.color}">■</span> ${esc(r.name)}: ${n(r.value, view==="process"&&processMetric==="mem"?1:2)} ${currentUnit}`).join("<br>");
  tooltip.style.left=Math.min(rect.width-470,Math.max(8,mx+14))+"px";
  tooltip.style.top=Math.max(8,my+14)+"px";
  tooltip.style.opacity=1;
}
function scheduleHover(evt){
  pendingHoverEvent=evt;
  if(hoverFrame) return;
  hoverFrame=window.requestAnimationFrame(()=>{
    hoverFrame=0;
    const event=pendingHoverEvent;
    pendingHoverEvent=null;
    if(event) hover(event);
  });
}
if(DATA){
  document.querySelectorAll(".tab").forEach(btn=>btn.addEventListener("click",()=>{
    document.querySelectorAll(".tab").forEach(b=>b.classList.remove("active"));
    btn.classList.add("active");
    view=btn.dataset.view;
    setOptions();
    redrawWithTransition();
  }));
  metricSelect.addEventListener("change",()=>{
    if(view==="fps") fpsMetric=metricSelect.value;
    else if(view==="perf") perfMetric=metricSelect.value;
    else if(view==="system") systemMetric=metricSelect.value;
    else if(view==="io") ioMetric=metricSelect.value;
    else if(view==="process") processMetric=metricSelect.value;
    redrawWithTransition();
  });
  processSearch.addEventListener("input",()=>{ renderCache.clear(); legendKey=""; draw(); });
  chartBox.addEventListener("mousemove",scheduleHover);
  chartBox.addEventListener("mouseleave",()=>{tooltip.style.opacity=0; clearOverlay();});
  window.addEventListener("resize",resizeCanvas);
  initStatic(); setOptions(); resizeCanvas();
}
</script>
</body>
</html>"""


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("run_dir", nargs="?", help="CS2 monitor run directory. Defaults to newest run.")
    args = parser.parse_args()

    base_dir = Path.cwd()
    run_dir = Path(args.run_dir).resolve() if args.run_dir else find_latest_run(base_dir)
    output_dir = run_dir / "charts"
    output_dir.mkdir(parents=True, exist_ok=True)

    frames_raw = read_presentmon(run_dir / "presentmon.csv")
    system_rows = read_system(run_dir / "system-samples.csv")
    frames, time_shift = align_presentmon_time(frames_raw, system_rows)

    if frames:
        start, end = frames[0][0], frames[-1][0]
    elif system_rows:
        start, end = system_rows[0]["time"], system_rows[-1]["time"]
    else:
        start = end = datetime.now()
    duration_seconds = max(1, int((end - start).total_seconds()))

    hardware = load_hardware()
    run_metadata = load_run_metadata(run_dir)
    total_memory_mb = parse_float(hardware.get("TotalMemoryMB")) if hardware else None
    available_values = [row["available_mb"] for row in system_rows if row.get("available_mb") is not None]
    if not total_memory_mb and available_values:
        total_memory_mb = max(available_values)
    total_memory_gb = total_memory_mb / 1024 if total_memory_mb else None

    target_process = run_metadata.get("targetProcess") or "cs2.exe"
    process_times, process_names, process_cpu, process_mem, process_stats = read_process_matrix(
        run_dir / "process-samples.csv", start, target_process
    )
    frame_times = [ms for _, ms in frames]
    p99, p999 = percentile_high(frame_times, 0.99), percentile_high(frame_times, 0.999)
    frame_stats = {
        "average": rounded(1000 / (avg(frame_times) or 1), 2) if frame_times else None,
        "low1": rounded(1000 / p99 if p99 else None, 2),
        "low01": rounded(1000 / p999 if p999 else None, 2),
        "minInstant": rounded(1000 / max(frame_times) if frame_times else None, 3),
        "maxFrameMs": rounded(max(frame_times) if frame_times else None, 3),
        "framesOver20": sum(1 for ms in frame_times if ms > 20),
        "framesOver33": sum(1 for ms in frame_times if ms > 33.3),
        "framesOver100": sum(1 for ms in frame_times if ms > 100),
    }

    vram_total_values = [row["vram_total_gb"] for row in system_rows if row.get("vram_total_gb")]
    vram_total_gb = max(vram_total_values) if vram_total_values else None
    vram_used_values = [row["vram_used_gb"] for row in system_rows if row.get("vram_used_gb") is not None]
    cpu_values = [row["cpu"] for row in system_rows if row.get("cpu") is not None]
    gpu_values = [row["gpu"] for row in system_rows if row.get("gpu") is not None]
    gpu_temp_values = [row["gpu_temp"] for row in system_rows if row.get("gpu_temp") is not None]
    gpu_clock_values = [row["gpu_clock"] for row in system_rows if row.get("gpu_clock") is not None]
    power_values = [row["power"] for row in system_rows if row.get("power") is not None]
    available_avg_mb = avg([float(v) for v in available_values]) if available_values else None
    available_avg_gb = available_avg_mb / 1024 if available_avg_mb else None
    mem_used_avg_gb = total_memory_gb - available_avg_gb if total_memory_gb and available_avg_gb is not None else None
    mem_used_pct_avg = mem_used_avg_gb / total_memory_gb * 100 if total_memory_gb and mem_used_avg_gb is not None else None
    vram_used_avg = avg(vram_used_values)
    vram_used_pct_avg = vram_used_avg / vram_total_gb * 100 if vram_total_gb and vram_used_avg is not None else None
    system_stats = {
        "cpuAvg": rounded(avg(cpu_values), 2),
        "cpuMax": rounded(max(cpu_values) if cpu_values else None, 2),
        "gpuAvg": rounded(avg(gpu_values), 2),
        "gpuTempAvg": rounded(avg(gpu_temp_values), 2),
        "gpuClockAvg": rounded(avg(gpu_clock_values), 0),
        "powerAvg": rounded(avg(power_values), 2),
        "vramUsedAvg": rounded(vram_used_avg, 2),
        "vramUsedPctAvg": rounded(vram_used_pct_avg, 2),
        "memUsedAvgGb": rounded(mem_used_avg_gb, 2),
        "memUsedPctAvg": rounded(mem_used_pct_avg, 2),
    }

    has_frame_data = len(frames) > 0
    data = {
        "brand": BRAND,
        "colors": COLORS,
        "run": {
            "dir": str(run_dir),
            "startLabel": start.strftime("%Y-%m-%d %H:%M:%S"),
            "endLabel": end.strftime("%Y-%m-%d %H:%M:%S"),
            "durationLabel": fmt_duration(duration_seconds),
            "timeShiftHours": time_shift,
        },
        "target": {
            "processName": target_process,
            "displayName": target_process,
        },
        "hardware": hardware,
        "hardwareDerived": {"totalMemoryGb": rounded(total_memory_gb, 2), "vramTotalGb": rounded(vram_total_gb, 2)},
        "counts": {"frames": len(frames), "hasFrameData": has_frame_data, "processSamples": len(process_times), "processes": len(process_names), "systemSamples": len(system_rows)},
        "frameStats": frame_stats,
        "systemStats": system_stats,
        "fps": bucket_fps(frames, 0.1),
        "system": series_from_system(system_rows, start, total_memory_mb),
        "process": {"t": process_times, "names": process_names, "cpu": process_cpu, "mem": process_mem, "stats": process_stats},
        "notes": {
            "frameDataCaptured": has_frame_data,
            "cpuFrequencyCaptured": any(v is not None for v in series_from_system(system_rows, start, total_memory_mb)["perf"]["cpuFreq"]),
        },
    }

    data_path = output_dir / "framescope-interactive-data.js"
    data_path.write_text("window.FRAMESCOPE_DATA = " + json.dumps(data, ensure_ascii=False, separators=(",", ":")) + ";\n", encoding="utf-8")
    html_path = output_dir / "framescope-interactive-report.html"
    html_path.write_text(make_html(), encoding="utf-8")
    manifest = {
        "report": str(html_path),
        "data": str(data_path),
        "frames": len(frames),
        "hasFrameData": has_frame_data,
        "reportKind": "full" if has_frame_data else "diagnostic",
        "processes": len(process_names),
        "processSamples": len(process_times),
        "systemSamples": len(system_rows),
        "cpuFrequencyCaptured": data["notes"]["cpuFrequencyCaptured"],
    }
    (output_dir / "framescope-interactive-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(manifest, ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
