using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

internal static partial class FrameScopeProcessSampler
{
    private sealed class ProcRow
    {
        public int Id;
        public string ProcessName;
        public double? CpuPct;
        public double WorkingSet;
        public double? ReadMBps;
        public double? WriteMBps;
    }

    private sealed class GroupStats
    {
        public int Count;
        public double CpuPct;
        public bool HasCpu;
        public double WorkingSet;
        public double ReadMBps;
        public double WriteMBps;
        public readonly List<int> Pids = new List<int>();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr processHandle, out IoCounters counters);
}
