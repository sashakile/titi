// titi repl — Interactive REPL for exploring the dependency graph
// CLI-16 implementation

namespace titi.Repl;

using titi.Affected;

/// <summary>Interactive read-eval-print loop for graph exploration.</summary>
public static class ReplEngine
{
    /// <summary>Run the REPL with the given graph, reading from input and writing to output/error.</summary>
    /// <param name="graph">The dependency graph, or null if graph build failed.</param>
    /// <param name="input">Text reader for REPL input (stdin in production).</param>
    /// <param name="output">Text writer for REPL output (stdout in production).</param>
    /// <param name="error">Text writer for error output (stderr in production).</param>
    /// <returns>Exit code: 0 on success, 1 if graph is null.</returns>
    public static int Run(MonorepoGraph? graph, TextReader input, TextWriter output, TextWriter error)
    {
        if (graph == null)
        {
            error.WriteLine("Error E001: GRAPH_BUILD_FAILED — could not build dependency graph.");
            error.WriteLine("  Ensure you are in a valid monorepo with .csproj files.");
            return 1;
        }

        while (true)
        {
            output.Write("titi> ");
            output.Flush();

            var line = input.ReadLine();
            if (line == null) // EOF (Ctrl+D)
            {
                output.WriteLine();
                return 0;
            }

            line = line.Trim();

            if (line.Length == 0) continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var command = parts[0].ToLowerInvariant();
            var args = parts[1..];

            switch (command)
            {
                case "quit":
                case "exit":
                    return 0;

                case "help":
                    PrintHelp(output);
                    break;

                case "deps":
                    PrintDeps(graph, args, output, error);
                    break;

                case "dependents":
                    PrintDependents(graph, args, output, error);
                    break;

                case "path":
                    PrintPath(graph, args, output, error);
                    break;

                case "info":
                    PrintInfo(graph, args, output, error);
                    break;

                case "affected":
                    PrintAffected(graph, args, output, error);
                    break;

                case "tree":
                    PrintTree(graph, args, output, error);
                    break;

                default:
                    error.WriteLine($"Unknown command: {command}");
                    error.WriteLine("  Type 'help' to see available commands.");
                    break;
            }
        }
    }

    static void PrintHelp(TextWriter output)
    {
        output.WriteLine("Available commands:");
        output.WriteLine("  deps <project>        Show direct dependencies of a project");
        output.WriteLine("  dependents <project>  Show direct dependents of a project");
        output.WriteLine("  path <from> <to>      Show dependency path between two packages");
        output.WriteLine("  info <package>        Show details for a specific package");
        output.WriteLine("  affected [--from <ref>]  Show affected projects (by change or all)");
        output.WriteLine("  tree <project> [--depth N]  Show dependency tree for a project");
        output.WriteLine("  help                  Show this help message");
        output.WriteLine("  quit, exit            Exit the REPL");
    }

    static void PrintDeps(MonorepoGraph graph, string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 1)
        {
            error.WriteLine("Usage: deps <project-id>");
            return;
        }

        var pkgId = args[0];
        var node = FindNode(graph, pkgId);
        if (node == null)
        {
            error.WriteLine($"Package not found: {pkgId}");
            return;
        }

        foreach (var dep in node.Dependencies)
        {
            var proj = graph.Nodes.GetValueOrDefault(dep.To)?.Project;
            var depPkg = proj?.PackageId ?? dep.To;
            output.WriteLine(depPkg);
        }
    }

    static void PrintDependents(MonorepoGraph graph, string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 1)
        {
            error.WriteLine("Usage: dependents <project-id>");
            return;
        }

        var pkgId = args[0];
        var node = FindNode(graph, pkgId);
        if (node == null)
        {
            error.WriteLine($"Package not found: {pkgId}");
            return;
        }

        foreach (var dep in node.Dependents)
        {
            var proj = graph.Nodes.GetValueOrDefault(dep.From)?.Project;
            var depPkg = proj?.PackageId ?? dep.From;
            output.WriteLine(depPkg);
        }
    }

    static GraphNode? FindNode(MonorepoGraph graph, string packageId)
    {
        return graph.Nodes.Values.FirstOrDefault(n =>
            n.Project.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase));
    }

        static void PrintPath(MonorepoGraph graph, string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 2)
        {
            error.WriteLine("Usage: path <from-package> <to-package>");
            return;
        }

        var fromPkg = args[0];
        var toPkg = args[1];

        // Find nodes matching the given package IDs
        var fromNode = FindNode(graph, fromPkg);
        var toNode = FindNode(graph, toPkg);

        if (fromNode == null)
        {
            error.WriteLine($"Package not found: {fromPkg}");
            return;
        }
        if (toNode == null)
        {
            error.WriteLine($"Package not found: {toPkg}");
            return;
        }

        // Same project: print just the name
        if (fromNode.Project.Path == toNode.Project.Path)
        {
            output.WriteLine(fromNode.Project.PackageId);
            return;
        }

        // BFS to find a path following dependency direction
        var visited = new HashSet<string>();
        var queue = new Queue<(string Path, List<string> Route)>();
        queue.Enqueue((fromNode.Project.Path, [fromNode.Project.PackageId]));
        visited.Add(fromNode.Project.Path);

        while (queue.Count > 0)
        {
            var (currentPath, route) = queue.Dequeue();

            if (currentPath == toNode.Project.Path)
            {
                output.WriteLine(string.Join(" -> ", route));
                return;
            }

            if (graph.Nodes.TryGetValue(currentPath, out var currentNode))
            {
                // Follow dependency edges only (from -> each subsequent node is depended on by the previous one)
                foreach (var dep in currentNode.Dependencies)
                {
                    if (!visited.Contains(dep.To) && graph.Nodes.ContainsKey(dep.To))
                    {
                        visited.Add(dep.To);
                        var depProj = graph.Nodes[dep.To].Project;
                        var newRoute = new List<string>(route) { depProj.PackageId };
                        queue.Enqueue((dep.To, newRoute));
                    }
                }
            }
        }

        output.WriteLine("No path found between the specified packages.");
    }

    static void PrintInfo(MonorepoGraph graph, string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 1)
        {
            error.WriteLine("Usage: info <package-id>");
            return;
        }

        var pkgId = args[0];
        var node = FindNode(graph, pkgId);

        if (node == null)
        {
            error.WriteLine($"Package not found: {pkgId}");
            return;
        }

        var p = node.Project;
        output.WriteLine($"Package:         {p.PackageId}");
        output.WriteLine($"Version:         {p.Version.Major}.{p.Version.Minor}.{p.Version.Patch}");
        output.WriteLine($"Path:            {p.Path}");
        output.WriteLine($"Packable:        {p.IsPackable}");
        output.WriteLine($"Test project:    {p.IsTestProject}");
        output.WriteLine($"Depth:           {node.Depth}");
        output.WriteLine($"Target Fx:       {string.Join(", ", p.TargetFrameworks.Select(t => t.Moniker))}");
        output.WriteLine($"Dependencies:    {node.Dependencies.Length}");
        output.WriteLine($"Dependents:      {node.Dependents.Length}");
    }

    static void PrintAffected(MonorepoGraph graph, string[] args, TextWriter output, TextWriter error)
    {
        // Parse --from <ref> argument
        var baseRef = "HEAD~1";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--from" && i + 1 < args.Length)
                baseRef = args[i + 1];
        }

        // Get changed files from git
        var (changedFiles, gitErr) = Analyzer.GetChangedFiles(graph.RepoRoot, baseRef);

        if (gitErr != null)
        {
            error.WriteLine($"Warning: git diff failed: {gitErr}");
        }

        // Build affected set
        var affected = Analyzer.BuildAffectedSet(changedFiles, graph);

        if (affected.DirectlyAffected.Length == 0 && affected.TransitivelyAffected.Length == 0)
        {
            output.WriteLine("No affected projects.");
            return;
        }

        output.WriteLine("Directly affected:");
        foreach (var proj in affected.DirectlyAffected.OrderBy(p => p.PackageId))
        {
            output.WriteLine($"  {proj.PackageId}");
        }

        if (affected.TransitivelyAffected.Length > 0)
        {
            output.WriteLine("Transitively affected:");
            foreach (var proj in affected.TransitivelyAffected.OrderBy(p => p.PackageId))
            {
                output.WriteLine($"  {proj.PackageId}");
            }
        }

        output.WriteLine($"Total: {affected.DirectlyAffected.Length + affected.TransitivelyAffected.Length} project(s)");
    }

    static void PrintTree(MonorepoGraph graph, string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 1)
        {
            error.WriteLine("Usage: tree <project-id> [--depth N]");
            return;
        }

        var pkgId = args[0];
        var node = FindNode(graph, pkgId);
        if (node == null)
        {
            error.WriteLine($"Package not found: {pkgId}");
            return;
        }

        // Parse --depth flag
        var maxDepth = 3;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--depth" && i + 1 < args.Length)
            {
                if (int.TryParse(args[i + 1], out var d) && d >= 0)
                    maxDepth = d;
                else
                    error.WriteLine($"Warning: invalid depth '{args[i + 1]}', using default 3");
            }
        }

        var visited = new HashSet<string>();
        PrintSubtree(node, graph, output, "", true, maxDepth, 0, visited);
    }

    static void PrintSubtree(GraphNode node, MonorepoGraph graph, TextWriter output, string prefix, bool isLast, int maxDepth, int currentDepth, HashSet<string> visited)
    {
        // Guard against cycles
        if (!visited.Add(node.Project.Path))
        {
            output.WriteLine($"{prefix}{ConnectorFor(isLast)}(cycle) {node.Project.PackageId}");
            return;
        }

        var connector = ConnectorFor(isLast);
        output.WriteLine($"{prefix}{connector}{node.Project.PackageId}  ({node.Project.Path})");

        // Stop recursion at max depth (or if no children)
        if (currentDepth >= maxDepth)
            return;

        var children = node.Dependencies
            .Select(d => graph.Nodes.GetValueOrDefault(d.To))
            .Where(n => n != null)
            .Cast<GraphNode>()
            .OrderBy(n => n.Project.PackageId)
            .ToList();

        for (int i = 0; i < children.Count; i++)
        {
            var childPrefix = prefix + (isLast ? "    " : "│   ");
            PrintSubtree(children[i], graph, output, childPrefix, i == children.Count - 1, maxDepth, currentDepth + 1, visited);
        }
    }

    static string ConnectorFor(bool isLast) => isLast ? "└── " : "├── ";
}
