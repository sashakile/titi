// TID-2: VSTest test discovery — parse --list-tests output into TestItem records

namespace titi.TestDiscovery;

using System.Text.Json;

public static class Parser
{
    /// <summary>Parse VSTest JSON output (dotnet test --list-tests on .NET 10).</summary>
    public static TestItem[] ParseVsTestJson(string json, TestTier tier)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("tests", out var testsProp))
                return [];

            var items = new List<TestItem>();
            foreach (var test in testsProp.EnumerateArray())
            {
                var fqn = test.GetProperty("fullyQualifiedName").GetString() ?? "";
                var displayName = test.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? fqn : fqn;
                var source = test.TryGetProperty("source", out var src) ? src.GetString() : null;
                var filePath = test.TryGetProperty("codeFilePath", out var cfp) ? cfp.GetString() : null;
                var executorUri = test.TryGetProperty("executorUri", out var eu) ? eu.GetString() : "";
                var framework = DetectFramework(executorUri ?? "");

                // Extract class name from FQN: "Namespace.Class.Method" → "Namespace.Class"
                var lastDot = fqn.LastIndexOf('.');
                var className = lastDot >= 0 ? fqn[..lastDot] : fqn;
                var methodName = lastDot >= 0 ? fqn[(lastDot + 1)..] : fqn;

                items.Add(new TestItem(
                    TestId: fqn,
                    AssemblyPath: source ?? "",
                    ClassName: className,
                    MethodName: methodName,
                    Framework: framework,
                    Tier: tier,
                    SourceFile: filePath,
                    LastOutcome: TestOutcome.None,
                    MeanDurationMs: 0,
                    Tags: []
                ));
            }

            return items.ToArray();
        }
        catch (JsonException)
        {
            // Malformed JSON — return empty
            return [];
        }
    }

    /// <summary>
    /// Parse VSTest <c>--list-tests</c> console output. On .NET 10 (verified
    /// against SDK 10.0.302) this is the ONLY output format available for
    /// <c>--list-tests</c>: <c>--report-json</c>/<c>--report-trx</c>/<c>--logger</c>
    /// all error out when combined with <c>--list-tests</c>. The output is:
    /// optional MSBuild preamble lines, a <c>"The following Tests are available:"</c>
    /// header, then one indented FQN per test (parameterized rows expanded as
    /// <c>Namespace.Class.Method(param: value)</c>).
    /// </summary>
    public static TestItem[] ParseVsTestConsole(string output, TestTier tier)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Prefer the explicit "The following Tests are available:" header as the
        // section boundary — everything after it is a test line. Fall back to a
        // per-line FQN pattern when the header is absent (e.g. legacy VSTest).
        var headerIndex = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("The following Tests are available", StringComparison.Ordinal))
            {
                headerIndex = i;
                break;
            }
        }

        var items = new List<TestItem>();
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (headerIndex >= 0)
            {
                if (i <= headerIndex) continue; // skip preamble + header
            }
            else if (!IsTestFqnLine(line))
            {
                continue;
            }

            if (!TryParseFqn(line, out var className, out var methodName, out var testId))
                continue;

            items.Add(new TestItem(
                TestId: testId,
                AssemblyPath: "",
                ClassName: className,
                MethodName: methodName,
                Framework: TestFramework.Xunit, // console format doesn't expose executor URI
                Tier: tier,
                SourceFile: null,
                LastOutcome: TestOutcome.None,
                MeanDurationMs: 0,
                Tags: []
            ));
        }

        return items.ToArray();
    }

    /// <summary>
    /// Auto-detect the VSTest <c>--list-tests</c> output format and dispatch.
    /// JSON output (if ever supported) routes to <see cref="ParseVsTestJson"/>;
    /// the default .NET 10 console text routes to <see cref="ParseVsTestConsole"/>.
    /// </summary>
    public static TestItem[] Parse(string output, TestTier tier)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var trimmed = output.AsSpan().TrimStart();
        if (trimmed.Length > 0 && trimmed[0] == '{')
            return ParseVsTestJson(output, tier);

        return ParseVsTestConsole(output, tier);
    }

    // A test FQN line matches `Namespace.Class.Method` with an optional
    // `(params)` suffix. It must start with an identifier char, contain at
    // least one dot separating identifiers, and have no spaces outside the
    // parameter list — this excludes MSBuild/restore noise and the header.
    private static bool IsTestFqnLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        var first = line[0];
        if (!char.IsLetterOrDigit(first) && first != '_') return false;
        if (!line.Contains('.')) return false;
        var paren = line.IndexOf('(');
        var head = paren >= 0 ? line[..paren] : line;
        // Head (Namespace.Class.Method) must contain no spaces.
        if (head.Contains(' ')) return false;
        return true;
    }

    // Split `Namespace.Class.Method[(params)]` into class, method, and full id.
    private static bool TryParseFqn(string fqn, out string className, out string methodName, out string testId)
    {
        className = methodName = testId = "";
        if (string.IsNullOrWhiteSpace(fqn)) return false;

        var paren = fqn.IndexOf('(');
        var head = paren >= 0 ? fqn[..paren] : fqn;
        var lastDot = head.LastIndexOf('.');
        if (lastDot < 0)
        {
            // No dot ⇒ not a real FQN (e.g. a stray preamble line with no header).
            return false;
        }

        className = head[..lastDot];
        methodName = head[(lastDot + 1)..];
        testId = fqn; // preserve the full identity incl. parameter row
        return true;
    }

    /// <summary>Detect test framework from VSTest executor URI.</summary>
    public static TestFramework DetectFramework(string executorUri)
    {
        if (executorUri.Contains("nunit", StringComparison.OrdinalIgnoreCase))
            return TestFramework.Nunit;
        if (executorUri.Contains("mstest", StringComparison.OrdinalIgnoreCase))
            return TestFramework.Mstest;
        // Default to xUnit (most common, and default for unknown)
        return TestFramework.Xunit;
    }
}
