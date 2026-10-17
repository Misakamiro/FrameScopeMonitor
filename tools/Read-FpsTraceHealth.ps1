$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FrameScopeTraceHealth {
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode)]
    static extern uint ControlTraceW(ulong handle, string name, IntPtr properties, uint code);
    public static uint[] Read() {
        IntPtr p = Marshal.AllocHGlobal(4096);
        try {
            for (int i = 0; i < 4096; i += 4) Marshal.WriteInt32(p, i, 0);
            Marshal.WriteInt32(p, 0, 4096);
            Marshal.WriteInt32(p, 116, 120);
            uint code = ControlTraceW(0, "FrameScope_FPS", p, 0);
            return new uint[] { code, (uint)Marshal.ReadInt32(p, 48),
                (uint)Marshal.ReadInt32(p, 88), (uint)Marshal.ReadInt32(p, 96),
                (uint)Marshal.ReadInt32(p, 100) };
        } finally { Marshal.FreeHGlobal(p); }
    }
}
'@
$values = [FrameScopeTraceHealth]::Read()
[ordered]@{
    QueryCode = $values[0]
    BufferSizeKiB = $values[1]
    EventsLost = $values[2]
    LogBuffersLost = $values[3]
    RealTimeBuffersLost = $values[4]
} | ConvertTo-Json
if ($values[0] -ne 0) { exit 1 }
