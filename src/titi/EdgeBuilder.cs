// TID-3b: Build test→source edges from a recorded test run (TD-03).
//
// Combines per-test outcomes (TRX) with covered source files (Cobertura) into
// TestToSourceEdge instances. Per TD-03, the granularity is file-level: one
// edge per (executed test, covered source file) pair, with origin :static and
// weight 1_000_000. This is an over-approximation (a test may be linked to a
// source file it never directly exercised, if another test in the same run
// did) — acceptable for the over-approximation invariant (SAFE-001).

namespace titi;

public static class EdgeBuilder
{
    /// <summary>
    /// Build file-level <see cref="TestToSourceEdge"/> instances from one
    /// recorded test run: every <em>executed</em> test (Passed or Failed) is
    /// linked to every covered source file. Skipped tests (NotExecuted) did
    /// not run, so they are excluded — linking them would falsely tie them to
    /// files they never touched. Duplicate source files are collapsed.
    /// </summary>
    public static TestToSourceEdge[] BuildFromRun(
        Coverage.TrxTestResult[] testResults,
        string[] coveredSourceFiles)
    {
        if (testResults.Length == 0 || coveredSourceFiles.Length == 0)
            return [];

        // Only executed tests contribute coverage relationships.
        var executed = testResults
            .Where(r => r.Outcome is TestOutcome.Passed or TestOutcome.Failed)
            .Select(r => r.TestName)
            .Distinct()
            .ToArray();

        if (executed.Length == 0)
            return [];

        var sources = coveredSourceFiles.Distinct().ToArray();

        var edges = new List<TestToSourceEdge>(executed.Length * sources.Length);
        foreach (var testName in executed)
        {
            foreach (var sourceFile in sources)
            {
                edges.Add(new TestToSourceEdge(
                    From: testName,
                    To: sourceFile,
                    Origin: EdgeOrigin.Static,
                    Weight: 1_000_000,
                    LineRanges: []  // file-level granularity (TD-03 known limitation)
                ));
            }
        }

        return edges.ToArray();
    }
}
