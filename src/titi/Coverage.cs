// TID-3: Cobertura XML → TestToSourceEdge parsing
// TID-3a: TRX (Visual Studio Test Results) → per-test outcomes/durations (TD-02)

namespace titi.Coverage;

using System.Xml.Linq;

/// <summary>
/// Per-test result parsed from a TRX (Visual Studio Test Results) file
/// produced by `dotnet test --logger trx`. <c>TestName</c> is the test's
/// fully-qualified name (VSTest <c>testName</c>), which for parameterized
/// rows includes the serialized arguments (e.g.
/// <c>Ns.Cls.Parse(input: "a")</c>). <c>ErrorMessage</c> is populated for
/// failed tests (message + stack) and skipped tests (the skip reason).
/// </summary>
public record TrxTestResult(
    string TestName,
    TestOutcome Outcome,
    long DurationMs,
    string? ErrorMessage
);

public static class Parser
{
    // TRX uses the Visual Studio TeamTest 2010 namespace.
    private static readonly XNamespace TrxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    /// <summary>
    /// Parse a TRX file (Visual Studio Test Results) into per-test results.
    /// Malformed XML, a non-TestRun root, or an absent <c>Results</c> section
    /// all yield an empty array (the caller decides whether to warn).
    /// </summary>
    public static TrxTestResult[] ParseTrx(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        try
        {
            var doc = XDocument.Parse(xml);
            if (doc.Root == null || doc.Root.Name != TrxNs + "TestRun")
                return [];

            var results = new List<TrxTestResult>();
            foreach (var ur in doc.Root.Descendants(TrxNs + "UnitTestResult"))
            {
                var testName = ur.Attribute("testName")?.Value;
                if (string.IsNullOrEmpty(testName))
                    continue;

                var outcomeStr = ur.Attribute("outcome")?.Value ?? "";
                var outcome = MapOutcome(outcomeStr);
                var durationMs = ParseDurationMs(ur.Attribute("duration")?.Value);
                var error = ExtractError(ur);

                results.Add(new TrxTestResult(
                    TestName: testName,
                    Outcome: outcome,
                    DurationMs: durationMs,
                    ErrorMessage: error
                ));
            }

            return results.ToArray();
        }
        catch
        {
            // Malformed TRX → empty result set (warn emitted by caller per TD-02).
            return [];
        }
    }

    private static TestOutcome MapOutcome(string outcomeStr) => outcomeStr switch
    {
        "Passed" => TestOutcome.Passed,
        "Failed" => TestOutcome.Failed,
        "NotExecuted" => TestOutcome.Skipped,
        // Timeout, Error, Aborted, etc. → treat as failure so the test is
        // re-selected next run (consistent with the always-run-on-failure rule).
        _ when !string.IsNullOrEmpty(outcomeStr) => TestOutcome.Failed,
        _ => TestOutcome.NotRun,
    };

    private static long ParseDurationMs(string? duration)
    {
        // TRX durations look like "00:00:00.0021727" (hh:mm:ss.fffffff).
        if (string.IsNullOrEmpty(duration))
            return 0;
        if (TimeSpan.TryParse(duration, out var ts))
            return (long)ts.TotalMilliseconds;
        return 0;
    }

    private static string? ExtractError(XElement unitTestResult)
    {
        var errorInfo = unitTestResult.Element(TrxNs + "Output")?.Element(TrxNs + "ErrorInfo");
        if (errorInfo == null)
            return null;

        var message = errorInfo.Element(TrxNs + "Message")?.Value;
        var stack = errorInfo.Element(TrxNs + "StackTrace")?.Value;

        if (string.IsNullOrEmpty(message) && string.IsNullOrEmpty(stack))
            return null;
        if (string.IsNullOrEmpty(stack))
            return message;
        if (string.IsNullOrEmpty(message))
            return stack;
        return message + Environment.NewLine + stack;
    }

    /// <summary>Parse Cobertura XML coverage report into TestToSourceEdge records.</summary>
    /// <param name="xml">Raw Cobertura XML content.</param>
    /// <param name="sourceRoot">Source root directory (from Cobertura &lt;sources&gt;).</param>
    /// <returns>Array of file-level TestToSourceEdge records.</returns>
    public static TestToSourceEdge[] ParseCobertura(string xml, string sourceRoot)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null || root.Name != "coverage")
                return [];

            var edges = new List<TestToSourceEdge>();

            // coverlet emits filenames relative to the <sources> root (the
            // common root of covered source files), not the caller-supplied
            // root. Try embedded roots first, then fall back to callerRoot.
            var sourceRoots = CollectSourceRoots(root, sourceRoot);

            foreach (var pkg in root.Descendants("package"))
            {
                foreach (var cls in pkg.Descendants("class"))
                {
                    var filename = cls.Attribute("filename")?.Value;
                    if (string.IsNullOrEmpty(filename))
                        continue;

                    // Canonicalize the coverage filename against the source
                    // roots and reject paths that escape them. A '..' segment
                    // or absolute path outside every root yields no edge.
                    var sourcePath = sourceRoots
                        .Select(candidate => ResolveUnderRoot(filename, candidate))
                        .FirstOrDefault(resolved => resolved != null);
                    if (sourcePath == null)
                        continue;

                    // Collect line ranges (only lines with positive hit counts)
                    var lineRanges = ParseCoveredLines(cls);

                    // Build edges per method (method name as the "from" field identifier)
                    foreach (var method in cls.Descendants("method"))
                    {
                        var methodName = method.Attribute("name")?.Value ?? "unknown";
                        var methodLines = ParseCoveredLines(method);
                        // A method with zero covered lines is uncovered; skip it
                        // to avoid false-positive test selections.
                        if (methodLines.Length == 0)
                            continue;

                        edges.Add(new TestToSourceEdge(
                            From: methodName,
                            To: sourcePath,
                            Origin: EdgeOrigin.Static,
                            Weight: 1_000_000,
                            LineRanges: methodLines
                        ));
                    }

                    // If no methods, create a file-level edge using class name
                    if (!cls.Descendants("method").Any())
                    {
                        var className = cls.Attribute("name")?.Value ?? "unknown";
                        // A class with zero covered lines is uncovered; skip it.
                        if (lineRanges.Length == 0)
                            continue;

                        edges.Add(new TestToSourceEdge(
                            From: className,
                            To: sourcePath,
                            Origin: EdgeOrigin.Static,
                            Weight: 1_000_000,
                            LineRanges: lineRanges
                        ));
                    }
                }
            }

            return edges.ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Build the ordered candidate source roots: embedded &lt;sources&gt;/&lt;source&gt;
    /// values first (authoritative for coverlet output), then the caller-supplied
    /// root as fallback. Deduplicates, preserving order.
    /// </summary>
    static List<string> CollectSourceRoots(XElement rootElement, string callerRoot)
    {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in rootElement.Descendants("source"))
        {
            var value = source.Value.Trim();
            if (value.Length == 0 || !seen.Add(value))
                continue;
            roots.Add(value);
        }
        if (!string.IsNullOrEmpty(callerRoot) && seen.Add(callerRoot))
            roots.Add(callerRoot);
        return roots;
    }

    // Canonicalize a coverage filename against sourceRoot. Accepts sourceRoot-
    // relative paths (coverlet), absolute paths under sourceRoot, and rejects
    // anything that escapes via '..' or resolves outside the root. Returns null
    // for out-of-root paths so the caller skips the edge entirely.
    private static string? ResolveUnderRoot(string filename, string sourceRoot)
    {
        if (string.IsNullOrEmpty(filename) || string.IsNullOrEmpty(sourceRoot))
            return null;

        // Normalize separators to '/' for the combine step on all platforms.
        var normalizedFilename = filename.Replace('\\', '/');
        var normalizedRoot = sourceRoot.Replace('\\', '/').TrimEnd('/');

        string combined = normalizedFilename.StartsWith('/')
            ? normalizedFilename
            : normalizedRoot + "/" + normalizedFilename;

        // Collapse '..' / '.' segments canonically without touching the disk.
        var segments = combined.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var stack = new List<string>(segments.Length);
        foreach (var seg in segments)
        {
            if (seg == ".")
                continue;
            if (seg == "..")
            {
                if (stack.Count == 0)
                    return null; // escapes the root
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            stack.Add(seg);
        }

        var canonical = "/" + string.Join('/', stack);
        var rootPrefix = normalizedRoot + "/";
        // Containment: canonical must be the root itself or live under it.
        if (canonical != normalizedRoot && !canonical.StartsWith(rootPrefix, StringComparison.Ordinal))
            return null;
        return canonical;
    }

    // Parse lines from a container (class or method), retaining only those
    // with a positive numeric hit count. Returns an array (empty when no
    // covered lines exist). Shared between class-level and method-level
    // parsing paths (REFACTOR: titi-k2x.18).
    private static (int Start, int End)[] ParseCoveredLines(XElement container)
    {
        var result = new List<(int Start, int End)>();
        foreach (var line in container.Descendants("line"))
        {
            var numStr = line.Attribute("number")?.Value;
            var hitsStr = line.Attribute("hits")?.Value;
            // Only retain lines with a positive numeric hit count. A missing
            // or invalid hits attribute is treated as uncovered (conservative).
            if (!int.TryParse(numStr, out var num))
                continue;
            if (int.TryParse(hitsStr, out var hits) && hits > 0)
                result.Add((num, num));
        }
        return result.ToArray();
    }
}
