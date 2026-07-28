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
}
