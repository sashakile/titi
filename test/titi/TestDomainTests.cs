// Tests for TID-1: Test-item domain types

namespace titi.Tests;

public class TestDomainTests
{
    // ── TestItem ──────────────────────────────────────────────────

    [Fact]
    public void TestItem_Created_HasExpectedFields()
    {
        var item = new TestItem(
            TestId: "Orion.Core.Data.Tests.UnitTest1.TestMethod1",
            AssemblyPath: "/repo/tests/Orion.Core.Data.Tests/bin/Debug/net10.0/Orion.Core.Data.Tests.dll",
            ClassName: "Orion.Core.Data.Tests.UnitTest1",
            MethodName: "TestMethod1",
            Framework: TestFramework.Xunit,
            Tier: TestTier.Unit,
            SourceFile: "src/Orion.Core.Data.Tests/UnitTest1.cs",
            LastOutcome: TestOutcome.None,
            MeanDurationMs: 0,
            Tags: []
        );

        Assert.Equal("Orion.Core.Data.Tests.UnitTest1.TestMethod1", item.TestId);
        Assert.Equal(TestFramework.Xunit, item.Framework);
        Assert.Equal(TestTier.Unit, item.Tier);
    }

    [Fact]
    public void TestItem_WithParameterized_RowsHaveUniqueTestIds()
    {
        var row1 = new TestItem(
            "TestClass.Test(data=1)", "/asm.dll", "TestClass", "Test",
            TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []
        );
        var row2 = new TestItem(
            "TestClass.Test(data=2)", "/asm.dll", "TestClass", "Test",
            TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []
        );

        Assert.NotEqual(row1.TestId, row2.TestId);
    }

    // ── TestToSourceEdge ──────────────────────────────────────────

    [Fact]
    public void TestToSourceEdge_Created_HasExpectedFields()
    {
        var edge = new TestToSourceEdge(
            From: "TestClass.TestMethod1",
            To: "src/Orion.Core.Data/Models/Foo.cs",
            Origin: EdgeOrigin.Static,
            Weight: 1_000_000,
            LineRanges: [(10, 25)]
        );

        Assert.Equal("TestClass.TestMethod1", edge.From);
        Assert.Equal(EdgeOrigin.Static, edge.Origin);
        Assert.Equal(1_000_000, edge.Weight);
        Assert.Single(edge.LineRanges);
    }

    // ── Enums ─────────────────────────────────────────────────────

    [Fact]
    public void AlwaysRunReason_Values_AreCorrect()
    {
        Assert.Equal(0, (int)AlwaysRunReason.LastRunFailed);
        Assert.Equal(1, (int)AlwaysRunReason.NewlyAdded);
        Assert.Equal(2, (int)AlwaysRunReason.NoHistory);
        Assert.Equal(3, (int)AlwaysRunReason.MustRun);
        Assert.Equal(4, (int)AlwaysRunReason.Quarantined);
    }

    [Fact]
    public void FallbackReason_Values_AreCorrect()
    {
        Assert.Equal(0, (int)FallbackReason.ConfidenceBelowThreshold);
        Assert.Equal(1, (int)FallbackReason.UnresolvedFile);
        Assert.Equal(2, (int)FallbackReason.AdapterFailure);
        Assert.Equal(3, (int)FallbackReason.EnvironmentChange);
    }

    // ── MissedSelectionIncident ───────────────────────────────────

    [Fact]
    public void MissedSelectionIncident_Created_HasExpectedFields()
    {
        var incident = new MissedSelectionIncident(
            ChangedContent: "src/Orion.Core.Data/Models/Foo.cs",
            MissedTestId: "Orion.Tests.TestClass.TestMethod1",
            Timestamp: DateTime.UtcNow,
            Status: IncidentStatus.Candidate
        );

        Assert.Equal(IncidentStatus.Candidate, incident.Status);
        Assert.Equal("src/Orion.Core.Data/Models/Foo.cs", incident.ChangedContent);
    }

    // ── TestSelectionResult ───────────────────────────────────────

    [Fact]
    public void TestSelectionResult_Created_HasExpectedFields()
    {
        var result = new TestSelectionResult(
            TestId: "TestClass.TestMethod1",
            Selected: true,
            Reasons: [("always-run", "Last run failed")],
            Confidence: 1.0,
            FallbackReason: null
        );

        Assert.True(result.Selected);
        Assert.Single(result.Reasons);
        Assert.Equal(1.0, result.Confidence);
    }

    // ── Modified TieredTestSet ────────────────────────────────────

    [Fact]
    public void TieredTestSet_WithItems_ContainsTestItems()
    {
        var item = new TestItem(
            "TestClass.TestMethod1", "/asm.dll", "TestClass", "TestMethod1",
            TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []
        );

        var tts = new TieredTestSet(
            Unit: [],
            Package: [],
            Integration: [],
            Compatibility: []
        )
        {
            Items = new() { [TestTier.Unit] = [item] }
        };

        Assert.NotEmpty(tts.Items);
        Assert.Equal("TestMethod1", tts.Items[TestTier.Unit][0].MethodName);
    }

    // ── Modified AffectedSet ──────────────────────────────────────

    [Fact]
    public void AffectedSet_WithSelectedTests_ContainsTestSelectionResults()
    {
        var result = new TestSelectionResult(
            "TestClass.TestMethod1", true, [("direct-hit", "File changed")], 1.0, null
        );

        var affected = new AffectedSet(
            ChangedFiles: ["src/Foo.cs"],
            DirectlyAffected: [],
            TransitivelyAffected: [],
            AffectedTests: new TieredTestSet([], [], [], [])
        )
        {
            SelectedTests = [result]
        };

        Assert.Single(affected.SelectedTests);
        Assert.True(affected.SelectedTests[0].Selected);
    }
}
