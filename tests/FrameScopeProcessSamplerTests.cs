using System;
using System.Collections.Generic;

public static class FrameScopeProcessSamplerTests
{
    public static int Main()
    {
        CsvEscapingRemainsStable();
        CsvLineFormattingRemainsStable();
        Console.WriteLine("FrameScopeProcessSamplerTests: PASS");
        return 0;
    }

    private static void CsvEscapingRemainsStable()
    {
        AssertEqual("", FrameScopeProcessSampler.TestCsv(null), "null CSV value");
        AssertEqual("plain", FrameScopeProcessSampler.TestCsv("plain"), "plain CSV value");
        AssertEqual("\"a,b\"", FrameScopeProcessSampler.TestCsv("a,b"), "comma CSV value");
        AssertEqual("\"a\"\"b\"", FrameScopeProcessSampler.TestCsv("a\"b"), "quote CSV value");
        AssertEqual("12.5", FrameScopeProcessSampler.TestCsv(12.5), "invariant numeric CSV value");
    }

    private static void CsvLineFormattingRemainsStable()
    {
        AssertEqual(
            "2026-05-26T00:00:00Z,1,\"Game,Helper\",12.5,\"a\"\"b\"",
            FrameScopeProcessSampler.TestBuildCsvLine(new object[] { "2026-05-26T00:00:00Z", 1, "Game,Helper", 12.5, "a\"b" }),
            "CSV line formatting");
    }

    private static void AssertEqual<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception(label + ": expected <" + expected + "> but got <" + actual + ">");
        }
    }
}

internal static partial class FrameScopeProcessSampler
{
    internal static string TestCsv(object value)
    {
        return Csv(value);
    }

    internal static string TestBuildCsvLine(IEnumerable<object> values)
    {
        return BuildCsvLine(values);
    }
}
