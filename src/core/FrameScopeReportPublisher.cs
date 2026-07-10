using System;
using System.IO;
using System.Linq;

internal static class FrameScopeReportPublisher
{
    internal static void Publish(string runDirectory, Action<string> generate)
    {
        PublishForTests(runDirectory, generate, Directory.Move);
    }

    internal static void PublishForTests(string runDirectory, Action<string> generate, Action<string, string> moveDirectory)
    {
        if (generate == null) throw new ArgumentNullException("generate");
        if (moveDirectory == null) throw new ArgumentNullException("moveDirectory");

        string run = Path.GetFullPath(runDirectory ?? "");
        Directory.CreateDirectory(run);
        RecoverInterruptedPublication(run);

        string charts = Path.Combine(run, "charts");
        string staging = Path.Combine(run, ".framescope-report-" + Guid.NewGuid().ToString("N"));
        string backup = Path.Combine(run, ".framescope-report-backup-" + Guid.NewGuid().ToString("N"));
        bool published = false;
        Directory.CreateDirectory(staging);
        try
        {
            generate(staging);
            FrameScopeReportArtifactState generated = FrameScopeReportArtifacts.InspectGeneratedDirectory(run, staging);
            if (!generated.IsComplete) throw new InvalidOperationException(generated.Error);

            if (Directory.Exists(charts)) moveDirectory(charts, backup);
            moveDirectory(staging, charts);
            published = true;
        }
        catch
        {
            if (!Directory.Exists(charts) && Directory.Exists(backup))
            {
                moveDirectory(backup, charts);
            }
            throw;
        }
        finally
        {
            DeleteDirectoryIfPresent(staging);
            if (published) DeleteDirectoryIfPresent(backup);
        }
    }

    internal static void RecoverInterruptedPublication(string runDirectory)
    {
        string run = Path.GetFullPath(runDirectory ?? "");
        if (!Directory.Exists(run)) return;
        string charts = Path.Combine(run, "charts");
        string[] backups = Directory.GetDirectories(run, ".framescope-report-backup-*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .ToArray();
        if (backups.Length == 0) return;

        bool chartsComplete = FrameScopeReportArtifacts.Inspect(run).IsComplete;
        string recoverableBackup = backups.FirstOrDefault(path => FrameScopeReportArtifacts.InspectGeneratedDirectory(run, path).IsComplete);
        if (!chartsComplete && !string.IsNullOrWhiteSpace(recoverableBackup))
        {
            DeleteDirectoryIfPresent(charts);
            Directory.Move(recoverableBackup, charts);
        }

        foreach (string backup in backups)
        {
            if (Directory.Exists(backup)) DeleteDirectoryIfPresent(backup);
        }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Directory.Delete(path, true);
    }
}
