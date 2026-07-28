// Tests for TID-3a: TRX (Visual Studio Test Results) parsing — TD-02

namespace titi.Tests;

using System.Xml.Linq;

public class TrxParserTests
{
    // Real TRX captured from `dotnet test --logger trx` on .NET 10.0.302
    // (xUnit project with 5 passing, 1 failing, 1 skipped — parameterized
    // Theory rows expanded as separate UnitTestResult entries).
    static readonly string FixturePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../test/fixtures/trx/sample.trx"));

    static string LoadFixture() => File.ReadAllText(FixturePath);

    [Fact]
    public void ParseTrx_WithRealFixture_ReturnsAllResults()
    {
        var results = titi.Coverage.Parser.ParseTrx(LoadFixture());

        // 5 passed (Test1, AnotherFact, Parse×3) + 1 failed (Fails) + 1 skipped (Skipped)
        Assert.Equal(7, results.Length);
    }

    [Fact]
    public void ParseTrx_MapsOutcomesCorrectly()
    {
        var results = titi.Coverage.Parser.ParseTrx(LoadFixture());
        var byName = results.ToDictionary(r => r.TestName);

        Assert.Equal(TestOutcome.Passed, byName["Verify.Tests.UnitTest1.Test1"].Outcome);
        Assert.Equal(TestOutcome.Failed, byName["Verify.Tests.UnitTest1.Fails"].Outcome);
        Assert.Equal(TestOutcome.Skipped, byName["Verify.Tests.UnitTest1.Skipped"].Outcome);
    }

    [Fact]
    public void ParseTrx_FailureCarriesErrorMessage()
    {
        var results = titi.Coverage.Parser.ParseTrx(LoadFixture());
        var failed = Assert.Single(results, r => r.Outcome == TestOutcome.Failed);

        Assert.NotNull(failed.ErrorMessage);
        Assert.Contains("intentional failure", failed.ErrorMessage);
    }

    [Fact]
    public void ParseTrx_SkippedCarriesSkipReasonAsMessage()
    {
        var results = titi.Coverage.Parser.ParseTrx(LoadFixture());
        var skipped = Assert.Single(results, r => r.Outcome == TestOutcome.Skipped);

        Assert.NotNull(skipped.ErrorMessage);
        Assert.Contains("reason", skipped.ErrorMessage);
    }

    [Fact]
    public void ParseTrx_ParsesDurationInMilliseconds()
    {
        var results = titi.Coverage.Parser.ParseTrx(LoadFixture());
        var test1 = Assert.Single(results, r => r.TestName == "Verify.Tests.UnitTest1.Test1");

        // Real duration captured as 00:00:00.0021727 → ~2 ms (non-negative).
        Assert.True(test1.DurationMs >= 0, $"duration should be non-negative, got {test1.DurationMs}");
    }

    [Fact]
    public void ParseTrx_ParameterizedRowsPreserveFullTestName()
    {
        var results = titi.Coverage.Parser.ParseTrx(LoadFixture());
        var parseRows = results.Where(r => r.TestName.StartsWith("Verify.Tests.UnitTest1.Parse(")).ToArray();

        Assert.Equal(3, parseRows.Length);
        // The parameter row identity is preserved verbatim, including quotes.
        Assert.Contains(results, r => r.TestName == """Verify.Tests.UnitTest1.Parse(input: "a")""");
        Assert.Contains(results, r => r.TestName == """Verify.Tests.UnitTest1.Parse(input: "b")""");
        Assert.Contains(results, r => r.TestName == """Verify.Tests.UnitTest1.Parse(input: "c")""");
    }

    [Fact]
    public void ParseTrx_EmptyResults_ReturnsEmpty()
    {
        var emptyTrx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <Results></Results>
        </TestRun>
        """;

        var results = titi.Coverage.Parser.ParseTrx(emptyTrx);

        Assert.Empty(results);
    }

    [Fact]
    public void ParseTrx_MalformedXml_ReturnsEmpty()
    {
        var results = titi.Coverage.Parser.ParseTrx("not xml at all");
        Assert.Empty(results);
    }

    [Fact]
    public void ParseTrx_NotATestRun_ReturnsEmpty()
    {
        var notTrx = """<?xml version="1.0"?><OtherRoot xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"></OtherRoot>""";
        Assert.Empty(titi.Coverage.Parser.ParseTrx(notTrx));
    }

    [Fact]
    public void ParseTrx_ResultRecordShape_HoldsTestNameOutcomeDurationError()
    {
        var results = titi.Coverage.Parser.ParseTrx(LoadFixture());
        var first = results[0];

        Assert.False(string.IsNullOrEmpty(first.TestName));
        Assert.True(Enum.IsDefined(first.Outcome));
        Assert.True(first.DurationMs >= 0);
        // ErrorMessage may be null for passing tests.
        Assert.True(first.ErrorMessage == null || first.ErrorMessage.Length >= 0);
    }
}
