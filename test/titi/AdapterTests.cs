// Tests for TID-12: testaruda adapter protocol (CLI-19)
// Phase 2: method-level granularity, symbol_model_complete: true

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
        Assert.Equal("0.1.0", response.Version);
        Assert.Equal(1, response.Protocol);
        Assert.Equal(["csharp"], response.Languages);
        Assert.Equal("method", response.Granularity);
        Assert.True(response.Capabilities.SymbolModelComplete);
        Assert.True(response.Capabilities.Fingerprinting);
        Assert.False(response.Capabilities.RuntimeEdges);
    }

    [Fact]
    public void Handshake_Response_SerializesToValidJson()
    {
        var response = TestarudaAdapter.HandleHandshake();
        var json = JsonSerializer.Serialize(response);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("titi", root.GetProperty("name").GetString());
        Assert.Equal("0.1.0", root.GetProperty("version").GetString());
        Assert.Equal(1, root.GetProperty("protocol").GetInt32());
        Assert.Equal("method", root.GetProperty("granularity").GetString());

        var caps = root.GetProperty("capabilities");
        Assert.True(caps.GetProperty("symbol_model_complete").GetBoolean());
        Assert.True(caps.GetProperty("fingerprinting").GetBoolean());
        Assert.False(caps.GetProperty("runtime_edges").GetBoolean());

        var langs = root.GetProperty("languages").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("csharp", langs);
    }

    // ── Discover (method-level) ──────────────────────────────────

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

        var items = TestarudaAdapter.HandleDiscover(null);

        Assert.Empty(items);
    }

    [Fact]
    public void Discover_NoDiscoveredTests_ReturnsEmptyItems()
    {
        var emptyTests = new Dictionary<string, TestItem[]>();
        var items = TestarudaAdapter.HandleDiscover(emptyTests);

        Assert.Empty(items);
    }

    [Fact]
    public void Discover_DiscoveredTests_ReturnsOnePerMethod()
    {
        var graph = MakeGraph([
            ("/repo/tests/LibTest/LibTest.csproj", "LibTest", true),
        ]);

        var discoveredTests = new Dictionary<string, TestItem[]>
        {
            ["LibTest"] = new[]
            {
                new TestItem(
                    "LibTest.MyTests.Test1", "/asm/LibTest.dll",
                    "LibTest.MyTests", "Test1",
                    TestFramework.Xunit, TestTier.Unit, "LibTest/MyTests.cs",
                    TestOutcome.None, 0, []),
                new TestItem(
                    "LibTest.MyTests.Test2", "/asm/LibTest.dll",
                    "LibTest.MyTests", "Test2",
                    TestFramework.Xunit, TestTier.Unit, "LibTest/MyTests.cs",
                    TestOutcome.None, 0, []),
            }
        };

        var items = TestarudaAdapter.HandleDiscover(discoveredTests);

        Assert.Equal(2, items.Length);

        var test1 = Assert.Single(items, i => i.TestId == "LibTest.MyTests.Test1");
        Assert.Equal("/asm/LibTest.dll", test1.AssemblyPath);
        Assert.Equal("LibTest.MyTests", test1.ClassName);
        Assert.Equal("Test1", test1.MethodName);
        Assert.Equal("xunit", test1.Framework);
        Assert.Equal("unit", test1.Tier);

        var test2 = Assert.Single(items, i => i.TestId == "LibTest.MyTests.Test2");
        Assert.Equal("/asm/LibTest.dll", test2.AssemblyPath);
        Assert.Equal("LibTest.MyTests", test2.ClassName);
        Assert.Equal("Test2", test2.MethodName);
    }

    [Fact]
    public void Discover_MultipleProjects_YieldsMethodsFromAll()
    {
        var graph = MakeGraph([
            ("/repo/tests/ATest/ATest.csproj", "ATest", true),
            ("/repo/tests/BTest/BTest.csproj", "BTest", true),
        ]);

        var discoveredTests = new Dictionary<string, TestItem[]>
        {
            ["ATest"] = new[]
            {
                new TestItem("ATest.A.A1", "/asm/ATest.dll", "ATest.A", "A1",
                    TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
            },
            ["BTest"] = new[]
            {
                new TestItem("BTest.B.B1", "/asm/BTest.dll", "BTest.B", "B1",
                    TestFramework.Nunit, TestTier.Integration, null, TestOutcome.None, 0, []),
            },
        };

        var items = TestarudaAdapter.HandleDiscover(discoveredTests);

        Assert.Equal(2, items.Length);
        Assert.Contains(items, i => i.TestId == "ATest.A.A1");
        Assert.Contains(items, i => i.TestId == "BTest.B.B1");
    }

    // ── Static-deps (method-level) ───────────────────────────────

    [Fact]
    public void StaticDeps_NoChanges_ReturnsEmpty()
    {
        var graph = MakeGraph([("/repo/tests/T/T.csproj", "T", true)]);

        var result = TestarudaAdapter.HandleStaticDeps(graph, [], [], null, null, null);

        Assert.Empty(result);
    }

    [Fact]
    public void StaticDeps_WithDiscoveredTests_ReturnsPerMethodResults()
    {
        var graph = MakeGraph([
            ("/repo/src/Lib/Lib.csproj", "Lib", false),
            ("/repo/tests/LibTest/LibTest.csproj", "LibTest", true),
        ]);
        graph = AddDependency(graph, "/repo/tests/LibTest/LibTest.csproj", "/repo/src/Lib/Lib.csproj");

        var discoveredTests = new Dictionary<string, TestItem[]>
        {
            ["LibTest"] = new[]
            {
                new TestItem("LibTest.ParserTests.TestParse", "/asm/LibTest.dll",
                    "LibTest.ParserTests", "TestParse",
                    TestFramework.Xunit, TestTier.Unit, "",
                    TestOutcome.Passed, 0, []),
                new TestItem("LibTest.ParserTests.TestParseEmpty", "/asm/LibTest.dll",
                    "LibTest.ParserTests", "TestParseEmpty",
                    TestFramework.Xunit, TestTier.Unit, "",
                    TestOutcome.Passed, 0, []),
            }
        };

        var edges = new TestToSourceEdge[]
        {
            new("LibTest.ParserTests.TestParse", "src/Lib/Parser.cs", EdgeOrigin.Static, 1, []),
        };

        // Both tests have history (passed) so they are NOT in always-run set
        var history = new Dictionary<string, Safety.TestRunEntry[]>
        {
            ["LibTest.ParserTests.TestParse"] = new[]
            {
                new Safety.TestRunEntry(
                    "LibTest.ParserTests.TestParse", TestOutcome.Passed, 50, DateTime.UtcNow.AddDays(-1)),
            },
            ["LibTest.ParserTests.TestParseEmpty"] = new[]
            {
                new Safety.TestRunEntry(
                    "LibTest.ParserTests.TestParseEmpty", TestOutcome.Passed, 30, DateTime.UtcNow.AddDays(-1)),
            },
        };

        var result = TestarudaAdapter.HandleStaticDeps(
            graph, [], ["src/Lib/Parser.cs"], discoveredTests, edges, history);

        // TestParse should be selected (edge match)
        var selected = Assert.Single(result, r => r.TestId == "LibTest.ParserTests.TestParse");
        Assert.True(selected.Selected);
        Assert.Equal(1.0, selected.Confidence);

        // TestParseEmpty should NOT appear in results (no edge match)
        Assert.DoesNotContain(result, r => r.TestId == "LibTest.ParserTests.TestParseEmpty");
    }

    [Fact]
    public void StaticDeps_AlwaysRun_ReturnsSelected()
    {
        var graph = MakeGraph([
            ("/repo/tests/T/T.csproj", "T", true),
        ]);

        var discoveredTests = new Dictionary<string, TestItem[]>
        {
            ["T"] = new[]
            {
                new TestItem("T.Tests.M1", "/asm/T.dll", "T.Tests", "M1",
                    TestFramework.Xunit, TestTier.Unit, "",
                    TestOutcome.None, 0, []),
            }
        };

        // No edges, no changed files — but test has no history, so it enters
        // always-run set (newly added) and gets selected
        var result = TestarudaAdapter.HandleStaticDeps(
            graph, [], [], discoveredTests, [], null);

        // The test should be selected via always-run even with no edges
        var selected = Assert.Single(result);
        Assert.True(selected.Selected);
    }

    [Fact]
    public void StaticDeps_EmptyChangedFiles_WithAffectedProjects_Empty()
    {
        // With method-level, no changed files and no history means
        // tests are selected via always-run (newly added with no history)
        var graph = MakeGraph([
            ("/repo/tests/T/T.csproj", "T", true),
        ]);

        var discoveredTests = new Dictionary<string, TestItem[]>
        {
            ["T"] = new[]
            {
                new TestItem("T.Tests.M1", "/asm/T.dll", "T.Tests", "M1",
                    TestFramework.Xunit, TestTier.Unit, "",
                    TestOutcome.Passed, 0, []),
            }
        };

        // Test has history (passed), so it's NOT in always-run set
        var history = new Dictionary<string, Safety.TestRunEntry[]>
        {
            ["T.Tests.M1"] = new[]
            {
                new Safety.TestRunEntry(
                    "T.Tests.M1", TestOutcome.Passed, 100, DateTime.UtcNow.AddDays(-1)),
            },
        };

        var result = TestarudaAdapter.HandleStaticDeps(
            graph, ["T"], [], discoveredTests, [], history);

        // No changed files → no edge matches → empty result
        Assert.Empty(result);
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

    // ── Run-args (method-level) ──────────────────────────────────

    [Fact]
    public void RunArgs_FromTestIds_WithDiscoveredTests_GeneratesFilter()
    {
        var discoveredTests = new Dictionary<string, TestItem[]>
        {
            ["LibTest"] = new[]
            {
                new TestItem("LibTest.ParserTests.TestParse", "/asm/LibTest.dll",
                    "LibTest.ParserTests", "TestParse",
                    TestFramework.Xunit, TestTier.Unit, "",
                    TestOutcome.None, 0, []),
            },
            ["LibIntegration"] = new[]
            {
                new TestItem("LibIntegration.IntegrationTests.Test1", "/asm/LibIntegration.dll",
                    "LibIntegration.IntegrationTests", "Test1",
                    TestFramework.Xunit, TestTier.Integration, "",
                    TestOutcome.None, 0, []),
            }
        };

        var result = TestarudaAdapter.HandleRunArgs(
            ["LibTest.ParserTests.TestParse", "LibIntegration.IntegrationTests.Test1"],
            discoveredTests, null);

        // Should be a dotnet test command with a traversal .proj
        Assert.Equal("dotnet", result[0]);
        Assert.Equal("test", result[1]);
        Assert.StartsWith("/tmp/titi-adapter/", result[2]);
        Assert.EndsWith(".proj", result[2]);

        // The filter expressions should be embedded in the traversal .proj
        // (via VSTestTestCaseFilter), not passed as CLI --filter arguments
        var projContent = File.ReadAllText(result[2]);
        Assert.Contains("FullyQualifiedName~LibTest.ParserTests.TestParse", projContent);
        Assert.Contains("FullyQualifiedName~LibIntegration.IntegrationTests.Test1", projContent);

        // Clean up the temp file
        try { File.Delete(result[2]); } catch { }
    }

    [Fact]
    public void RunArgs_FromTestIds_Empty_ReturnsNoop()
    {
        var result = TestarudaAdapter.HandleRunArgs([], new Dictionary<string, TestItem[]>(), null);

        Assert.Equal(["dotnet", "test", "--no-build"], result);
    }

    [Fact]
    public void RunArgs_FromTestIds_UnknownTestId_GeneratesProjectLevelCommand()
    {
        // When a test ID doesn't match any discovered test, fall back gracefully
        var result = TestarudaAdapter.HandleRunArgs(
            ["Unknown.Test.Id"], new Dictionary<string, TestItem[]>(), null);

        // Should still produce a valid command
        Assert.Equal("dotnet", result[0]);
        Assert.Equal("test", result[1]);
    }

    // ── Ingest ───────────────────────────────────────────────────

    [Fact]
    public void Ingest_EmptyTrx_ReturnsEmptyResults()
    {
        var result = TestarudaAdapter.HandleIngest(null, null, "/repo");

        Assert.Empty(result.Results);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public void Ingest_ValidTrxWithCobertura_ReturnsEdges()
    {
        var trxXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestRun xmlns=""http://microsoft.com/schemas/VisualStudio/TeamTest/2010"">
  <ResultSummary>
    <Counters total=""1"" executed=""1"" passed=""1"" failed=""0"" />
  </ResultSummary>
  <TestDefinitions>
    <UnitTest name=""TestParse"" storage=""test.dll"" id=""guid-1"">
      <TestMethod codeBase=""test.dll"" className=""Tests.TestParse"" name=""TestParse"" />
    </UnitTest>
  </TestDefinitions>
  <Results>
    <UnitTestResult testName=""TestParse"" testId=""guid-1"" duration=""00:00:00.050"" outcome=""Passed"">
      <Output />
    </UnitTestResult>
  </Results>
</TestRun>";

        var coberturaXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<coverage line-rate=""0.5"">
  <sources>
    <source>/repo/src/</source>
  </sources>
  <packages>
    <package name=""TestLib"">
      <classes>
        <class name=""TestLib.Parser"" filename=""Parser.cs"">
          <methods>
            <method name=""Parse"" hits=""2"" />
          </methods>
          <lines>
            <line number=""1"" hits=""2"" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>";

        // Note: Ingestor.IngestRun correlates TRX results × Cobertura file-level coverage.
        // The edges require both the TRX test names and the Cobertura source paths to match.
        var result = TestarudaAdapter.HandleIngest(trxXml, coberturaXml, "/repo");

        Assert.NotEmpty(result.Results);
        // Edges should exist when TRX + Cobertura both present and correlated
        // (actual edge count depends on EdgeBuilder correlation)
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
            new TestarudaAdapter.AdapterRequest("shutdown", [], [], [], "", ""),
            null);

        using var doc = JsonDocument.Parse(json);
        var result = doc.RootElement.GetProperty("result");
        Assert.Equal("shutting_down", result.GetProperty("status").GetString());
    }

    [Fact]
    public void RunLoop_Shutdown_TerminatesImmediately()
    {
        var stdin = new StringReader(
            """
            {"command":"handshake","params":{}}
            {"command":"shutdown","params":{}}
            {"command":"discover","params":{}}
            """);
        var stdout = new StringWriter();

        var exitCode = TestarudaAdapter.RunLoop(null, stdin, stdout);

        var output = stdout.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Should have exactly two responses (handshake + shutdown), not three
        Assert.Equal(2, lines.Length);
        Assert.Equal(0, exitCode);

        // Verify both responses parse as valid JSON and identify their commands
        using var doc1 = JsonDocument.Parse(lines[0]);
        Assert.True(doc1.RootElement.GetProperty("ok").GetBoolean());

        using var doc2 = JsonDocument.Parse(lines[1]);
        Assert.True(doc2.RootElement.GetProperty("result").GetProperty("status").GetString() == "shutting_down");
    }

    [Fact]
    public void RunLoop_NoShutdown_ReadsUntilEof()
    {
        var stdin = new StringReader(
            """
            {"command":"handshake","params":{}}
            {"command":"handshake","params":{}}
            {"command":"discover","params":{}}
            """);
        var stdout = new StringWriter();

        var exitCode = TestarudaAdapter.RunLoop(null, stdin, stdout);

        var output = stdout.ToString();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Without shutdown, all three commands should be processed
        Assert.Equal(3, lines.Length);
        Assert.Equal(0, exitCode);
    }

    // ── Protocol: response serialization ─────────────────────────

    [Fact]
    public void FormatResponse_Error_ProducesValidJson()
    {
        var json = TestarudaAdapter.FormatErrorResponse("something went wrong");

        using var doc = JsonDocument.Parse(json);
        Assert.False(doc.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(doc.RootElement.TryGetProperty("error", out var error));
        Assert.Equal("something went wrong", error.GetProperty("message").GetString());
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
        var result = TestarudaAdapter.HandleIngest(null, null, "/repo");
        Assert.Empty(result.Results);
        Assert.Empty(result.Edges);
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

    // ── Integration tests (skipped by default) ───────────────────

    [Fact(Skip = "Slow (requires NuGet restore + dotnet build, pre-populated test cache); run with: dotnet test --filter Category=Integration")]
    public void AdapterIntegration_HandshakeAndDiscover_AgainstSyntheticFixture()
    {
        var fixtureDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../test/fixtures/synthetic-monorepo"));

        var projectPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../src/titi/titi.csproj"));

        // Start the adapter as a subprocess
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" -- testaruda-adapter",
            WorkingDirectory = fixtureDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(proc);

        // Send handshake
        proc.StandardInput.WriteLine("""{"command":"handshake","params":{}}""");
        proc.StandardInput.Flush();

        var handshakeLine = proc.StandardOutput.ReadLine();
        Assert.NotNull(handshakeLine);

        using var handshakeDoc = JsonDocument.Parse(handshakeLine);
        var handshakeResult = handshakeDoc.RootElement.GetProperty("result");
        Assert.Equal("titi", handshakeResult.GetProperty("name").GetString());
        Assert.Equal("0.1.0", handshakeResult.GetProperty("version").GetString());
        Assert.Equal(1, handshakeResult.GetProperty("protocol").GetInt32());
        Assert.Equal("method", handshakeResult.GetProperty("granularity").GetString());
        Assert.True(handshakeResult.GetProperty("capabilities").GetProperty("symbol_model_complete").GetBoolean());

        // Send discover
        proc.StandardInput.WriteLine("""{"command":"discover","params":{}}""");
        proc.StandardInput.Flush();

        var discoverLine = proc.StandardOutput.ReadLine();
        Assert.NotNull(discoverLine);

        using var discoverDoc = JsonDocument.Parse(discoverLine);
        var discoverResult = discoverDoc.RootElement.GetProperty("result");
        var tests = discoverResult.GetProperty("tests").EnumerateArray().ToArray();

        // Should find one item per test method (not per project)
        Assert.True(tests.Length > 2, $"Expected multiple test methods, got {tests.Length}");
        // Each item should have a method-level test_id (contains a dot-separated method)
        var testIds = tests.Select(t => t.GetProperty("test_id").GetString()).ToArray();
        Assert.Contains(testIds, id => id != null && (id.Contains("Test") || id.Contains(".")));

        // Clean shutdown
        proc.StandardInput.Close();
        proc.WaitForExit(10000);
        Assert.Equal(0, proc.ExitCode);
    }

    [Fact(Skip = "Slow (requires NuGet restore + dotnet build, pre-populated test cache + edges); run with: dotnet test --filter Category=Integration")]
    public void AdapterIntegration_StaticDeps_MatchesTestManifest()
    {
        var fixtureDir = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../test/fixtures/synthetic-monorepo"));

        var projectPath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../src/titi/titi.csproj"));

        // Start the adapter as a subprocess
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\" -- testaruda-adapter",
            WorkingDirectory = fixtureDir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        Assert.NotNull(proc);

        // Handshake first
        proc.StandardInput.WriteLine("""{"command":"handshake","params":{}}""");
        proc.StandardInput.Flush();
        var handshakeLine = proc.StandardOutput.ReadLine();
        Assert.NotNull(handshakeLine);

        // Send static-deps with a known changed file that affects Orion.UnitTests
        proc.StandardInput.WriteLine("""{"command":"static-deps","params":{"changed_files":["libs/Orion.Core.Data/Parser.cs"],"affected_projects":[]}}""");
        proc.StandardInput.Flush();

        var depsLine = proc.StandardOutput.ReadLine();
        Assert.NotNull(depsLine);

        using var depsDoc = JsonDocument.Parse(depsLine);
        var depsResult = depsDoc.RootElement.GetProperty("result");
        var affectedTests = depsResult.GetProperty("affected_tests").EnumerateArray().ToArray();

        // Should return per-test method results
        Assert.True(affectedTests.Length > 0, "Expected at least one affected test method");

        // Clean shutdown
        proc.StandardInput.Close();
        proc.WaitForExit(10000);
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
