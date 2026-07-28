// Tests for TID-5a: SelectionLoader (edges + history cache loading)

namespace titi.Tests;

using System.Text.Json;

public class SelectionLoaderTests
{
    [Fact]
    public void LoadEdges_ReadsJsonEdgeIndex_RegardlessOfEdnExtension()
    {
        using var tmp = new TempDir();
        var cacheDir = Path.Combine(tmp.Path, "test-cache");
        Directory.CreateDirectory(Path.Combine(cacheDir, "edges"));
        var edgesPath = Path.Combine(cacheDir, "edges", "edges.edn");
        // The edges file is JSON (written by TestsRecordCommand) despite .edn.
        // Use camelCase to match the [JsonPropertyName] attributes on JsonEdge.
        var json = JsonSerializer.Serialize(new object[]
        {
            new { from = "Orion.Tests.A.M1", to = "/repo/src/Foo.cs", origin = 0, weight = 1_000_000L,
                  lineRanges = new[] { new { start = 1, end = 5 } } },
            new { from = "Orion.Tests.A.M2", to = "/repo/src/Bar.cs", origin = 0, weight = 1_000_000L,
                  lineRanges = Array.Empty<object>() },
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(edgesPath, json);

        var edges = titi.SelectionLoader.LoadEdges(cacheDir);

        Assert.Equal(2, edges.Length);
        Assert.Contains(edges, e => e.From == "Orion.Tests.A.M1" && e.To.EndsWith("Foo.cs"));
        Assert.All(edges, e => Assert.Equal(EdgeOrigin.Static, e.Origin));
        var m1 = Assert.Single(edges, e => e.From == "Orion.Tests.A.M1");
        Assert.Single(m1.LineRanges);
        Assert.Equal((1, 5), m1.LineRanges[0]);
    }

    [Fact]
    public void LoadEdges_MissingFile_ReturnsEmpty()
    {
        using var tmp = new TempDir();
        Assert.Empty(titi.SelectionLoader.LoadEdges(tmp.Path));
    }

    [Fact]
    public void LoadEdges_MalformedJson_ReturnsEmpty()
    {
        using var tmp = new TempDir();
        var cacheDir = Path.Combine(tmp.Path, "test-cache");
        Directory.CreateDirectory(Path.Combine(cacheDir, "edges"));
        File.WriteAllText(Path.Combine(cacheDir, "edges", "edges.edn"), "not json");

        Assert.Empty(titi.SelectionLoader.LoadEdges(cacheDir));
    }

    [Fact]
    public void LoadEdges_DropsEmptyFromOrTo()
    {
        using var tmp = new TempDir();
        var cacheDir = Path.Combine(tmp.Path, "test-cache");
        Directory.CreateDirectory(Path.Combine(cacheDir, "edges"));
        var json = JsonSerializer.Serialize(new object[]
        {
            new { from = "", to = "/repo/x.cs", origin = 0, weight = 1L, lineRanges = Array.Empty<object>() },
            new { from = "T", to = "", origin = 0, weight = 1L, lineRanges = Array.Empty<object>() },
            new { from = "T2", to = "/repo/y.cs", origin = 0, weight = 1L, lineRanges = Array.Empty<object>() },
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        File.WriteAllText(Path.Combine(cacheDir, "edges", "edges.edn"), json);

        var edges = titi.SelectionLoader.LoadEdges(cacheDir);
        Assert.Single(edges);
        Assert.Equal("T2", edges[0].From);
    }

    [Fact]
    public void LoadHistory_ReadsEdnHistory()
    {
        using var tmp = new TempDir();
        var cacheDir = Path.Combine(tmp.Path, "test-cache");
        Directory.CreateDirectory(cacheDir);
        var history = new Dictionary<string, titi.Safety.TestRunEntry[]>
        {
            ["Orion.A.M1"] = new[]
            {
                new titi.Safety.TestRunEntry("Orion.A.M1", TestOutcome.Passed, 3, DateTime.UtcNow),
            },
        };
        File.WriteAllText(Path.Combine(cacheDir, "history.edn"), titi.HistoryStore.SerializeEdn(history));

        var loaded = titi.SelectionLoader.LoadHistory(cacheDir);

        Assert.Single(loaded);
        Assert.Equal(TestOutcome.Passed, loaded["Orion.A.M1"][^1].Outcome);
    }

    [Fact]
    public void LoadHistory_MissingFile_ReturnsEmpty()
    {
        using var tmp = new TempDir();
        Assert.Empty(titi.SelectionLoader.LoadHistory(tmp.Path));
    }

    sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "titi-sl-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
