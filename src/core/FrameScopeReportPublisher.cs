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
                try { moveDirectory(backup, charts); }
                catch (Exception restore) { LogRecoveryFailure(run, restore); throw; }
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
        RecoverInterruptedPublicationForTests(runDirectory, Directory.Move);
    }

    internal static void RecoverInterruptedPublicationForTests(string runDirectory, Action<string, string> moveDirectory)
    {
        string run = Path.GetFullPath(runDirectory ?? "");
        if (!Directory.Exists(run)) return;
        string charts = Path.Combine(run, "charts");
        string[] backups = Directory.GetDirectories(run, ".framescope-report-backup-*", SearchOption.TopDirectoryOnly)
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .ToArray();
        if (backups.Length == 0) return;

        if (StructurallyValid(FrameScopeReportArtifacts.Inspect(run))) return;
        string recoverableBackup = backups.FirstOrDefault(path => StructurallyValid(FrameScopeReportArtifacts.InspectGeneratedDirectory(run, path)));
        if (string.IsNullOrWhiteSpace(recoverableBackup)) return;
        string displaced = Path.Combine(run, ".framescope-report-incomplete-" + Guid.NewGuid().ToString("N"));
        try
        {
            if (Directory.Exists(charts)) moveDirectory(charts, displaced);
            moveDirectory(recoverableBackup, charts);
        }
        catch (Exception ex)
        {
            LogRecoveryFailure(run, ex);
            if (!Directory.Exists(charts) && Directory.Exists(displaced))
            {
                try { moveDirectory(displaced, charts); }
                catch (Exception restore) { LogRecoveryFailure(run, restore); }
            }
            throw;
        }
    }

    private static bool StructurallyValid(FrameScopeReportArtifactState state)
    {
        return state.HtmlExists && state.DataExists && state.ManifestValid && state.PathsMatchRun;
    }

    private static void LogRecoveryFailure(string run, Exception error)
    {
        try { File.AppendAllText(Path.Combine(run, "report-publication-recovery.log"), DateTime.UtcNow.ToString("o") + " " + error + Environment.NewLine); } catch { }
    }

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return;
        Directory.Delete(path, true);
    }
}
