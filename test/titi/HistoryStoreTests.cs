// Tests for TID-4a: run-history persistence (TD-06 / task 4.6)
// EDN format: {test-id [{:test-id "..." :outcome :passed :duration-ms 2 :timestamp "..."}]}

namespace titi.Tests;

public class HistoryStoreTests
{
    static readonly DateTime Ts = new(2026, 7, 28, 13, 0, 0, DateTimeKind.Utc);

    static titi.Coverage.TrxTestResult[] Results(params (string, TestOutcome, long)[] rs)
        => rs.Select(r => new titi.Coverage.TrxTestResult(r.Item1, r.Item2, r.Item3, null)).ToArray();

    [Fact]
    public void AppendResults_AddsEntryPerTest_MostRecentLast()
    {
        var history = new Dictionary<string, titi.Safety.TestRunEntry[]>();
        var results = Results(
            ("Orion.A.M1", TestOutcome.Passed, 2),
            ("Orion.A.M2", TestOutcome.Failed, 5));

        var updated = titi.HistoryStore.AppendResults(history, results, Ts);

        Assert.Equal(2, updated.Count);
        Assert.Single(updated["Orion.A.M1"]);
        Assert.Equal(TestOutcome.Passed, updated["Orion.A.M1"][0].Outcome);
        Assert.Equal(2, updated["Orion.A.M1"][0].DurationMs);
        Assert.Equal(Ts, updated["Orion.A.M1"][0].Timestamp);
        Assert.Equal(TestOutcome.Failed, updated["Orion.A.M2"][0].Outcome);
    }

    [Fact]
    public void AppendResults_AppendsToExistingHistory()
    {
        var existing = new Dictionary<string, titi.Safety.TestRunEntry[]>
        {
            ["Orion.A.M1"] = new[] { new titi.Safety.TestRunEntry("Orion.A.M1", TestOutcome.Passed, 1, Ts.AddDays(-1)) },
        };
        var results = Results(("Orion.A.M1", TestOutcome.Failed, 3));

        var updated = titi.HistoryStore.AppendResults(existing, results, Ts);

        Assert.Equal(2, updated["Orion.A.M1"].Length);
        // most-recent-last: the new entry is at the end.
        Assert.Equal(TestOutcome.Failed, updated["Orion.A.M1"][1].Outcome);
        Assert.Equal(TestOutcome.Passed, updated["Orion.A.M1"][0].Outcome);
    }

    [Fact]
    public void AppendResults_RetentionEvictsOldestBeyond100()
    {
        var existing = new Dictionary<string, titi.Safety.TestRunEntry[]>();
        var entries = Enumerable.Range(0, 100)
            .Select(i => new titi.Safety.TestRunEntry("Orion.A.M1", TestOutcome.Passed, i, Ts.AddDays(-100 + i)))
            .ToArray();
        existing["Orion.A.M1"] = entries;

        var results = Results(("Orion.A.M1", TestOutcome.Failed, 999));

        var updated = titi.HistoryStore.AppendResults(existing, results, Ts, maxPerTest: 100);

        Assert.Equal(100, updated["Orion.A.M1"].Length);
        // oldest evicted, newest is the just-appended failure (most-recent-last).
        Assert.Equal(TestOutcome.Failed, updated["Orion.A.M1"][^1].Outcome);
        Assert.Equal(999, updated["Orion.A.M1"][^1].DurationMs);
        // the previous-oldest (duration 0) should be gone.
        Assert.DoesNotContain(updated["Orion.A.M1"], e => e.DurationMs == 0);
    }

    [Fact]
    public void AppendResults_EmptyResults_ReturnsHistoryUnchanged()
    {
        var existing = new Dictionary<string, titi.Safety.TestRunEntry[]>
        {
            ["Orion.A.M1"] = new[] { new titi.Safety.TestRunEntry("Orion.A.M1", TestOutcome.Passed, 1, Ts) },
        };

        var updated = titi.HistoryStore.AppendResults(existing, Array.Empty<titi.Coverage.TrxTestResult>(), Ts);

        Assert.Single(updated);
        Assert.Single(updated["Orion.A.M1"]);
    }

    [Fact]
    public void SerializeEdn_ProducesEdnMapWithKeywordKeys()
    {
        var history = new Dictionary<string, titi.Safety.TestRunEntry[]>
        {
            ["Orion.A.M1"] = new[]
            {
                new titi.Safety.TestRunEntry("Orion.A.M1", TestOutcome.Passed, 2, Ts),
            },
        };

        var edn = titi.HistoryStore.SerializeEdn(history);

        Assert.Contains("\"Orion.A.M1\"", edn);
        Assert.Contains(":test-id", edn);
        Assert.Contains(":outcome", edn);
        Assert.Contains(":passed", edn);
        Assert.Contains(":duration-ms", edn);
        Assert.Contains(":timestamp", edn);
        // EDN map braces.
        Assert.StartsWith("{", edn.TrimStart());
        Assert.EndsWith("}", edn.TrimEnd());
    }

    [Fact]
    public void ParseEdn_RoundTripsSerializeEdn()
    {
        var history = new Dictionary<string, titi.Safety.TestRunEntry[]>
        {
            ["Orion.A.M1"] = new[]
            {
                new titi.Safety.TestRunEntry("Orion.A.M1", TestOutcome.Passed, 2, Ts),
                new titi.Safety.TestRunEntry("Orion.A.M1", TestOutcome.Failed, 5, Ts.AddSeconds(1)),
            },
            ["Orion.A.M2"] = new[]
            {
                new titi.Safety.TestRunEntry("Orion.A.M2", TestOutcome.Skipped, 0, Ts),
            },
        };

        var edn = titi.HistoryStore.SerializeEdn(history);
        var parsed = titi.HistoryStore.ParseEdn(edn);

        Assert.Equal(2, parsed.Count);
        Assert.Equal(2, parsed["Orion.A.M1"].Length);
        Assert.Equal(TestOutcome.Passed, parsed["Orion.A.M1"][0].Outcome);
        Assert.Equal(TestOutcome.Failed, parsed["Orion.A.M1"][1].Outcome);
        Assert.Equal(5, parsed["Orion.A.M1"][1].DurationMs);
        Assert.Equal(TestOutcome.Skipped, parsed["Orion.A.M2"][0].Outcome);
        Assert.Equal(Ts, parsed["Orion.A.M1"][0].Timestamp);
    }

    [Fact]
    public void ParseEdn_EmptyOrMissing_ReturnsEmpty()
    {
        Assert.Empty(titi.HistoryStore.ParseEdn(""));
        Assert.Empty(titi.HistoryStore.ParseEdn("{}"));
    }

    [Fact]
    public void ParseEdn_Malformed_ReturnsEmpty()
    {
        Assert.Empty(titi.HistoryStore.ParseEdn("not edn"));
    }

    [Fact]
    public void CompactIfOversized_DropsEntriesBeyondRetention_WhenOver10Mb()
    {
        // Build a history where one test has 150 entries (over retention of 100)
        // and the serialized form is forced over the threshold.
        var entries = Enumerable.Range(0, 150)
            .Select(i => new titi.Safety.TestRunEntry("Orion.A.Big", TestOutcome.Passed, i, Ts.AddSeconds(i)))
            .ToArray();
        var history = new Dictionary<string, titi.Safety.TestRunEntry[]> { ["Orion.A.Big"] = entries };

        // Use a tiny threshold so compaction triggers without a 10MB fixture.
        var compacted = titi.HistoryStore.CompactIfOversized(history, maxEntriesPerTest: 100, maxSizeBytes: 1);

        Assert.Equal(100, compacted["Orion.A.Big"].Length);
        // kept the 100 most-recent (largest duration-ms indices).
        Assert.Equal(149, compacted["Orion.A.Big"][^1].DurationMs);
    }

    [Fact]
    public void CompactIfOversized_NoopWhenUnderThreshold()
    {
        var history = new Dictionary<string, titi.Safety.TestRunEntry[]>
        {
            ["Orion.A.M1"] = new[] { new titi.Safety.TestRunEntry("Orion.A.M1", TestOutcome.Passed, 1, Ts) },
        };

        var compacted = titi.HistoryStore.CompactIfOversized(history, maxEntriesPerTest: 100, maxSizeBytes: 10_000_000);

        Assert.Single(compacted["Orion.A.M1"]);
    }
}
