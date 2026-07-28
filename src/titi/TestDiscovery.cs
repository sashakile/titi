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

    /// <summary>Parse VSTest console output fallback (one test FQN per line).</summary>
    public static TestItem[] ParseVsTestConsole(string output, TestTier tier)
    {
        if (string.IsNullOrWhiteSpace(output))
            return [];

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var items = new List<TestItem>();

        foreach (var line in lines)
        {
            var fqn = line.Trim();
            if (string.IsNullOrEmpty(fqn)) continue;

            var lastDot = fqn.LastIndexOf('.');
            var className = lastDot >= 0 ? fqn[..lastDot] : fqn;
            var methodName = lastDot >= 0 ? fqn[(lastDot + 1)..] : fqn;

            items.Add(new TestItem(
                TestId: fqn,
                AssemblyPath: "",
                ClassName: className,
                MethodName: methodName,
                Framework: TestFramework.Xunit, // console format doesn't specify framework
                Tier: tier,
                SourceFile: null,
                LastOutcome: TestOutcome.None,
                MeanDurationMs: 0,
                Tags: []
            ));
        }

        return items.ToArray();
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
