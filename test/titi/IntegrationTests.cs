// Integration tests — run titi CLI against sample-monorepo fixture

namespace titi.Tests;

using System.Diagnostics;
using System.Text.Json;

public class IntegrationTests
{
    static readonly string FixtureDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../test/fixtures/sample-monorepo"));

    static readonly string ProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../src/titi/titi.csproj"));

    /// <summary>Run titi open Orion.Core.Data against the sample-monorepo fixture.</summary>
    [Fact]
    public void TitiOpen_AgainstFixture_GeneratesSlnx()
    {
        // Arrange
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{ProjectPath}\" -- open Orion.Core.Data",
            WorkingDirectory = FixtureDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // Act
        using var proc = Process.Start(startInfo);
        Assert.NotNull(proc);
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30000);

        // Assert: exit code 0
        Assert.True(proc.ExitCode == 0, $"Exit code {proc.ExitCode}: {stderr}");

        // Assert: JSON output with solutionPath
        var output = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stdout);
        Assert.NotNull(output);
        Assert.True(output.ContainsKey("solutionPath"));

        var slnxPath = output["solutionPath"].GetString();
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

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{ProjectPath}\" -- clean",
            WorkingDirectory = FixtureDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(startInfo);
        Assert.NotNull(proc);
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(5000);

        Assert.True(proc.ExitCode == 0, $"Exit code {proc.ExitCode}: {stderr}");
        Assert.False(Directory.Exists(titiDir), ".titi/ should be removed");
    }
}
