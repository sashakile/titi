// titi.graph — Pure data transformations on the dependency graph
// Receives converted ProjectDescriptor records, operates on immutable data only.

namespace titi.Graph;

using Microsoft.Build.Graph;

public static class GraphBuilder
{
    /// <summary>Build a MonorepoGraph from MSBuild ProjectGraph + project descriptors.</summary>
    public static MonorepoGraph Build(
        ProjectGraph msbuildGraph,
        Dictionary<string, ProjectDescriptor> descriptors,
        string repoRoot)
    {
        var nodes = new Dictionary<string, GraphNode>();
        var topoOrder = new List<string>();

        foreach (var msNode in msbuildGraph.ProjectNodesTopologicallySorted)
        {
            var path = msNode.ProjectInstance.FullPath;
            if (!descriptors.TryGetValue(path, out var desc))
                continue;

            topoOrder.Add(path);

            var deps = new List<GraphEdge>();
            var dependents = new List<GraphEdge>();

            foreach (var refPath in msNode.ProjectReferences.Select(r => r.ProjectInstance.FullPath))
            {
                if (descriptors.ContainsKey(refPath))
                {
                    deps.Add(new GraphEdge(
                        From: path,
                        To: refPath,
                        Mode: ReferenceMode.Binary,
                        VersionRange: null,
                        IsTransitive: false
                    ));
                }
            }

            nodes[path] = new GraphNode(
                Project: desc,
                Dependencies: deps.ToArray(),
                Dependents: [],  // filled below
                Depth: 0
            );
        }

        // Fill dependents
        foreach (var (path, node) in nodes)
        {
            foreach (var dep in node.Dependencies)
            {
                if (nodes.TryGetValue(dep.To, out var depNode))
                {
                    var edge = new GraphEdge(
                        From: dep.To,
                        To: path,
                        Mode: dep.Mode,
                        VersionRange: dep.VersionRange,
                        IsTransitive: dep.IsTransitive
                    );
                    nodes[dep.To] = depNode with
                    {
                        Dependents = [.. depNode.Dependents, edge]
                    };
                }
            }
        }

        // Compute depth via BFS from entry points (nodes with no dependents)
        ComputeDepths(nodes, topoOrder);

        return new MonorepoGraph(
            Nodes: nodes,
            TopologicalOrder: [.. topoOrder],
            RepoRoot: repoRoot,
            BuiltAt: DateTime.UtcNow,
            Fingerprints: []
        );
    }

    static void ComputeDepths(Dictionary<string, GraphNode> nodes, List<string> topoOrder)
    {
        var depths = new Dictionary<string, int>();
        foreach (var path in topoOrder)
        {
            var node = nodes[path];
            int maxDepth = 0;
            foreach (var dep in node.Dependencies)
            {
                if (depths.TryGetValue(dep.To, out var d))
                    maxDepth = Math.Max(maxDepth, d + 1);
            }
            depths[path] = maxDepth;
            nodes[path] = node with { Depth = maxDepth };
        }
    }
}
