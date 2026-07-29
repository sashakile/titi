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

            foreach (var pkg in root.Descendants("package"))
            {
                foreach (var cls in pkg.Descendants("class"))
                {
                    var filename = cls.Attribute("filename")?.Value;
                    if (string.IsNullOrEmpty(filename))
                        continue;

                    // Canonicalize the coverage filename against sourceRoot and
                    // reject paths that escape it. coverlet emits sourceRoot-
                    // relative filenames, but the report is caller-controlled:
                    // a '..' segment or absolute path outside sourceRoot would
                    // produce edges that cross-match files outside the repo.
                    var sourcePath = ResolveUnderRoot(filename, sourceRoot);
                    if (sourcePath == null)
                        continue;

                    // Collect line ranges
                    var lineRanges = new List<(int Start, int End)>();
                    foreach (var line in cls.Descendants("line"))
                    {
                        var numStr = line.Attribute("number")?.Value;
                        if (int.TryParse(numStr, out var num))
                        {
                            lineRanges.Add((num, num));
                        }
                    }

                    // Build edges per method (method name as the "from" field identifier)
                    foreach (var method in cls.Descendants("method"))
                    {
                        var methodName = method.Attribute("name")?.Value ?? "unknown";
                        var methodLines = new List<(int Start, int End)>();
                        foreach (var line in method.Descendants("line"))
                        {
                            var numStr = line.Attribute("number")?.Value;
                            if (int.TryParse(numStr, out var num))
                                methodLines.Add((num, num));
                        }

                        edges.Add(new TestToSourceEdge(
                            From: methodName,
                            To: sourcePath,
                            Origin: EdgeOrigin.Static,
                            Weight: 1_000_000,
                            LineRanges: methodLines.ToArray()
                        ));
                    }

                    // If no methods, create a file-level edge using class name
                    if (!cls.Descendants("method").Any())
                    {
                        var className = cls.Attribute("name")?.Value ?? "unknown";
                        edges.Add(new TestToSourceEdge(
                            From: className,
                            To: sourcePath,
                            Origin: EdgeOrigin.Static,
                            Weight: 1_000_000,
                            LineRanges: lineRanges.ToArray()
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
}
