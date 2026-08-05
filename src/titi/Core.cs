// titi.core — Entry point and command dispatch
// (:gen-class :main true) equivalent in C#
// Interop namespace MUST be loaded first to ensure MSBuildLocator fires

using titi.Interop;
using titi.Config;
using titi.Graph;
using titi.Swap;
using titi.Solution;
using titi.Affected;
using titi.TestCli;
using titi.Safety;
using titi.TestManifest;
using titi.Adapter;
using titi.Repl;

namespace titi.Core;

public static class Program
{
    public static int Main(string[] args)
    {
        // Step 1: Initialize MSBuild interop FIRST
        try
        {
            MsBuildSetup.Initialize();
        }
        catch (Exception ex)
        {
            var err = new TitiError(
                ErrorCode.MsbuildNotFound,
                $"E007: MSBuild not found: {ex.Message}",
                new() { ["command"] = "init", ["phase"] = "graph-build" },
                ["Ensure .NET SDK is installed", "Run: dotnet --version"]
            );
            PrintError(err);
            return 7;
        }

        // Step 2: Dispatch commands
        return args switch
        {
            ["open", var packageId, ..] => OpenCommand(packageId, args[2..]),
            ["affected", ..] => AffectedCommand(args[1..]),
            ["tests", "list", ..] => TestsListCommand(args[2..]),
            ["tests", "ingest", ..] => TestsIngestCommand(args[2..]),
            ["tests", "record", ..] => TestsRecordCommand(),
            ["version", "detect", ..] => VersionDetectCommand(),
            ["version", "plan", ..] => VersionPlanCommand(),
            ["version", "validate", ..] => VersionValidateCommand(args[2..]),
            ["testaruda-adapter", ..] => TestarudaAdapterCommand(),
            ["test-manifest", ..] => TestManifestCommand(args[1..]),
            ["repl"] => ReplCommand(),
            ["clean"] => CleanCommand(),
            ["--help"] or ["-h"] or [] => PrintHelp(),
            _ => UnknownCommand(args[0])
        };
    }

    static (MonorepoGraph? Graph, TitiConfig? Config, int ExitCode) BuildGraphForRepo()
    {
        // Allow override via TESTARUDA_PROJECT_DIR so the wrapper script can cd
        // to a neutral directory (avoiding target repo's global.json SDK paths)
        // while still discovering projects in the target repo (testaruda-vx7).
        var testarudaDir = Environment.GetEnvironmentVariable("TESTARUDA_PROJECT_DIR");
        var repoRoot = testarudaDir ?? Environment.CurrentDirectory;

        var (config, configErr) = ConfigLoader.Load(repoRoot);
        if (configErr != null)
        {
            PrintError(configErr);
            return (null, null, 9);
        }

        var prefix = config!.Prefix;
        var sourceRoots = config.SourceRoot.Select(sr => Path.GetFullPath(Path.Combine(repoRoot, sr))).ToArray();

        Console.Error.WriteLine($"Discovering projects under {string.Join(", ", sourceRoots)}...");
        var projects = MsBuildSetup.DiscoverProjects(sourceRoots, prefix);
        if (projects.Length == 0)
        {
            Console.Error.WriteLine($"No projects found matching prefix '{prefix}' under {string.Join(", ", sourceRoots)}");
            return (null, config, 1);
        }

        Console.Error.WriteLine($"Building dependency graph from {projects.Length} projects...");
        var msGraph = MsBuildSetup.BuildGraph(projects);
        var descriptors = new Dictionary<string, ProjectDescriptor>();

        foreach (var node in msGraph.ProjectNodesTopologicallySorted)
        {
            descriptors[node.ProjectInstance.FullPath] = MsBuildSetup.ConvertNode(node, config.EffectiveTestSdkIds);
        }

        var graph = GraphBuilder.Build(msGraph, descriptors, repoRoot);
        return (graph, config, 0);
    }

    static int OpenCommand(string packageId, string[] flags)
    {
        var (graph, config, exitCode) = BuildGraphForRepo();
        if (graph == null || exitCode != 0)
            return exitCode;

        // Compute swap
        Console.Error.WriteLine($"Computing swap for {packageId}...");
        var swapResult = SwapEngine.Compute(
            graph,
            [packageId],
            config!.VersionPolicy,
            includeTransitive: true,
            config.Prefix,
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, config.SourceRoot[0]))
        );

        // Generate solution
        Console.Error.WriteLine("Generating solution...");
        var (slnxPath, slnErr) = SolutionGenerator.Generate(swapResult, ".titi", packageId);

        if (slnErr != null)
        {
            PrintError(slnErr);
            return 1;
        }

        // Print result
        var output = new titi.Serialization.OpenCommandOutput(
            SolutionPath: slnxPath,
            Swapped: swapResult.Swapped.Select(s => new titi.Serialization.SwappedEntry(
                PackageId: s.PackageId,
                LocalSourcePath: s.LocalSourcePath
            )).ToArray(),
            Retained: swapResult.Retained.Select(r => new titi.Serialization.RetainedEntry(
                PackageId: r.PackageId,
                Reason: (int)r.Reason,
                Detail: r.Detail
            )).ToArray(),
            ProjectCount: swapResult.Swapped.Length + swapResult.Retained.Length
        );

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            output,
            titi.Serialization.TitiJsonContext.Default.OpenCommandOutput));

        // Regenerate lock file after swap (VN-04) — only when packages were actually swapped
        if (swapResult.Swapped.Length > 0)
        {
            Console.Error.WriteLine("Regenerating lock file...");
            try
            {
                // Route through the safe process runner (titi-mo7): concurrent
                // drain, bounded timeout, process-tree termination. The prior
                // inline Process never drained stdout and read ExitCode after a
                // possibly-expired WaitForExit, which could hang or orphan
                // dotnet children.
                var (ok, _, stderr) = RunDotnet("restore --force-evaluate", Environment.CurrentDirectory);
                if (!ok)
                {
                    Console.Error.WriteLine($"Warning: Lock file regeneration failed: {stderr.Trim()}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Warning: Could not regenerate lock file: {ex.Message}");
            }
        }

        return 0;
    }

    static int AffectedCommand(string[] args)
    {
        var (graph, config, exitCode) = BuildGraphForRepo();
        if (graph == null || exitCode != 0)
            return exitCode;

        // Parse --base flag
        var baseRef = "HEAD~1";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--base" && i + 1 < args.Length)
                baseRef = args[i + 1];
        }

        // Get changed files from git
        Console.Error.WriteLine($"Running git diff {baseRef}..HEAD...");
        var (changedFiles, gitErr) = Analyzer.GetChangedFiles(Environment.CurrentDirectory, baseRef);

        if (gitErr != null)
        {
            Console.Error.WriteLine($"Warning: git diff failed: {gitErr}");
            Console.Error.WriteLine("Fallback: all projects will be reported as affected");
        }
        else if (changedFiles is null || changedFiles.Length == 0)
        {
            Console.Error.WriteLine("No changes detected since last commit.");
        }

        // Build affected set
        var affected = Analyzer.BuildAffectedSet(changedFiles, graph);

        // Run safety selection when a test-edge cache exists (5.6). Discovery
        // runs `dotnet test --list-tests` per affected test project; edges and
        // history load from .titi/test-cache/. When no cache exists, selection
        // is empty (matches prior behavior — project-level affected set only).
        var repoRoot = Environment.CurrentDirectory;
        var cacheDir = Path.Combine(repoRoot, ".titi", "test-cache");
        var edges = SelectionLoader.LoadEdges(cacheDir);
        var history = SelectionLoader.LoadHistory(cacheDir);

        var affectedTestProjects = affected.DirectlyAffected
            .Concat(affected.TransitivelyAffected)
            .Where(p => p.IsTestProject)
            .Distinct()
            .ToArray();

        var allItems = new List<TestItem>();
        if (edges.Length > 0 && affectedTestProjects.Length > 0)
        {
            Console.Error.WriteLine($"Discovering test items from {affectedTestProjects.Length} affected test project(s)...");
            DiscoverTestItems(affectedTestProjects, repoRoot, cacheDir, allItems);
        }
        // Fall back to static analysis when no coverage edges exist (TID-0ej).
        // Discover test items, compute static edges, and persist them.
        if (edges.Length == 0 && affectedTestProjects.Length > 0)
        {
            Console.Error.WriteLine("No coverage edges found. Falling back to static dependency analysis...");
            DiscoverTestItems(affectedTestProjects, repoRoot, cacheDir, allItems);
            if (allItems.Count > 0)
            {
                var discoveredTests = GroupTestItemsByPackageId(allItems, affectedTestProjects);
                edges = StaticEdgeAnalyzer.AnalyzeAll(graph, discoveredTests);
                if (edges.Length > 0)
                {
                    StaticEdgeAnalyzer.PersistStaticEdges(cacheDir, edges);
                    Console.Error.WriteLine($"Computed {edges.Length} static edge(s).");
                }
            }
        }

        var alwaysRun = titi.Safety.Selection.ComputeAlwaysRunSet(allItems.ToArray(), FlattenLatest(history));
        var selectedTests = titi.Safety.Selection.ComputeSelectedTests(
            allItems.ToArray(), edges, alwaysRun, affected.ChangedFiles);
        affected = affected with { SelectedTests = selectedTests };

        // Compute edge freshness and history depth for the confidence model
        var edgeFreshnessAffected = edges.Length > 0 ? 1.0 : 0.0;
        var historyDepthAffected = history.Count;

        // Print result using upgraded formatter
        Console.WriteLine(Formatter.FormatAffectedUpgrade(affected, edgeFreshnessAffected, historyDepthAffected));
        return 0;
    }

    static int TestsListCommand(string[] args)
    {
        var projectPath = args.Length > 0 ? args[0] : null;

        if (projectPath == null || !File.Exists(projectPath))
        {
            Console.Error.WriteLine("Usage: titi tests list <path-to-csproj>");
            return 1;
        }

        Console.Error.WriteLine($"Listing tests for {projectPath}...");

        var repoRoot = Environment.CurrentDirectory;
        var cacheDir = Path.Combine(repoRoot, ".titi", "test-cache");
        var projDir = Path.GetDirectoryName(projectPath) ?? "";
        var fingerprint = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, projectPath);
        // Use the project path as cache key (sanitized) so two projects with
        // the same filename in different directories don't collide.
        var cacheKey = projDir.Length > 0
            ? $"{projDir.Replace(Path.DirectorySeparatorChar, '.')}.{Path.GetFileNameWithoutExtension(projectPath)}"
            : Path.GetFileNameWithoutExtension(projectPath);

        var items = titi.TestDiscovery.DiscoveryCache.GetOrDiscover(cacheDir, cacheKey, fingerprint, () =>
        {
            try
            {
                // Route through the safe process runner (titi-mo7): concurrent
                // drain, bounded timeout, process-tree termination. The prior
                // inline Process drained stdout then stderr sequentially before
                // checking timeout, which could deadlock on full pipes.
                var (stdout, stderr, ok) = RunDotnetListTests(projectPath, repoRoot);
                if (!ok)
                {
                    Console.Error.WriteLine($"dotnet test --list-tests failed: {stderr.Split('\n').FirstOrDefault()}");
                    return [];
                }

                return titi.TestDiscovery.Parser.Parse(stdout, TestTier.Unit);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error listing tests: {ex.Message}");
                return [];
            }
        });

        Console.WriteLine(Formatter.FormatTestItems(items));
        return 0;
    }

    static int TestsIngestCommand(string[] args)
    {
        // Parse args: trx-path [--coverage cobertura-path]
        var trxPath = args.Length > 0 ? args[0] : null;
        var coveragePath = args.SkipWhile(a => a != "--coverage").Skip(1).FirstOrDefault();

        if (trxPath == null || !File.Exists(trxPath))
        {
            Console.Error.WriteLine("Usage: titi tests ingest <trx-path> [--coverage <cobertura-path>]");
            return 1;
        }

        Console.Error.WriteLine($"Ingesting {trxPath}...");
        var repoRoot = Environment.CurrentDirectory;
        var titiDir = Path.Combine(repoRoot, ".titi");
        var cacheDir = Path.Combine(titiDir, "test-cache");
        var edgesDir = Path.Combine(cacheDir, "edges");
        Directory.CreateDirectory(edgesDir);

        var trxXml = File.ReadAllText(trxPath);
        string? coberturaXml = null;
        if (coveragePath != null && File.Exists(coveragePath))
        {
            Console.Error.WriteLine($"Reading coverage from {coveragePath}...");
            coberturaXml = File.ReadAllText(coveragePath);
        }

        // Load prior run history so this ingest appends to it (TD-06).
        var historyPath = Path.Combine(cacheDir, "history.json");
        var priorHistory = File.Exists(historyPath)
            ? HistoryStore.ParseEdn(File.ReadAllText(historyPath))
            : new Dictionary<string, Safety.TestRunEntry[]>();

        // Correlate TRX + Cobertura into per-test×source edges (CLI-21), and
        // update run history (TD-06).
        var ingest = Ingestor.IngestRun(trxXml, coberturaXml, repoRoot, priorHistory);

        // CLI-21 "malformed input": exit 1, warn, do NOT modify the edge cache.
        if (ingest.IsMalformed)
        {
            Console.Error.WriteLine($"Warning: could not parse TRX file {trxPath}; edge cache not modified.");
            return 1;
        }

        // Only write the edge index when coverage was provided — a TRX-only
        // ingest updates run history but builds no edges, and must NOT
        // overwrite a prior edge index with an empty array.
        if (coberturaXml != null)
        {
            var edgesPath = Path.Combine(edgesDir, "edges.json");
            var edgeEntries = ingest.Edges.Select(e => new titi.Serialization.EdgeEntry(
                From: e.From,
                To: e.To,
                Origin: (int?)e.Origin,
                Weight: e.Weight,
                LineRanges: e.LineRanges.Select(lr => new titi.Serialization.LineRangeEntry(lr.Start, lr.End)).ToArray()
            )).ToArray();
            File.WriteAllText(edgesPath,
                System.Text.Json.JsonSerializer.Serialize(
                    edgeEntries,
                    titi.Serialization.TitiJsonContext.Default.EdgeEntryArray));
            Console.Error.WriteLine($"Wrote {ingest.Edges.Length} edges to {edgesPath}");
        }

        // Persist run history (TD-06): append entries, evict beyond 100/test,
        // compact when the file exceeds 10 MB.
        if (ingest.History != null)
        {
            var compacted = HistoryStore.CompactIfOversized(ingest.History);
            File.WriteAllText(historyPath, HistoryStore.SerializeEdn(compacted));
            Console.Error.WriteLine($"Wrote run history to {historyPath}");
        }

        Console.Error.WriteLine($"Ingested {ingest.Results.Length} test result(s) from {trxPath}.");
        Console.Error.WriteLine("Ingest complete.");
        return 0;
    }

    static int TestsRecordCommand()
    {
        var (graph, config, exitCode) = BuildGraphForRepo();
        if (graph == null || exitCode != 0)
            return exitCode;

        var repoRoot = Environment.CurrentDirectory;
        var titiDir = Path.Combine(repoRoot, ".titi");
        var cacheDir = Path.Combine(titiDir, "test-cache");
        var edgesDir = Path.Combine(cacheDir, "edges");
        var runsRoot = Path.Combine(cacheDir, "runs");
        var projectsDir = Path.Combine(edgesDir, "projects");

        var testProjects = graph.Nodes.Values
            .Where(n => n.Project.IsTestProject)
            .Select(n => n.Project)
            .ToArray();

        if (testProjects.Length == 0)
        {
            Console.Error.WriteLine("No test projects found in the graph.");
            return 0;
        }

        // Load prior per-project fingerprints and run history (TID-11).
        var priorFingerprints = RecordPlanner.LoadProjectFingerprints(cacheDir);
        var historyPath = Path.Combine(cacheDir, "history.json");
        var history = File.Exists(historyPath)
            ? HistoryStore.ParseEdn(File.ReadAllText(historyPath))
            : new Dictionary<string, Safety.TestRunEntry[]>();

        // Determine which projects changed (per-project fingerprint diff).
        var changedProjects = RecordPlanner.ComputeChangedProjects(priorFingerprints, testProjects);
        var unchangedPackageIds = testProjects
            .Select(p => p.PackageId)
            .Except(changedProjects.Select(c => c.PackageId))
            .ToHashSet();

        Console.Error.WriteLine($"Recording {testProjects.Length} test project(s) with coverage...");
        Console.Error.WriteLine($"  {changedProjects.Length} changed, {unchangedPackageIds.Count} unchanged");
        Directory.CreateDirectory(runsRoot);
        Directory.CreateDirectory(projectsDir);

        var allEdges = new List<TestToSourceEdge>();
        var failures = 0;
        var RecordedOk = new HashSet<string>();

        // Only re-run changed projects (TID-11 incremental).
        if (changedProjects.Length > 0)
        {
            Console.Error.WriteLine($"Re-recording {changedProjects.Length} changed project(s)...");
            foreach (var plan in RecordPlanner.PlanTestRuns(changedProjects, runsRoot))
            {
                Directory.CreateDirectory(plan.ResultsDir);
                try
                {
                    Console.Error.WriteLine($"  dotnet {plan.Arguments}");
                    var (ranOk, stdout, stderr) = RunDotnet(plan.Arguments, repoRoot);
                    if (!ranOk)
                    {
                        failures++;
                        Console.Error.WriteLine($"  test run failed for {plan.ProjectPath}: {stderr.Split('\n').FirstOrDefault()}");
                        continue;
                    }

                    var (trxPath, coberturaPath) = ArtifactLocator.FindArtifacts(plan.ResultsDir);
                    if (trxPath == null)
                    {
                        failures++;
                        Console.Error.WriteLine($"  no TRX produced for {plan.ProjectPath}");
                        continue;
                    }

                    var trxResults = titi.Coverage.Parser.ParseTrx(File.ReadAllText(trxPath));
                    history = HistoryStore.AppendResults(history, trxResults, DateTime.UtcNow);
                    string[] coveredSources = [];
                    if (coberturaPath != null)
                    {
                        var coberturaEdges = titi.Coverage.Parser.ParseCobertura(
                            File.ReadAllText(coberturaPath), repoRoot);
                        coveredSources = coberturaEdges.Select(e => e.To).Distinct().ToArray();
                    }

                    var projectEdges = EdgeBuilder.BuildFromRun(trxResults, coveredSources);
                    allEdges.AddRange(projectEdges);

                    // Track successful project for fingerprint advancement
                    RecordedOk.Add(plan.PackageId);

                    // Write per-project edge file for future incremental runs.
                    var projEdgePath = titi.RecordPlanner.EdgeFilePath(projectsDir, plan.PackageId);
                    var projEdgeEntries = projectEdges.Select(e => new titi.Serialization.EdgeEntry(
                        From: e.From,
                        To: e.To,
                        Origin: (int?)e.Origin,
                        Weight: e.Weight,
                        LineRanges: e.LineRanges.Select(lr => new titi.Serialization.LineRangeEntry(lr.Start, lr.End)).ToArray()
                    )).ToArray();
                    File.WriteAllText(projEdgePath,
                        System.Text.Json.JsonSerializer.Serialize(
                            projEdgeEntries,
                            titi.Serialization.TitiJsonContext.Default.EdgeEntryArray));
                }
                finally
                {
                    try { Directory.Delete(plan.ResultsDir, recursive: true); } catch { }
                }
            }
        }

        // Load edges from unchanged projects (from prior per-project files).
        if (unchangedPackageIds.Count > 0)
        {
            Console.Error.WriteLine($"Preserving edges for {unchangedPackageIds.Count} unchanged project(s)...");
            foreach (var packageId in unchangedPackageIds)
            {
                var projEdgePath = titi.RecordPlanner.EdgeFilePath(projectsDir, packageId);
                if (File.Exists(projEdgePath))
                {
                    try
                    {
                        var json = File.ReadAllText(projEdgePath);
                        var edges = System.Text.Json.JsonSerializer.Deserialize(
                            json, titi.Serialization.TitiJsonContext.Default.ListJsonEdge);
                        if (edges != null)
                        {
                            allEdges.AddRange(edges.Select(e => new TestToSourceEdge(
                                From: e.From ?? "", To: e.To ?? "",
                                Origin: SelectionLoader.ParseOrigin(e.Origin),
                                Weight: e.Weight,
                                LineRanges: (e.LineRanges ?? []).Select(lr => (lr.Start, lr.End)).ToArray()
                            )));
                        }
                    }
                    catch { /* skip corrupt per-project file */ }
                }
            }
        }

        // Clean up per-project files for projects no longer in the graph.
        var currentIds = testProjects.Select(p => titi.RecordPlanner.EdgeFileKey(p.PackageId)).ToHashSet();
        if (Directory.Exists(projectsDir))
        {
            foreach (var file in Directory.EnumerateFiles(projectsDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!currentIds.Contains(name))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }

        // Write combined edges.json (the file that SelectionLoader.LoadEdges reads).
        var edgesPath = Path.Combine(edgesDir, "edges.json");
        var allEdgeEntries = allEdges.Select(e => new titi.Serialization.EdgeEntry(
            From: e.From,
            To: e.To,
            Origin: (int?)e.Origin,
            Weight: e.Weight,
            LineRanges: e.LineRanges.Select(lr => new titi.Serialization.LineRangeEntry(lr.Start, lr.End)).ToArray()
        )).ToArray();
        File.WriteAllText(edgesPath,
            System.Text.Json.JsonSerializer.Serialize(
                allEdgeEntries,
                titi.Serialization.TitiJsonContext.Default.EdgeEntryArray));

        // Update per-project fingerprints only for projects that were
        // successfully recorded. Failed projects keep their prior fingerprints
        // so they are retried on the next incremental run.
        var currentFingerprints = new Dictionary<string, string>();
        foreach (var proj in testProjects)
        {
            var projDir = Path.GetDirectoryName(proj.Path) ?? "";
            // Only advance fingerprint for unchanged projects (read from prior)
            // or successfully-recorded changed projects (compute fresh).
            if (RecordedOk.Contains(proj.PackageId) || unchangedPackageIds.Contains(proj.PackageId))
            {
                currentFingerprints[proj.PackageId] = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, proj.Path);
            }
        }
        // Preserve prior fingerprints for projects that failed recording.
        // They keep their old fingerprint so they're re-detected as changed
        // on the next run.
        foreach (var kv in priorFingerprints)
        {
            if (!currentFingerprints.ContainsKey(kv.Key))
                currentFingerprints[kv.Key] = kv.Value;
        }
        RecordPlanner.SaveProjectFingerprints(cacheDir, currentFingerprints);

        // Persist run history (TD-06): evict beyond 100/test, compact >10MB.
        var compactedHistory = HistoryStore.CompactIfOversized(history);
        File.WriteAllText(historyPath, HistoryStore.SerializeEdn(compactedHistory));

        Console.Error.WriteLine($"Wrote {allEdges.Count} edges to {edgesPath}");
        if (failures > 0)
        {
            Console.Error.WriteLine($"{failures} test project(s) failed during recording.");
            return 1;
        }
        return 0;
    }

    // Content fingerprint over every project's .csproj and source files (.cs)
    // so incremental recording detects in-place source edits, not just
    // structural (add/remove) changes (CLI-22 incremental scenario).
    static string ComputeSourceFingerprint(MonorepoGraph graph)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hashes = new List<string>();
        foreach (var node in graph.Nodes.Values.OrderBy(n => n.Project.Path, StringComparer.Ordinal))
        {
            var projDir = Path.GetDirectoryName(node.Project.Path) ?? "";
            hashes.Add(HashFile(sha, node.Project.Path));
            foreach (var src in Directory.EnumerateFiles(projDir, "*.cs", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                hashes.Add(HashFile(sha, src));
            }
        }
        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', hashes));
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    static string HashFile(System.Security.Cryptography.SHA256 sha, string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
        catch
        {
            return $"missing:{path}";
        }
    }

    // Flatten per-test history vectors to the most-recent entry (always-run
    // only cares about the last outcome).
    static Dictionary<string, titi.Safety.TestRunEntry> FlattenLatest(
        Dictionary<string, titi.Safety.TestRunEntry[]> history)
    {
        var flat = new Dictionary<string, titi.Safety.TestRunEntry>();
        foreach (var kv in history)
        {
            if (kv.Value.Length > 0)
                flat[kv.Key] = kv.Value[^1]; // most-recent-last
        }
        return flat;
    }

    /// <summary>
    /// Discover test items from affected test projects into <paramref name="allItems"/>.
    /// Uses DiscoveryCache for caching and fingerprint-based invalidation.
    /// </summary>
    static void DiscoverTestItems(
        ProjectDescriptor[] affectedTestProjects,
        string repoRoot,
        string cacheDir,
        List<TestItem> allItems)
    {
        foreach (var proj in affectedTestProjects)
        {
            var projDir = Path.GetDirectoryName(proj.Path) ?? "";
            var fingerprint = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, proj.Path);
            var items = titi.TestDiscovery.DiscoveryCache.GetOrDiscover(cacheDir, proj.PackageId, fingerprint, () =>
            {
                var (stdout, stderr, ok) = RunDotnetListTests(proj.Path, repoRoot);
                if (!ok)
                {
                    Console.Error.WriteLine($"  warning: list-tests failed for {proj.PackageId}: {stderr.Split('\n').FirstOrDefault()}");
                    return [];
                }
                return titi.TestDiscovery.Parser.Parse(stdout, TestTier.Unit);
            });
            allItems.AddRange(items);
        }
    }

    /// <summary>
    /// Group test items by their project PackageId, extracted from the TestId
    /// prefix (everything before the first "::" separator). Items without a
    /// "::" separator are assigned to the first affected project as fallback.
    /// </summary>
    static Dictionary<string, TestItem[]> GroupTestItemsByPackageId(
        List<TestItem> allItems,
        ProjectDescriptor[] affectedTestProjects)
    {
        return allItems
            .GroupBy(i =>
            {
                var sep = i.TestId.IndexOf("::", StringComparison.Ordinal);
                return sep >= 0 ? i.TestId[..sep] : affectedTestProjects.FirstOrDefault()?.PackageId ?? "";
            })
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
    }

    internal static (bool Ok, string Stdout, string Stderr) RunDotnet(string arguments, string workingDir)
    {
        var result = RunProcess("dotnet", arguments, workingDir, 600_000);
        return (result.Ok, result.Stdout ?? "", result.Stderr ?? "");
    }

    // `dotnet test --list-tests` for `titi affected` discovery. Returns stdout
    // (the console test list) on success; the caller parses via TestDiscovery.Parser.
    internal static (string Stdout, string Stderr, bool Ok) RunDotnetListTests(string projectPath, string workingDir)
    {
        var args = $"test \"{projectPath}\" --list-tests";
        var result = RunProcess("dotnet", args, workingDir, 60_000);
        return (result.Stdout ?? "", result.Stderr ?? "", result.Ok);
    }

    static int TestManifestCommand(string[] args)
    {
        var (graph, config, exitCode) = BuildGraphForRepo();
        if (graph == null || exitCode != 0)
            return exitCode;

        // Parse flags
        var baseRef = "HEAD~1";
        var tierFilter = (TestTier?)null;
        var selectMode = false;
        var listMode = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--base" when i + 1 < args.Length:
                    baseRef = args[++i];
                    break;
                case "--tier" when i + 1 < args.Length:
                    tierFilter = args[++i].ToLower() switch
                    {
                        "unit" => TestTier.Unit,
                        "package" => TestTier.Package,
                        "integration" => TestTier.Integration,
                        "compatibility" => TestTier.Compatibility,
                        var v => throw new ArgumentException($"Unknown tier: {v}")
                    };
                    break;
                case "--select":
                    selectMode = true;
                    break;
                case "--list":
                    listMode = true;
                    break;
            }
        }

        var repoRoot = Environment.CurrentDirectory;

        // Get changed files from git
        Console.Error.WriteLine($"Running git diff {baseRef}..HEAD...");
        var (changedFiles, gitErr) = titi.Affected.Analyzer.GetChangedFiles(repoRoot, baseRef);

        if (gitErr != null)
        {
            Console.Error.WriteLine($"Warning: git diff failed: {gitErr}");
        }

        // Build affected set
        var affected = titi.Affected.Analyzer.BuildAffectedSet(changedFiles, graph);
        var affectedTestProjects = affected.DirectlyAffected
            .Concat(affected.TransitivelyAffected)
            .Where(p => p.IsTestProject)
            .DistinctBy(p => p.PackageId)
            .ToArray();

        // Apply --tier filter if specified
        if (tierFilter.HasValue)
        {
            affectedTestProjects = affectedTestProjects
                .Where(p => MatchesTier(p, tierFilter.Value, config?.TestTiers))
                .ToArray();
        }

        if (affectedTestProjects.Length == 0)
        {
            Console.Error.WriteLine("No affected test projects found.");
            if (selectMode)
            {
                // Exit code 20: safe to skip (no affected tests)
                return 20;
            }
            // Generate empty Traversal
            var manifestDir = Path.Combine(repoRoot, ".titi", "manifest");
            Directory.CreateDirectory(manifestDir);
            var outputPath = Path.Combine(manifestDir, "test-manifest.proj");
            var xml = titi.TestManifest.TraversalGenerator.Generate([], null);
            File.WriteAllText(outputPath, xml);
            Console.Error.WriteLine($"Wrote empty Traversal to {outputPath}");
            return 0;
        }

        if (!selectMode)
        {
            // Project-level Traversal (no filtering)
            var manifestDir = Path.Combine(repoRoot, ".titi", "manifest");
            Directory.CreateDirectory(manifestDir);
            var outputPath = Path.Combine(manifestDir, "test-manifest.proj");
            var xml = titi.TestManifest.TraversalGenerator.Generate(affectedTestProjects, null);
            File.WriteAllText(outputPath, xml);
            Console.Error.WriteLine($"Wrote Traversal to {outputPath} with {affectedTestProjects.Length} project(s)");
            return 0;
        }

        // ── --select mode: per-test filtered Traversal ──────────────

        // Load edges and history from cache
        var cacheDir = Path.Combine(repoRoot, ".titi", "test-cache");
        var edges = SelectionLoader.LoadEdges(cacheDir);
        var history = SelectionLoader.LoadHistory(cacheDir);

        if (edges.Length == 0)
        {
            // Fall back to static analysis (TID-0ej)
            Console.Error.WriteLine("No coverage edges found. Falling back to static dependency analysis...");
            Console.Error.WriteLine("Discovering test items for static analysis...");
            var staticAllItems = new List<TestItem>();
            DiscoverTestItems(affectedTestProjects, repoRoot, cacheDir, staticAllItems);
            if (staticAllItems.Count > 0)
            {
                var discoveredTests = GroupTestItemsByPackageId(staticAllItems, affectedTestProjects);
                edges = StaticEdgeAnalyzer.AnalyzeAll(graph, discoveredTests);
                if (edges.Length > 0)
                {
                    StaticEdgeAnalyzer.PersistStaticEdges(cacheDir, edges);
                    Console.Error.WriteLine($"Computed {edges.Length} static edge(s).");
                }
            }

            if (edges.Length == 0)
            {
                // No edges available at all: fall back to project-level with warning
                Console.Error.WriteLine("Warning: no test-to-source edges found (static or coverage). Falling back to project-level Traversal.");
                Console.Error.WriteLine("  Run 'titi tests record' to build the coverage edge index.");

                var manifestDir = Path.Combine(repoRoot, ".titi", "manifest");
                Directory.CreateDirectory(manifestDir);
                var outputPath = Path.Combine(manifestDir, "test-manifest.proj");
                var xml = titi.TestManifest.TraversalGenerator.Generate(affectedTestProjects, null);
                File.WriteAllText(outputPath, xml);
                Console.Error.WriteLine($"Wrote project-level Traversal to {outputPath}");
                return 0;
            }
        }

        // Discover test items from affected test projects
        Console.Error.WriteLine($"Discovering test items from {affectedTestProjects.Length} affected test project(s)...");
        var allItems = new List<TestItem>();
        foreach (var proj in affectedTestProjects)
        {
            var projDir = Path.GetDirectoryName(proj.Path) ?? "";
            var fingerprint = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, proj.Path);
            var cacheKey = proj.PackageId;
            var items = titi.TestDiscovery.DiscoveryCache.GetOrDiscover(cacheDir, cacheKey, fingerprint, () =>
            {
                var (stdout, stderr, ok) = RunDotnetListTests(proj.Path, repoRoot);
                if (!ok)
                {
                    Console.Error.WriteLine($"  warning: list-tests failed for {proj.PackageId}: {stderr.Split('\n').FirstOrDefault()}");
                    return [];
                }
                return titi.TestDiscovery.Parser.Parse(stdout, TestTier.Unit);
            });
            allItems.AddRange(items);
        }

        var itemsArray = allItems.ToArray();
        var alwaysRun = FlattenLatest(history);
        var alwaysRunSet = titi.Safety.Selection.ComputeAlwaysRunSet(itemsArray, alwaysRun);
        var selectedTests = titi.Safety.Selection.ComputeSelectedTests(
            itemsArray, edges, alwaysRunSet, affected.ChangedFiles);

        var selectedItems = itemsArray
            .Where(i => selectedTests.Any(s => s.TestId == i.TestId && s.Selected))
            .ToArray();

        // Determine confidence using the documented weighted model (titi-e59).
        // The old code used DirectlyAffected.Length / ChangedFiles.Length which
        // could assign a low score to a fully resolved multi-file change.
        var resolvedFiles = affected.ResolvedFiles.Length > 0
            ? affected.ResolvedFiles
            : affected.ChangedFiles;
        var edgeFreshness = edges.Length > 0 ? 1.0 : 0.0;
        var historyDepth = history.Count;
        var confidence = titi.Safety.Selection.ComputeConfidence(
            affected.ChangedFiles, resolvedFiles, edgeFreshness, historyDepth);

        // ── Exit on low confidence before emitting partial output ──
        // When confidence falls below the configured threshold, suppress
        // all test-ID or Traversal output and return exit 10 with an
        // actionable diagnostic identifying the affected projects that
        // must be run in full.
        if (confidence < (config?.TestDetection.FallbackThreshold ?? 0.7))
        {
            if (listMode)
            {
                Console.Error.WriteLine("Selection confidence below threshold.");
                PrintAffectedProjectsDiagnostic(affected);
            }
            else
            {
                Console.Error.WriteLine("Selection confidence below threshold.");
                PrintAffectedProjectsDiagnostic(affected);
            }
            return 10;
        }

        // --list mode: print selected test IDs
        if (listMode)
        {
            var output = titi.TestManifest.TestManifestCommand.FormatListOutput(selectedTests);
            foreach (var line in output)
                Console.WriteLine(line);

            if (output.Length == 0)
                return 20; // safe to skip
            return 0;
        }

        // ── Generate per-test filtered Traversal ──────────────────

        // Group selected items by project (using PackageId as key, mapped from test-id assembly prefix)
        // We need to map test items back to their project. Use the AssemblyPath prefix from test items.
        var projSelectedItems = new Dictionary<string, List<TestItem>>();
        foreach (var item in selectedItems)
        {
            // Find which project this item belongs to (by matching assembly path prefix)
            var pkgId = FindProjectForItem(item, affectedTestProjects);
            if (pkgId == null) continue;

            if (!projSelectedItems.ContainsKey(pkgId))
                projSelectedItems[pkgId] = new List<TestItem>();
            projSelectedItems[pkgId].Add(item);
        }

        // Build per-project filters
        var projectFilters = new Dictionary<string, string>();
        var manifestDir2 = Path.Combine(repoRoot, ".titi", "manifest");
        Directory.CreateDirectory(manifestDir2);

        foreach (var kv in projSelectedItems)
        {
            var framework = titi.TestManifest.FilterExprBuilder.GetCommonFramework(kv.Value.ToArray());
            if (framework == null)
            {
                Console.Error.WriteLine($"  warning: mixed frameworks in {kv.Key}, falling back to project-level Traversal");
                // Fall back to project-level: include the project without filter
                var proj = affectedTestProjects.FirstOrDefault(p => p.PackageId == kv.Key);
                if (proj != null)
                {
                    projectFilters[kv.Key] = ""; // No filter — run all tests
                }
                continue;
            }

            var batches = titi.TestManifest.FilterExprBuilder.BatchFilters(
                kv.Value.ToArray(), framework.Value,
                maxFilterLength: 4000,
                batchSize: config?.TestDetection.BatchSize ?? 100);

            if (batches.Count == 0)
            {
                Console.Error.WriteLine($"  warning: no filter generated for {kv.Key}, using project-level");
                continue;
            }

            for (int b = 0; b < batches.Count; b++)
            {
                var (expr, batchItems) = batches[b];

                // Determine the project descriptor for this package
                var proj = affectedTestProjects.FirstOrDefault(p => p.PackageId == kv.Key);
                if (proj == null) continue;

                var batchName = batches.Count > 1
                    ? $"test-manifest-{SanitizeForPath(kv.Key)}-batch-{b + 1:D3}.proj"
                    : $"test-manifest-{SanitizeForPath(kv.Key)}.proj";
                var batchPath = Path.Combine(manifestDir2, batchName);

                var perProjectFilters = new Dictionary<string, string>
                {
                    [kv.Key] = expr
                };

                var xml = titi.TestManifest.TraversalGenerator.Generate(
                    [proj], perProjectFilters, batchName: batchName);
                File.WriteAllText(batchPath, xml);
                Console.Error.WriteLine($"  Wrote batch {batchPath} ({batchItems.Length} test(s))");

                // Log parameterized-row over-approximation warnings
                var paramRows = batchItems.Where(i => i.TestId.Contains('(')).ToArray();
                if (paramRows.Length > 0)
                {
                    Console.Error.WriteLine($"    note: {paramRows.Length} parameterized row(s) — whole-method selected (over-approximation)");
                }
            }
        }

        if (selectedItems.Length == 0)
        {
            Console.Error.WriteLine("No tests selected — safe to skip test phase.");
            return 20;
        }

        Console.Error.WriteLine($"Generated filtered Traversal(s) for {selectedItems.Length} selected test(s)");
        return 0;
    }

    /// <summary>Map a TestItem back to its owning project's PackageId.</summary>
    static string? FindProjectForItem(TestItem item, ProjectDescriptor[] projects)
    {
        // Match by assembly path prefix (testId format: "<assembly>::...")
        var testId = item.TestId;
        var assemblySep = testId.IndexOf("::", StringComparison.Ordinal);
        if (assemblySep < 0)
            return projects.FirstOrDefault()?.PackageId; // fallback

        var assemblyPrefix = testId[..assemblySep];

        // Try exact match on package name or assembly name
        foreach (var proj in projects)
        {
            if (proj.PackageId == assemblyPrefix)
                return proj.PackageId;
            var projName = Path.GetFileNameWithoutExtension(proj.Path);
            if (projName == assemblyPrefix)
                return proj.PackageId;
        }

        // Fallback: return the first project
        return projects.FirstOrDefault()?.PackageId;
    }

    /// <summary>Check if a project matches a specific test tier.</summary>
    static bool MatchesTier(ProjectDescriptor proj, TestTier tier, TestTierConfig? tierConfig)
    {
        if (tierConfig == null)
        {
            // Default heuristic: package name contains tier name
            var tierName = tier.ToString().ToLower();
            return proj.PackageId.Contains(tierName, StringComparison.OrdinalIgnoreCase);
        }

        var isUnit = tierConfig.Unit?.Any(p => proj.PackageId.Contains(p, StringComparison.OrdinalIgnoreCase)) ?? false;
        var isPackage = tierConfig.Package?.Any(p => proj.PackageId.Contains(p, StringComparison.OrdinalIgnoreCase)) ?? false;
        var isIntegration = tierConfig.Integration?.Any(p => proj.PackageId.Contains(p, StringComparison.OrdinalIgnoreCase)) ?? false;
        var isCompatibility = tierConfig.Compatibility?.Any(p => proj.PackageId.Contains(p, StringComparison.OrdinalIgnoreCase)) ?? false;

        return tier switch
        {
            TestTier.Unit => isUnit,
            TestTier.Package => isPackage,
            TestTier.Integration => isIntegration,
            TestTier.Compatibility => isCompatibility,
            _ => true,
        };
    }

    static int VersionDetectCommand()
    {
        var (graph, _, exitCode) = BuildGraphForRepo();
        if (graph == null || exitCode != 0)
            return exitCode;

        Console.Error.WriteLine("Detecting NBGV-managed versions...");
        var result = titi.Versioning.VersionDetector.Detect(graph);

        var output = new titi.Serialization.VersionDetectOutput(
            Projects: result.Projects.Select(p => new titi.Serialization.ProjectVersionEntry(
                PackageId: p.PackageId,
                CurrentVersion: p.CurrentVersion,
                IsManaged: p.IsManaged
            )).ToArray(),
            ManagedCount: result.ManagedCount,
            UnmanagedCount: result.UnmanagedCount
        );

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            output,
            titi.Serialization.TitiJsonContext.Default.VersionDetectOutput));
        return 0;
    }

    static int VersionPlanCommand()
    {
        var (graph, config, exitCode) = BuildGraphForRepo();
        if (graph == null || exitCode != 0)
            return exitCode;

        Console.Error.WriteLine("Reading changesets...");
        var repoRoot = Path.GetFullPath(Environment.CurrentDirectory);
        var (valid, invalid) = titi.Versioning.ChangesetReader.ReadChangesets(repoRoot);

        if (invalid.Length > 0)
        {
            foreach (var iv in invalid)
                Console.Error.WriteLine($"Warning: {iv.Description}");
        }

        if (valid.Length == 0)
        {
            Console.Error.WriteLine("No valid changesets found — nothing to plan");
            return invalid.Length > 0 ? 1 : 0;
        }

        var packageBumps = titi.Versioning.ChangesetReader.AggregateByPackage(valid);

        Console.Error.WriteLine($"Computing version plan for {packageBumps.Count} packages...");
        var plan = titi.Versioning.CascadingBumpEngine.Compute(
            graph, packageBumps,
            apiCompatProvider: null); // ApiCompat not wired yet — falls back to Breaking

        var output = new titi.Serialization.VersionPlanOutput(
            Entries: plan.Entries.Select(e => new titi.Serialization.VersionPlanEntryOutput(
                PackageId: e.PackageId,
                BaselineVersion: e.BaselineVersion,
                NewVersion: e.NewVersion,
                AppliedBump: e.AppliedBump.ToString(),
                Classification: e.Classification.ToString(),
                IsPropagated: e.IsPropagated
            )).ToArray(),
            Issues: plan.Issues,
            HasErrors: plan.HasErrors
        );

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            output,
            titi.Serialization.TitiJsonContext.Default.VersionPlanOutput));
        return plan.HasErrors ? 1 : 0;
    }

    static int VersionValidateCommand(string[] flags)
    {
        var (graph, config, exitCode) = BuildGraphForRepo();
        if (graph == null || exitCode != 0)
            return exitCode;

        var applyFix = flags.Contains("--fix");

        Console.Error.WriteLine("Validating version configuration...");

        var repoRoot = Path.GetFullPath(Environment.CurrentDirectory);
        var cpm = titi.Versioning.CpmDetector.Detect(repoRoot);

        var issues = new List<titi.Serialization.ValidationIssue>();

        if (!cpm.Enabled)
        {
            issues.Add(new titi.Serialization.ValidationIssue(
                Severity: "warning",
                Code: "CPM-001",
                Message: "Central Package Management (CPM) is not enabled",
                Detail: cpm.Diagnostic ?? "Add Directory.Packages.props with ManagePackageVersionsCentrally=true",
                Location: cpm.PackagesPropsPath ?? repoRoot
            ));
        }

        if (!cpm.TransitivePinningEnabled && cpm.Enabled)
        {
            issues.Add(new titi.Serialization.ValidationIssue(
                Severity: "warning",
                Code: "CPM-002",
                Message: "CentralPackageTransitivePinningEnabled is not enabled",
                Detail: "Set CentralPackageTransitivePinningEnabled=true in Directory.Packages.props for monorepo transitive version floors",
                Location: cpm.PackagesPropsPath ?? repoRoot
            ));
        }

        if (cpm.TransitivePinningEnabled && !cpm.RestoreUseLegacyDependencyResolver)
        {
            issues.Add(new titi.Serialization.ValidationIssue(
                Severity: "warning",
                Code: "CPM-004",
                Message: "RestoreUseLegacyDependencyResolver is not enabled",
                Detail: "Set RestoreUseLegacyDependencyResolver=true in Directory.Packages.props when transitive pinning is enabled to avoid false NU1605 warnings from the NuGet 6.12 CPM regression",
                Location: cpm.PackagesPropsPath ?? repoRoot
            ));

            if (applyFix && cpm.PackagesPropsPath != null)
            {
                try
                {
                    var props = File.ReadAllText(cpm.PackagesPropsPath);
                    if (!props.Contains("<RestoreUseLegacyDependencyResolver>"))
                    {
                        // Insert before the closing PropertyGroup
                        props = props.Replace("</PropertyGroup>",
                            "    <RestoreUseLegacyDependencyResolver>true</RestoreUseLegacyDependencyResolver>\n  </PropertyGroup>");
                        File.WriteAllText(cpm.PackagesPropsPath, props);
                        Console.Error.WriteLine("Applied RestoreUseLegacyDependencyResolver=true to Directory.Packages.props");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to apply workaround: {ex.Message}");
                }
            }
        }

        if (cpm.Enabled && cpm.PackageVersions != null && cpm.PackageVersions.Length == 0)
        {
            issues.Add(new titi.Serialization.ValidationIssue(
                Severity: "info",
                Code: "CPM-003",
                Message: "No PackageVersion entries defined",
                Detail: "CPM is enabled but no PackageVersion items were found in Directory.Packages.props",
                Location: cpm.PackagesPropsPath!
            ));
        }

        // Check lock file configuration (VN-04)
        var packagesPropsPath = cpm.PackagesPropsPath ?? Path.Combine(repoRoot, "Directory.Packages.props");
        var lockFileEnabled = false;
        if (cpm.HasPackagesProps)
        {
            try
            {
                var props = File.ReadAllText(packagesPropsPath);
                lockFileEnabled = props.Contains("<RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>", StringComparison.OrdinalIgnoreCase);
            }
            catch { }
        }

        if (!lockFileEnabled)
        {
            issues.Add(new titi.Serialization.ValidationIssue(
                Severity: "warning",
                Code: "LF-001",
                Message: "RestorePackagesWithLockFile is not enabled",
                Detail: "Set RestorePackagesWithLockFile=true in Directory.Packages.props to enable lock file management for reproducible restores",
                Location: packagesPropsPath
            ));

            if (applyFix && cpm.HasPackagesProps)
            {
                try
                {
                    var props = File.ReadAllText(packagesPropsPath);
                    if (!props.Contains("<RestorePackagesWithLockFile>"))
                    {
                        props = props.Replace("</PropertyGroup>",
                            "    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>\n  </PropertyGroup>");
                        File.WriteAllText(packagesPropsPath, props);
                        Console.Error.WriteLine("Applied RestorePackagesWithLockFile=true to Directory.Packages.props");
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to apply lock file setting: {ex.Message}");
                }
            }
        }

        // Check for lock files in the repo
        var lockFiles = Directory.GetFiles(repoRoot, "packages.lock.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains("/bin/") && !f.Contains("/obj/") && !f.Contains("/.titi/"))
            .ToArray();
        if (lockFileEnabled && lockFiles.Length == 0)
        {
            issues.Add(new titi.Serialization.ValidationIssue(
                Severity: "info",
                Code: "LF-002",
                Message: "No lock files found",
                Detail: "RestorePackagesWithLockFile is enabled but no packages.lock.json files exist. Run 'dotnet restore --force-evaluate' to generate them",
                Location: packagesPropsPath
            ));
        }

        // Check AssemblyVersion patterns
        var avChecks = titi.Versioning.VersionDetector.CheckAssemblyVersions(graph);
        foreach (var av in avChecks)
        {
            if (!av.IsCorrect)
            {
                issues.Add(new titi.Serialization.ValidationIssue(
                    Severity: "error",
                    Code: "AV-001",
                    Message: $"Incorrect AssemblyVersion for {av.PackageId}",
                    Detail: $"Expected '{av.ExpectedAssemblyVersion}', got '{av.CurrentAssemblyVersion}' — should be {{Major}}.0.0.0",
                    Location: av.ProjectPath
                ));

                if (applyFix && av.ExpectedAssemblyVersion != null)
                {
                    try
                    {
                        var csproj = File.ReadAllText(av.ProjectPath);
                        if (csproj.Contains("<AssemblyVersion>"))
                        {
                            csproj = System.Text.RegularExpressions.Regex.Replace(
                                csproj,
                                @"<AssemblyVersion>.*?</AssemblyVersion>",
                                $"<AssemblyVersion>{av.ExpectedAssemblyVersion}</AssemblyVersion>");
                        }
                        else
                        {
                            // Insert before closing Project tag
                            csproj = csproj.Replace("</Project>",
                                $"  <PropertyGroup>\n    <AssemblyVersion>{av.ExpectedAssemblyVersion}</AssemblyVersion>\n  </PropertyGroup>\n</Project>");
                        }
                        File.WriteAllText(av.ProjectPath, csproj);
                        Console.Error.WriteLine($"Fixed AssemblyVersion for {av.PackageId}: {av.CurrentAssemblyVersion} -> {av.ExpectedAssemblyVersion}");
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"Failed to fix AssemblyVersion for {av.PackageId}: {ex.Message}");
                    }
                }
            }
        }

        var output = new titi.Serialization.VersionValidateOutput(
            CpmEnabled: cpm.Enabled,
            TransitivePinningEnabled: cpm.TransitivePinningEnabled,
            HasPackagesProps: cpm.HasPackagesProps,
            PackageVersionCount: cpm.PackageVersions?.Length ?? 0,
            Issues: issues.ToArray()
        );

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(
            output,
            titi.Serialization.TitiJsonContext.Default.VersionValidateOutput));

        return issues.Count > 0 ? 1 : 0;
    }

    static int TestarudaAdapterCommand()
    {
        var (graph, config, exitCode) = BuildGraphForRepo();
        if (graph == null || exitCode != 0)
            return exitCode;

        Console.Error.WriteLine("Starting testaruda adapter (project-level, Phase 1)...");
        Console.Error.WriteLine("Reading commands from stdin, writing responses to stdout...");

        return TestarudaAdapter.RunLoop(graph, Console.In, Console.Out);
    }

    static int CleanCommand()
    {
        var titiDir = ".titi";
        if (Directory.Exists(titiDir))
        {
            Directory.Delete(titiDir, recursive: true);
            Console.Error.WriteLine($"Removed {titiDir}/");
        }
        else
        {
            Console.Error.WriteLine($"Nothing to clean — {titiDir}/ does not exist");
        }
        return 0;
    }

    static int ReplCommand()
    {
        var (graph, _, exitCode) = BuildGraphForRepo();
        if (graph == null) return exitCode;
        return ReplEngine.Run(graph, Console.In, Console.Out, Console.Error);
    }

    static int PrintHelp()
    {
        Console.WriteLine("titi — .NET Monorepo Orchestration CLI");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  titi open <package-id>   Generate transient .slnx with reference swapping");
        Console.WriteLine("  titi affected [--base]   List projects affected by current changes");
        Console.WriteLine("  titi tests list <proj>   List test items in a project");
        Console.WriteLine("  titi tests ingest <trx>   Ingest test results and coverage");
        Console.WriteLine("  titi tests record         Run all tests and record results");
        Console.WriteLine("  titi test-manifest [--tier <tier>]   Generate Traversal .proj for affected tests");
        Console.WriteLine("  titi test-manifest --select [--list]  Generate per-test filtered Traversal");
        Console.WriteLine("  titi testaruda-adapter   Start testaruda adapter (JSON-over-stdio protocol)");
        Console.WriteLine("  titi repl                Interactive dependency graph REPL");
        Console.WriteLine("  titi clean               Remove all titi-generated artifacts");
        Console.WriteLine("  titi --help               Show this help");
        return 0;
    }

    static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        Console.Error.WriteLine("Run 'titi --help' for usage.");
        return 2;
    }

    static void PrintError(TitiError err)
    {
        Console.Error.WriteLine($"Error {(int)err.Code:D4}: {err.Message}");
        foreach (var suggestion in err.Suggestions)
            Console.Error.WriteLine($"  Suggested: {suggestion}");
    }

    /// <summary>
    /// Run a subprocess with async stream draining, enforced timeout, and
    /// process-tree termination on timeout.
    /// </summary>
    public static (bool Ok, string? Stdout, string? Stderr) RunProcess(
        string fileName, string arguments, string workingDir, int timeoutMs)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null)
            return (false, null, "failed to start process");

        // Asynchronously drain both streams to prevent deadlock.
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();

        if (proc.WaitForExit(timeoutMs))
        {
            // Process exited within timeout.
            proc.WaitForExit(); // ensure async handlers complete
            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            return (proc.ExitCode == 0, stdout, stderr);
        }

        // Timeout exceeded — kill the process tree.
        try { proc.Kill(entireProcessTree: true); } catch { }
        proc.WaitForExit(); // allow Kill to complete
        return (false, null, $"Process timed out after {timeoutMs}ms and was terminated");
    }

    /// <summary>Sanitize a project path or package id for use as a filename.</summary>
    static string SanitizeForPath(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    /// <summary>
    /// Print an actionable diagnostic identifying projects that must be run
    /// in full when selection confidence falls below the configured threshold.
    /// </summary>
    static void PrintAffectedProjectsDiagnostic(AffectedSet affected)
    {
        Console.Error.WriteLine("Affected projects that must be run in full:");
        foreach (var proj in affected.DirectlyAffected)
            Console.Error.WriteLine($"  {proj.PackageId} (direct)");
        foreach (var proj in affected.TransitivelyAffected)
            Console.Error.WriteLine($"  {proj.PackageId} (transitive)");
    }
}
