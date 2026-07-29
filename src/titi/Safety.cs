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
        // Canonicalize changed files once: reduce each to its repo-relative
        // path (the canonical key used for matching). This makes absolute
        // edge paths ("/repo/src/a/Foo.cs") match repo-relative changed files
        // ("a/Foo.cs") without a raw substring test, AND prevents same-basename
        // collisions (a/Foo.cs vs b/Foo.cs) by comparing the full relative path
        // rather than just the last segment.
        var changedSet = new HashSet<string>(changedFiles, StringComparer.Ordinal);
        var changedCanonical = changedFiles
            .Select(CanonicalRelativePath)
            .Where(s => !string.IsNullOrEmpty(s))
            .ToHashSet(StringComparer.Ordinal);

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

            // Check edges against changed files (path-normalized match).
            var matched = false;
            foreach (var edge in edges)
            {
                if (edge.From != item.TestId)
                    continue;

                if (IsPathMatch(edge.To, changedSet, changedCanonical))
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

    // Match an edge's source path against the changed-file set. Direct
    // equality handles already-normalized inputs; otherwise compare canonical
    // segment-joined tail-suffixes of the edge path against the precomputed
    // changed-file set. This is segment-aware on the FULL relative path, not
    // just the basename, so a/Foo.cs does not match changed b/Foo.cs while
    // /repo/src/a/Foo.cs still matches repo-relative a/Foo.cs.
    private static bool IsPathMatch(string edgeTo, HashSet<string> changedSet, HashSet<string> changedCanonical)
    {
        if (changedSet.Contains(edgeTo))
            return true;
        var segs = edgeTo.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Walk tail-suffixes from the last segment upward (shortest first), so
        // a longer changed path wins over a coincidental single-segment suffix.
        for (int start = segs.Length - 1; start >= 0; start--)
        {
            var suffix = string.Join('/', segs[start..]);
            if (changedCanonical.Contains(suffix))
                return true;
        }
        return false;
    }

    // Reduce a path to its canonical segment-joined form: normalize
    // separators to '/' and collapse '.'/'..' segments without touching the
    // disk. Repo-relative inputs ("a/Foo.cs") pass through unchanged; this is
    // the key compared against the edge's tail-suffixes.
    private static string CanonicalRelativePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "";
        var segments = path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(segments.Length);
        foreach (var seg in segments)
        {
            if (seg == ".")
                continue;
            if (seg == "..")
            {
                if (stack.Count > 0)
                    stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(seg);
        }
        return string.Join('/', stack);
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
