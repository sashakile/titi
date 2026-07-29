// Tests for TID-3c: titi tests ingest correlation (CLI-21) — TRX+Cobertura
// must produce per-test×source-file edges via EdgeBuilder, not method-keyed edges.

namespace titi.Tests;

using System.Xml.Linq;

public class IngestorTests
{
    // Minimal well-formed TRX with 2 passing tests + 1 skipped (not executed).
    const string TwoPassedOneSkippedTrx = """
    <?xml version="1.0" encoding="utf-8"?>
    <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
      <Results>
        <UnitTestResult testName="Orion.Tests.A.M1" outcome="Passed" duration="00:00:00.0010000"/>
        <UnitTestResult testName="Orion.Tests.A.M2" outcome="Passed" duration="00:00:00.0020000"/>
        <UnitTestResult testName="Orion.Tests.A.Skipped" outcome="NotExecuted" duration="00:00:00.0000000"/>
      </Results>
    </TestRun>
    """;

    // Cobertura listing 3 covered source files (one method each, so ParseCobertura
    // yields one edge per file → 3 distinct covered source paths).
    const string ThreeSourcesCobertura = """
    <?xml version="1.0" encoding="UTF-8"?>
    <coverage line-rate="0.5" version="0.1">
      <sources><source>/repo/src</source></sources>
      <packages>
        <package name="P">
          <classes>
            <class name="C1" filename="A.cs" line-rate="0.5"><methods><method name="M" line-rate="0.5"><lines><line number="1" hits="1"/></lines></method></methods><lines><line number="1" hits="1"/></lines></class>
            <class name="C2" filename="B.cs" line-rate="0.5"><methods><method name="M" line-rate="0.5"><lines><line number="2" hits="1"/></lines></method></methods><lines><line number="2" hits="1"/></lines></class>
            <class name="C3" filename="C.cs" line-rate="0.5"><methods><method name="M" line-rate="0.5"><lines><line number="3" hits="1"/></lines></method></methods><lines><line number="3" hits="1"/></lines></class>
          </classes>
        </package>
      </packages>
    </coverage>
    """;

    [Fact]
    public void IngestRun_TrxAndCobertura_BuildsTestSourceCrossProduct()
    {
        // 2 executed tests (skipped excluded) × 3 covered source files = 6 edges.
        var result = titi.Ingestor.IngestRun(TwoPassedOneSkippedTrx, ThreeSourcesCobertura, "/repo/src");

        Assert.False(result.IsMalformed);
        Assert.Equal(6, result.Edges.Length);
    }

    [Fact]
    public void IngestRun_EdgesAreFromTestNameToSourceFile_NotMethodKeyed()
    {
        var result = titi.Ingestor.IngestRun(TwoPassedOneSkippedTrx, ThreeSourcesCobertura, "/repo/src");

        // The bug being fixed: edges must be keyed by testName, not by covered
        // method name (e.g. "M", "Parse"). Every From must be a TRX testName.
        var fromNames = result.Edges.Select(e => e.From).ToHashSet();
        Assert.Contains("Orion.Tests.A.M1", fromNames);
        Assert.Contains("Orion.Tests.A.M2", fromNames);
        Assert.DoesNotContain("M", fromNames);  // not the Cobertura method name
        // The skipped test gets no edges.
        Assert.DoesNotContain(result.Edges, e => e.From == "Orion.Tests.A.Skipped");
    }

    [Fact]
    public void IngestRun_TrxOnly_NoEdgesBuilt()
    {
        var result = titi.Ingestor.IngestRun(TwoPassedOneSkippedTrx, coberturaXml: null, "/repo/src");

        Assert.False(result.IsMalformed);
        Assert.Equal(3, result.Results.Length);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void IngestRun_MalformedTrx_IsMalformedAndEmpty()
    {
        var result = titi.Ingestor.IngestRun("not xml at all", ThreeSourcesCobertura, "/repo/src");

        Assert.True(result.IsMalformed);
        Assert.Empty(result.Results);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void IngestRun_NonTestRunRoot_IsMalformed()
    {
        var notTrx = """<?xml version="1.0"?><OtherRoot xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"></OtherRoot>""";
        var result = titi.Ingestor.IngestRun(notTrx, ThreeSourcesCobertura, "/repo/src");

        Assert.True(result.IsMalformed);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void IngestRun_EmptyTrx_NotMalformedNoResults()
    {
        var emptyTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results/></TestRun>
        """;
        var result = titi.Ingestor.IngestRun(emptyTrx, ThreeSourcesCobertura, "/repo/src");

        Assert.False(result.IsMalformed);
        Assert.Empty(result.Results);
        // 0 executed tests × 3 sources = 0 edges.
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void IngestRun_EdgesWrittenToCacheDirPath_NotFlatTestCacheRoot()
    {
        // The edges path must be <cacheDir>/edges/edges.json (TD-03), not the
        // flat <cacheDir>/edges.json the old ingest wrote. Verified at the command
        // level via the path the Ingestor suggests; here we just confirm the
        // canonical relative path constant.
        Assert.Equal(Path.Combine("test-cache", "edges", "edges.json"), titi.Ingestor.EdgesRelativePath);
    }
}
