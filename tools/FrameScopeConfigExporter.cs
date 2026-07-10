using System;

internal static class FrameScopeConfigExporter
{
    private static int Main(string[] args)
    {
        if (args == null || args.Length != 1) return 2;
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        config.DataRoot = "framescope-runs";
        FrameScopeConfigStore.Save(args[0], config);
        return 0;
    }
}
