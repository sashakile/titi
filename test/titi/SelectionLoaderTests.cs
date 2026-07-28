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
        File.WriteAllText(edgesPath, JsonSerializer.Serialize(new object[]
        {
            new { From = "Orion.Tests.A.M1", To = "/repo/src/Foo.cs", Origin = 0, Weight = 1_000_000L,
                  LineRanges = new[] { new { Start = 1, End = 5 } } },
            new { From = "Orion.Tests.A.M2", To = "/repo/src/Bar.cs", Origin = 0, Weight = 1_000_000L,
                  LineRanges = Array.Empty<object>() },
        }));

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
        File.WriteAllText(Path.Combine(cacheDir, "edges", "edges.edn"), JsonSerializer.Serialize(new object[]
        {
            new { From = "", To = "/repo/x.cs", Origin = 0, Weight = 1L, LineRanges = Array.Empty<object>() },
            new { From = "T", To = "", Origin = 0, Weight = 1L, LineRanges = Array.Empty<object>() },
            new { From = "T2", To = "/repo/y.cs", Origin = 0, Weight = 1L, LineRanges = Array.Empty<object>() },
        }));

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
