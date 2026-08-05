// titi.affected — Git diff integration and impact analysis
// Maps changed files to affected projects via the dependency graph

namespace titi.Affected;

public static class Analyzer
{
    /// <summary>Build AffectedSet from changed files and the dependency graph.</summary>
    /// <param name="changedFiles">List of changed file paths (relative to repo root), or null for fallback (all projects affected).</param>
    /// <param name="graph">The monorepo dependency graph.</param>
    /// <returns>The computed AffectedSet.</returns>
    public static AffectedSet BuildAffectedSet(string[]? changedFiles, MonorepoGraph graph)
    {
        // Fallback: when no git info available, all projects are directly affected
        if (changedFiles is null)
        {
            return new AffectedSet(
                ChangedFiles: [],
                DirectlyAffected: graph.Nodes.Values.Select(n => n.Project).ToArray(),
                TransitivelyAffected: [],
                AffectedTests: new TieredTestSet([], [], [], [])
            );
        }

        // No changes at all
        if (changedFiles.Length == 0)
        {
            return new AffectedSet(
                ChangedFiles: [],
                DirectlyAffected: [],
                TransitivelyAffected: [],
                AffectedTests: new TieredTestSet([], [], [], [])
            );
        }

        var directlySet = new HashSet<string>();
        var transitiveSet = new HashSet<string>();
        var resolvedFiles = new List<string>();
        var projectIndex = new Dictionary<string, (string Path, ProjectDescriptor Project)>();

        // Build a path→project mapping (check SourceDir and path prefixes)
        foreach (var (path, node) in graph.Nodes)
        {
            var srcDir = node.Project.Properties.GetValueOrDefault("SourceDir", "");
            if (!string.IsNullOrEmpty(srcDir))
            {
                try
                {
                    var relPath = Path.GetRelativePath(graph.RepoRoot, srcDir);
                    // Index by both SourceDir path and .csproj directory path
                    if (!projectIndex.ContainsKey(relPath))
                        projectIndex[relPath] = (path, node.Project);
                }
                catch (ArgumentException)
                {
                    // Paths on different volumes or incompatible roots — skip SourceDir indexing
                }
            }

            // Also index by directory of the .csproj
            var projDir = Path.GetDirectoryName(path) ?? "";
            try
            {
                var relProjDir = Path.GetRelativePath(graph.RepoRoot, projDir);
                if (!projectIndex.ContainsKey(relProjDir))
                    projectIndex[relProjDir] = (path, node.Project);
            }
            catch (ArgumentException)
            {
                // Skip if path isn't relative to repoRoot
            }
        }

        foreach (var changedFile in changedFiles)
        {
            var matched = false;
            foreach (var (dir, (nodePath, proj)) in projectIndex)
            {
                if (changedFile.StartsWith(dir, StringComparison.OrdinalIgnoreCase)
                    || dir.StartsWith(changedFile, StringComparison.OrdinalIgnoreCase))
                {
                    directlySet.Add(nodePath);
                    matched = true;
                    resolvedFiles.Add(changedFile);
                    break;
                }
            }

            // Fallback: try matching by filename similarity
            if (!matched)
            {
                var changedName = Path.GetFileNameWithoutExtension(changedFile);
                foreach (var (nodePath, proj) in graph.Nodes.Select(kv => (kv.Key, kv.Value.Project)))
                {
                    if (proj.PackageId.Contains(changedName, StringComparison.OrdinalIgnoreCase)
                        || proj.Path.Contains(changedName, StringComparison.OrdinalIgnoreCase))
                    {
                        directlySet.Add(nodePath);
                        resolvedFiles.Add(changedFile);
                        break;
                    }
                }
            }
        }

        // Compute transitive closure: all downstream consumers of directly affected projects
        var visited = new HashSet<string>(directlySet);
        var queue = new Queue<string>(directlySet);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!graph.Nodes.TryGetValue(current, out var node))
                continue;

            foreach (var dep in node.Dependents)
            {
                // dep.To is the consumer, dep.From is the dependency
                if (visited.Add(dep.To))
                {
                    transitiveSet.Add(dep.To);
                    queue.Enqueue(dep.To);
                }
            }
        }

        return new AffectedSet(
            ChangedFiles: changedFiles,
            DirectlyAffected: directlySet.Select(p => graph.Nodes[p].Project).ToArray(),
            TransitivelyAffected: transitiveSet.Select(p => graph.Nodes[p].Project).ToArray(),
            AffectedTests: new TieredTestSet([], [], [], [])
        )
        {
            ResolvedFiles = resolvedFiles.ToArray()
        };
    }

    /// <summary>Run git diff to get changed files between two refs.</summary>
    public static (string[]? Files, string? Error) GetChangedFiles(string repoRoot, string? baseRef)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"diff --name-only {(baseRef ?? "HEAD~1")}..HEAD",
                WorkingDirectory = repoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null)
                return (null, "Failed to start git process");

            // Read stdout and stderr concurrently to avoid deadlock
            var outputTask = proc.StandardOutput.ReadToEndAsync();
            var errorTask = proc.StandardError.ReadToEndAsync();
            proc.WaitForExit();

            var output = outputTask.Result;
            var error = errorTask.Result;

            if (proc.ExitCode != 0)
                return (null, $"git exited with code {proc.ExitCode}: {error.Trim()}");

            var files = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // A successful diff with zero changed files is an empty array, NOT null.
            // null is reserved for the unavailable-git/fallback signal so affected
            // analysis selects every project only on real git failure, not on a
            // clean no-change invocation.
            return (files, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }
}
