// TID-3c: Correlate TRX + Cobertura into per-test×source-file edges (CLI-21).
//
// `titi tests ingest` builds TestToSourceEdge instances by combining per-test
// outcomes (TRX, keyed by testName) with covered source files (Cobertura,
// file-level granularity). The result is the test×source cross-product built
// by EdgeBuilder — NOT the method-keyed edges ParseCobertura yields alone.
// This mirrors the correlation `titi tests record` performs per CLI-21/5.2.

namespace titi;

using System.Xml.Linq;

public record IngestResult(
    Coverage.TrxTestResult[] Results,
    TestToSourceEdge[] Edges,
    bool IsMalformed,
    Dictionary<string, Safety.TestRunEntry[]>? History);

public static class Ingestor
{
    private static readonly XNamespace TrxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>
    /// Canonical edge-index path under the titi cache dir (TD-03):
    /// <c>test-cache/edges/edges.json</c>.
    /// </summary>
    public static string EdgesRelativePath => Path.Combine("test-cache", "edges", "edges.json");

    /// <summary>
    /// Correlate a TRX run with optional Cobertura coverage into per-test×source
    /// edges. Malformed TRX (unparseable or non-TestRun root) is distinguished
    /// from a valid-but-empty TRX: <see cref="IngestResult.IsMalformed"/> is true
    /// only for the former, so the caller can exit non-zero per CLI-21.
    /// When <paramref name="priorHistory"/> is supplied, the returned
    /// <see cref="IngestResult.History"/> is the appended+retained history
    /// (TD-06); otherwise it is a fresh history from this run's results.
    /// </summary>
    public static IngestResult IngestRun(
        string trxXml,
        string? coberturaXml,
        string sourceRoot,
        Dictionary<string, Safety.TestRunEntry[]>? priorHistory = null,
        DateTime? recordedAt = null)
    {
        if (!TryParseTrxStrict(trxXml, out var results, out var isMalformed))
            return new IngestResult([], [], IsMalformed: true, History: priorHistory);

        if (isMalformed)
            return new IngestResult([], [], IsMalformed: true, History: priorHistory);

        // Update run history (TD-06): one entry per ingested result, retention 100.
        var history = HistoryStore.AppendResults(
            priorHistory ?? new Dictionary<string, Safety.TestRunEntry[]>(),
            results,
            recordedAt ?? DateTime.UtcNow);

        if (string.IsNullOrEmpty(coberturaXml))
            return new IngestResult(results, [], IsMalformed: false, History: history);

        var coberturaEdges = Coverage.Parser.ParseCobertura(coberturaXml, sourceRoot);
        var coveredSources = coberturaEdges.Select(e => e.To).Distinct().ToArray();
        var edges = EdgeBuilder.BuildFromRun(results, coveredSources);
        return new IngestResult(results, edges, IsMalformed: false, History: history);
    }

    // Strict TRX parse: returns false (malformed) if the XML is unparseable or
    // the root is not a TeamTest TestRun. A valid TestRun with zero results is
    // NOT malformed — returns true with an empty results array.
    private static bool TryParseTrxStrict(string xml, out Coverage.TrxTestResult[] results, out bool isMalformed)
    {
        results = [];
        isMalformed = false;

        if (string.IsNullOrWhiteSpace(xml))
        {
            isMalformed = true;
            return false;
        }

        try
        {
            var doc = XDocument.Parse(xml);
            if (doc.Root == null || doc.Root.Name != TrxNs + "TestRun")
            {
                isMalformed = true;
                return false;
            }
        }
        catch
        {
            isMalformed = true;
            return false;
        }

        // Root is a valid TestRun — delegate result extraction to the existing
        // parser (which is namespace-aware and tolerant of missing attributes).
        results = Coverage.Parser.ParseTrx(xml);
        return true;
    }
}
