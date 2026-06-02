using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

internal static class FrameScopeIconTests
{
    private static readonly int[] RequiredSizes = { 16, 24, 32, 48, 64, 128, 256 };

    private static int Main()
    {
        try
        {
            string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            string iconPath = Path.Combine(root, "assets", "icon", "framescope-icon.ico");
            string appExe = Path.Combine(root, "FrameScopeMonitor.exe");
            string setupExe = Path.Combine(root, "dist", "FrameScopeMonitor-Setup.exe");
            string fullSetupExe = Path.Combine(root, "dist", "FrameScopeMonitor-Full-Setup.exe");

            IconFileExists(iconPath);
            IconContainsRequiredSizes(iconPath);
            IconCanBeLoadedForWindowAndTray(iconPath);
            AppIconHelperLoadsWindowAndTrayIcons(root);
            ExecutableContainsIconResource(appExe, "FrameScopeMonitor.exe");
            ExecutableContainsIconResource(setupExe, "FrameScopeMonitor-Setup.exe");
            ExecutableContainsIconResource(fullSetupExe, "FrameScopeMonitor-Full-Setup.exe");
            InstallerDisplayIconPathUsesMainExe();

            Console.WriteLine("FrameScopeIconTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FrameScopeIconTests: FAIL " + ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void IconFileExists(string iconPath)
    {
        AssertTrue(File.Exists(iconPath), "Missing application icon: " + iconPath);
    }

    private static void IconContainsRequiredSizes(string iconPath)
    {
        HashSet<int> sizes = ReadIcoSizes(iconPath);
        foreach (int requiredSize in RequiredSizes)
        {
            AssertTrue(sizes.Contains(requiredSize), "Icon is missing " + requiredSize + "x" + requiredSize + " image.");
        }
    }

    private static void IconCanBeLoadedForWindowAndTray(string iconPath)
    {
        using (Icon windowIcon = new Icon(iconPath))
        using (Icon trayIcon = new Icon(iconPath, new Size(16, 16)))
        {
            AssertTrue(windowIcon.Width > 0 && windowIcon.Height > 0, "Window icon did not load.");
            AssertTrue(trayIcon.Width == 16 && trayIcon.Height == 16, "Tray icon did not load as 16x16.");
        }
    }

    private static void AppIconHelperLoadsWindowAndTrayIcons(string root)
    {
        using (Icon windowIcon = FrameScopeAppIcon.LoadWindowIcon(root))
        using (Icon trayIcon = FrameScopeAppIcon.LoadTrayIcon(root))
        {
            AssertTrue(windowIcon.Width > 0 && windowIcon.Height > 0, "Application window icon helper did not load an icon.");
            AssertTrue(trayIcon.Width == 16 && trayIcon.Height == 16, "Application tray icon helper did not load a 16x16 icon.");
        }
    }

    private static void ExecutableContainsIconResource(string exePath, string label)
    {
        AssertTrue(File.Exists(exePath), "Missing executable for icon resource check: " + exePath);
        AssertTrue(HasGroupIconResource(exePath), label + " does not contain a group icon PE resource.");
    }

    private static void InstallerDisplayIconPathUsesMainExe()
    {
        string setupSource = File.ReadAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "packaging", "FrameScopeSetupNative.cs"));
        AssertTrue(setupSource.Contains("key.SetValue(\"DisplayIcon\", exePath, RegistryValueKind.String);"), "Installer must register DisplayIcon against FrameScopeMonitor.exe.");
    }

    private static HashSet<int> ReadIcoSizes(string iconPath)
    {
        byte[] bytes = File.ReadAllBytes(iconPath);
        if (bytes.Length < 6) throw new InvalidOperationException("ICO file is too small.");
        ushort reserved = BitConverter.ToUInt16(bytes, 0);
        ushort type = BitConverter.ToUInt16(bytes, 2);
        ushort count = BitConverter.ToUInt16(bytes, 4);
        if (reserved != 0 || type != 1 || count == 0) throw new InvalidOperationException("ICO header is invalid.");
        if (bytes.Length < 6 + count * 16) throw new InvalidOperationException("ICO directory is incomplete.");

        HashSet<int> sizes = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            int offset = 6 + i * 16;
            int width = bytes[offset] == 0 ? 256 : bytes[offset];
            int height = bytes[offset + 1] == 0 ? 256 : bytes[offset + 1];
            AssertTrue(width == height, "Icon image is not square: " + width + "x" + height);
            sizes.Add(width);
        }

        return sizes;
    }

    private static bool HasGroupIconResource(string exePath)
    {
        IntPtr module = LoadLibraryEx(exePath, IntPtr.Zero, LoadLibraryFlags.LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero) return false;
        try
        {
            bool found = false;
            EnumResNameProc callback = delegate
            {
                found = true;
                return false;
            };
            EnumResourceNames(module, new IntPtr(14), callback, IntPtr.Zero);
            GC.KeepAlive(callback);
            return found;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private delegate bool EnumResNameProc(IntPtr hModule, IntPtr type, IntPtr name, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, LoadLibraryFlags dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool EnumResourceNames(IntPtr hModule, IntPtr lpType, EnumResNameProc lpEnumFunc, IntPtr lParam);

    [Flags]
    private enum LoadLibraryFlags : uint
    {
        LOAD_LIBRARY_AS_DATAFILE = 0x00000002
    }
}
