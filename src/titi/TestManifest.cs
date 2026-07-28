// TID-9: titi test-manifest --select/--list (5.4/5.5)
//
// FilterExprBuilder — pure filter expression generation from selected test items.
// TraversalGenerator — generate Traversal .proj XML with VSTestTestCaseFilter support.

namespace titi.TestManifest;

using System.Text;
using System.Web; // For percent-decoding
using System.Xml.Linq;

public static class FilterExprBuilder
{
    /// <summary>
    /// Derive a VSTest <c>FullyQualifiedName</c> from a <c>TestItem.testId</c>.
    /// The testId format is <c>&lt;assembly&gt;::&lt;namespace.class&gt;::&lt;method[(args)]&gt;</c>.
    /// The output is <c>&lt;namespace.class.method&gt;</c> (dot-separated FQN).
    /// </summary>
    public static string DeriveFqn(string testId)
    {
        // Split on "::" — there may be 2 or 3 segments
        var parts = testId.Split("::", 3, StringSplitOptions.None);
        if (parts.Length < 3)
        {
            // Not enough segments; return as-is
            return testId;
        }

        // parts[0] = assembly (discard), parts[1] = namespace.class, parts[2] = method[(args)]
        var nsClass = parts[1];
        var method = parts[2];

        // Percent-decode any encoded characters in the method name
        try { method = Uri.UnescapeDataString(method); }
        catch { /* leave as-is on failure */ }

        return $"{nsClass}.{method}";
    }

    /// <summary>
    /// Extract just the method name (before any argument list) from a testId's method segment.
    /// E.g. "Parse(\"a\", 1)" → "Parse". Handles percent-encoded parentheses.
    /// Used for parameterized row fallback.
    /// </summary>
    public static string MethodOnly(string testId)
    {
        var parts = testId.Split("::", 3, StringSplitOptions.None);
        if (parts.Length < 3)
            return testId;

        var method = parts[2];

        // Percent-decode before searching for '(' so percent-encoded args are handled
        try { method = Uri.UnescapeDataString(method); }
        catch { /* leave as-is on failure */ }

        var parenIdx = method.IndexOf('(');
        if (parenIdx >= 0)
            method = method[..parenIdx];

        return method;
    }

    /// <summary>
    /// Build a <c>--filter</c> expression string from selected test items for a given
    /// framework. Returns null for empty items or unknown framework (caller handles fallback).
    /// </summary>
    public static string? BuildFilter(TestItem[] items, TestFramework framework)
    {
        if (items.Length == 0)
            return null;

        // For unknown framework types, return null so the caller falls back to
        // individual test invocations (one per test, framework-agnostic).
        if (framework is not (TestFramework.Xunit or TestFramework.Nunit or TestFramework.Mstest))
            return null;

        var filterParts = new List<string>();

        foreach (var item in items)
        {
            var fqn = DeriveFqn(item.TestId);
            // If the FQN contains '(' after the final '.', this is a parameterized
            // row — use MethodOnly for whole-method fallback.
            var dotIdx = fqn.LastIndexOf('.');
            var methodPart = dotIdx >= 0 ? fqn[(dotIdx + 1)..] : fqn;
            if (methodPart.Contains('('))
            {
                fqn = fqn[..(dotIdx + 1)] + MethodOnly(item.TestId);
            }

            filterParts.Add(framework switch
            {
                TestFramework.Xunit => $"FullyQualifiedName~{fqn}",
                TestFramework.Nunit => $"FullyQualifiedName=={fqn}",
                TestFramework.Mstest => $"FullyQualifiedName~{fqn}",
                _ => $"FullyQualifiedName~{fqn}", // fallback
            });
        }

        return string.Join("|", filterParts);
    }

    /// <summary>
    /// Batch selected test items into filter expressions that stay under
    /// <paramref name="maxFilterLength"/> characters. Each batch produces one
    /// filter expression (usable as VSTestTestCaseFilter) and the items in that batch.
    /// </summary>
    public static List<(string Expression, TestItem[] Items)> BatchFilters(
        TestItem[] items, TestFramework framework,
        int maxFilterLength = 4000, int batchSize = 100)
    {
        var batches = new List<(string Expression, TestItem[] Items)>();

        if (items.Length == 0)
            return batches;

        // If all items fit in one filter under the length limit, return a single batch.
        var allExpr = BuildFilter(items, framework);
        if (allExpr != null && allExpr.Length <= maxFilterLength)
        {
            batches.Add((allExpr, items));
            return batches;
        }

        // Otherwise, batch by batchSize and build per-batch filters.
        var currentBatch = new List<TestItem>();
        foreach (var item in items)
        {
            currentBatch.Add(item);
            if (currentBatch.Count >= batchSize)
            {
                var expr = BuildFilter(currentBatch.ToArray(), framework);
                if (expr != null)
                    batches.Add((expr, currentBatch.ToArray()));
                currentBatch.Clear();
            }
        }

        // Last partial batch
        if (currentBatch.Count > 0)
        {
            var expr = BuildFilter(currentBatch.ToArray(), framework);
            if (expr != null)
                batches.Add((expr, currentBatch.ToArray()));
        }

        return batches;
    }

    /// <summary>
    /// Get the test framework for a list of test items. If all items share the same
    /// framework, returns that framework. If mixed, returns null (unknown).
    /// </summary>
    internal static TestFramework? GetCommonFramework(TestItem[] items)
    {
        if (items.Length == 0)
            return null;

        var first = items[0].Framework;
        for (int i = 1; i < items.Length; i++)
        {
            if (items[i].Framework != first)
                return null; // mixed framework
        }
        return first;
    }
}

public static class TraversalGenerator
{
    static readonly XNamespace MsbNs = "http://schemas.microsoft.com/developer/msbuild/2003";

    /// <summary>
    /// Generate a Traversal .proj XML string.
    /// </summary>
    /// <param name="projects">Test projects to include.</param>
    /// <param name="projectFilters">
    /// Optional: per-project (keyed by PackageId) filter expressions to set as
    /// VSTestTestCaseFilter. Null or empty means no filtering.
    /// </param>
    /// <param name="batchName">Optional batch suffix for multi-file generation.</param>
    public static string Generate(
        ProjectDescriptor[] projects,
        Dictionary<string, string>? projectFilters,
        string? batchName = null)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(MsbNs + "Project",
                new XAttribute("Sdk", "Microsoft.Build.Traversal"),
                new XElement(MsbNs + "ItemGroup",
                    projects.Select(p =>
                    {
                        var include = p.Path;
                        var element = new XElement(MsbNs + "ProjectReference",
                            new XAttribute("Include", include));

                        // Add AdditionalProperties with VSTestTestCaseFilter if available
                        if (projectFilters != null &&
                            projectFilters.TryGetValue(p.PackageId, out var filter) &&
                            !string.IsNullOrEmpty(filter))
                        {
                            element.Add(new XElement(MsbNs + "AdditionalProperties",
                                $"VSTestTestCaseFilter={filter}"));
                        }

                        return element;
                    })
                )
            )
        );

        using var sw = new StringWriter();
        doc.Save(sw);
        return sw.ToString();
    }
}

public static class TestManifestCommand
{
    /// <summary>
    /// Format selected test IDs for --list output: one selected test ID per line.
    /// Only tests with Selected==true are included.
    /// </summary>
    public static string[] FormatListOutput(TestSelectionResult[] selected)
    {
        return selected
            .Where(s => s.Selected)
            .Select(s => s.TestId)
            .ToArray();
    }
}
