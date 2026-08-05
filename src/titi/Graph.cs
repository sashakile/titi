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

        // Build a package-id → project-path index for PackageReference resolution.
        // The first project with a matching PackageId wins (no duplicates expected).
        var packageIndex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, desc) in descriptors)
        {
            if (!string.IsNullOrEmpty(desc.PackageId))
                packageIndex.TryAdd(desc.PackageId, path);
        }

        foreach (var msNode in msbuildGraph.ProjectNodesTopologicallySorted)
        {
            var path = msNode.ProjectInstance.FullPath;
            if (!descriptors.TryGetValue(path, out var desc))
                continue;

            topoOrder.Add(path);

            var deps = new List<GraphEdge>();
            var dependents = new List<GraphEdge>();

            // ── ProjectReference edges ────────────────────────────────
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

            // ── PackageReference edges (titi-2uk) ─────────────────────
            // Internal PackageReferences are matched by PackageId against
            // the package index. The identity policy is: PackageId property
            // matching (already established by Swap.cs).
            foreach (var pkgRef in desc.PackageRefs)
            {
                if (packageIndex.TryGetValue(pkgRef.PackageId, out var internalPath))
                {
                    // Skip if this is a self-reference (package referencing itself)
                    if (internalPath == path)
                        continue;

                    // Version compatibility check: semver-compatible by default.
                    // Major-version mismatches suppress the edge.
                    if (IsVersionCompatible(pkgRef, desc))
                    {
                        deps.Add(new GraphEdge(
                            From: path,
                            To: internalPath,
                            Mode: ReferenceMode.Binary,
                            VersionRange: pkgRef.VersionRange,
                            IsTransitive: false
                        ));
                    }
                }
                // Package references with no matching project are external
                // NuGet packages — silently ignored.
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

    /// <summary>
    /// Check whether a PackageReference is version-compatible with its
    /// matching internal project. Semver-compatible by default: major-version
    /// mismatches suppress the edge.
    /// </summary>
    internal static bool IsVersionCompatible(PackageRef pkgRef, ProjectDescriptor project)
    {
        if (string.IsNullOrEmpty(pkgRef.VersionRange) || pkgRef.VersionRange == "*")
            return true;

        // Parse the version from the range string (handles ranges like
        // "1.0.0", "[1.0.0, 2.0.0)", "1.0.0-*", etc.)
        var rangeStr = pkgRef.VersionRange
            .TrimStart('[').TrimStart('(')  // strip range brackets
            .Split(',', StringSplitOptions.TrimEntries)[0]  // take lower bound
            .Split('-')[0];  // strip prerelease suffix

        var parts = rangeStr.Split('.');
        if (parts.Length > 0 && int.TryParse(parts[0], out var refMajor))
        {
            // Major version mismatch → suppress (incompatible by semver rules)
            if (refMajor != project.Version.Major)
                return false;
        }
        return true;
    }
}
