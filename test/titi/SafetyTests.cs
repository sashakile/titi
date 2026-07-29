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

    [Fact]
    public void ComputeSelectedTests_EdgeMatch_NormalizesAbsoluteVsRelativePaths()
    {
        // git diff returns repo-relative paths ("src/Foo.cs"); Cobertura edges
        // carry sourceRoot-relative or absolute paths. Matching must normalize.
        var testItems = new[]
        {
            new TestItem("test-A", "/asm.dll", "C", "A", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, []),
        };
        var edges = new[]
        {
            new TestToSourceEdge("test-A", "/repo/src/Foo.cs", EdgeOrigin.Static, 1_000_000, []),
        };

        var results = titi.Safety.Selection.ComputeSelectedTests(testItems, edges, [], ["src/Foo.cs"]);

        Assert.Contains(results, r => r.TestId == "test-A" && r.Selected);
    }

    [Fact]
    public void ComputeSelectedTests_EdgeMatch_NoFalsePositiveOnSubstring()
    {
        // Bug being fixed: edge.To.Contains(changedFile) matched "Foo.cs"
        // against "/repo/src/NotFoo.cs" (false positive). Path matching must
        // be segment-aware, not a raw substring test.
        var testItems = new[]
        {
            new TestItem("test-A", "/asm.dll", "C", "A", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, []),
        };
        var edges = new[]
        {
            new TestToSourceEdge("test-A", "/repo/src/NotFoo.cs", EdgeOrigin.Static, 1_000_000, []),
        };

        var results = titi.Safety.Selection.ComputeSelectedTests(testItems, edges, [], ["Foo.cs"]);

        var a = Assert.Single(results);
        Assert.False(a.Selected, $"expected no match, but {a.TestId} was selected via substring false-positive");
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

    // ── titi-k2x.8: canonical repo-relative path matching ─────

    [Fact]
    public void ComputeSelectedTests_SameBasename_DifferentDirs_DoNotCrossMatch()
    {
        // Bug: suffix matching reduced to the basename, so an edge to
        // a/Foo.cs was selected when b/Foo.cs changed. Selection must compare
        // canonical repo-relative paths, not just the last path segment.
        var testItems = new[]
        {
            new TestItem("test-A", "/asm.dll", "C", "A", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, []),
        };
        var edges = new[]
        {
            new TestToSourceEdge("test-A", "/repo/src/a/Foo.cs", EdgeOrigin.Static, 1_000_000, []),
        };

        // Changed file is in a DIFFERENT directory but same basename.
        var results = titi.Safety.Selection.ComputeSelectedTests(testItems, edges, [], ["b/Foo.cs"]);

        var a = Assert.Single(results);
        Assert.False(a.Selected,
            "edge to a/Foo.cs must not match changed b/Foo.cs (basename collision)");
    }

    [Fact]
    public void ComputeSelectedTests_SameRelativePath_Matches_AbsoluteAndRelative()
    {
        // Repo-relative and absolute representations of the same in-root file
        // must still match after canonicalization.
        var testItems = new[]
        {
            new TestItem("test-A", "/asm.dll", "C", "A", TestFramework.Xunit, TestTier.Unit,
                null, TestOutcome.Passed, 0, []),
        };
        var edges = new[]
        {
            new TestToSourceEdge("test-A", "/repo/src/a/Foo.cs", EdgeOrigin.Static, 1_000_000, []),
        };

        var results = titi.Safety.Selection.ComputeSelectedTests(testItems, edges, [], ["a/Foo.cs"]);

        var a = Assert.Single(results);
        Assert.True(a.Selected,
            "edge to /repo/src/a/Foo.cs must match changed a/Foo.cs after canonicalization");
    }
}
