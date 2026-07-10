using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class FrameScopeJsonFileTests
{
    public static int Main()
    {
        try
        {
            FirstWriteCreatesUtf8JsonWithoutBom();
            ExistingJsonIsReplacedAtomically();
            ReplaceFailurePreservesOldJsonAndCleansTemporaryFile();
            Console.WriteLine("FrameScopeJsonFileTests: PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.GetType().FullName + ": " + ex.Message);
            return 1;
        }
    }

    private static void FirstWriteCreatesUtf8JsonWithoutBom()
    {
        WithTemporaryDirectory(delegate(string dir)
        {
            string path = Path.Combine(dir, "state.json");
            const string json = "{\"message\":\"FrameScope 测试\"}";

            FrameScopeJsonFile.Write(path, json);

            byte[] bytes = File.ReadAllBytes(path);
            AssertTrue(bytes.Length >= 3, "created JSON has content");
            AssertFalse(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "JSON must not contain a UTF-8 BOM");
            AssertEqual(json, File.ReadAllText(path, new UTF8Encoding(false)), "created JSON content");
            AssertEqual(1, Directory.GetFiles(dir).Length, "first write leaves only the destination");
        });
    }

    private static void ExistingJsonIsReplacedAtomically()
    {
        WithTemporaryDirectory(delegate(string dir)
        {
            string path = Path.Combine(dir, "state.json");
            FrameScopeJsonFile.Write(path, "{\"version\":1}");

            FrameScopeJsonFile.Write(path, "{\"version\":2}");

            AssertEqual("{\"version\":2}", File.ReadAllText(path, Encoding.UTF8), "replacement content");
            AssertEqual(1, Directory.GetFiles(dir).Length, "successful replacement leaves no temporary file");
        });
    }

    private static void ReplaceFailurePreservesOldJsonAndCleansTemporaryFile()
    {
        WithTemporaryDirectory(delegate(string dir)
        {
            string path = Path.Combine(dir, "state.json");
            const string oldJson = "{\"version\":1}";
            FrameScopeJsonFile.Write(path, oldJson);
            string injectedTempPath = "";
            bool failedAsExpected = false;

            try
            {
                FrameScopeJsonFile.Write(path, "{\"version\":2}", delegate(string temporaryPath, string destinationPath)
                {
                    injectedTempPath = temporaryPath;
                    AssertEqual(Path.GetFullPath(dir), Path.GetDirectoryName(Path.GetFullPath(temporaryPath)), "temporary file directory");
                    AssertEqual(Path.GetFullPath(path), Path.GetFullPath(destinationPath), "replace destination");
                    throw new IOException("synthetic replace failure");
                });
            }
            catch (IOException ex)
            {
                failedAsExpected = ex.Message.Contains("synthetic replace failure");
            }

            AssertTrue(failedAsExpected, "injected replacement failure is propagated");
            AssertEqual(oldJson, File.ReadAllText(path, Encoding.UTF8), "old JSON survives replacement failure");
            AssertTrue(!string.IsNullOrWhiteSpace(injectedTempPath), "replacement action received temporary path");
            AssertFalse(File.Exists(injectedTempPath), "temporary file is removed after replacement failure");
            AssertEqual(1, Directory.GetFiles(dir).Length, "failed replacement leaves only the old destination");
        });
    }

    private static void WithTemporaryDirectory(Action<string> action)
    {
        string dir = Path.Combine(Path.GetTempPath(), "framescope-json-file-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            action(dir);
        }
        finally
        {
            try { Directory.Delete(dir, true); }
            catch { }
        }
    }

    private static void AssertTrue(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
    }

    private static void AssertFalse(bool condition, string label)
    {
        AssertTrue(!condition, label);
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}
