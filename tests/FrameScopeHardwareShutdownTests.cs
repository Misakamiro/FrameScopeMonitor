using System;

internal static class FrameScopeHardwareShutdownTests
{
    private static int Main()
    {
        if (!FrameScopeHardwareServiceClient.MayStartService(false, false)) throw new Exception("normal start refused");
        if (FrameScopeHardwareServiceClient.MayStartService(true, false)
            || FrameScopeHardwareServiceClient.MayStartService(false, true)
            || FrameScopeHardwareServiceClient.MayStartService(true, true)) throw new Exception("late hardware start allowed");
        Console.WriteLine("FrameScopeHardwareShutdownTests: PASS");
        return 0;
    }
}
