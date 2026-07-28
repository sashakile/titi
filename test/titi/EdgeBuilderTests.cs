// Tests for TID-3b: build test→source edges from TRX + Cobertura (TD-03)

namespace titi.Tests;

public class EdgeBuilderTests
{
    // Minimal TRX results — 5 executed tests (3 pass, 1 fail, 1 skip).
    static readonly titi.Coverage.TrxTestResult[] SampleResults =
    [
        new("Orion.Tests.Foo.A", TestOutcome.Passed, 2, null),
        new("Orion.Tests.Foo.B", TestOutcome.Passed, 3, null),
        new("Orion.Tests.Foo.C", TestOutcome.Passed, 1, null),
        new("Orion.Tests.Foo.D", TestOutcome.Failed, 5, "boom"),
        new("Orion.Tests.Foo.E", TestOutcome.Skipped, 0, "reason"),
    ];

    static readonly string[] CoveredSources =
    [
        "/repo/src/Orion.Core/Parser.cs",
        "/repo/src/Orion.Core/Graph.cs",
        "/repo/src/Orion.Core/Swap.cs",
    ];

    [Fact]
    public void BuildFromRun_FourExecutedTimesThreeSources_Returns12Edges()
    {
        // 4 executed tests (Passed+Failed) × 3 covered source files = 12 edges.
        // The skipped test (NotExecuted) did not run, so it gets no edges.
        var edges = titi.EdgeBuilder.BuildFromRun(SampleResults, CoveredSources);

        Assert.Equal(12, edges.Length);
    }

    [Fact]
    public void BuildFromRun_EdgesHaveStaticOriginAndFullWeight()
    {
        var edges = titi.EdgeBuilder.BuildFromRun(SampleResults, CoveredSources);

        Assert.All(edges, e =>
        {
            Assert.Equal(EdgeOrigin.Static, e.Origin);
            Assert.Equal(1_000_000, e.Weight);
        });
    }

    [Fact]
    public void BuildFromRun_FromIsTestNameToIsSourceFile()
    {
        var edges = titi.EdgeBuilder.BuildFromRun(SampleResults, CoveredSources);

        Assert.All(edges, e =>
        {
            Assert.Contains(SampleResults.Select(r => r.TestName), n => n == e.From);
            Assert.Contains(CoveredSources, s => s == e.To);
        });
        // The skipped test never appears as a `From`.
        Assert.DoesNotContain(edges, e => e.From == "Orion.Tests.Foo.E");
    }

    [Fact]
    public void BuildFromRun_IsFileLevel_LineRangesEmpty()
    {
        var edges = titi.EdgeBuilder.BuildFromRun(SampleResults, CoveredSources);

        // File-level granularity (TD-03 known limitation): no line ranges.
        Assert.All(edges, e => Assert.Empty(e.LineRanges));
    }

    [Fact]
    public void BuildFromRun_NoTests_ReturnsEmpty()
    {
        Assert.Empty(titi.EdgeBuilder.BuildFromRun([], CoveredSources));
    }

    [Fact]
    public void BuildFromRun_NoSources_ReturnsEmpty()
    {
        Assert.Empty(titi.EdgeBuilder.BuildFromRun(SampleResults, []));
    }

    [Fact]
    public void BuildFromRun_SkippedOnlyTestsGetNoEdges()
    {
        var skippedOnly = new[]
        {
            new titi.Coverage.TrxTestResult("Ns.Cls.Skipped", TestOutcome.Skipped, 0, "skip"),
        };

        Assert.Empty(titi.EdgeBuilder.BuildFromRun(skippedOnly, CoveredSources));
    }

    [Fact]
    public void BuildFromRun_DeduplicatesRepeatedSourceFiles()
    {
        var sourcesWithDupes = new[]
        {
            "/repo/a.cs",
            "/repo/a.cs",
            "/repo/b.cs",
        };
        var tests = new[]
        {
            new titi.Coverage.TrxTestResult("Ns.T", TestOutcome.Passed, 1, null),
        };

        var edges = titi.EdgeBuilder.BuildFromRun(tests, sourcesWithDupes);

        // 1 executed test × 2 distinct source files = 2 edges (no dupe pairs).
        Assert.Equal(2, edges.Length);
        var pairs = edges.Select(e => (e.From, e.To)).ToHashSet();
        Assert.Equal(2, pairs.Count);
    }

    [Fact]
    public void BuildFromRun_ParameterizedTestNamesPreserved()
    {
        var tests = new[]
        {
            new titi.Coverage.TrxTestResult("""Ns.Cls.Parse(input: "a")""", TestOutcome.Passed, 1, null),
            new titi.Coverage.TrxTestResult("""Ns.Cls.Parse(input: "b")""", TestOutcome.Passed, 1, null),
        };

        var edges = titi.EdgeBuilder.BuildFromRun(tests, new[] { "/repo/f.cs" });

        Assert.Equal(2, edges.Length);
        Assert.Contains(edges, e => e.From == """Ns.Cls.Parse(input: "a")""");
        Assert.Contains(edges, e => e.From == """Ns.Cls.Parse(input: "b")""");
    }
}
