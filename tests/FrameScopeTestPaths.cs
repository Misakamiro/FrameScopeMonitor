using System;
using System.IO;

internal static class FrameScopeTestPaths
{
    internal static string SourceRoot
    {
        get
        {
            string root = Environment.GetEnvironmentVariable("FRAMESCOPE_TEST_REPO_ROOT");
            return string.IsNullOrWhiteSpace(root)
                ? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."))
                : Path.GetFullPath(root);
        }
    }

    internal static string RuntimeRoot
    {
        get
        {
            string root = Environment.GetEnvironmentVariable("FRAMESCOPE_TEST_RUNTIME_ROOT");
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                throw new InvalidOperationException("Set FRAMESCOPE_TEST_RUNTIME_ROOT to this build's isolated runtime before running production integration tests.");
            return Path.GetFullPath(root);
        }
    }
}
