using System;
using System.IO;
using System.Text;

public static class FrameScopeJsonFile
{
    private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

    public static void Write(string path, string json)
    {
        Write(path, json, ReplaceFile);
    }

    internal static void Write(string path, string json, Action<string, string> replaceAction)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("JSON path is empty.", "path");
        if (replaceAction == null) throw new ArgumentNullException("replaceAction");

        string destinationPath = Path.GetFullPath(path);
        string directoryPath = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(directoryPath)) throw new InvalidOperationException("JSON path has no parent directory.");
        Directory.CreateDirectory(directoryPath);

        string temporaryPath = Path.Combine(
            directoryPath,
            ".fsj-" + Guid.NewGuid().ToString("N").Substring(0, 16));

        try
        {
            byte[] bytes = Utf8WithoutBom.GetBytes(json ?? "");
            using (FileStream stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }

            if (File.Exists(destinationPath))
            {
                replaceAction(temporaryPath, destinationPath);
            }
            else
            {
                try
                {
                    File.Move(temporaryPath, destinationPath);
                }
                catch (IOException)
                {
                    if (!File.Exists(destinationPath)) throw;
                    replaceAction(temporaryPath, destinationPath);
                }
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); }
                catch { }
            }
        }
    }

    private static void ReplaceFile(string temporaryPath, string destinationPath)
    {
        File.Replace(temporaryPath, destinationPath, null, true);
    }
}
