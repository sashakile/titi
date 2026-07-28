// TID-12: testaruda adapter subcommand (CLI-19)
// Phase 1: project-level granularity, symbol_model_complete: false
//
// JSON-over-stdio adapter protocol. One JSON object per line on both stdin
// (requests) and stdout (responses). The adapter builds or loads the
// MonorepoGraph once during handshake and answers all commands from
// in-memory state.

namespace titi.Adapter;

using System.Text.Json;
using System.Text.Json.Serialization;
using titi.Affected;

/// <summary>
/// Handles the testaruda adapter protocol: handshake, discover, static-deps,
/// fingerprint, run-args, and ingest commands.
/// </summary>
public static class TestarudaAdapter
{
    // ── Protocol types ───────────────────────────────────────────

    /// <summary>Handshake response payload.</summary>
    public record HandshakeResponse(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("languages")] string[] Languages,
        [property: JsonPropertyName("granularity")] string Granularity,
        [property: JsonPropertyName("symbol_model_complete")] bool SymbolModelComplete,
        [property: JsonPropertyName("runtime_edges")] bool RuntimeEdges
    );

    /// <summary>An adapter test item (project-level granularity in Phase 1).</summary>
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

    /// <summary>An ingest result item.</summary>
    public record IngestResult(
        [property: JsonPropertyName("test_name")] string TestName,
        [property: JsonPropertyName("outcome")] string Outcome,
        [property: JsonPropertyName("duration_ms")] long DurationMs
    );

    // ── Request types ────────────────────────────────────────────

    /// <summary>Parsed adapter request from testaruda.</summary>
    public record AdapterRequest(
        string Command,
        string[] ChangedFiles,
        string[] AffectedProjects,
        string[] TestIds,
        string TrxPath
    );

    // ── Handshake ────────────────────────────────────────────────

    /// <summary>Produce the handshake response.</summary>
    public static HandshakeResponse HandleHandshake()
    {
        return new HandshakeResponse(
            Name: "titi",
            Languages: ["csharp"],
            Granularity: "project",
            SymbolModelComplete: false,
            RuntimeEdges: false
        );
    }

    // ── Discover ─────────────────────────────────────────────────

    /// <summary>Emit one test item per test project in the graph.</summary>
    public static AdapterTestItem[] HandleDiscover(MonorepoGraph graph)
    {
        return graph.Nodes.Values
            .Where(n => n.Project.IsTestProject)
            .Select(n => new AdapterTestItem(
                testId: n.Project.PackageId,
                assemblyPath: n.Project.Path,
                className: n.Project.PackageId,
                methodName: "all",
                framework: TestFramework.Xunit,  // default for project-level
                tier: TestTier.Unit              // default tier
            ))
            .ToArray();
    }

    // ── Static-deps ──────────────────────────────────────────────

    /// <summary>
    /// Return affected test items. Uses titi's AffectedSet computation.
    /// In Phase 1 (project-level), each test item is a whole project.
    /// </summary>
    public static StaticDepsItem[] HandleStaticDeps(
        MonorepoGraph graph, string[] affectedProjects, string[] changedFiles)
    {
        // Build a minimal AffectedSet from the pre-computed affected projects
        // and the changed files list.
        var directlyAffected = new List<ProjectDescriptor>();
        var transitivelyAffected = new List<ProjectDescriptor>();

        foreach (var pkgId in affectedProjects)
        {
            var node = graph.Nodes.Values
                .FirstOrDefault(n => n.Project.PackageId == pkgId);
            if (node != null)
            {
                directlyAffected.Add(node.Project);
            }
        }

        // If no affected projects provided, use the full affected set computation
        if (directlyAffected.Count == 0 && changedFiles.Length > 0)
        {
            var affectedSet = Analyzer.BuildAffectedSet(changedFiles, graph);
            directlyAffected.AddRange(affectedSet.DirectlyAffected);
            transitivelyAffected.AddRange(affectedSet.TransitivelyAffected);
        }

        var allAffected = directlyAffected
            .Concat(transitivelyAffected)
            .Where(p => p.IsTestProject)
            .DistinctBy(p => p.PackageId)
            .ToArray();

        if (allAffected.Length == 0)
            return [];

        return allAffected.Select(p => new StaticDepsItem(
            TestId: p.PackageId,
            Selected: true,
            Confidence: 1.0  // project-level, full confidence
        )).ToArray();
    }

    // ── Fingerprint ──────────────────────────────────────────────

    /// <summary>Return the graph's fingerprint data verbatim.</summary>
    public static Dictionary<string, string> HandleFingerprint(MonorepoGraph graph)
    {
        return new Dictionary<string, string>(graph.Fingerprints);
    }

    // ── Run-args ─────────────────────────────────────────────────

    /// <summary>
    /// Generate a dotnet test command for the given test projects.
    /// When filters are provided, generates a Traversal .proj with VSTestTestCaseFilter.
    /// </summary>
    public static string[] HandleRunArgs(
        ProjectDescriptor[] projects,
        Dictionary<string, string>? projectFilters)
    {
        if (projects.Length == 0)
            return ["dotnet", "test", "--no-build"];

        // Write the traversal to a unique temp path per invocation
        var tempDir = Path.Combine(Path.GetTempPath(), "titi-adapter");
        Directory.CreateDirectory(tempDir);
        var traversalPath = Path.Combine(tempDir, $"test-manifest-{Guid.NewGuid():N}.proj");

        var xml = TestManifest.TraversalGenerator.Generate(projects, projectFilters);
        File.WriteAllText(traversalPath, xml);

        if (projectFilters != null && projectFilters.Count > 0)
        {
            // With per-project filters, pass them via --filter
            var allFilters = string.Join(" | ",
                projectFilters.Values.Where(v => !string.IsNullOrEmpty(v)));
            return ["dotnet", "test", traversalPath, "--filter", $"\"{allFilters}\""];
        }

        return ["dotnet", "test", traversalPath];
    }

    // ── Ingest ───────────────────────────────────────────────────

    /// <summary>Parse a TRX file and return per-test results.</summary>
    public static IngestResult[] HandleIngest(string? trxPath)
    {
        if (trxPath == null || !File.Exists(trxPath))
            return [];

        try
        {
            var trxXml = File.ReadAllText(trxPath);
            var results = Coverage.Parser.ParseTrx(trxXml);

            return results.Select(r => new IngestResult(
                TestName: r.TestName,
                Outcome: r.Outcome.ToString().ToLower(),
                DurationMs: r.DurationMs
            )).ToArray();
        }
        catch
        {
            return [];
        }
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
            }

            return new AdapterRequest(command, changedFiles, affectedProjects, testIds, trxPath);
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
        return JsonSerializer.Serialize(new { error = message });
    }

    /// <summary>Format a handshake response as JSON.</summary>
    public static string FormatResponse(HandshakeResponse response)
    {
        return JsonSerializer.Serialize(new { result = response });
    }

    /// <summary>Format discover items as JSON.</summary>
    public static string FormatResponse(AdapterTestItem[] items)
    {
        return JsonSerializer.Serialize(new { result = new { tests = items } });
    }

    /// <summary>Format static-deps items as JSON.</summary>
    public static string FormatResponse(StaticDepsItem[] items)
    {
        return JsonSerializer.Serialize(new { result = new { affected_tests = items } });
    }

    /// <summary>Format fingerprint data as JSON.</summary>
    public static string FormatResponse(Dictionary<string, string> fingerprints)
    {
        return JsonSerializer.Serialize(new { result = new { fingerprints } });
    }

    /// <summary>Format run-args as JSON.</summary>
    public static string FormatResponse(string[] args)
    {
        return JsonSerializer.Serialize(new { result = new { command = args } });
    }

    /// <summary>Format ingest results as JSON.</summary>
    public static string FormatResponse(IngestResult[] results)
    {
        return JsonSerializer.Serialize(new { result = new { results } });
    }

    // ── Main loop ────────────────────────────────────────────────

    /// <summary>
    /// Run the adapter main loop: read JSON commands from stdin, process them,
    /// write JSON responses to stdout. This is the entry point for the
    /// titi testaruda-adapter subcommand.
    /// </summary>
    public static int RunLoop(MonorepoGraph? graph, TextReader stdin, TextWriter stdout)
    {
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

            var responseJson = ProcessCommand(request, graph, stdout);
            stdout.WriteLine(responseJson);
            stdout.Flush();
        }

        return 0;
    }

    /// <summary>Process a single adapter command and return the response JSON.</summary>
    public static string ProcessCommand(AdapterRequest request, MonorepoGraph? graph, TextWriter? stderr = null)
    {
        switch (request.Command)
        {
            case "handshake":
                return FormatResponse(HandleHandshake());

            case "discover":
                if (graph == null)
                    return FormatErrorResponse("Graph not available (handshake did not complete)");
                return FormatResponse(HandleDiscover(graph));

            case "static-deps":
                if (graph == null)
                    return FormatErrorResponse("Graph not available (handshake did not complete)");
                return FormatResponse(HandleStaticDeps(graph, request.AffectedProjects, request.ChangedFiles));

            case "fingerprint":
                if (graph == null)
                    return FormatErrorResponse("Graph not available (handshake did not complete)");
                return FormatResponse(HandleFingerprint(graph));

            case "run-args":
                if (graph == null)
                    return FormatErrorResponse("Graph not available (handshake did not complete)");
                return FormatResponse(HandleRunArgsFromTestIds(graph, request.TestIds));

            case "ingest":
                return FormatResponse(HandleIngest(request.TrxPath));

            case "shutdown":
                return JsonSerializer.Serialize(new { result = new { status = "shutting_down" } });

            default:
                return FormatErrorResponse($"Unknown command: {request.Command}");
        }
    }

    /// <summary>
    /// Resolve test IDs to ProjectDescriptor[] and generate run-args.
    /// </summary>
    static string[] HandleRunArgsFromTestIds(MonorepoGraph graph, string[] testIds)
    {
        var projects = graph.Nodes.Values
            .Where(n => testIds.Contains(n.Project.PackageId))
            .Select(n => n.Project)
            .ToArray();

        return HandleRunArgs(projects, null);
    }
}
