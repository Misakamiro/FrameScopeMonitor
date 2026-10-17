"""Compare live OSD samples against an independent D3D Present-call trace."""
import bisect
import csv
import json
import math
import statistics
import sys


def read_csv(path):
    with open(path, encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def main():
    frames = read_csv(sys.argv[1])
    overlay = read_csv(sys.argv[2])
    assert frames and all(row["run_valid"] == "true" for row in frames)
    assert all(row["present_s_ok"] == "true" and row["state_verified"] == "true" for row in frames)
    frequency = int(frames[0]["qpc_frequency"])
    origin = int(frames[0]["measurement_start_qpc"])
    times = [(int(row["present_start_qpc"]) - origin) / frequency for row in frames]
    intervals = [times[i] - times[i - 1] for i in range(1, len(times))]
    stamps = times[1:]
    assert all(value > 0 for value in intervals)

    def reference(at, window, fraction):
        left = bisect.bisect_left(stamps, at - window)
        right = bisect.bisect_right(stamps, at)
        values = intervals[left:right]
        if not values or at < window + 0.5:
            return None
        worst = max(1, math.ceil(len(values) * fraction))
        return 1 / statistics.mean(sorted(values, reverse=True)[:worst])

    comparisons = []
    live = []
    for row in overlay:
        if not row.get("qpc") or row["fps"] in ("", "ERR"):
            continue
        at = (int(row["qpc"]) - origin) / frequency
        if 0 <= at <= times[-1]:
            live.append({"time": at, "fps": float(row["fps"])})
        if at < 12 or at > times[-1]:
            continue
        for field, window, fraction in [("fps_1low", 10, 0.01), ("fps_01low", 30, 0.001)]:
            if row[field] == "" or at < window + 2:
                continue
            # The producer caches Low at 1 Hz. Bound the expected reference
            # over the preceding 1.5 seconds instead of assuming simultaneous reads.
            refs = [reference(at - lag / 10, window, fraction) for lag in range(16)]
            refs = [value for value in refs if value is not None]
            if not refs:
                continue
            observed = float(row[field])
            tolerance = max(2, max(refs) * 0.02)
            comparisons.append({
                "time": at, "field": field, "observed": observed,
                "referenceMin": min(refs), "referenceMax": max(refs),
                "withinCacheEnvelope": min(refs) - tolerance <= observed <= max(refs) + tolerance,
            })
    checks = []
    for field in ("fps_1low", "fps_01low"):
        group = [row for row in comparisons if row["field"] == field]
        assert len(group) >= 10, f"Insufficient measured {field} samples"
        matched = sum(row["withinCacheEnvelope"] for row in group)
        checks.append({"field": field, "samples": len(group), "matched": matched, "fraction": matched / len(group)})
    phases = []
    for phase in sorted(set(int(row["phase"]) for row in frames)):
        selected = [row for row in frames if int(row["phase"]) == phase]
        begin = float(selected[0]["present_start_seconds"])
        end = float(selected[-1]["present_start_seconds"])
        actual = (len(selected) - 1) / (end - begin)
        readings = [row["fps"] for row in live if begin + 2 < row["time"] < end - 1]
        phases.append({"phase": phase, "target": int(selected[0]["target_fps"]),
                       "actualPresentRate": actual, "osdMedian": statistics.median(readings) if readings else None})
    result = {"frames": len(frames), "phases": phases, "lowChecks": checks, "comparisons": comparisons,
              "scope": "Actual successful API submissions, 1.5s Low-cache envelope; not scanout or anti-cheat verification"}
    with open(sys.argv[3], "w", encoding="utf-8") as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2)
    print(json.dumps({key: value for key, value in result.items() if key != "comparisons"}, ensure_ascii=False))
    assert all(check["fraction"] >= 0.95 for check in checks), "Low disagrees with independent frame-time reference"
    assert all(phase["osdMedian"] and abs(phase["osdMedian"] - phase["actualPresentRate"]) / phase["actualPresentRate"] < 0.05 for phase in phases)


if __name__ == "__main__":
    main()
