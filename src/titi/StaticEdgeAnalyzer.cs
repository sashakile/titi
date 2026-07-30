// TID-ov7: Level 3 — Method-level call graph for static edge analysis
//
// Produces test→source edges without running tests. Three levels:
//   Level 1 — Project references (.csproj <ProjectReference>) — test method × source file cross-product
//   Level 2 — Using statement analysis — test file × source file based on namespace match
//   Level 3 — Method call graph — test method × source file based on method call resolution
//
// Level 1 is an over-approximation (every test method in a project gets edges
// to every source file in every referenced project). Level 2 is more precise
// (only test files whose using-directives match a source project's namespace
// get edges to that project's source files). Level 3 is the most precise:
// only source files containing methods that are actually called from test code
// get edges.
//
// SAFE-001: All levels over-approximate (never miss a real dependency).
//
// Edge weights (lower than coverage edges at 1_000_000 in EdgeBuilder.cs):
//   L1: 500_000 — project reference cross-product
//   L2: 800_000 — using-statement match (more precise, higher weight)
//   L3: 950_000 — method call resolution (most precise, highest weight)
//
// When no test items are available (discoveredTests is null/empty), edges use
// synthetic From prefixes: "$pkg:{PackageId}" for L1, "$file:{path}" for L2/L3.

namespace titi;

using System.Text.RegularExpressions;

public static class StaticEdgeAnalyzer
{
    // Regex for `using Namespace;` or `using static Namespace.Type;`
    private static readonly Regex UsingRegex = new(
        @"^\s*using\s+(?:static\s+)?([^;]+?)(?:\s*;)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Regex for file-scoped namespace declaration: `namespace X.Y.Z;`
    private static readonly Regex NamespaceDeclRegex = new(
        @"^\s*namespace\s+([^\s{;]+)(?:\s*[\{;])?\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Regex for method declarations: `public [static] ReturnType MethodName(`
    // Captures the method name. Handles access modifiers, static, override,
    // virtual, abstract, and generic return types.
    private static readonly Regex MethodDeclRegex = new(
        @"\b(?:public|internal|private|protected)\s+" +
        @"(?:static\s+)?" +
        @"(?:override\s+|virtual\s+|abstract\s+)?" +
        @"[\w\?\[\],<>~`]+\s+" +
        @"(\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Regex for class/record/struct declarations: `public [static] class ClassName`
    private static readonly Regex TypeDeclRegex = new(
        @"\b(?:public|internal|private|protected)?\s*(?:static\s+)?(?:class|record|struct)\s+(\w+)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Regex for method calls in test code: `TypeName.MethodName(`
    // Matches identifier-pairs where the first starts with uppercase (type name by convention).
    // Also matches `new TypeName(` for constructor calls.
    private static readonly Regex MethodCallRegex = new(
        @"\b([A-Z]\w*)\.(\w+)\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex NewTypeRegex = new(
        @"\bnew\s+([A-Z]\w*)\s*\(",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // Weight hierarchy: coverage edges (EdgeBuilder) = 1_000_000 > L3 > L2 > L1
    private const long WeightL1ProjectRefs = 500_000;
    private const long WeightL2UsingStatements = 800_000;
    private const long WeightL3MethodCalls = 950_000;

    /// <summary>
    /// Run all three levels of static analysis and return merged edges.
    /// When <paramref name="discoveredTests"/> is null or empty, returns
    /// synthetic edges prefixed with <c>$pkg:</c> (L1) or <c>$file:</c> (L2/L3)
    /// — no test-method granularity.
    ///
    /// Merge priority: L3 (most precise) > L2 > L1 (over-approximation).
    /// Higher-weight edges win for the same (From, To) pair.
    /// </summary>
    public static TestToSourceEdge[] AnalyzeAll(
        MonorepoGraph graph,
        Dictionary<string, TestItem[]>? discoveredTests)
    {
        if (graph.Nodes.Count == 0)
            return [];

        var l1Edges = AnalyzeProjectReferences(graph, discoveredTests);
        var l2Edges = AnalyzeUsingStatements(graph, discoveredTests);
        var l3Edges = AnalyzeMethodCalls(graph, discoveredTests);

        // Merge: L3 > L2 > L1 priority. Emit in order of decreasing precision
        // so higher-weight edges win for the same (From, To) pair.
        var edgeSet = new HashSet<(string From, string To)>();
        var result = new List<TestToSourceEdge>(
            l3Edges.Length + l2Edges.Length + l1Edges.Length);

        foreach (var edge in l3Edges)
        {
            if (edgeSet.Add((edge.From, edge.To)))
                result.Add(edge);
        }

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

    // ── Level 3: Method call graph ───────────────────────────────

    /// <summary>
    /// Parse method calls in test code and map them to source files containing
    /// the called method. Uses regex-based approach:
    ///
    /// 1. Build a method→source-file map from source projects (parse method
    ///    declarations like <c>public static ReturnType MethodName(...)</c>).
    /// 2. Build a class→source-file map from source projects (parse class,
    ///    record, struct declarations).
    /// 3. Scan test source files for <c>TypeName.MethodName(</c> patterns
    ///    (uppercase-starting type name by convention) and <c>new TypeName(</c>
    ///    constructor calls.
    /// 4. Match against the method/class maps and emit precise edges.
    ///
    /// Instance method calls on variables (e.g., <c>repo.Save(...)</c>) are not
    /// resolved by this level — they fall through to L2 namespace matching.
    ///
    /// When <paramref name="discoveredTests"/> is null or empty, uses
    /// synthetic file-level identifiers prefixed with "$file:".
    /// </summary>
    public static TestToSourceEdge[] AnalyzeMethodCalls(
        MonorepoGraph graph,
        Dictionary<string, TestItem[]>? discoveredTests)
    {
        var result = new List<TestToSourceEdge>();

        // Build method→file and class→file maps from source projects
        var methodToFiles = BuildMethodMap(graph);
        var classToFiles = BuildTypeMap(graph);
        if (methodToFiles.Count == 0 && classToFiles.Count == 0)
            return [];

        foreach (var (testProjectPath, testNode) in graph.Nodes)
        {
            if (!testNode.Project.IsTestProject)
                continue;

            var testPkgId = testNode.Project.PackageId;
            var testItems = discoveredTests?.GetValueOrDefault(testPkgId);

            // Scan test source files for method calls
            var testDir = Path.GetDirectoryName(testProjectPath) ?? "";
            var testCsFiles = EnumerateSourceFiles(testDir);

            if (testCsFiles.Length == 0)
                continue;

            // Build call→files map per test source file
            var testFileSrcFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var testFile in testCsFiles)
            {
                var matchedSrcFiles = ParseMethodCalls(testFile, methodToFiles, classToFiles);
                if (matchedSrcFiles.Count > 0)
                    testFileSrcFiles[testFile] = matchedSrcFiles;
            }

            if (testFileSrcFiles.Count == 0)
                continue;

            if (testItems != null && testItems.Length > 0)
            {
                // Method-level edges: for each test item, find which test
                // source file it belongs to, then emit edges to matched sources.
                foreach (var item in testItems)
                {
                    var matchedFile = FindTestFileForItem(item, testCsFiles, testDir);

                    if (matchedFile == null || !testFileSrcFiles.TryGetValue(matchedFile, out var matchedSrcFiles))
                        continue;

                    foreach (var srcFile in matchedSrcFiles)
                    {
                        result.Add(new TestToSourceEdge(
                            From: item.TestId,
                            To: srcFile,
                            Origin: EdgeOrigin.Static,
                            Weight: WeightL3MethodCalls,
                            LineRanges: []
                        ));
                    }
                }
            }
            else
            {
                // File-level edges (no test-item granularity)
                foreach (var (testFile, matchedSrcFiles) in testFileSrcFiles)
                {
                    foreach (var srcFile in matchedSrcFiles)
                    {
                        result.Add(new TestToSourceEdge(
                            From: $"$file:{testFile}",
                            To: srcFile,
                            Origin: EdgeOrigin.Static,
                            Weight: WeightL3MethodCalls,
                            LineRanges: []
                        ));
                    }
                }
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Build a map from method name → set of source file paths by scanning
    /// all source projects' <c>.cs</c> files for method declarations.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> BuildMethodMap(MonorepoGraph graph)
    {
        var methodToFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

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
                    var matches = MethodDeclRegex.Matches(content);

                    foreach (Match m in matches)
                    {
                        var methodName = m.Groups[1].Value;
                        if (!methodToFiles.ContainsKey(methodName))
                            methodToFiles[methodName] = new HashSet<string>(StringComparer.Ordinal);
                        methodToFiles[methodName].Add(csFile);
                    }
                }
                catch
                {
                    // skip unreadable files
                }
            }
        }

        return methodToFiles;
    }

    /// <summary>
    /// Build a map from type name (class/record/struct) → set of source file
    /// paths by scanning all source projects' <c>.cs</c> files for type declarations.
    /// </summary>
    internal static Dictionary<string, HashSet<string>> BuildTypeMap(MonorepoGraph graph)
    {
        var typeToFiles = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

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
                    var matches = TypeDeclRegex.Matches(content);

                    foreach (Match m in matches)
                    {
                        var typeName = m.Groups[1].Value;
                        if (!typeToFiles.ContainsKey(typeName))
                            typeToFiles[typeName] = new HashSet<string>(StringComparer.Ordinal);
                        typeToFiles[typeName].Add(csFile);
                    }
                }
                catch
                {
                    // skip unreadable files
                }
            }
        }

        return typeToFiles;
    }

    /// <summary>
    /// Parse a C# source file for method call patterns and return the set of
    /// source file paths that contain the called methods or types.
    ///
    /// Matches:
    ///   - <c>TypeName.MethodName(</c> — static method calls where TypeName
    ///     starts with uppercase (convention for class types).
    ///   - <c>new TypeName(</c> — constructor calls.
    ///
    /// For each match, looks up the method/type name in the provided maps
    /// and returns the corresponding source file paths.
    /// </summary>
    internal static HashSet<string> ParseMethodCalls(
        string filePath,
        Dictionary<string, HashSet<string>> methodToFiles,
        Dictionary<string, HashSet<string>> classToFiles)
    {
        var matchedFiles = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var content = File.ReadAllText(filePath);

            // Match `TypeName.MethodName(` patterns
            var callMatches = MethodCallRegex.Matches(content);
            foreach (Match m in callMatches)
            {
                var typeName = m.Groups[1].Value;
                var methodName = m.Groups[2].Value;

                // Look up method name in the method map
                if (methodToFiles.TryGetValue(methodName, out var methodFiles))
                {
                    foreach (var f in methodFiles)
                        matchedFiles.Add(f);
                }

                // Also check if the type name is a known class (catches `new TypeName(...)` 
                // patterns that the constructor regex might miss, or type-qualified calls
                // where the method name is generic)
                if (classToFiles.TryGetValue(typeName, out var classFiles))
                {
                    foreach (var f in classFiles)
                        matchedFiles.Add(f);
                }
            }

            // Match `new TypeName(` constructor calls (catches cases where
            // the type is used via constructor but not via static method call)
            var newMatches = NewTypeRegex.Matches(content);
            foreach (Match m in newMatches)
            {
                var typeName = m.Groups[1].Value;

                if (classToFiles.TryGetValue(typeName, out var classFiles))
                {
                    foreach (var f in classFiles)
                        matchedFiles.Add(f);
                }
            }
        }
        catch
        {
            // skip unreadable files
        }

        return matchedFiles;
    }

    // ── Shared Helpers ───────────────────────────────────────────

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
    /// when no match is found — the caller degrades gracefully (edges omitted
    /// for that item, other levels still apply).
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

        // No match found — return null so caller falls back to lower levels
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