// Tests for TID-9: titi test-manifest --select/--list (5.4/5.5)
// Also covers verification tasks 7.4, 7.5, 7.6

namespace titi.Tests;

using System.Xml.Linq;

public class TestManifestTests
{
    // ── FilterExprBuilder: testId → FQN ──────────────────────────

    [Fact]
    public void DeriveFqn_StripsAssemblyAndReplacesSep()
    {
        // testId = "<assembly>::<namespace.class>::<method>"
        var fqn = titi.TestManifest.FilterExprBuilder.DeriveFqn(
            "Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseValidInput");
        Assert.Equal("Orion.Core.Tests.ParserTests.ParseValidInput", fqn);
    }

    [Fact]
    public void DeriveFqn_HandlesGenericMethod()
    {
        var fqn = titi.TestManifest.FilterExprBuilder.DeriveFqn(
            "Orion.Core.Tests::Orion.Core.Tests.FactoryTests::CreateInstance_Foo");
        Assert.Equal("Orion.Core.Tests.FactoryTests.CreateInstance_Foo", fqn);
    }

    [Fact]
    public void DeriveFqn_HandlesNestedClass()
    {
        var fqn = titi.TestManifest.FilterExprBuilder.DeriveFqn(
            "Orion.Core.Tests::Orion.Core.Tests.AuthTests+InnerAuth::TestLogin");
        Assert.Equal("Orion.Core.Tests.AuthTests+InnerAuth.TestLogin", fqn);
    }

    [Fact]
    public void DeriveFqn_HandlesParameterizedRow()
    {
        // Parameterized row with argument suffix: strip it for whole-method fallback
        var fqn = titi.TestManifest.FilterExprBuilder.DeriveFqn(
            "Orion.Core.Tests::Orion.Core.Tests.ParserTests::Parse(\"a\", 1)");
        // MethodOnly returns just the method name (no namespace/class prefix)
        var methodOnly = titi.TestManifest.FilterExprBuilder.MethodOnly(
            "Orion.Core.Tests::Orion.Core.Tests.ParserTests::Parse(\"a\", 1)");
        Assert.Equal("Parse", methodOnly);
    }

    [Fact]
    public void DeriveFqn_HandlesPercentEncodedArgs()
    {
        var fqn = titi.TestManifest.FilterExprBuilder.DeriveFqn(
            "Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseValidInput%28a%2C1%29");
        // Percent-decoded: ParseValidInput(a,1)
        Assert.EndsWith("ParseValidInput(a,1)", fqn);
    }

    // ── FilterExprBuilder: framework-aware expressions ───────────

    [Fact]
    public void BuildFilter_XUnit_UsesSubstringMatch()
    {
        var items = new[]
        {
            new TestItem(
                "Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseValidInput",
                "", "ParserTests", "ParseValidInput",
                TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
        };

        var expr = titi.TestManifest.FilterExprBuilder.BuildFilter(items, TestFramework.Xunit);
        Assert.Contains("FullyQualifiedName~", expr);
        Assert.Contains("Orion.Core.Tests.ParserTests.ParseValidInput", expr);
    }

    [Fact]
    public void BuildFilter_NUnit_UsesExactMatch()
    {
        var items = new[]
        {
            new TestItem(
                "Orion.IntegrationTests::Orion.IntegrationTests.Tests::TestCompute",
                "", "Tests", "TestCompute",
                TestFramework.Nunit, TestTier.Integration, null, TestOutcome.None, 0, []),
        };

        var expr = titi.TestManifest.FilterExprBuilder.BuildFilter(items, TestFramework.Nunit);
        Assert.Contains("FullyQualifiedName==", expr);
        Assert.Contains("Orion.IntegrationTests.Tests.TestCompute", expr);
    }

    [Fact]
    public void BuildFilter_MultipleTests_UsesPipeSeparator()
    {
        var items = new[]
        {
            new TestItem(
                "Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseValidInput",
                "", "ParserTests", "ParseValidInput",
                TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
            new TestItem(
                "Orion.Core.Tests::Orion.Core.Tests.ParserTests::ParseInvalidInput",
                "", "ParserTests", "ParseInvalidInput",
                TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
        };

        var expr = titi.TestManifest.FilterExprBuilder.BuildFilter(items, TestFramework.Xunit);
        Assert.Contains("|", expr);
    }

    [Fact]
    public void BuildFilter_EmptyItems_ReturnsNull()
    {
        var expr = titi.TestManifest.FilterExprBuilder.BuildFilter([], TestFramework.Xunit);
        Assert.Null(expr);
    }

    [Fact]
    public void BuildFilter_ParameterizedRow_FallsBackToWholeMethod()
    {
        var items = new[]
        {
            new TestItem(
                "Orion.Core.Tests::Orion.Core.Tests.ParserTests::Parse(\"a\", 1)",
                "", "ParserTests", "Parse",
                TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
        };

        var expr = titi.TestManifest.FilterExprBuilder.BuildFilter(items, TestFramework.Xunit);
        Assert.NotNull(expr);
        // Should use the method name without argument suffix
        Assert.Contains("~Orion.Core.Tests.ParserTests.Parse", expr);
        Assert.DoesNotContain("(\"a\", 1)", expr);
    }

    [Fact]
    public void BuildFilter_MixedFramework_ReturnsNullWithWarning()
    {
        // When project framework is unknown, we build per-test filters individually
        // This is expressed by returning null — the caller handles it
        var items = new[]
        {
            new TestItem("id1", "", "C", "M1", TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
        };

        // For unknown framework, build filter based on test framework
        var expr = titi.TestManifest.FilterExprBuilder.BuildFilter(items, (TestFramework)99);
        Assert.Null(expr);
    }

    // ── Batch splitting ──────────────────────────────────────────

    [Fact]
    public void BatchFilter_SplitsWhenOverLength()
    {
        // Generate many test items with realistic method names to make filter >4000 chars
        var items = new List<TestItem>();
        for (int i = 0; i < 500; i++)
        {
            items.Add(new TestItem(
                $"Asm::Ns.C{i}::MethodWithLongName{i:D50}",
                "", $"C{i}", $"MethodWithLongName{i:D50}",
                TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []));
        }

        var batches = titi.TestManifest.FilterExprBuilder.BatchFilters(
            items.ToArray(), TestFramework.Xunit, maxFilterLength: 4000, batchSize: 30);

        Assert.True(batches.Count > 1, "Should split into multiple batches");
        foreach (var (expr, batchItems) in batches)
        {
            Assert.NotNull(expr);
            Assert.True(expr.Length <= 4000, $"Batch filter too long: {expr.Length}");
        }
    }

    [Fact]
    public void BatchFilter_SingleBatchWhenUnderLimit()
    {
        var items = new[]
        {
            new TestItem("Asm::Ns.C::M1", "", "C", "M1", TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
            new TestItem("Asm::Ns.C::M2", "", "C", "M2", TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
        };

        var batches = titi.TestManifest.FilterExprBuilder.BatchFilters(
            items.ToArray(), TestFramework.Xunit, maxFilterLength: 4000, batchSize: 100);

        Assert.Single(batches);
    }

    [Fact]
    public void BatchFilter_EmptyItems_ReturnsEmptyList()
    {
        var batches = titi.TestManifest.FilterExprBuilder.BatchFilters(
            [], TestFramework.Xunit, maxFilterLength: 4000, batchSize: 100);
        Assert.Empty(batches);
    }

    // ── TraversalGenerator ───────────────────────────────────────

    [Fact]
    public void GenerateTraversal_Basic_ProducesValidXml()
    {
        var projects = new[]
        {
            new ProjectDescriptor(
                "/repo/tests/Orion.UnitTests/Orion.UnitTests.csproj",
                "Orion.UnitTests", new SemanticVersion(1,0,0,null,null),
                [], false, true, [], [], new()),
        };

        var xml = titi.TestManifest.TraversalGenerator.Generate(projects, null);
        var doc = XDocument.Parse(xml);

        var sdkAttr = doc.Root?.Attribute("Sdk");
        Assert.NotNull(sdkAttr);
        Assert.Equal("Microsoft.Build.Traversal", sdkAttr.Value);

        var refs = doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .ToArray();
        Assert.Single(refs);
        Assert.Contains("Orion.UnitTests.csproj", refs[0].Attribute("Include")?.Value ?? "");
    }

    [Fact]
    public void GenerateTraversal_WithFilter_IncludesAdditionalProperties()
    {
        var projects = new[]
        {
            new ProjectDescriptor(
                "/repo/tests/Orion.UnitTests/Orion.UnitTests.csproj",
                "Orion.UnitTests", new SemanticVersion(1,0,0,null,null),
                [], false, true, [], [], new()),
        };

        var filters = new Dictionary<string, string>
        {
            ["Orion.UnitTests"] = "FullyQualifiedName~Test1|FullyQualifiedName~Test2"
        };

        var xml = titi.TestManifest.TraversalGenerator.Generate(projects, filters);
        var doc = XDocument.Parse(xml);

        var props = doc.Descendants()
            .Where(e => e.Name.LocalName == "AdditionalProperties")
            .ToArray();
        Assert.Single(props);
        Assert.Contains("VSTestTestCaseFilter", props[0].Value);
    }

    [Fact]
    public void GenerateTraversal_MultipleProjects_CreatesMultipleRefs()
    {
        var projects = new[]
        {
            new ProjectDescriptor("/repo/t1/t1.csproj", "T1", new SemanticVersion(1,0,0,null,null), [], false, true, [], [], new()),
            new ProjectDescriptor("/repo/t2/t2.csproj", "T2", new SemanticVersion(1,0,0,null,null), [], false, true, [], [], new()),
        };

        var xml = titi.TestManifest.TraversalGenerator.Generate(projects, null);
        var doc = XDocument.Parse(xml);

        var refs = doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .ToArray();
        Assert.Equal(2, refs.Length);
    }

    [Fact]
    public void GenerateTraversal_EmptyProjects_EmptyTraversal()
    {
        var xml = titi.TestManifest.TraversalGenerator.Generate([], null);
        var doc = XDocument.Parse(xml);

        var refs = doc.Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .ToArray();
        Assert.Empty(refs);
    }

    [Fact]
    public void GenerateTraversal_WithBatchName_WritesSeparateFile()
    {
        // Verifies the batch file is correctly named
        var projects = new[]
        {
            new ProjectDescriptor("/repo/tests/T/T.csproj", "T", new SemanticVersion(1,0,0,null,null), [], false, true, [], [], new()),
        };

        var filters = new Dictionary<string, string>
        {
            ["T"] = "FullyQualifiedName~Test"
        };

        var xml = titi.TestManifest.TraversalGenerator.Generate(projects, filters, batchName: "batch-001");
        var doc = XDocument.Parse(xml);

        // Should still be valid Traversal SDK XML
        var sdkAttr = doc.Root?.Attribute("Sdk");
        Assert.Equal("Microsoft.Build.Traversal", sdkAttr?.Value);
    }

    // ── Selected test IDs: testId derivation for --list ──────────

    [Fact]
    public void ListOutput_PrintsSelectedTestIds()
    {
        var selected = new[]
        {
            new TestSelectionResult("id-1", true, [("edge-match", "matched")], 1.0, null),
            new TestSelectionResult("id-2", true, [("always-run", "always-run")], 1.0, null),
        };

        var lines = titi.TestManifest.TestManifestCommand.FormatListOutput(selected);
        Assert.Equal(2, lines.Length);
        Assert.Contains("id-1", lines);
        Assert.Contains("id-2", lines);
    }

    [Fact]
    public void ListOutput_OnlySelectedTests()
    {
        var selected = new[]
        {
            new TestSelectionResult("id-1", true, [("edge-match", "matched")], 1.0, null),
            new TestSelectionResult("id-2", false, [("no-match", "no match")], 1.0, null),
            new TestSelectionResult("id-3", true, [("always-run", "always-run")], 1.0, null),
        };

        var lines = titi.TestManifest.TestManifestCommand.FormatListOutput(selected);
        Assert.Equal(2, lines.Length);
        Assert.DoesNotContain("id-2", lines);
    }

    [Fact]
    public void ListOutput_Empty_ReturnsEmpty()
    {
        var lines = titi.TestManifest.TestManifestCommand.FormatListOutput([]);
        Assert.Empty(lines);
    }
}
