// TID-0ej: Pure static dependency edge analysis
//
// Produces test→source edges without running tests. Two levels:
//   Level 1 — Project references (.csproj <ProjectReference>) — test method × source file cross-product
//   Level 2 — Using statement analysis — test file × source file based on namespace match
//
// Level 1 is an over-approximation (every test method in a project gets edges
// to every source file in every referenced project), while Level 2 is more
// precise (only test files whose using-directives match a source project's
// namespace get edges to that project's source files).
//
// SAFE-001: Both levels over-approximate (never miss a real dependency) —
// a test file that uses a type from a referenced project always has either a
// project reference (L1) or a using directive (L2).
//
// Edge weights (lower than coverage edges at 1_000_000 in EdgeBuilder.cs):
//   L1: 500_000 — project reference cross-product
//   L2: 800_000 — using-statement match (more precise, higher weight)
//
// When no test items are available (discoveredTests is null/empty), edges use
// synthetic From prefixes: "$pkg:{PackageId}" for L1, "$file:{path}" for L2.

namespace titi;

using System.Text.RegularExpressions;

public static class StaticEdgeAnalyzer
{
    // Regex for `using Namespace;` or `using static Namespace.Type;`
    // Captures the namespace portion before the optional .Type suffix for static using.
    private static readonly Regex UsingRegex = new(
        @"^\s*using\s+(?:static\s+)?([^;]+?)(?:\s*;)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Regex for file-scoped namespace declaration: `namespace X.Y.Z;`
    private static readonly Regex NamespaceDeclRegex = new(
        @"^\s*namespace\s+([^\s{;]+)(?:\s*[\{;])?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Weight hierarchy: coverage edges (EdgeBuilder) = 1_000_000 > L2 = 800_000 > L1 = 500_000
    private const long WeightL1ProjectRefs = 500_000;
    private const long WeightL2UsingStatements = 800_000;

    /// <summary>
    /// Run both levels of static analysis and return merged edges.
    /// When <paramref name="discoveredTests"/> is null or empty, returns
    /// synthetic edges prefixed with <c>$pkg:</c> (L1) or <c>$file:</c> (L2)
    /// — no test-method granularity.
    /// Level 2 (using-statements) is preferred over Level 1 (project-refs)
    /// when both produce the same (From, To) pair — deduplicated via HashSet.
    /// </summary>
    public static TestToSourceEdge[] AnalyzeAll(
        MonorepoGraph graph,
        Dictionary<string, TestItem[]>? discoveredTests)
    {
        if (graph.Nodes.Count == 0)
            return [];

        var l1Edges = AnalyzeProjectReferences(graph, discoveredTests);
        var l2Edges = AnalyzeUsingStatements(graph, discoveredTests);

        // Merge: L2 is more precise, so emit L2 edges first; L1 edges
        // fill in gaps for (From, To) pairs that L2 didn't cover.
        var edgeSet = new HashSet<(string From, string To)>();
        var result = new List<TestToSourceEdge>(l2Edges.Length + l1Edges.Length);

        foreach (var edge in l2Edges)
        {
            if (edgeSet.Add((edge.From, edge.To)))
                result.Add(edge);
        }

        foreach (var edge in l1Edges)
        {
            if (edgeSet.Add((edge.From, edge.To)))
                result.Add(edge);
        }

        return result.ToArray();
    }

    // ── Level 1: Project references ──────────────────────────────

    /// <summary>
    /// For each test project, find all source projects it references via
    /// graph dependency edges. Emit an edge from each test item to every
    /// source file in each referenced project (method × file cross-product).
    ///
    /// When <paramref name="discoveredTests"/> is null or empty, uses
    /// synthetic package-level identifiers prefixed with "$pkg:" so the
    /// edges can still be used for project-level resolution.
    /// </summary>
    public static TestToSourceEdge[] AnalyzeProjectReferences(
        MonorepoGraph graph,
        Dictionary<string, TestItem[]>? discoveredTests)
    {
        var result = new List<TestToSourceEdge>();

        // Build fast lookup: project path → node
        var pathToNode = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        foreach (var (path, node) in graph.Nodes)
            pathToNode[path] = node;

        foreach (var (testProjectPath, testNode) in graph.Nodes)
        {
            if (!testNode.Project.IsTestProject)
                continue;

            var testPkgId = testNode.Project.PackageId;
            var testItems = discoveredTests?.GetValueOrDefault(testPkgId);

            // Find source projects this test project depends on (directly),
            // using graph dependencies where To is a source project.
            var sourceNodePaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var dep in testNode.Dependencies)
            {
                if (pathToNode.TryGetValue(dep.To, out var depNode))
                {
                    // Only source projects — skip test→test deps
                    if (!depNode.Project.IsTestProject)
                        sourceNodePaths.Add(dep.To);
                }
            }

            // For each source project, enumerate all .cs files
            var sourceFilesByPkg = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var srcPath in sourceNodePaths)
            {
                if (pathToNode.TryGetValue(srcPath, out var srcNode))
                {
                    var srcDir = Path.GetDirectoryName(srcPath) ?? "";
                    var files = EnumerateSourceFiles(srcDir);
                    sourceFilesByPkg[srcNode.Project.PackageId] = files;
                }
            }

            if (sourceFilesByPkg.Count == 0)
                continue;

            if (testItems != null && testItems.Length > 0)
            {
                // Method-level edges: each test method → each source file
                foreach (var item in testItems)
                {
                    foreach (var srcFiles in sourceFilesByPkg.Values)
                    {
                        foreach (var srcFile in srcFiles)
                        {
                            result.Add(new TestToSourceEdge(
                                From: item.TestId,
                                To: srcFile,
                                Origin: EdgeOrigin.Static,
                                Weight: WeightL1ProjectRefs,
                                LineRanges: []
                            ));
                        }
                    }
                }
            }
            else
            {
                // Project-level edges (no test-item granularity available)
                foreach (var srcFiles in sourceFilesByPkg.Values)
                {
                    foreach (var srcFile in srcFiles)
                    {
                        result.Add(new TestToSourceEdge(
                            From: $"$pkg:{testPkgId}",
                            To: srcFile,
                            Origin: EdgeOrigin.Static,
                            Weight: WeightL1ProjectRefs,
                            LineRanges: []
                        ));
                    }
                }
            }
        }

        return result.ToArray();
    }

    // ── Level 2: Using statement analysis ────────────────────────

    /// <summary>
    /// For each test project, scan test source files for <c>using Namespace;</c>
    /// statements. Build a namespace→file map from source projects (by parsing
    /// <c>namespace X.Y.Z;</c> declarations in their source files). Emit edges
    /// from each test item to source files whose namespace matches a using directive.
    ///
    /// When <paramref name="discoveredTests"/> is null or empty, uses
    /// synthetic file-level identifiers prefixed with "$file:".
    /// </summary>
    public static TestToSourceEdge[] AnalyzeUsingStatements(
        MonorepoGraph graph,
        Dictionary<string, TestItem[]>? discoveredTests)
    {
        var result = new List<TestToSourceEdge>();

        // Build namespace→file map from source projects
        var nsToFiles = BuildNamespaceMap(graph);
        if (nsToFiles.Count == 0)
            return [];

        foreach (var (testProjectPath, testNode) in graph.Nodes)
        {
            if (!testNode.Project.IsTestProject)
                continue;

            var testPkgId = testNode.Project.PackageId;
            var testItems = discoveredTests?.GetValueOrDefault(testPkgId);

            // Scan test source files for using directives
            var testDir = Path.GetDirectoryName(testProjectPath) ?? "";
            var testCsFiles = EnumerateSourceFiles(testDir);

            if (testCsFiles.Length == 0)
                continue;

            // Build using→files map per test source file
            var testFileUsings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var testFile in testCsFiles)
            {
                var usings = ParseUsingDirectives(testFile);
                if (usings.Count > 0)
                    testFileUsings[testFile] = usings;
            }

            if (testFileUsings.Count == 0)
                continue;

            if (testItems != null && testItems.Length > 0)
            {
                // Method-level edges: for each test item, find which test
                // source file it belongs to, then match using→namespace.
                foreach (var item in testItems)
                {
                    var matchedFile = FindTestFileForItem(item, testCsFiles, testDir);

                    if (matchedFile == null || !testFileUsings.TryGetValue(matchedFile, out var usings))
                        continue;

                    foreach (var usingNs in usings)
                    {
                        if (nsToFiles.TryGetValue(usingNs, out var matchedSrcFiles))
                        {
                            foreach (var srcFile in matchedSrcFiles)
                            {
                                result.Add(new TestToSourceEdge(
                                    From: item.TestId,
                                    To: srcFile,
                                    Origin: EdgeOrigin.Static,
                                    Weight: WeightL2UsingStatements,
                                    LineRanges: []
                                ));
                            }
                        }
                    }
                }
            }
            else
            {
                // File-level edges (no test-item granularity)
                foreach (var (testFile, usings) in testFileUsings)
                {
                    foreach (var usingNs in usings)
                    {
                        if (nsToFiles.TryGetValue(usingNs, out var matchedSrcFiles))
                        {
                            foreach (var srcFile in matchedSrcFiles)
                            {
                                result.Add(new TestToSourceEdge(
                                    From: $"$file:{testFile}",
                                    To: srcFile,
                                    Origin: EdgeOrigin.Static,
                                    Weight: WeightL2UsingStatements,
                                    LineRanges: []
                                ));
                            }
                        }
                    }
                }
            }
        }

        return result.ToArray();
    }

    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>
    /// Enumerate all <c>.cs</c> files in a project directory, excluding
    /// <c>obj/</c> and <c>bin/</c> subdirectories. Returns absolute paths.
    /// </summary>
    internal static string[] EnumerateSourceFiles(string projectDir)
    {
        if (!Directory.Exists(projectDir))
            return [];

        try
        {
            var files = Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var rel = Path.GetRelativePath(projectDir, f);
                    var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    return !segments.Any(s =>
                        s.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
                        s.Equals("bin", StringComparison.OrdinalIgnoreCase));
                })
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();
            return files;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Build a map from namespace → source file paths by scanning all
    /// source projects' <c>.cs</c> files for <c>namespace</c> declarations.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> BuildNamespaceMap(MonorepoGraph graph)
    {
        var nsToFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var (path, node) in graph.Nodes)
        {
            if (node.Project.IsTestProject)
                continue;

            var projDir = Path.GetDirectoryName(path) ?? "";
            var csFiles = EnumerateSourceFiles(projDir);

            foreach (var csFile in csFiles)
            {
                try
                {
                    var content = File.ReadAllText(csFile);
                    var match = NamespaceDeclRegex.Match(content);
                    if (match.Success)
                    {
                        var ns = match.Groups[1].Value.Trim();
                        if (!nsToFiles.ContainsKey(ns))
                            nsToFiles[ns] = new HashSet<string>(StringComparer.Ordinal);
                        nsToFiles[ns].Add(csFile);
                    }
                }
                catch
                {
                    // skip unreadable files
                }
            }
        }

        return nsToFiles;
    }

    /// <summary>
    /// Parse <c>using Namespace;</c> (and <c>using static Namespace.Type;</c>)
    /// directives from a C# source file. Returns the namespace portion of each
    /// using directive.
    ///
    /// Handles:
    ///   - <c>using static Namespace.Type</c>: extracts the namespace portion
    ///     (everything before the last dot-separated type name).
    ///   - <c>using static Foo</c> (single segment): skips — Foo is a type, not a namespace.
    ///   - <c>using Foo = Bar;</c> (alias): skips — not a namespace import.
    ///
    /// Filters out common system and test-framework namespaces (System.*, Xunit,
    /// NUnit, MSTest, etc.) that are never project-level dependencies.
    /// </summary>
    internal static HashSet<string> ParseUsingDirectives(string filePath)
    {
        var usings = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var content = File.ReadAllText(filePath);
            var matches = UsingRegex.Matches(content);

            foreach (Match m in matches)
            {
                var full = m.Groups[1].Value.Trim().TrimEnd('.');

                // Skip using-alias directives (e.g., `using Foo = Bar;`) — they
                // don't represent namespace imports (CORR-001).
                if (full.Contains('='))
                    continue;

                // For `using static Namespace.Type`, extract the namespace part
                // (everything except the final type name). Single-segment `using
                // static Foo` is a type, not a namespace — skip it (CORR-002).
                var segments = full.Split('.');
                if (segments.Length > 1)
                {
                    if (m.Value.Contains("using static "))
                    {
                        var ns = string.Join(".", segments.Take(segments.Length - 1));
                        if (!string.IsNullOrEmpty(ns))
                            usings.Add(ns);
                        continue;
                    }
                }
                else if (m.Value.Contains("using static "))
                {
                    // Single-segment `using static Foo` — Foo is a type, skip.
                    continue;
                }

                usings.Add(full);
            }

            // Remove common system/third-party namespaces that are not project-level
            usings.RemoveWhere(ns =>
                ns.StartsWith("System", StringComparison.Ordinal) ||
                ns.StartsWith("Microsoft", StringComparison.Ordinal) ||
                ns.StartsWith("Xunit", StringComparison.Ordinal) ||
                ns.StartsWith("NUnit", StringComparison.Ordinal) ||
                ns.StartsWith("MSTest", StringComparison.Ordinal) ||
                ns.StartsWith("NUnit3TestAdapter", StringComparison.Ordinal) ||
                ns.StartsWith("Coverlet", StringComparison.Ordinal));
        }
        catch
        {
            // skip unreadable files
        }

        return usings;
    }

    /// <summary>
    /// Try to find which test source file a <see cref="TestItem"/> belongs to
    /// by matching the class name against file contents. Returns <c>null</c>
    /// when no match is found — the caller degrades gracefully (L2 edges
    /// omitted for that item, L1 edges still apply).
    /// </summary>
    internal static string? FindTestFileForItem(
        TestItem item,
        string[] testCsFiles,
        string testProjectDir)
    {
        // Try matching by SourceFile field first
        if (!string.IsNullOrEmpty(item.SourceFile))
        {
            var fullPath = Path.IsPathRooted(item.SourceFile)
                ? item.SourceFile
                : Path.Combine(testProjectDir, item.SourceFile);
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Try matching class name against file contents
        var className = item.ClassName;
        if (!string.IsNullOrEmpty(className))
        {
            var shortName = className.Split('.').LastOrDefault() ?? className;
            foreach (var file in testCsFiles)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    if (content.Contains($"class {shortName}", StringComparison.Ordinal) ||
                        content.Contains($"record {shortName}", StringComparison.Ordinal) ||
                        content.Contains($"struct {shortName}", StringComparison.Ordinal))
                    {
                        return file;
                    }
                }
                catch
                {
                    // skip unreadable files
                }
            }
        }

        // No match found — return null so caller falls back to L1-only coverage
        return null;
    }

    /// <summary>
    /// Persist static edges to the edge cache alongside coverage edges.
    /// Writes to <c>.titi/test-cache/edges/static-edges.json</c>.
    ///
    /// Note: No file locking is used, consistent with the rest of titi's cache
    /// writes. Concurrent processes writing the same edges is benign — the
    /// output is deterministic for the same graph and code.
    /// </summary>
    public static void PersistStaticEdges(
        string cacheDir,
        TestToSourceEdge[] edges)
    {
        if (edges.Length == 0)
            return;

        var edgesDir = Path.Combine(cacheDir, "edges");
        Directory.CreateDirectory(edgesDir);

        var edgeEntries = edges.Select(e => new Serialization.EdgeEntry(
            From: e.From,
            To: e.To,
            Origin: (int?)e.Origin,
            Weight: e.Weight,
            LineRanges: e.LineRanges.Select(lr => new Serialization.LineRangeEntry(lr.Start, lr.End)).ToArray()
        )).ToArray();

        var staticPath = Path.Combine(edgesDir, "static-edges.json");
        File.WriteAllText(staticPath,
            System.Text.Json.JsonSerializer.Serialize(
                edgeEntries,
                Serialization.TitiJsonContext.Default.EdgeEntryArray));
    }

    /// <summary>
    /// Load previously-persisted static edges from
    /// <c>.titi/test-cache/edges/static-edges.json</c>.
    /// Returns empty array if the file doesn't exist or is corrupt.
    /// </summary>
    public static TestToSourceEdge[] LoadPersistedStaticEdges(string cacheDir)
    {
        var staticPath = Path.Combine(cacheDir, "edges", "static-edges.json");
        if (!File.Exists(staticPath))
            return [];

        try
        {
            var json = File.ReadAllText(staticPath);
            var arr = System.Text.Json.JsonSerializer.Deserialize(
                json, Serialization.TitiJsonContext.Default.EdgeEntryArray);
            if (arr == null || arr.Length == 0)
                return [];

            return arr.Select(e => new TestToSourceEdge(
                From: e.From ?? "",
                To: e.To ?? "",
                Origin: SelectionLoader.ParseOrigin(e.Origin),
                Weight: e.Weight,
                LineRanges: (e.LineRanges ?? []).Select(lr => (lr.Start, lr.End)).ToArray()
            )).Where(e => !string.IsNullOrEmpty(e.From) && !string.IsNullOrEmpty(e.To)).ToArray();
        }
        catch
        {
            return [];
        }
    }
}