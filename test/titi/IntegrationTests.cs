// Integration tests — run titi CLI against sample-monorepo fixture

namespace titi.Tests;

using System.Text.Json;

public class IntegrationTests
{
    static readonly string FixtureDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../test/fixtures/sample-monorepo"));

    /// <summary>Run titi open Orion.Core.Data against the sample-monorepo fixture.</summary>
    [Fact]
    public void TitiOpen_AgainstFixture_GeneratesSlnx()
    {
        var (output, stderr) = TitiTestRunner.RunTitiAndParseJson(
            ["open", "Orion.Core.Data"], FixtureDir);

        Assert.True(output.RootElement.TryGetProperty("solutionPath", out var slnxProp),
            $"Expected 'solutionPath' in JSON output. Stderr: {stderr}");
        var slnxPath = slnxProp.GetString();
        Assert.NotNull(slnxPath);
        var fullSlnxPath = Path.IsPathRooted(slnxPath) ? slnxPath : Path.Combine(FixtureDir, slnxPath);
        Assert.True(File.Exists(fullSlnxPath), $".slnx not found at {fullSlnxPath}");

        // Assert: .slnx contains InTitiContext=true
        var slnxContent = File.ReadAllText(fullSlnxPath);
        Assert.Contains("InTitiContext", slnxContent);
        Assert.Contains("true", slnxContent);
        Assert.Contains("Orion.Core.Data", slnxContent);

        // Cleanup
        var titiDir = Path.Combine(FixtureDir, ".titi");
        if (Directory.Exists(titiDir))
            Directory.Delete(titiDir, recursive: true);
    }

    /// <summary>Run titi clean against fixture with .titi/ present.</summary>
    [Fact]
    public void TitiClean_WithTitiDir_RemovesIt()
    {
        var titiDir = Path.Combine(FixtureDir, ".titi");

        // Create a fake .titi/ directory
        Directory.CreateDirectory(titiDir);
        File.WriteAllText(Path.Combine(titiDir, "test.txt"), "hello");

        var (stdout, stderr, exitCode) = TitiTestRunner.RunTiti(["clean"], FixtureDir, 5000);

        Assert.True(exitCode == 0, $"Exit code {exitCode}: {stderr}");
        Assert.False(Directory.Exists(titiDir), ".titi/ should be removed");
    }
}
