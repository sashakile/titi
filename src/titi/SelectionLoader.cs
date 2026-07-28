// TID-5a: Load selection context (edges + history + discovered test items)
// for `titi affected`. Pure loading from the cache dir; discovery (running
// dotnet test --list-tests) is kept in the command so it stays separable.

namespace titi;

using System.Text.Json;
using System.Text.Json.Serialization;
using titi.Safety;
using titi.Serialization;

public static class SelectionLoader
{
    /// <summary>
    /// Load the test→source edge index from <c>.titi/test-cache/edges/edges.edn</c>.
    /// The file is JSON (written by TestsRecordCommand/TestsIngestCommand) despite
    /// the <c>.edn</c> extension. Missing or unparseable file -> empty array.
    /// </summary>
    public static TestToSourceEdge[] LoadEdges(string cacheDir)
    {
        var edgesPath = Path.Combine(cacheDir, "edges", "edges.edn");
        if (!File.Exists(edgesPath))
            return [];

        try
        {
            var json = File.ReadAllText(edgesPath);
            var arr = JsonSerializer.Deserialize(json, TitiJsonContext.Default.ListJsonEdge) ?? [];
            return arr.Select(e => new TestToSourceEdge(
                From: e.From ?? "",
                To: e.To ?? "",
                Origin: ParseOrigin(e.Origin),
                Weight: e.Weight,
                LineRanges: (e.LineRanges ?? []).Select(lr => (lr.Start, lr.End)).ToArray()
            )).Where(e => !string.IsNullOrEmpty(e.From) && !string.IsNullOrEmpty(e.To)).ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Load run history from <c>.titi/test-cache/history.edn</c> (EDN).
    /// Missing file -> empty dict (all discovered tests treated as no-history).
    /// </summary>
    public static Dictionary<string, TestRunEntry[]> LoadHistory(string cacheDir)
    {
        var historyPath = Path.Combine(cacheDir, "history.edn");
        if (!File.Exists(historyPath))
            return [];
        return HistoryStore.ParseEdn(File.ReadAllText(historyPath));
    }

    /// <summary>JSON shape for edges.edn serialization.</summary>
    internal sealed class JsonEdge
    {
        [JsonPropertyName("from")]
        public string? From { get; set; }
        [JsonPropertyName("to")]
        public string? To { get; set; }
        [JsonPropertyName("origin")]
        public int? Origin { get; set; }
        [JsonPropertyName("weight")]
        public long Weight { get; set; }
        [JsonPropertyName("lineRanges")]
        public List<JsonLineRange>? LineRanges { get; set; }
    }

    internal sealed class JsonLineRange
    {
        [JsonPropertyName("start")]
        public int Start { get; set; }
        [JsonPropertyName("end")]
        public int End { get; set; }
    }

    internal static EdgeOrigin ParseOrigin(int? n) => n switch
    {
        0 => EdgeOrigin.Static,
        1 => EdgeOrigin.Runtime,
        2 => EdgeOrigin.Manual,
        _ => EdgeOrigin.Static,
    };
}
