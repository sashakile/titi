// titi.swap — Reference swap engine
// PackageRef → ProjectRef conversion with cycle detection (Kahn's algorithm + DFS back-edge)

namespace titi.Swap;

using System.Collections.Generic;

public static class SwapEngine
{
    /// <summary>Compute a reference swap for the given targets.</summary>
    public static SwapResult Compute(
        MonorepoGraph graph,
        string[] targets,
        VersionPolicy versionPolicy,
        bool includeTransitive,
        string prefix,
        string sourceRoot)
    {
        var swapped = new List<SwappedRef>();
        var retained = new List<RetainedRef>();
        var cycles = new List<CycleReport>();

        foreach (var target in targets)
        {
            // Find local source by looking up the actual project path from the graph
            var projectNode = graph.Nodes.Values.FirstOrDefault(n =>
                n.Project.PackageId.Equals(target, StringComparison.OrdinalIgnoreCase));

            if (projectNode == null)
            {
                retained.Add(new RetainedRef(
                    PackageId: target,
                    Reason: RetainedReason.NoLocalSource,
                    Detail: $"No project with PackageId '{target}' found in graph"
                ));
                continue;
            }

            var localSourcePath = projectNode.Project.Path;
            if (!File.Exists(localSourcePath))
            {
                retained.Add(new RetainedRef(
                    PackageId: target,
                    Reason: RetainedReason.NoLocalSource,
                    Detail: $"No .csproj found at {localSourcePath}"
                ));
                continue;
            }

            // Build proposed edges for cycle check
            var consumers = FindConsumers(graph, target);
            var proposedEdges = new List<(string From, string To)>();

            foreach (var consumer in consumers)
            {
                proposedEdges.Add((consumer, localSourcePath));
            }

            // Check for cycles using Kahn's algorithm
            var cycle = DetectCycle(graph, proposedEdges, localSourcePath);
            if (cycle != null)
            {
                cycles.Add(cycle);
                retained.Add(new RetainedRef(
                    PackageId: target,
                    Reason: RetainedReason.CyclePrevention,
                    Detail: $"Cycle detected: {string.Join(" → ", cycle.Cycle)}"
                ));
                continue;
            }

            var localVersion = projectNode.Project.Version
                ?? new SemanticVersion(1, 0, 0, null, null);

            swapped.Add(new SwappedRef(
                PackageId: target,
                FromVersion: "",
                LocalSourcePath: localSourcePath,
                LocalVersion: localVersion,
                Consumers: consumers.ToArray()
            ));
        }

        return new SwapResult(
            Swapped: swapped.ToArray(),
            Retained: retained.ToArray(),
            Cycles: cycles.ToArray(),
            MsbuildContext: new MSBuildContext(
                InTitiContext: "true",
                TitiPrefix: prefix,
                TitiSourceRoot: sourceRoot,
                AdditionalProps: []
            )
        );
    }

    /// <summary>Find all graph nodes that consume a given package via PackageReference.</summary>
    static List<string> FindConsumers(MonorepoGraph graph, string packageId)
    {
        var consumers = new List<string>();
        foreach (var (path, node) in graph.Nodes)
        {
            if (node.Project.PackageRefs.Any(r =>
                r.PackageId.Equals(packageId, StringComparison.OrdinalIgnoreCase)))
            {
                consumers.Add(path);
            }
        }
        return consumers;
    }

    /// <summary>Kahn's algorithm + DFS back-edge detection for cycle checking.</summary>
    static CycleReport? DetectCycle(
        MonorepoGraph graph,
        List<(string From, string To)> proposedEdges,
        string targetPath)
    {
        // Build adjacency list including proposed edges
        var adj = new Dictionary<string, List<string>>();
        var inDegree = new Dictionary<string, int>();

        foreach (var (path, node) in graph.Nodes)
        {
            if (!adj.ContainsKey(path))
                adj[path] = [];
            if (!inDegree.ContainsKey(path))
                inDegree[path] = 0;

            foreach (var dep in node.Dependencies)
            {
                adj[path].Add(dep.To);
            }
        }

        foreach (var (from, to) in proposedEdges)
        {
            if (!adj.ContainsKey(from))
                adj[from] = [];
            if (!adj.ContainsKey(to))
                adj[to] = [];
            if (!inDegree.ContainsKey(from))
                inDegree[from] = 0;
            if (!inDegree.ContainsKey(to))
                inDegree[to] = 0;

            adj[from].Add(to);
        }

        // Compute in-degrees
        foreach (var (u, neighbors) in adj)
        {
            foreach (var v in neighbors)
            {
                inDegree[v] = inDegree.GetValueOrDefault(v, 0) + 1;
            }
        }

        // Kahn's algorithm
        var queue = new Queue<string>();
        foreach (var (node, deg) in inDegree)
        {
            if (deg == 0) queue.Enqueue(node);
        }

        var sortedCount = 0;
        while (queue.Count > 0)
        {
            var u = queue.Dequeue();
            sortedCount++;

            if (adj.TryGetValue(u, out var neighbors))
            {
                foreach (var v in neighbors)
                {
                    inDegree[v]--;
                    if (inDegree[v] == 0) queue.Enqueue(v);
                }
            }
        }

        if (sortedCount == adj.Count)
            return null; // No cycle

        // DFS back-edge detection for cycle reporting
        var cycle = DfsFindBackEdge(adj, targetPath);
        return new CycleReport(
            Cycle: cycle.ToArray(),
            EdgesToPreserve: [],
            Diagnostic: $"Swap would introduce a circular dependency involving {targetPath}"
        );
    }

    static List<string> DfsFindBackEdge(Dictionary<string, List<string>> adj, string start)
    {
        var visited = new HashSet<string>();
        var recStack = new HashSet<string>();
        var path = new List<string>();
        var cycle = new List<string>();

        bool Dfs(string node)
        {
            visited.Add(node);
            recStack.Add(node);
            path.Add(node);

            if (adj.TryGetValue(node, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    if (!visited.Contains(neighbor))
                    {
                        if (Dfs(neighbor))
                        {
                            if (cycle.Count > 0 && cycle[0] == cycle[^1])
                                return true;
                            if (recStack.Contains(neighbor))
                            {
                                cycle.Add(neighbor);
                                return true;
                            }
                            return true;
                        }
                    }
                    else if (recStack.Contains(neighbor))
                    {
                        // Found back edge — extract cycle
                        var idx = path.IndexOf(neighbor);
                        cycle = [.. path.Skip(idx), neighbor];
                        return true;
                    }
                }
            }

            recStack.Remove(node);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        Dfs(start);
        return cycle;
    }
}
