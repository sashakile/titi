// Tests for TID-12: testaruda adapter protocol (CLI-19)
// Phase 1: project-level granularity, symbol_model_complete: false

namespace titi.Tests;

using System.Text.Json;
using titi.Adapter;

public class AdapterTests
{
    // ── Handshake ────────────────────────────────────────────────

    [Fact]
    public void Handshake_ReturnsCorrectCapabilities()
    {
        var response = TestarudaAdapter.HandleHandshake();

        Assert.Equal("titi", response.Name);
        Assert.Equal(["csharp"], response.Languages);
        Assert.Equal("project", response.Granularity);
        Assert.False(response.SymbolModelComplete);
        Assert.False(response.RuntimeEdges);
    }

    [Fact]
    public void Handshake_Response_SerializesToValidJson()
    {
        var response = TestarudaAdapter.HandleHandshake();
        var json = JsonSerializer.Serialize(response);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("titi", root.GetProperty("name").GetString());
        Assert.Equal("project", root.GetProperty("granularity").GetString());
        Assert.False(root.GetProperty("symbol_model_complete").GetBoolean());
        Assert.False(root.GetProperty("runtime_edges").GetBoolean());

        var langs = root.GetProperty("languages").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("csharp", langs);
    }

    // ── Discover ─────────────────────────────────────────────────

    [Fact]
    public void Discover_EmptyGraph_ReturnsEmptyItems()
    {
        var graph = new MonorepoGraph(
            Nodes: new(),
            TopologicalOrder: [],
            RepoRoot: "/repo",
            BuiltAt: DateTime.UtcNow,
            Fingerprints: new()
        );

        var items = TestarudaAdapter.HandleDiscover(graph);

        Assert.Empty(items);
    }

    [Fact]
    public void Discover_OnlyTestProjects_ReturnsOnePerProject()
    {
        var graph = MakeGraph([
            ("/repo/src/Lib/Lib.csproj", "Lib", false),
            ("/repo/tests/LibTest/LibTest.csproj", "LibTest", true),
            ("/repo/tests/LibIntegration/LibIntegration.csproj", "LibIntegration", true),
        ]);

        var items = TestarudaAdapter.HandleDiscover(graph);

        Assert.Equal(2, items.Length);
        Assert.Contains(items, i => i.TestId.StartsWith("LibTest"));
        Assert.Contains(items, i => i.TestId.StartsWith("LibIntegration"));
        Assert.DoesNotContain(items, i => i.TestId == "Lib");
    }

    [Fact]
    public void Discover_TestItem_HasExpectedFields()
    {
        var graph = MakeGraph([
            ("/repo/tests/MyTest/MyTest.csproj", "MyTest", true),
        ]);

        var items = TestarudaAdapter.HandleDiscover(graph);

        var item = Assert.Single(items);
        Assert.Equal("MyTest", item.TestId);
        Assert.Equal("/repo/tests/MyTest/MyTest.csproj", item.AssemblyPath);
        Assert.Equal("MyTest", item.ClassName); // whole-project item
        Assert.Equal("all", item.MethodName);   // project-level granularity
        Assert.Equal("xunit", item.Framework); // default
        Assert.Equal("unit", item.Tier);
    }

    // ── Static-deps ──────────────────────────────────────────────

    [Fact]
    public void StaticDeps_NoChanges_ReturnsEmpty()
    {
        var graph = MakeGraph([("/repo/tests/T/T.csproj", "T", true)]);

        var result = TestarudaAdapter.HandleStaticDeps(graph, [], []);

        Assert.Empty(result);
    }

    [Fact]
    public void StaticDeps_ChangedFileAffectsTestProject_ReturnsThatProject()
    {
        // Lib.csproj at /repo/src/Lib/ → changed src/Lib/Foo.cs
        // LibTest.csproj depends on Lib → affected
        var graph = MakeGraph([
            ("/repo/src/Lib/Lib.csproj", "Lib", false),
            ("/repo/tests/LibTest/LibTest.csproj", "LibTest", true),
        ]);

        // Wire LibTest → Lib dependency
        graph = AddDependency(graph, "/repo/tests/LibTest/LibTest.csproj", "/repo/src/Lib/Lib.csproj");

        var result = TestarudaAdapter.HandleStaticDeps(
            graph, ["LibTest"], ["src/Lib/Foo.cs"]);

        Assert.NotEmpty(result);
        Assert.Contains(result, r => r.TestId == "LibTest");
    }

    [Fact]
    public void StaticDeps_UnchangedTestProject_NotReturned()
    {
        var graph = MakeGraph([
            ("/repo/src/Lib/Lib.csproj", "Lib", false),
            ("/repo/tests/LibTest/LibTest.csproj", "LibTest", true),
            ("/repo/tests/OtherTest/OtherTest.csproj", "OtherTest", true),
        ]);

        graph = AddDependency(graph, "/repo/tests/LibTest/LibTest.csproj", "/repo/src/Lib/Lib.csproj");

        // Only Lib.cs changed — OtherTest should not be affected
        var result = TestarudaAdapter.HandleStaticDeps(
            graph, ["LibTest"], ["src/Lib/Foo.cs"]);

        Assert.DoesNotContain(result, r => r.TestId == "OtherTest");
    }

    // ── Fingerprint ──────────────────────────────────────────────

    [Fact]
    public void Fingerprint_ReturnsGraphFingerprints()
    {
        var fingerprints = new Dictionary<string, string>
        {
            ["/repo/src/Lib/Lib.csproj"] = "abc123",
            ["/repo/tests/LibTest/LibTest.csproj"] = "def456",
        };

        var graph = new MonorepoGraph(
            Nodes: new(),
            TopologicalOrder: [],
            RepoRoot: "/repo",
            BuiltAt: DateTime.UtcNow,
            Fingerprints: fingerprints
        );

        var result = TestarudaAdapter.HandleFingerprint(graph);

        Assert.Equal(fingerprints, result);
    }

    // ── Run-args ─────────────────────────────────────────────────

    [Fact]
    public void RunArgs_ReturnsDotnetTestCommand()
    {
        var projects = new[]
        {
            new ProjectDescriptor(
                "/repo/tests/LibTest/LibTest.csproj", "LibTest",
                new SemanticVersion(1, 0, 0, null, null),
                [], false, true, [], [], new()),
        };

        var result = TestarudaAdapter.HandleRunArgs(projects, null);

        Assert.Equal("dotnet", result[0]);
        Assert.Equal("test", result[1]);
        Assert.StartsWith("/tmp/titi-adapter/", result[2]);
        Assert.EndsWith(".proj", result[2]);
    }

    [Fact]
    public void RunArgs_WithFilter_IncludesFilterFlag()
    {
        var projects = new[]
        {
            new ProjectDescriptor(
                "/repo/tests/LibTest/LibTest.csproj", "LibTest",
                new SemanticVersion(1, 0, 0, null, null),
                [], false, true, [], [], new()),
        };

        var filters = new Dictionary<string, string>
        {
            ["LibTest"] = "FullyQualifiedName~Test1|FullyQualifiedName~Test2"
        };

        var result = TestarudaAdapter.HandleRunArgs(projects, filters);

        // Should include --filter argument
        var joined = string.Join(" ", result);
        Assert.Contains("--filter", joined);
    }

    // ── Ingest ───────────────────────────────────────────────────

    [Fact]
    public void Ingest_EmptyTrx_ReturnsEmptyResults()
    {
        var result = TestarudaAdapter.HandleIngest(null);

        Assert.Empty(result);
    }

    // ── Protocol: request parsing ────────────────────────────────

    [Fact]
    public void ParseRequest_HandshakeCommand_ReturnsCorrectType()
    {
        var json = """{"command":"handshake","params":{}}""";
        var request = TestarudaAdapter.ParseRequest(json);

        Assert.NotNull(request);
        Assert.Equal("handshake", request.Command);
    }

    [Fact]
    public void ParseRequest_DiscoverCommand_ReturnsCorrectType()
    {
        var json = """{"command":"discover","params":{}}""";
        var request = TestarudaAdapter.ParseRequest(json);

        Assert.NotNull(request);
        Assert.Equal("discover", request.Command);
    }

    [Fact]
    public void ParseRequest_StaticDepsCommand_WithChangedFiles()
    {
        var json = """{"command":"static-deps","params":{"changed_files":["src/Foo.cs"],"affected_projects":["LibTest"]}}""";
        var request = TestarudaAdapter.ParseRequest(json);

        Assert.NotNull(request);
        Assert.Equal("static-deps", request.Command);
        Assert.Equal(["src/Foo.cs"], request.ChangedFiles);
        Assert.Equal(["LibTest"], request.AffectedProjects);
    }

    [Fact]
    public void ParseRequest_FingerprintCommand()
    {
        var json = """{"command":"fingerprint","params":{}}""";
        var request = TestarudaAdapter.ParseRequest(json);

        Assert.NotNull(request);
        Assert.Equal("fingerprint", request.Command);
    }

    [Fact]
    public void ParseRequest_RunArgsCommand_WithTestIds()
    {
        var json = """{"command":"run-args","params":{"test_ids":["LibTest","LibIntegration"]}}""";
        var request = TestarudaAdapter.ParseRequest(json);

        Assert.NotNull(request);
        Assert.Equal("run-args", request.Command);
        Assert.Equal(["LibTest", "LibIntegration"], request.TestIds);
    }

    [Fact]
    public void ParseRequest_IngestCommand_WithTrxPath()
    {
        var json = """{"command":"ingest","params":{"trx_path":"/repo/test-results.trx"}}""";
        var request = TestarudaAdapter.ParseRequest(json);

        Assert.NotNull(request);
        Assert.Equal("ingest", request.Command);
        Assert.Equal("/repo/test-results.trx", request.TrxPath);
    }

    [Fact]
    public void ParseRequest_MalformedJson_ReturnsNull()
    {
        var request = TestarudaAdapter.ParseRequest("not json");
        Assert.Null(request);
    }

    [Fact]
    public void ParseRequest_UnknownCommand_ReturnsRequestWithUnknownCommand()
    {
        var json = """{"command":"unknown","params":{}}""";
        var request = TestarudaAdapter.ParseRequest(json);

        Assert.NotNull(request);
        Assert.Equal("unknown", request.Command);
    }

    // ── Shutdown ──────────────────────────────────────────────────

    [Fact]
    public void Shutdown_ReturnsShuttingDownStatus()
    {
        var json = TestarudaAdapter.ProcessCommand(
            new TestarudaAdapter.AdapterRequest("shutdown", [], [], [], ""),
            null);

        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("shutting_down", result.GetProperty("status").GetString());
    }

    // ── Run-args edge cases ───────────────────────────────────────

    [Fact]
    public void RunArgs_EmptyProjects_ReturnsNoopCommand()
    {
        var result = TestarudaAdapter.HandleRunArgs([], null);

        Assert.Equal(["dotnet", "test", "--no-build"], result);
    }

    [Fact]
    public void RunArgs_GeneratesUniqueTempPath()
    {
        var projects = new[]
        {
            new ProjectDescriptor(
                "/repo/tests/P/P.csproj", "P",
                new SemanticVersion(1, 0, 0, null, null),
                [], false, true, [], [], new()),
        };

        var result1 = TestarudaAdapter.HandleRunArgs(projects, null);
        var result2 = TestarudaAdapter.HandleRunArgs(projects, null);

        // Each invocation should produce a unique temp file path
        Assert.NotEqual(result1[2], result2[2]);

        // Clean up
        try { File.Delete(result1[2]); } catch { }
        try { File.Delete(result2[2]); } catch { }
    }

    // ── Protocol: response serialization ─────────────────────────

    [Fact]
    public void FormatResponse_Error_ProducesValidJson()
    {
        var json = TestarudaAdapter.FormatErrorResponse("something went wrong");

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Equal("something went wrong", error.GetString());
    }

    [Fact]
    public void FormatResponse_Handshake_ProducesValidJson()
    {
        var response = TestarudaAdapter.HandleHandshake();
        var json = TestarudaAdapter.FormatResponse(response);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("result", out var result));
        Assert.Equal("titi", result.GetProperty("name").GetString());
    }

    [Fact]
    public void FormatResponse_DiscoverItems_ProducesValidJson()
    {
        var items = new[]
        {
            new TestarudaAdapter.AdapterTestItem("T1", "/asm.dll", "C", "M", TestFramework.Xunit, TestTier.Unit)
        };
        var json = TestarudaAdapter.FormatResponse(items);

        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("result");
        var tests = result.GetProperty("tests").EnumerateArray().ToArray();
        Assert.Single(tests);
        Assert.Equal("T1", tests[0].GetProperty("test_id").GetString());
    }

    // ── Ingest edge cases ────────────────────────────────────────

    [Fact]
    public void Ingest_NonExistentTrxFile_ReturnsEmpty()
    {
        var result = TestarudaAdapter.HandleIngest("/nonexistent/results.trx");
        Assert.Empty(result);
    }

    // ── CLI dispatch ─────────────────────────────────────────────

    [Fact]
    public void HelpIncludesAdapterCommand()
    {
        // Verify the adapter command is mentioned in help output
        // (redirect Console.Out to avoid interfering with other tests)
        var originalOut = Console.Out;
        try
        {
            using var sw = new StringWriter();
            Console.SetOut(sw);
            var exitCode = titi.Core.Program.Main(["--help"]);
            var output = sw.ToString();
            Assert.Contains("testaruda-adapter", output);
            Assert.Contains("tests list", output);
            Assert.Contains("test-manifest", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>Create a minimal MonorepoGraph from a list of (path, packageId, isTestProject) tuples.</summary>
    static MonorepoGraph MakeGraph((string Path, string PackageId, bool IsTestProject)[] projects)
    {
        var nodes = new Dictionary<string, GraphNode>();
        var topo = new List<string>();

        foreach (var (path, pkgId, isTest) in projects)
        {
            var desc = new ProjectDescriptor(
                path, pkgId,
                new SemanticVersion(1, 0, 0, null, null),
                [], false, isTest, [], [], new()
            );

            nodes[path] = new GraphNode(
                Project: desc,
                Dependencies: [],
                Dependents: [],
                Depth: 0
            );
            topo.Add(path);
        }

        return new MonorepoGraph(
            Nodes: nodes,
            TopologicalOrder: topo.ToArray(),
            RepoRoot: "/repo",
            BuiltAt: DateTime.UtcNow,
            Fingerprints: new()
        );
    }

    /// <summary>Add a dependency edge from one project to another.</summary>
    static MonorepoGraph AddDependency(MonorepoGraph graph, string from, string to)
    {
        var nodes = new Dictionary<string, GraphNode>(graph.Nodes);

        if (nodes.TryGetValue(from, out var fromNode))
        {
            var deps = fromNode.Dependencies.Append(
                new GraphEdge(from, to, ReferenceMode.Binary, null, false)
            ).ToArray();
            nodes[from] = fromNode with { Dependencies = deps };
        }

        if (nodes.TryGetValue(to, out var toNode))
        {
            var dependents = toNode.Dependents.Append(
                new GraphEdge(to, from, ReferenceMode.Binary, null, false)
            ).ToArray();
            nodes[to] = toNode with { Dependents = dependents };
        }

        return graph with { Nodes = nodes };
    }
}
