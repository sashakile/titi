// titi repl — Interactive REPL for exploring the dependency graph
// CLI-16 implementation

namespace titi.Repl;

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
                    PrintDeps(graph, output);
                    break;

                case "dependents":
                    PrintDependents(graph, output);
                    break;

                case "path":
                    PrintPath(graph, args, output, error);
                    break;

                case "info":
                    PrintInfo(graph, args, output, error);
                    break;

                case "affected":
                    PrintAffected(graph, output);
                    break;

                case "tree":
                    PrintTree(graph, output);
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
        output.WriteLine("  deps                  Show all project dependencies");
        output.WriteLine("  dependents            Show all project dependents");
        output.WriteLine("  path <from> <to>      Show dependency path between two packages");
        output.WriteLine("  info <package>        Show details for a specific package");
        output.WriteLine("  affected              Show all projects (affected by any change)");
        output.WriteLine("  tree                  Show dependency tree");
        output.WriteLine("  help                  Show this help message");
        output.WriteLine("  quit, exit            Exit the REPL");
    }

    static void PrintDeps(MonorepoGraph graph, TextWriter output)
    {
        foreach (var node in graph.Nodes.Values.OrderBy(n => n.Project.PackageId))
        {
            var pkgId = node.Project.PackageId;
            output.WriteLine($"{pkgId}:");
            if (node.Dependencies.Length == 0)
            {
                output.WriteLine("  (no dependencies)");
            }
            else
            {
                foreach (var dep in node.Dependencies.OrderBy(d => d.To))
                {
                    var proj = graph.Nodes.GetValueOrDefault(dep.To)?.Project;
                    var depPkg = proj?.PackageId ?? dep.To;
                    output.WriteLine($"  -> {depPkg} [{dep.Mode}]");
                }
            }
            output.WriteLine();
        }
    }

    static void PrintDependents(MonorepoGraph graph, TextWriter output)
    {
        foreach (var node in graph.Nodes.Values.OrderBy(n => n.Project.PackageId))
        {
            var pkgId = node.Project.PackageId;
            output.WriteLine($"{pkgId}:");
            if (node.Dependents.Length == 0)
            {
                output.WriteLine("  (no dependents)");
            }
            else
            {
                foreach (var dep in node.Dependents.OrderBy(d => d.From))
                {
                    var proj = graph.Nodes.GetValueOrDefault(dep.From)?.Project;
                    var depPkg = proj?.PackageId ?? dep.From;
                    output.WriteLine($"  <- {depPkg} [{dep.Mode}]");
                }
            }
            output.WriteLine();
        }
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
        var fromNode = graph.Nodes.Values.FirstOrDefault(n =>
            n.Project.PackageId.Equals(fromPkg, StringComparison.OrdinalIgnoreCase));
        var toNode = graph.Nodes.Values.FirstOrDefault(n =>
            n.Project.PackageId.Equals(toPkg, StringComparison.OrdinalIgnoreCase));

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
                // Follow dependencies (forward direction)
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

                // Also follow dependents (reverse direction)
                foreach (var dep in currentNode.Dependents)
                {
                    if (!visited.Contains(dep.From) && graph.Nodes.ContainsKey(dep.From))
                    {
                        visited.Add(dep.From);
                        var depProj = graph.Nodes[dep.From].Project;
                        var newRoute = new List<string>(route) { depProj.PackageId };
                        queue.Enqueue((dep.From, newRoute));
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
        var node = graph.Nodes.Values.FirstOrDefault(n =>
            n.Project.PackageId.Equals(pkgId, StringComparison.OrdinalIgnoreCase));

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

    static void PrintAffected(MonorepoGraph graph, TextWriter output)
    {
        // In the REPL, without git context, all projects are considered "affected"
        foreach (var node in graph.Nodes.Values.OrderBy(n => n.Project.PackageId))
        {
            output.WriteLine($"{node.Project.PackageId}");
            output.WriteLine($"  Depth: {node.Depth}");
            output.WriteLine($"  Path: {node.Project.Path}");
            output.WriteLine($"  Type: {(node.Project.IsTestProject ? "test" : "library")}");
            output.WriteLine();
        }
        output.WriteLine($"Total: {graph.Nodes.Count} project(s)");
    }

    static void PrintTree(MonorepoGraph graph, TextWriter output)
    {
        // Find root nodes (depth 0) and print their subtrees
        var roots = graph.Nodes.Values
            .Where(n => n.Depth == 0)
            .OrderBy(n => n.Project.PackageId);

        foreach (var root in roots)
        {
            PrintSubtree(root, graph, output, "", true);
        }

        // If no depth-0 nodes, show all topologically
        if (!roots.Any())
        {
            foreach (var node in graph.Nodes.Values.OrderBy(n => n.Project.PackageId))
            {
                output.WriteLine($"{node.Project.PackageId}");
            }
        }
    }

    static void PrintSubtree(GraphNode node, MonorepoGraph graph, TextWriter output, string prefix, bool isLast, HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>();

        // Guard against cycles — the graph should be a DAG, but defensive
        if (!visited.Add(node.Project.Path))
        {
            output.WriteLine($"{prefix}{ConnectorFor(isLast)}(cycle) {node.Project.PackageId}");
            return;
        }

        var connector = ConnectorFor(isLast);
        output.WriteLine($"{prefix}{connector}{node.Project.PackageId}  ({node.Project.Path})");

        var children = node.Dependencies
            .Select(d => graph.Nodes.GetValueOrDefault(d.To))
            .Where(n => n != null)
            .Cast<GraphNode>()
            .OrderBy(n => n.Project.PackageId)
            .ToList();

        for (int i = 0; i < children.Count; i++)
        {
            var childPrefix = prefix + (isLast ? "    " : "│   ");
            PrintSubtree(children[i], graph, output, childPrefix, i == children.Count - 1, visited);
        }
    }

    static string ConnectorFor(bool isLast) => isLast ? "└── " : "├── ";
}
