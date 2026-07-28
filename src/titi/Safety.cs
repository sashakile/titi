// TID-4: Safety invariants and selection logic

namespace titi.Safety;

/// <summary>Entry from run history for a test item.</summary>
public record TestRunEntry(string TestId, TestOutcome Outcome, long DurationMs, DateTime Timestamp);

public static class Selection
{
    /// <summary>Compute the set of test IDs that must always run.</summary>
    public static HashSet<string> ComputeAlwaysRunSet(
        TestItem[] discoveredItems,
        Dictionary<string, TestRunEntry> history)
    {
        var alwaysRun = new HashSet<string>();

        foreach (var item in discoveredItems)
        {
            // No history → newly added or never run
            if (!history.TryGetValue(item.TestId, out var lastRun))
            {
                alwaysRun.Add(item.TestId);
                continue;
            }

            // Last run failed → must run again
            if (lastRun.Outcome == TestOutcome.Failed)
            {
                alwaysRun.Add(item.TestId);
                continue;
            }

            // Freshly discovered item not in recording → newly added
            // "Newly added" is relative to last recording, not discovery.
            // Since we don't have recording history here, treat items
            // without a recording entry as newly added.
            if (item.LastOutcome == TestOutcome.None && !history.ContainsKey(item.TestId))
            {
                alwaysRun.Add(item.TestId);
            }
        }

        return alwaysRun;
    }

    /// <summary>Compute confidence score from resolution ratio, edge freshness, and history depth.</summary>
    public static double ComputeConfidence(
        string[] changedFiles,
        string[] resolvedFiles,
        double edgeFreshness,
        int historyDepth)
    {
        if (changedFiles.Length == 0)
            return 1.0;

        var resolutionRatio = (double)resolvedFiles.Length / changedFiles.Length;
        var freshFactor = Math.Min(1.0, edgeFreshness);
        var depthFactor = Math.Min(1.0, historyDepth / 10.0);

        // Weighted average: resolution (60%), freshness (25%), depth (15%)
        return resolutionRatio * 0.6 + freshFactor * 0.25 + depthFactor * 0.15;
    }

    /// <summary>Compute selected tests based on edges, always-run set, and changed files.</summary>
    public static TestSelectionResult[] ComputeSelectedTests(
        TestItem[] testItems,
        TestToSourceEdge[] edges,
        HashSet<string> alwaysRun,
        string[] changedFiles)
    {
        var results = new List<TestSelectionResult>();
        var changedSet = new HashSet<string>(changedFiles);

        foreach (var item in testItems)
        {
            var reasons = new List<(string Kind, string Description)>();

            // Check always-run set first
            if (alwaysRun.Contains(item.TestId))
            {
                reasons.Add(("always-run", "Test is in always-run set"));
                results.Add(new TestSelectionResult(
                    item.TestId, true, reasons.ToArray(), 1.0, null));
                continue;
            }

            // Check edges against changed files
            var matched = false;
            foreach (var edge in edges)
            {
                if (edge.From != item.TestId)
                    continue;

                if (changedSet.Contains(edge.To) || changedSet.Any(c => edge.To.Contains(c)))
                {
                    reasons.Add(("edge-match", $"Source file {edge.To} changed"));
                    matched = true;
                    break;
                }
            }

            if (matched)
            {
                results.Add(new TestSelectionResult(
                    item.TestId, true, reasons.ToArray(), 1.0, null));
            }
            else
            {
                results.Add(new TestSelectionResult(
                    item.TestId, false, [("no-match", "No edge matched changed files")], 1.0, null));
            }
        }

        return results.ToArray();
    }

    /// <summary>Record a missed-selection incident.</summary>
    public static MissedSelectionIncident RecordMissedSelection(
        string changedContent,
        string missedTestId,
        ref List<MissedSelectionIncident> incidents)
    {
        var incident = new MissedSelectionIncident(
            ChangedContent: changedContent,
            MissedTestId: missedTestId,
            Timestamp: DateTime.UtcNow,
            Status: IncidentStatus.Candidate
        );
        incidents.Add(incident);
        return incident;
    }
}
