using System;
using System.Collections.Generic;

public static class FrameScopeProcessSamplerTests
{
    public static int Main()
    {
        CsvEscapingRemainsStable();
        CsvLineFormattingRemainsStable();
        AliasMatchingUsesTheCompleteBaseNameSet();
        ParentMonitorOwnsSamplerLifetime();
        SamplerCliResolvesAllAliases();
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

    private static void AliasMatchingUsesTheCompleteBaseNameSet()
    {
        List<string> aliases = FrameScopeTargetLifecycle.ParseBaseNameAliases(" AliasB ; AliasA ; aliasb ", "fallback");
        AssertEqual(2, aliases.Count, "normalized process sampler alias count");
        AssertEqual("aliasa", aliases[0], "normalized process sampler first alias");
        AssertEqual("aliasb", aliases[1], "normalized process sampler second alias");
        AssertEqual(true, FrameScopeTargetLifecycle.MatchesAnyAlias("ALIASB", aliases), "secondary alias matches");
        AssertEqual(false, FrameScopeTargetLifecycle.MatchesAnyAlias("other", aliases), "unrelated process does not match");
    }

    private static void ParentMonitorOwnsSamplerLifetime()
    {
        AssertEqual(false, FrameScopeTargetLifecycle.ShouldStopSampler(true, true, false), "live parent keeps sampler alive through a target gap");
        AssertEqual(true, FrameScopeTargetLifecycle.ShouldStopSampler(true, false, true), "dead parent stops parent-owned sampler");
        AssertEqual(true, FrameScopeTargetLifecycle.ShouldStopSampler(false, true, false), "standalone sampler stops when all aliases are absent");
        AssertEqual(false, FrameScopeTargetLifecycle.ShouldStopSampler(false, true, true), "standalone sampler continues while any alias is present");
    }

    private static void SamplerCliResolvesAllAliases()
    {
        List<string> aliases = FrameScopeProcessSampler.TestResolveTargetAliases(new[]
        {
            "--target", "AliasA.exe",
            "--target-aliases", "AliasB;AliasA"
        });
        AssertEqual(2, aliases.Count, "process sampler CLI alias count");
        AssertEqual("aliasa", aliases[0], "process sampler CLI first alias");
        AssertEqual("aliasb", aliases[1], "process sampler CLI second alias");
        AssertEqual(false, FrameScopeProcessSampler.TestShouldStopSampling(true, true, false, false), "process sampler live parent owns a target gap");
        AssertEqual(true, FrameScopeProcessSampler.TestShouldStopSampling(true, true, true, true), "process sampler explicit stop request wins");
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

    internal static List<string> TestResolveTargetAliases(string[] args)
    {
        return ResolveTargetAliases(args);
    }

    internal static bool TestShouldStopSampling(bool parentOwned, bool parentRunning, bool anyAliasRunning, bool stopRequested)
    {
        return ShouldStopSampling(parentOwned, parentRunning, anyAliasRunning, stopRequested);
    }
}
