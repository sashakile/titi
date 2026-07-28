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

    // ── Real .NET 10 console output (verified against SDK 10.0.302) ──
    // dotnet test --list-tests emits PLAIN CONSOLE TEXT by default.
    // There is no JSON output mode for --list-tests: --report-json,
    // --report-trx, and --logger all error out when combined with it.
    // The output is: preamble lines, a "The following Tests are available:"
    // header, then one indented FQN per test (parameterized rows expanded).

    [Fact]
    public void ParseVsTestConsole_WithRealNet10Output_SkipsPreambleAndHeader()
    {
        // Captured from `dotnet test --list-tests` on .NET 10.0.302.
        var consoleOutput = """
        Test run for /repo/Tests/bin/Debug/net10.0/Tests.dll (.NETCoreApp,Version=v10.0)
        The following Tests are available:
            Orion.Tests.UnitTest1.Test1
            Orion.Tests.UnitTest1.AnotherFact
        """;

        var items = titi.TestDiscovery.Parser.ParseVsTestConsole(consoleOutput, TestTier.Unit);

        Assert.Equal(2, items.Length);
        Assert.All(items, i =>
        {
            Assert.StartsWith("Orion.Tests.UnitTest1.", i.TestId);
            Assert.DoesNotContain("Test run for", i.TestId);
            Assert.DoesNotContain("available", i.TestId);
        });
    }

    [Fact]
    public void ParseVsTestConsole_WithParameterized_SplitsClassAndMethod()
    {
        var consoleOutput = """
        The following Tests are available:
            Orion.Tests.UnitTest1.Parse(input: "a")
            Orion.Tests.UnitTest1.InsertRow(row: 42)
        """;

        var items = titi.TestDiscovery.Parser.ParseVsTestConsole(consoleOutput, TestTier.Unit);

        Assert.Equal(2, items.Length);
        var parse = Assert.Single(items, i => i.MethodName == "Parse");
        Assert.Equal("Orion.Tests.UnitTest1", parse.ClassName);
        // TestId preserves the parameter row identity.
        Assert.Equal("Orion.Tests.UnitTest1.Parse(input: \"a\")", parse.TestId);
        var insert = Assert.Single(items, i => i.MethodName == "InsertRow");
        Assert.Equal("Orion.Tests.UnitTest1.InsertRow(row: 42)", insert.TestId);
    }

    [Fact]
    public void ParseVsTestConsole_WithBuildNoise_OnlyParsesTestLines()
    {
        // Real runs interleave MSBuild restore/build lines before the header.
        var consoleOutput = """
        Determining projects to restore...
        All projects are up-to-date for restore.
        Tests -> /repo/Tests/bin/Debug/net10.0/Tests.dll
        Test run for /repo/Tests/bin/Debug/net10.0/Tests.dll (.NETCoreApp,Version=v10.0)
        The following Tests are available:
            Orion.Tests.UnitTest1.Test1
        """;

        var items = titi.TestDiscovery.Parser.ParseVsTestConsole(consoleOutput, TestTier.Unit);

        Assert.Single(items);
        Assert.Equal("Orion.Tests.UnitTest1.Test1", items[0].TestId);
    }

    [Fact]
    public void ParseVsTestConsole_EmptyOutput_ReturnsEmpty()
    {
        Assert.Empty(titi.TestDiscovery.Parser.ParseVsTestConsole("", TestTier.Unit));
        Assert.Empty(titi.TestDiscovery.Parser.ParseVsTestConsole("   \n  \n", TestTier.Unit));
    }

    [Fact]
    public void Parse_AutoDetectsConsoleFormat()
    {
        var consoleOutput = """
        The following Tests are available:
            Orion.Tests.UnitTest1.Test1
        """;

        var items = titi.TestDiscovery.Parser.Parse(consoleOutput, TestTier.Unit);

        Assert.Single(items);
        Assert.Equal("Orion.Tests.UnitTest1.Test1", items[0].TestId);
    }

    [Fact]
    public void Parse_AutoDetectsJsonFormat()
    {
        var json = """
        {
          "tests": [
            {
              "fullyQualifiedName": "Orion.Tests.UnitTest1.Test1",
              "executorUri": "executor://xunit/VsTestRunner2"
            }
          ]
        }
        """;

        var items = titi.TestDiscovery.Parser.Parse(json, TestTier.Unit);

        Assert.Single(items);
        Assert.Equal(TestFramework.Xunit, items[0].Framework);
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
