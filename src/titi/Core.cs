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
            ["clean"] => CleanCommand(),
            ["--help"] or ["-h"] or [] => PrintHelp(),
            _ => UnknownCommand(args[0])
        };
    }

    static (MonorepoGraph? Graph, TitiConfig? Config, int ExitCode) BuildGraphForRepo()
    {
        var repoRoot = Environment.CurrentDirectory;

        var (config, configErr) = ConfigLoader.Load(repoRoot);
        if (configErr != null)
        {
            PrintError(configErr);
            return (null, null, 9);
        }

        var prefix = config!.Prefix;
        var sourceRoot = Path.GetFullPath(Path.Combine(repoRoot, config.SourceRoot));

        Console.Error.WriteLine($"Discovering projects under {sourceRoot}...");
        var projects = MsBuildSetup.DiscoverProjects(sourceRoot, prefix);
        if (projects.Length == 0)
        {
            Console.Error.WriteLine($"No projects found matching prefix '{prefix}' under {sourceRoot}");
            return (null, config, 1);
        }

        Console.Error.WriteLine($"Building dependency graph from {projects.Length} projects...");
        var msGraph = MsBuildSetup.BuildGraph(projects);
        var descriptors = new Dictionary<string, ProjectDescriptor>();

        foreach (var node in msGraph.ProjectNodesTopologicallySorted)
        {
            descriptors[node.ProjectInstance.FullPath] = MsBuildSetup.ConvertNode(node);
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
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, config.SourceRoot))
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
        var output = new Dictionary<string, object>
        {
            ["solutionPath"] = slnxPath,
            ["swapped"] = swapResult.Swapped.Select(s => new { s.PackageId, s.LocalSourcePath }).ToArray(),
            ["retained"] = swapResult.Retained.Select(r => new { r.PackageId, r.Reason, r.Detail }).ToArray(),
            ["projectCount"] = swapResult.Swapped.Length + swapResult.Retained.Length,
        };

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
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

        // Try to run safety selection if test edges are available (stub for now)
        // Uses discovered Items from TieredTestSet if populated
        var allItems = graph.Nodes.Values
            .Where(n => n.Project.IsTestProject)
            .SelectMany(_ => Array.Empty<TestItem>())
            .ToArray();

        var selectedTests = Safety.Selection.ComputeSelectedTests(
            allItems, [], new HashSet<string>(), affected.ChangedFiles);
        affected = affected with { SelectedTests = selectedTests };

        // Print result using upgraded formatter
        Console.WriteLine(Formatter.FormatAffectedUpgrade(affected));
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
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "dotnet",
                // .NET 10 `dotnet test --list-tests` emits PLAIN CONSOLE TEXT —
                // there is no JSON output mode for --list-tests (the --report-*
                // and --logger flags error out when combined with it). We
                // auto-detect JSON-vs-console in Parser.Parse.
                Arguments = $"test \"{projectPath}\" --list-tests",
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
            {
                Console.Error.WriteLine("Failed to start dotnet process");
                return 7;
            }

            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);

            if (proc.ExitCode != 0)
            {
                Console.Error.WriteLine($"dotnet test --list-tests failed (exit {proc.ExitCode})");
                Console.Error.WriteLine(stderr);
            }

            var items = titi.TestDiscovery.Parser.Parse(stdout, TestTier.Unit);
            Console.WriteLine(Formatter.FormatTestItems(items));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error listing tests: {ex.Message}");
            return 7;
        }
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

        // Correlate TRX + Cobertura into per-test×source edges (CLI-21).
        var ingest = Ingestor.IngestRun(trxXml, coberturaXml, repoRoot);

        // CLI-21 "malformed input": exit 1, warn, do NOT modify the edge cache.
        if (ingest.IsMalformed)
        {
            Console.Error.WriteLine($"Warning: could not parse TRX file {trxPath}; edge cache not modified.");
            return 1;
        }

        // Only write the edge index when coverage was provided — a TRX-only
        // ingest updates run history (deferred: task 4.6) but builds no edges,
        // and must NOT overwrite a prior edge index with an empty array.
        if (coberturaXml != null)
        {
            var edgesPath = Path.Combine(edgesDir, "edges.edn");
            File.WriteAllText(edgesPath,
                System.Text.Json.JsonSerializer.Serialize(
                    ingest.Edges.Select(e => new { e.From, e.To, e.Origin, e.Weight, e.LineRanges }),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            Console.Error.WriteLine($"Wrote {ingest.Edges.Length} edges to {edgesPath}");
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
        var fingerprintPath = Path.Combine(cacheDir, "fingerprint");

        // Incremental recording (CLI-22): if the edge cache is fresh relative to
        // the current source-file fingerprint, skip re-running tests and exit 0.
        var currentFingerprint = ComputeSourceFingerprint(graph);
        if (Directory.Exists(edgesDir)
            && File.Exists(fingerprintPath)
            && File.ReadAllText(fingerprintPath) == currentFingerprint)
        {
            Console.Error.WriteLine("Edge cache is fresh — no tests re-run.");
            return 0;
        }

        var testProjects = graph.Nodes.Values
            .Where(n => n.Project.IsTestProject)
            .Select(n => n.Project)
            .ToArray();

        if (testProjects.Length == 0)
        {
            Console.Error.WriteLine("No test projects found in the graph.");
            return 0;
        }

        Console.Error.WriteLine($"Recording {testProjects.Length} test project(s) with coverage...");
        Directory.CreateDirectory(runsRoot);
        Directory.CreateDirectory(edgesDir);

        var allEdges = new List<TestToSourceEdge>();
        var failures = 0;
        foreach (var plan in RecordPlanner.PlanTestRuns(testProjects, runsRoot))
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
                string[] coveredSources = [];
                if (coberturaPath != null)
                {
                    var coberturaEdges = titi.Coverage.Parser.ParseCobertura(
                        File.ReadAllText(coberturaPath), repoRoot);
                    coveredSources = coberturaEdges.Select(e => e.To).Distinct().ToArray();
                }

                allEdges.AddRange(EdgeBuilder.BuildFromRun(trxResults, coveredSources));
            }
            finally
            {
                // Clean up the transient results dir whether the run succeeded,
                // failed, or produced no artifacts — never leak run state.
                try { Directory.Delete(plan.ResultsDir, recursive: true); } catch { }
            }
        }

        // Persist the edge index (TD-03: .titi/test-cache/edges/).
        var edgesPath = Path.Combine(edgesDir, "edges.edn");
        File.WriteAllText(edgesPath,
            System.Text.Json.JsonSerializer.Serialize(
                allEdges.Select(e => new { e.From, e.To, e.Origin, e.Weight, e.LineRanges }),
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(fingerprintPath, currentFingerprint);

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

    static (bool Ok, string Stdout, string Stderr) RunDotnet(string arguments, string workingDir)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null)
            return (false, "", "failed to start dotnet");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(600_000); // 10 min per project
        return (proc.ExitCode == 0, stdout, stderr);
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
        Console.WriteLine("  titi clean               Remove all titi-generated artifacts");
        Console.WriteLine("  titi --help               Show this help");
        return 0;
    }

    static int UnknownCommand(string cmd)
    {
        Console.Error.WriteLine($"Unknown command: {cmd}");
        Console.Error.WriteLine("Run 'titi --help' for usage.");
        return 1;
    }

    static void PrintError(TitiError err)
    {
        Console.Error.WriteLine($"Error {(int)err.Code:D4}: {err.Message}");
        foreach (var suggestion in err.Suggestions)
            Console.Error.WriteLine($"  Suggested: {suggestion}");
    }
}
