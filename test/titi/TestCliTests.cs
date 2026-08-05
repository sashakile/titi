// Tests for TID-5: Test CLI commands

namespace titi.Tests;

using System.Text.Json;

public class TestCliTests
{
    [Fact]
    public void FormatTestsList_WithItems_PrintsJson()
    {
        var items = new[]
        {
            new TestItem("id1", "/asm.dll", "C", "M1", TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
            new TestItem("id2", "/asm.dll", "C", "M2", TestFramework.Nunit, TestTier.Integration, null, TestOutcome.None, 0, []),
        };

        var json = titi.TestCli.Formatter.FormatTestItems(items);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(parsed.TryGetProperty("tests", out var tests));
        Assert.Equal(2, tests.GetArrayLength());
    }

    [Fact]
    public void FormatAffectedUpgrade_IncludesSelectedTests()
    {
        var affected = new AffectedSet(
            ["src/Foo.cs"], [], [],
            new TieredTestSet([], [], [], [])
        )
        {
            SelectedTests = [
                new TestSelectionResult("test-1", true, [("edge", "matched")], 1.0, null)
            ]
        };

        var json = titi.TestCli.Formatter.FormatAffectedUpgrade(affected);
        var parsed = JsonSerializer.Deserialize<JsonElement>(json);

        Assert.True(parsed.TryGetProperty("selectedTests", out var st));
        Assert.Equal(1, st.GetArrayLength());
        Assert.True(parsed.TryGetProperty("confidence", out var conf));
    }

    [Fact]
    public void FormatTestsList_Empty_ReturnsEmptyArray()
    {
        var json = titi.TestCli.Formatter.FormatTestItems([]);
        Assert.Contains("\"tests\"", json);
        Assert.Contains("[]", json);
    }

    [Fact]
    public void FormatAffectedUpgrade_MultiFileSingleProject_ReturnsWeightedConfidence()
    {
        // Regression: the old code used DirectlyAffected.Length / ChangedFiles.Length
        // which gives 0.2 for 1 project with 5 changed files.
        // The proper model uses the weighted ComputeConfidence with resolved file count.
        var testProj = new ProjectDescriptor(
            "src/MyProj/MyProj.csproj", "MyProj",
            new SemanticVersion(1, 0, 0, null, null),
            [new Tfm("net10.0", ".NETCoreApp", 10.0)], true, true, [], [], []);
        var affected = new AffectedSet(
            ["src/Foo.cs", "src/Bar.cs", "src/Baz.cs", "src/Qux.cs", "src/Quxx.cs"],
            [testProj],
            [],
            new TieredTestSet([], [], [], [])
        )
        {
            ResolvedFiles = ["src/Foo.cs", "src/Bar.cs", "src/Baz.cs", "src/Qux.cs", "src/Quxx.cs"]
        };

        var json = titi.TestCli.Formatter.FormatAffectedUpgrade(affected,
            edgeFreshness: 1.0, historyDepth: 10);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        Assert.True(parsed.TryGetProperty("confidence", out var conf));
        // All files resolved, fresh edges, deep history: 1.0*0.6 + 1.0*0.25 + 1.0*0.15 = 1.0
        Assert.Equal(1.0, conf.GetDouble(), precision: 2);
    }

    [Fact]
    public void FormatAffectedUpgrade_PartialResolve_ReflectsWeightedModel()
    {
        // 3 of 5 files resolved → resolution ratio 0.6
        // Old code: 1 project / 5 files = 0.2
        var testProj = new ProjectDescriptor(
            "src/MyProj/MyProj.csproj", "MyProj",
            new SemanticVersion(1, 0, 0, null, null),
            [new Tfm("net10.0", ".NETCoreApp", 10.0)], true, true, [], [], []);
        var affected = new AffectedSet(
            ["src/Foo.cs", "src/Bar.cs", "src/Baz.cs", "src/Qux.cs", "src/Quxx.cs"],
            [testProj],
            [],
            new TieredTestSet([], [], [], [])
        )
        {
            ResolvedFiles = ["src/Foo.cs", "src/Bar.cs", "src/Baz.cs"]
        };

        var json = titi.TestCli.Formatter.FormatAffectedUpgrade(affected,
            edgeFreshness: 0.0, historyDepth: 0);
        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);

        Assert.True(parsed.TryGetProperty("confidence", out var conf));
        // 0.6 * 0.6 + 0.0 * 0.25 + 0.0 * 0.15 = 0.36
        Assert.Equal(0.36, conf.GetDouble(), precision: 2);
    }
}
