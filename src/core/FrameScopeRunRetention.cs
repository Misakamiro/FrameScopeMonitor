using System;

internal static class FrameScopeRunRetention
{
    // Log limits never authorize deleting reports or their source samples.
    internal static void MaintainHistory(string historyPath, Func<string, string> resolveRunDirectory)
    {
        FrameScopeHistoryFile.Compact(historyPath, resolveRunDirectory, delegate(string run) { return true; }, int.MaxValue);
    }
}
