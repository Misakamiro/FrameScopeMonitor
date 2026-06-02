using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class FrameScopeAppIcon
{
    internal const string RelativeIconPath = @"assets\icon\framescope-icon.ico";

    public static Icon LoadWindowIcon(string root)
    {
        return LoadIcon(root, Size.Empty);
    }

    public static Icon LoadTrayIcon(string root)
    {
        return LoadIcon(root, new Size(16, 16));
    }

    private static Icon LoadIcon(string root, Size requestedSize)
    {
        foreach (string candidate in GetIconCandidates(root))
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                return requestedSize.IsEmpty ? new Icon(candidate) : new Icon(candidate, requestedSize);
            }
            catch
            {
            }
        }

        try
        {
            string executablePath = Application.ExecutablePath;
            if (File.Exists(executablePath))
            {
                using (Icon associated = Icon.ExtractAssociatedIcon(executablePath))
                {
                    if (associated != null)
                    {
                        return requestedSize.IsEmpty ? (Icon)associated.Clone() : new Icon(associated, requestedSize);
                    }
                }
            }
        }
        catch
        {
        }

        return requestedSize.IsEmpty ? (Icon)SystemIcons.Application.Clone() : new Icon(SystemIcons.Application, requestedSize);
    }

    private static IEnumerable<string> GetIconCandidates(string root)
    {
        if (!string.IsNullOrWhiteSpace(root))
        {
            yield return Path.Combine(root, RelativeIconPath);
        }

        string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(baseDirectory))
        {
            yield return Path.Combine(baseDirectory, RelativeIconPath);
        }

        string executableDirectory = "";
        try
        {
            executableDirectory = Path.GetDirectoryName(Application.ExecutablePath);
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(executableDirectory))
        {
            yield return Path.Combine(executableDirectory, RelativeIconPath);
        }
    }
}
