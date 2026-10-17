using System;

public static class FrameScopeHotkeyConfigTests
{
    public static void Main()
    {
        DefaultsUseShiftF10();
        EmptyValuesRestoreDefault();
        Console.WriteLine("FrameScopeHotkeyConfigTests: PASS");
    }

    private static void DefaultsUseShiftF10()
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        AssertEqual("Shift+F10", config.OverlayHotkey, "default overlay hotkey");
    }

    private static void EmptyValuesRestoreDefault()
    {
        FrameScopeConfig config = FrameScopeConfigStore.CreateDefaultConfig();
        config.OverlayHotkey = "  ";
        FrameScopeConfigStore.Normalize(config);
        AssertEqual("Shift+F10", config.OverlayHotkey, "empty overlay hotkey fallback");
    }

    private static void AssertEqual(string expected, string actual, string message)
    {
        if (!String.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException(message + ": expected " + expected + ", got " + actual);
    }
}
