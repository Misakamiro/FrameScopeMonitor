"""poll_overlay.py — 轮询 FrameScopeHardwareService 命名管道，记录 fps 读数曲线。

用法：python poll_overlay.py <seconds>   （CSV 输出到 stdout，自行重定向）
每 200ms 发一次 get_overlay_data，记录墙钟秒 + fps / fps_1low / fps_01low。
纯 ctypes 实现，无第三方依赖。
"""
import ctypes
import csv
import json
import sys
import time

GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 3
PIPE_READMODE_MESSAGE = 2
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.CreateFileW.restype = ctypes.c_void_p
kernel32.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
kernel32.ReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32), ctypes.c_void_p]
kernel32.WriteFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32), ctypes.c_void_p]
kernel32.SetNamedPipeHandleState.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint32), ctypes.c_void_p, ctypes.c_void_p]
kernel32.QueryPerformanceCounter.argtypes = [ctypes.POINTER(ctypes.c_int64)]
kernel32.CloseHandle.argtypes = [ctypes.c_void_p]

PIPE_NAME = r"\\.\pipe\FrameScopeHardwareService"


def connect():
    h = kernel32.CreateFileW(PIPE_NAME, GENERIC_READ | GENERIC_WRITE, 0, None, OPEN_EXISTING, 0, None)
    if h == INVALID_HANDLE_VALUE or h is None:
        raise OSError(f"connect pipe failed err={ctypes.get_last_error()}")
    mode = ctypes.c_uint32(PIPE_READMODE_MESSAGE)
    if not kernel32.SetNamedPipeHandleState(h, ctypes.byref(mode), None, None):
        raise OSError("SetNamedPipeHandleState failed")
    return h


def request(h, method, params=None, req_id="1"):
    payload = json.dumps({"id": req_id, "method": method, "params": params or {}}).encode()
    written = ctypes.c_uint32(0)
    if not kernel32.WriteFile(h, payload, len(payload), ctypes.byref(written), None):
        raise OSError("WriteFile failed")
    buf = ctypes.create_string_buffer(1 << 20)
    read = ctypes.c_uint32(0)
    if not kernel32.ReadFile(h, buf, len(buf) - 1, ctypes.byref(read), None):
        raise OSError("ReadFile failed")
    return json.loads(buf.raw[: read.value].decode("utf-8", "replace"))


def main():
    seconds = float(sys.argv[1]) if len(sys.argv) > 1 else 30
    h = connect()
    seq = 0
    rows = []
    t0 = time.time()
    while time.time() - t0 < seconds:
        seq += 1
        try:
            resp = request(h, "get_overlay_data", {}, str(seq))
            qpc = ctypes.c_int64()
            kernel32.QueryPerformanceCounter(ctypes.byref(qpc))
            result = resp.get("result") or {}
            rows.append([
                round(time.time() - t0, 2),
                result.get("fps"),
                result.get("fps_1low"),
                result.get("fps_01low"),
                result.get("present_mode"),
                qpc.value,
            ])
        except OSError as e:
            rows.append([round(time.time() - t0, 2), "ERR", str(e), "", "", ""])
            kernel32.CloseHandle(h)
            h = connect()
        time.sleep(0.2)
    kernel32.CloseHandle(h)
    writer = csv.writer(sys.stdout)
    writer.writerow(["t", "fps", "fps_1low", "fps_01low", "present_mode", "qpc"])
    writer.writerows(rows)
    fps_vals = [r[1] for r in rows if isinstance(r[1], (int, float))]
    if fps_vals:
        print(f"# samples={len(rows)} max={max(fps_vals)} last10={[v for v in fps_vals[-10:]]}", file=sys.stderr)
        print(f"# 1low last10={[r[2] for r in rows[-10:]]}", file=sys.stderr)
        print(f"# 0.1low last10={[r[3] for r in rows[-10:]]}", file=sys.stderr)
        print(f"# present_mode last={rows[-1][4] if rows else None}", file=sys.stderr)


if __name__ == "__main__":
    main()
