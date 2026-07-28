// Tests for TID-4: Safety invariants and selection logic

namespace titi.Tests;

using titi.Safety;

public class SafetyTests
{
    // ── Always-run set ───────────────────────────────────────────

    [Fact]
    public void ComputeAlwaysRunSet_LastRunFailed_Included()
    {
        var items = new[]
        {
            new TestItem("id1", "/asm.dll", "C", "M1", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Failed, 0, [])
        };

        var alwaysRun = titi.Safety.Selection.ComputeAlwaysRunSet(items, []);

        Assert.Contains("id1", alwaysRun);
    }

    [Fact]
    public void ComputeAlwaysRunSet_NoHistory_Included()
    {
        var items = new[]
        {
            new TestItem("new-test", "/asm.dll", "C", "New", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.None, 0, [])
        };

        var alwaysRun = titi.Safety.Selection.ComputeAlwaysRunSet(items, []);

        Assert.Contains("new-test", alwaysRun);
    }

    [Fact]
    public void ComputeAlwaysRunSet_RecentlyPassed_Excluded()
    {
        var items = new[]
        {
            new TestItem("stable-test", "/asm.dll", "C", "Stable", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, [])
        };

        var history = new Dictionary<string, TestRunEntry>
        {
            ["stable-test"] = new TestRunEntry("stable-test", TestOutcome.Passed, 50, DateTime.UtcNow)
        };

        var alwaysRun = titi.Safety.Selection.ComputeAlwaysRunSet(items, history);

        Assert.DoesNotContain("stable-test", alwaysRun);
    }

    [Fact]
    public void ComputeAlwaysRunSet_NewlyAdded_RelativeToRecording()
    {
        var items = new[]
        {
            new TestItem("existing", "/asm.dll", "C", "Existing", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, []),
            new TestItem("newly-added", "/asm.dll", "C", "New", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, [])
        };

        // Only "existing" has run history
        var history = new Dictionary<string, TestRunEntry>
        {
            ["existing"] = new TestRunEntry("existing", TestOutcome.Passed, 50, DateTime.UtcNow)
        };

        var alwaysRun = titi.Safety.Selection.ComputeAlwaysRunSet(items, history);

        // "newly-added" has no history → always-run
        Assert.Contains("newly-added", alwaysRun);
        // "existing" has passing history → not always-run
        Assert.DoesNotContain("existing", alwaysRun);
    }

    // ── Confidence scoring ───────────────────────────────────────

    [Fact]
    public void ComputeConfidence_AllFilesResolved_ReturnsOne()
    {
        var confidence = titi.Safety.Selection.ComputeConfidence(
            changedFiles: ["src/Foo.cs", "src/Bar.cs"],
            resolvedFiles: ["src/Foo.cs", "src/Bar.cs"],
            edgeFreshness: 1.0,
            historyDepth: 10
        );

        Assert.Equal(1.0, confidence);
    }

    [Fact]
    public void ComputeConfidence_HalfResolved_ReturnsWeightedValue()
    {
        var confidence = titi.Safety.Selection.ComputeConfidence(
            changedFiles: ["src/Foo.cs", "src/Bar.cs"],
            resolvedFiles: ["src/Foo.cs"],
            edgeFreshness: 0.0,
            historyDepth: 0
        );

        // 0.5 * 0.6 + 0.0 * 0.25 + 0.0 * 0.15 = 0.3
        Assert.Equal(0.3, confidence, precision: 2);
    }

    [Fact]
    public void ComputeConfidence_BelowThreshold_FallsBack()
    {
        var confidence = titi.Safety.Selection.ComputeConfidence(
            changedFiles: ["src/Foo.cs", "src/Bar.cs", "src/Baz.cs", "src/Qux.cs"],
            resolvedFiles: ["src/Foo.cs"],
            edgeFreshness: 0.3,
            historyDepth: 1
        );

        Assert.True(confidence < 0.7);
    }

    // ── Selected tests ───────────────────────────────────────────

    [Fact]
    public void ComputeSelectedTests_IncludesAlwaysRunAndEdgeMatches()
    {
        var testItems = new[]
        {
            new TestItem("test-A", "/asm.dll", "C", "A", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, []),
            new TestItem("test-B", "/asm.dll", "C", "B", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Failed, 0, []),
        };

        var edges = new[]
        {
            new TestToSourceEdge("test-A", "src/Foo.cs", EdgeOrigin.Static, 1_000_000, []),
        };

        var alwaysRun = new HashSet<string> { "test-B" };
        var changedFiles = new[] { "src/Foo.cs" };

        var results = titi.Safety.Selection.ComputeSelectedTests(testItems, edges, alwaysRun, changedFiles);

        Assert.Contains(results, r => r.TestId == "test-A" && r.Selected);  // matched edge
        Assert.Contains(results, r => r.TestId == "test-B" && r.Selected);  // always-run
    }

    [Fact]
    public void ComputeSelectedTests_UnmatchedTest_NotSelected()
    {
        var testItems = new[]
        {
            new TestItem("test-C", "/asm.dll", "C", "C", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, []),
        };

        var edges = new[]
        {
            new TestToSourceEdge("test-A", "src/Foo.cs", EdgeOrigin.Static, 1_000_000, []),
        };

        var results = titi.Safety.Selection.ComputeSelectedTests(testItems, edges, [], ["src/Bar.cs"]);

        Assert.Contains(results, r => r.TestId == "test-C" && !r.Selected);
    }

    // ── Missed-selection incident ────────────────────────────────

    [Fact]
    public void RecordMissedSelection_CreatesCandidateIncident()
    {
        var incidents = new List<MissedSelectionIncident>();
        var incident = titi.Safety.Selection.RecordMissedSelection(
            "src/ChangedFile.cs", "missed-test-42", ref incidents);

        Assert.Equal(IncidentStatus.Candidate, incident.Status);
        Assert.Contains(incidents, i => i.MissedTestId == "missed-test-42");
    }
}
