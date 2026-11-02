using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

internal static class FrameScopeFullInstallerCore
{
    public static int Run(Func<bool> available, Func<int> installRuntime, Func<int> installApp)
    {
        if (!available())
        {
            int code = installRuntime();
            if (code == 1602 || code == 3010) return code;
            // Recheck even after a nonzero exit: another installer may already
            // have installed the shared runtime while this one was starting.
            if (!available())
            {
                if (code != 0) return code;
                throw new InvalidOperationException("WebView2 安装结束后仍未检测到运行时，未继续安装 FrameScope。");
            }
        }
        return installApp();
    }

    public static bool IsValidRuntimeVersion(string raw)
    {
        Version version;
        return Version.TryParse(raw, out version) && version > new Version(0, 0, 0, 0);
    }

    public static bool HasRuntime()
    {
        const string path = @"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        {
            try
            {
                using (var root = RegistryKey.OpenBaseKey(hive, view))
                using (var key = root.OpenSubKey(path))
                    if (key != null && IsValidRuntimeVersion(key.GetValue("pv") as string)) return true;
            }
            catch (System.Security.SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return false;
    }

    public static string QuoteArgument(string value)
    {
        var text = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in value)
        {
            if (c == '\\') { slashes++; continue; }
            text.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
            text.Append(c);
            slashes = 0;
        }
        text.Append('\\', slashes * 2);
        return text.Append('"').ToString();
    }

    public static void ExtractVerified(Stream source, string expected, string path)
    {
        bool created = false;
        try
        {
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                source.CopyTo(output, 65536);
                output.Flush(true);
            }
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
                if (!String.Equals(BitConverter.ToString(sha.ComputeHash(input)).Replace("-", ""), expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("完整安装包内置文件校验失败，未执行该文件。");
        }
        catch
        {
            if (created) File.Delete(path);
            throw;
        }
    }
}
