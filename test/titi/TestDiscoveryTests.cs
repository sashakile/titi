// Tests for TID-2: VSTest test discovery

namespace titi.Tests;

using System.Text.Json;

public class TestDiscoveryTests
{
    // ── VSTest JSON output parsing ───────────────────────────────

    [Fact]
    public void ParseVsTestJson_WithValidOutput_ReturnsTestItems()
    {
        var json = """
        {
          "tests": [
            {
              "fullyQualifiedName": "Orion.Tests.UnitTest1.TestMethod1",
              "displayName": "TestMethod1",
              "executorUri": "executor://xunit/VsTestRunner2",
              "source": "/repo/tests/bin/Debug/net10.0/Orion.Tests.dll",
              "codeFilePath": "/repo/tests/UnitTest1.cs",
              "lineNumber": 15
            }
          ]
        }
        """;

        var items = titi.TestDiscovery.Parser.ParseVsTestJson(json, TestTier.Unit);

        Assert.Single(items);
        Assert.Equal("Orion.Tests.UnitTest1.TestMethod1", items[0].TestId);
        Assert.Equal(TestFramework.Xunit, items[0].Framework);
        Assert.Equal(TestTier.Unit, items[0].Tier);
    }

    [Fact]
    public void ParseVsTestJson_WithNunitOutput_DetectsNunitFramework()
    {
        var json = """
        {
          "tests": [
            {
              "fullyQualifiedName": "Orion.Tests.NunitTest1.Test1",
              "displayName": "Test1",
              "executorUri": "executor://nunit/VsTestRunner2",
              "source": "/repo/tests/bin/Debug/net10.0/Orion.Tests.dll"
            }
          ]
        }
        """;

        var items = titi.TestDiscovery.Parser.ParseVsTestJson(json, TestTier.Unit);

        Assert.Equal(TestFramework.Nunit, items[0].Framework);
    }

    [Fact]
    public void ParseVsTestJson_WithMstestOutput_DetectsMstestFramework()
    {
        var json = """
        {
          "tests": [
            {
              "fullyQualifiedName": "Orion.Tests.MsTest1.Test1",
              "displayName": "Test1",
              "executorUri": "executor://mstest/VsTestRunner2",
              "source": "/repo/tests/bin/Debug/net10.0/Orion.Tests.dll"
            }
          ]
        }
        """;

        var items = titi.TestDiscovery.Parser.ParseVsTestJson(json, TestTier.Unit);

        Assert.Equal(TestFramework.Mstest, items[0].Framework);
    }

    [Fact]
    public void ParseVsTestJson_WithParameterized_RowsAreSeparateItems()
    {
        var json = """
        {
          "tests": [
            {
              "fullyQualifiedName": "Orion.Tests.DataTest.Run(data:1)",
              "displayName": "Run(data:1)",
              "executorUri": "executor://xunit/VsTestRunner2",
              "source": "/repo/tests/bin/Debug/net10.0/Orion.Tests.dll"
            },
            {
              "fullyQualifiedName": "Orion.Tests.DataTest.Run(data:2)",
              "displayName": "Run(data:2)",
              "executorUri": "executor://xunit/VsTestRunner2",
              "source": "/repo/tests/bin/Debug/net10.0/Orion.Tests.dll"
            }
          ]
        }
        """;

        var items = titi.TestDiscovery.Parser.ParseVsTestJson(json, TestTier.Unit);

        Assert.Equal(2, items.Length);
        Assert.Contains(items, i => i.TestId.Contains("data:1"));
        Assert.Contains(items, i => i.TestId.Contains("data:2"));
    }

    [Fact]
    public void ParseVsTestJson_WithNoTests_ReturnsEmpty()
    {
        var json = """{"tests": []}""";
        var items = titi.TestDiscovery.Parser.ParseVsTestJson(json, TestTier.Unit);
        Assert.Empty(items);
    }

    [Fact]
    public void ParseVsTestJson_WithMalformedJson_ReturnsEmptyWithWarning()
    {
        var items = titi.TestDiscovery.Parser.ParseVsTestJson("not json", TestTier.Unit);
        Assert.Empty(items);
    }

    [Fact]
    public void ParseVsTestJson_WithConsoleFallback_ParsesLines()
    {
        var consoleOutput = """
        Orion.Tests.UnitTest1.TestMethod1
        Orion.Tests.UnitTest1.TestMethod2
        """;

        var items = titi.TestDiscovery.Parser.ParseVsTestConsole(consoleOutput, TestTier.Unit);

        Assert.Equal(2, items.Length);
        Assert.All(items, i => Assert.Equal(TestFramework.Xunit, i.Framework));
    }

    // ── Executor URI to Framework mapping ────────────────────────

    [Fact]
    public void DetectFramework_FromExecutorUri_MapsCorrectly()
    {
        Assert.Equal(TestFramework.Xunit, titi.TestDiscovery.Parser.DetectFramework("executor://xunit/VsTestRunner2"));
        Assert.Equal(TestFramework.Nunit, titi.TestDiscovery.Parser.DetectFramework("executor://nunit/VsTestRunner2"));
        Assert.Equal(TestFramework.Mstest, titi.TestDiscovery.Parser.DetectFramework("executor://mstest/VsTestRunner2"));
        Assert.Equal(TestFramework.Xunit, titi.TestDiscovery.Parser.DetectFramework("executor://unknown/VsTestRunner2"));
        Assert.Equal(TestFramework.Xunit, titi.TestDiscovery.Parser.DetectFramework(""));
    }
}
