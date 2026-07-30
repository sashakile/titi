// TID-12: testaruda adapter subcommand (CLI-19)
// Phase 2: method-level granularity, symbol_model_complete: true
//
// JSON-over-stdio adapter protocol. One JSON object per line on both stdin
// (requests) and stdout (responses). The adapter builds or loads the
// MonorepoGraph once during handshake and answers all commands from
// in-memory state. Test items are pre-discovered during startup or passed
// by the caller for method-level granularity.

namespace titi.Adapter;

using System.Text.Json;
using System.Text.Json.Serialization;
using titi.Affected;
using titi.Safety;
using titi.TestManifest;
using titi.TestDiscovery;
using titi.Core;

/// <summary>
/// Handles the testaruda adapter protocol: handshake, discover, static-deps,
/// fingerprint, run-args, and ingest commands.
/// </summary>
public static class TestarudaAdapter
{
    // ── Protocol types ───────────────────────────────────────────

    /// <summary>Handshake capabilities sub-object.</summary>
    public record CapabilitiesResponse(
        [property: JsonPropertyName("symbol_model_complete")] bool SymbolModelComplete,
        [property: JsonPropertyName("fingerprinting")] bool Fingerprinting,
        [property: JsonPropertyName("runtime_edges")] bool RuntimeEdges
    );

    /// <summary>Handshake response payload.</summary>
    public record HandshakeResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("protocol")] int Protocol,
        [property: JsonPropertyName("languages")] string[] Languages,
        [property: JsonPropertyName("granularity")] string Granularity,
        [property: JsonPropertyName("capabilities")] CapabilitiesResponse Capabilities
    );

    /// <summary>An adapter test item (method-level granularity in Phase 2).</summary>
    public record AdapterTestItem(
        [property: JsonPropertyName("test_id")] string TestId,
        [property: JsonPropertyName("assembly_path")] string AssemblyPath,
        [property: JsonPropertyName("class_name")] string ClassName,
        [property: JsonPropertyName("method_name")] string MethodName,
        [property: JsonPropertyName("framework")] string Framework,
        [property: JsonPropertyName("tier")] string Tier
    )
    {
        public AdapterTestItem(string testId, string assemblyPath, string className, string methodName,
            TestFramework framework, TestTier tier)
            : this(testId, assemblyPath, className, methodName,
                  framework.ToString().ToLower(), tier.ToString().ToLower())
        { }
    }

    /// <summary>A static-deps result item.</summary>
    public record StaticDepsItem(
        [property: JsonPropertyName("test_id")] string TestId,
        [property: JsonPropertyName("selected")] bool Selected,
        [property: JsonPropertyName("confidence")] double Confidence
    );

    /// <summary>An ingest result with both test results and correlation edges.</summary>
    public record MethodIngestResult(
        Coverage.TrxTestResult[] Results,
        TestToSourceEdge[] Edges
    );

    // ── Request types ────────────────────────────────────────────

    /// <summary>Parsed adapter request from testaruda.</summary>
    public record AdapterRequest(
        string Command,
        string[] ChangedFiles,
        string[] AffectedProjects,
        string[] TestIds,
        string TrxPath,
        string CoberturaPath
    );

    // ── Handshake ────────────────────────────────────────────────

    /// <summary>Produce the handshake response (method-level).</summary>
    public static HandshakeResponse HandleHandshake()
    {
        return new HandshakeResponse(
            Name: "titi",
            Version: "0.1.0",
            Protocol: 1,
            Languages: ["csharp"],
            Granularity: "method",
            Capabilities: new CapabilitiesResponse(
                SymbolModelComplete: true,
                Fingerprinting: true,
                RuntimeEdges: false
            )
        );
    }

    // ── Discover (method-level) ──────────────────────────────────

    /// <summary>
    /// Emit one test item per method from pre-discovered test items.
    /// When <paramref name="discoveredTests"/> is null or empty, returns empty
    /// (no test items discovered yet). Tests are keyed by their owning
    /// project's PackageId.
    /// </summary>
    public static AdapterTestItem[] HandleDiscover(
        Dictionary<string, TestItem[]>? discoveredTests)
    {
        if (discoveredTests == null || discoveredTests.Count == 0)
            return [];

        var items = new List<AdapterTestItem>();

        foreach (var (pkgId, tests) in discoveredTests)
        {
            foreach (var test in tests)
            {
                items.Add(new AdapterTestItem(
                    testId: test.TestId,
                    assemblyPath: test.AssemblyPath,
                    className: test.ClassName,
                    methodName: test.MethodName,
                    framework: test.Framework,
                    tier: test.Tier
                ));
            }
        }

        return items.ToArray();
    }

    // ── Static-deps (method-level) ───────────────────────────────

    /// <summary>
    /// Return per-method selection results using pre-discovered test items,
    /// edges, and changed files. Only test items belonging to the requested
    /// <paramref name="affectedProjects"/> are considered for selection.
    /// When test-item data is missing, falls back to project-level empty result
    /// (no method-level analysis possible).
    /// </summary>
    public static StaticDepsItem[] HandleStaticDeps(
        MonorepoGraph graph,
        string[] affectedProjects,
        string[] changedFiles,
        Dictionary<string, TestItem[]>? discoveredTests,
        TestToSourceEdge[]? edges,
        Dictionary<string, Safety.TestRunEntry[]>? history)
    {
        if (discoveredTests == null || discoveredTests.Count == 0)
            return [];

        // Filter to only the requested affected projects.
        // When affectedProjects is empty (caller didn't specify), use all
        // discovered projects for backward compatibility.
        Dictionary<string, TestItem[]> scopedTests;
        if (affectedProjects.Length > 0)
        {
            var affectedSet = new HashSet<string>(affectedProjects, StringComparer.Ordinal);
            scopedTests = new Dictionary<string, TestItem[]>(StringComparer.Ordinal);
            foreach (var (pkgId, items) in discoveredTests)
            {
                if (affectedSet.Contains(pkgId))
                    scopedTests[pkgId] = items;
            }
        }
        else
        {
            scopedTests = discoveredTests;
        }

        if (scopedTests.Count == 0)
            return [];

        // Flatten all test items from scoped projects
        var allItems = scopedTests.Values
            .SelectMany(t => t)
            .ToArray();

        if (allItems.Length == 0)
            return [];

        // Build always-run set from history (flatten to latest entry per test)
        Dictionary<string, Safety.TestRunEntry> flattenedHistory = new();
        if (history != null)
        {
            foreach (var (testId, entries) in history)
            {
                if (entries.Length > 0)
                    flattenedHistory[testId] = entries.OrderByDescending(e => e.Timestamp).First();
            }
        }
        var alwaysRun = Selection.ComputeAlwaysRunSet(allItems, flattenedHistory);

        // Compute selected tests using edges and changed files
        var selectedTests = Selection.ComputeSelectedTests(
            allItems,
            edges ?? [],
            alwaysRun,
            changedFiles);

        // Map selected tests to StaticDepsItem with method-level granularity
        return selectedTests
            .Where(s => s.Selected)
            .Select(s => new StaticDepsItem(
                TestId: s.TestId,
                Selected: s.Selected,
                Confidence: s.Confidence
            ))
            .ToArray();
    }

    // ── Fingerprint ──────────────────────────────────────────────

    /// <summary>Return the graph's fingerprint data verbatim.</summary>
    public static Dictionary<string, string> HandleFingerprint(MonorepoGraph graph)
    {
        return new Dictionary<string, string>(graph.Fingerprints);
    }

    // ── Run-args (method-level) ──────────────────────────────────

    /// <summary>
    /// Generate a dotnet test command with per-test --filter expressions.
    /// Takes test IDs, the full set of discovered test items, and a map of
    /// packageId → ProjectDescriptor from the graph. Groups selected tests by
    /// project and produces a traversal .proj with per-project
    /// VSTestTestCaseFilter.
    /// </summary>
    public static string[] HandleRunArgs(
        string[] testIds,
        Dictionary<string, TestItem[]> discoveredTests,
        Dictionary<string, ProjectDescriptor>? projectMap)
    {
        if (testIds.Length == 0)
            return ["dotnet", "test", "--no-build"];

        // Build reverse maps: testId → TestItem, testId → PackageId
        var testIdToItem = new Dictionary<string, TestItem>(StringComparer.Ordinal);
        var pkgByTestId = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (pkgId, tests) in discoveredTests)
        {
            foreach (var test in tests)
            {
                testIdToItem[test.TestId] = test;
                pkgByTestId[test.TestId] = pkgId;
            }
        }

        // Group selected test IDs by project
        var projectTestItems = new Dictionary<string, List<TestItem>>(StringComparer.Ordinal);
        foreach (var testId in testIds)
        {
            if (testIdToItem.TryGetValue(testId, out var item) &&
                pkgByTestId.TryGetValue(testId, out var pkgId))
            {
                if (!projectTestItems.ContainsKey(pkgId))
                    projectTestItems[pkgId] = new List<TestItem>();
                projectTestItems[pkgId].Add(item);
            }
        }

        if (projectTestItems.Count == 0)
            return ["dotnet", "test"];

        // Build per-project filters and resolve real ProjectDescriptor from map
        var projectFilters = new Dictionary<string, string>(StringComparer.Ordinal);
        var projects = new List<ProjectDescriptor>();

        foreach (var (pkgId, items) in projectTestItems)
        {
            var framework = FilterExprBuilder.GetCommonFramework(items.ToArray());
            if (framework == null)
                continue;

            var filterExpr = FilterExprBuilder.BuildFilter(items.ToArray(), framework.Value);
            if (filterExpr != null)
                projectFilters[pkgId] = filterExpr;

            // Resolve the actual ProjectDescriptor from the project map
            // (which contains real paths for the traversal .proj Include attribute)
            ProjectDescriptor project;
            if (projectMap != null && projectMap.TryGetValue(pkgId, out var mapped))
            {
                project = mapped;
            }
            else
            {
                // Fallback: create a minimal descriptor (path may be invalid)
                project = new ProjectDescriptor(
                    pkgId, pkgId,
                    new SemanticVersion(1, 0, 0, null, null),
                    [], false, true, [], [], new());
            }
            projects.Add(project);
        }

        if (projects.Count == 0)
            return ["dotnet", "test"];

        // Write traversal .proj and return command
        var tempDir = Path.Combine(Path.GetTempPath(), "titi-adapter");
        Directory.CreateDirectory(tempDir);
        var traversalPath = Path.Combine(tempDir, $"test-manifest-{Guid.NewGuid():N}.proj");

        var xml = TraversalGenerator.Generate(projects.ToArray(),
            projectFilters.Count > 0 ? projectFilters : null);
        File.WriteAllText(traversalPath, xml);

        return ["dotnet", "test", traversalPath];
    }

    // ── Ingest (method-level, with edge building) ────────────────

    /// <summary>
    /// Parse TRX results and optionally correlate with Cobertura coverage
    /// to build TestToSourceEdge records. This is the method-level equivalent:
    /// it delegates to Ingestor.IngestRun for full correlation.
    /// </summary>
    public static MethodIngestResult HandleIngest(
        string? trxXml,
        string? coberturaXml,
        string sourceRoot)
    {
        if (string.IsNullOrEmpty(trxXml))
            return new MethodIngestResult([], []);

        var ingestResult = Ingestor.IngestRun(
            trxXml, coberturaXml, sourceRoot);

        return new MethodIngestResult(
            ingestResult.Results,
            ingestResult.Edges);
    }

    // ── Request parsing ──────────────────────────────────────────

    /// <summary>Parse a JSON request line from testaruda.</summary>
    public static AdapterRequest? ParseRequest(string jsonLine)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonLine);
            var root = doc.RootElement;

            var command = root.GetProperty("command").GetString() ?? "";
            var changedFiles = Array.Empty<string>();
            var affectedProjects = Array.Empty<string>();
            var testIds = Array.Empty<string>();
            var trxPath = "";
            var coberturaPath = "";

            // Parse params if present (params-style protocol, matching all adapters)
            if (root.TryGetProperty("params", out var paramsEl))
            {
                if (paramsEl.TryGetProperty("changed_files", out var cf))
                    changedFiles = cf.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

                if (paramsEl.TryGetProperty("affected_projects", out var ap))
                    affectedProjects = ap.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

                if (paramsEl.TryGetProperty("test_ids", out var ti))
                    testIds = ti.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

                if (paramsEl.TryGetProperty("trx_path", out var tp))
                    trxPath = tp.GetString() ?? "";

                if (paramsEl.TryGetProperty("cobertura_path", out var cp))
                    coberturaPath = cp.GetString() ?? "";
            }

            return new AdapterRequest(command, changedFiles, affectedProjects, testIds, trxPath, coberturaPath);
        }
        catch
        {
            return null;
        }
    }

    // ── Response serialization ───────────────────────────────────

    /// <summary>Format an error response JSON.</summary>
    public static string FormatErrorResponse(string message)
    {
        return JsonSerializer.Serialize(new { ok = false, error = new { message } });
    }

    /// <summary>Format a handshake response as JSON.</summary>
    public static string FormatResponse(HandshakeResponse response)
    {
        return JsonSerializer.Serialize(new { ok = true, result = response });
    }

    /// <summary>Format discover items as JSON using the canonical testaruda adapter protocol format.
    /// Maps titi's internal fields to: node_id (test_id), suite_kind (ClassName.MethodName), file (assembly_path).
    /// result is a direct array, not wrapped in {tests: [...]}.</summary>
    public static string FormatResponse(AdapterTestItem[] items)
    {
        var canonical = items.Select(item => new
        {
            node_id = item.TestId,
            suite_kind = $"{item.ClassName ?? "?"}.{item.MethodName ?? "?"}",
            file = item.AssemblyPath
        }).ToArray();
        return JsonSerializer.Serialize(new { ok = true, result = canonical });
    }

    /// <summary>Format static-deps items as JSON.</summary>
    public static string FormatResponse(StaticDepsItem[] items)
    {
        return JsonSerializer.Serialize(new { ok = true, result = new { affected_tests = items } });
    }

    /// <summary>Format fingerprint data as JSON.</summary>
    public static string FormatResponse(Dictionary<string, string> fingerprints)
    {
        return JsonSerializer.Serialize(new { ok = true, result = new { fingerprints } });
    }

    /// <summary>Format run-args as JSON.</summary>
    public static string FormatResponse(string[] args)
    {
        return JsonSerializer.Serialize(new { ok = true, result = new { command = args } });
    }

    /// <summary>Format ingest results as JSON.</summary>
    public static string FormatResponse(MethodIngestResult results)
    {
        return JsonSerializer.Serialize(new { ok = true, result = new
        {
            results = results.Results,
            edges = results.Edges
        } });
    }

    // ── Main loop ────────────────────────────────────────────────

    /// <summary>
    /// Run the adapter main loop: read JSON commands from stdin, process them,
    /// write JSON responses to stdout. Pre-discovers test items for all test
    /// projects during startup when cache and graph are available.
    /// </summary>
    public static int RunLoop(MonorepoGraph? graph, TextReader stdin, TextWriter stdout)
    {
        // In Phase 2, we attempt to pre-discover test items from the graph's
        // test projects during startup. This happens once, before the loop.
        // When test-item cache doesn't exist yet, discoveredTests will be empty
        // and the adapter falls back gracefully (returns empty lists).
        var discoveredTests = DiscoverAllTestItems(graph);

        string? line;
        while ((line = stdin.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var request = ParseRequest(line);
            if (request == null)
            {
                stdout.WriteLine(FormatErrorResponse("Malformed request"));
                stdout.Flush();
                continue;
            }

            var responseJson = ProcessCommand(request, graph, discoveredTests, stdout);
            stdout.WriteLine(responseJson);
            stdout.Flush();

            // Terminate the loop immediately after shutdown — don't read further commands
            if (request.Command == "shutdown")
                break;
        }

        return 0;
    }

    /// <summary>Process a single adapter command and return the response JSON.</summary>
    public static string ProcessCommand(
        AdapterRequest request,
        MonorepoGraph? graph,
        Dictionary<string, TestItem[]>? discoveredTests = null,
        TextWriter? stderr = null)
    {
        switch (request.Command)
        {
            case "handshake":
                return FormatResponse(HandleHandshake());

            case "discover":
                if (graph == null)
                    return FormatErrorResponse("Graph not available (handshake did not complete)");
                return FormatResponse(HandleDiscover(discoveredTests));

            case "static-deps":
                if (graph == null)
                    return FormatErrorResponse("Graph not available (handshake did not complete)");
                var edges = LoadEdgesFromCache(graph.RepoRoot);
                // Fall back to static analysis when no coverage edges exist (TID-0ej).
                // Static edges are computed from project references and using-statements,
                // then persisted to the cache for subsequent fast loads.
                if (edges.Length == 0)
                {
                    edges = LoadStaticEdges(graph, discoveredTests);
                }
                var history = LoadHistoryFromCache(graph.RepoRoot);
                return FormatResponse(HandleStaticDeps(
                    graph, request.AffectedProjects, request.ChangedFiles,
                    discoveredTests, edges, history));

            case "fingerprint":
                if (graph == null)
                    return FormatErrorResponse("Graph not available (handshake did not complete)");
                return FormatResponse(HandleFingerprint(graph));

            case "run-args":
                var projectMap = BuildProjectMap(graph);
                return FormatResponse(HandleRunArgs(request.TestIds, discoveredTests ?? new(), projectMap));

            case "ingest":
                var coberturaXml = !string.IsNullOrEmpty(request.CoberturaPath) && File.Exists(request.CoberturaPath)
                    ? File.ReadAllText(request.CoberturaPath)
                    : null;
                var trxXml = !string.IsNullOrEmpty(request.TrxPath) && File.Exists(request.TrxPath)
                    ? File.ReadAllText(request.TrxPath)
                    : null;
                var sourceRoot = graph?.RepoRoot ?? Environment.CurrentDirectory;
                return FormatResponse(HandleIngest(trxXml, coberturaXml, sourceRoot));

            case "shutdown":
                return JsonSerializer.Serialize(new { ok = true, result = new { status = "shutting_down" } });

            default:
                return FormatErrorResponse($"Unknown command: {request.Command}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Discover all test items from test projects in the graph.
    /// Uses DiscoveryCache to load or run discovery. When the cache doesn't
    /// exist or is stale, runs dotnet test --list-tests to discover test items.
    /// Failures are handled gracefully — problematic projects get empty results.
    /// </summary>
    static Dictionary<string, TestItem[]> DiscoverAllTestItems(MonorepoGraph? graph)
    {
        if (graph == null)
            return new();

        var cacheDir = Path.Combine(graph.RepoRoot, ".titi", "test-cache");
        var result = new Dictionary<string, TestItem[]>(StringComparer.Ordinal);

        var testProjects = graph.Nodes.Values
            .Where(n => n.Project.IsTestProject)
            .ToArray();

        foreach (var node in testProjects)
        {
            var proj = node.Project;
            var projDir = Path.GetDirectoryName(proj.Path) ?? "";
            var fingerprint = DiscoveryCache.ComputeFingerprint(projDir, proj.Path);
            var items = DiscoveryCache.GetOrDiscover(cacheDir, proj.PackageId, fingerprint, () =>
            {
                // Run dotnet test --list-tests to discover test items.
                // This is the same logic used by `titi affected` in Core.cs.
                var (stdout, stderr, ok) = Program.RunDotnetListTests(proj.Path, graph.RepoRoot);
                if (!ok)
                {
                    Console.Error.WriteLine($"  warning: list-tests failed for {proj.PackageId}: {stderr.Split('\n').FirstOrDefault()}");
                    return [];
                }
                return TestDiscovery.Parser.Parse(stdout, TestTier.Unit);
            });
            if (items.Length > 0)
                result[proj.PackageId] = items;
        }

        return result;
    }

    /// <summary>Load edges from the test-cache, relative to the repo root.</summary>
    static TestToSourceEdge[] LoadEdgesFromCache(string repoRoot)
    {
        var cacheDir = Path.Combine(repoRoot, ".titi", "test-cache");
        return SelectionLoader.LoadEdges(cacheDir);
    }

    /// <summary>Load or compute static edges as fallback.</summary>
    static TestToSourceEdge[] LoadStaticEdges(
        MonorepoGraph graph,
        Dictionary<string, TestItem[]>? discoveredTests)
    {
        var cacheDir = Path.Combine(graph.RepoRoot, ".titi", "test-cache");

        // Try loading persisted static edges first
        var persisted = StaticEdgeAnalyzer.LoadPersistedStaticEdges(cacheDir);
        if (persisted.Length > 0)
            return persisted;

        // Compute fresh static edges and persist them
        var fresh = StaticEdgeAnalyzer.AnalyzeAll(graph, discoveredTests);
        if (fresh.Length > 0)
            StaticEdgeAnalyzer.PersistStaticEdges(cacheDir, fresh);

        return fresh;
    }

    /// <summary>Load test-run history from the test-cache.</summary>
    static Dictionary<string, Safety.TestRunEntry[]> LoadHistoryFromCache(string repoRoot)
    {
        var cacheDir = Path.Combine(repoRoot, ".titi", "test-cache");
        return SelectionLoader.LoadHistory(cacheDir);
    }

    /// <summary>Build a packageId → ProjectDescriptor map from the graph.</summary>
    static Dictionary<string, ProjectDescriptor> BuildProjectMap(MonorepoGraph? graph)
    {
        if (graph == null)
            return new();

        return graph.Nodes.Values
            .Select(n => n.Project)
            .ToDictionary(p => p.PackageId, p => p, StringComparer.Ordinal);
    }


}
