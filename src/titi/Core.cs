// titi.core — Entry point and command dispatch
// (:gen-class :main true) equivalent in C#
// Interop namespace MUST be loaded first to ensure MSBuildLocator fires

using titi.Interop;
using titi.Config;
using titi.Graph;
using titi.Swap;
using titi.Solution;
using titi.Affected;

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

        // Print result
        var output = new Dictionary<string, object>
        {
            ["changedFiles"] = affected.ChangedFiles,
            ["directlyAffected"] = affected.DirectlyAffected.Select(p => new { p.PackageId, p.Path }).ToArray(),
            ["transitivelyAffected"] = affected.TransitivelyAffected.Select(p => new { p.PackageId, p.Path }).ToArray(),
            ["totalAffected"] = affected.DirectlyAffected.Length + affected.TransitivelyAffected.Length,
        };

        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        return 0;
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
