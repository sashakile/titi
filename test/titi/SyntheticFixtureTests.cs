// Tests for TID-7: Synthetic fixture integration

namespace titi.Tests;

using System.Diagnostics;
using System.Text.Json;

public class SyntheticFixtureTests
{
    static readonly string FixtureDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../test/fixtures/synthetic-monorepo"));

    static readonly string ProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../src/titi/titi.csproj"));

    [Fact(Skip = "Slow (requires NuGet restore); run with: dotnet test --filter Category=Integration")]
    public void TitiOpen_WithSyntheticFixture_FindsAllProjects()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{ProjectPath}\" -- open Orion.Core.Data",
            WorkingDirectory = FixtureDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi);
        Assert.NotNull(proc);
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30000);

        Assert.True(proc.ExitCode == 0, $"Exit code {proc.ExitCode}: {stderr}");

        var output = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stdout);
        Assert.NotNull(output);
        Assert.True(output.ContainsKey("projectCount"));
        Assert.True(output["projectCount"].GetInt32() > 0);
    }

    [Fact(Skip = "Slow (requires NuGet restore); run with: dotnet test --filter Category=Integration")]
    public void SyntheticFixture_ProjectsBuild()
    {
        // Verify the synthetic fixture itself builds
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "build tests/Orion.UnitTests/Orion.UnitTests.csproj --nologo -v q",
            WorkingDirectory = FixtureDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi);
        Assert.NotNull(proc);
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(120000);

        Assert.True(proc.ExitCode == 0, $"Build failed: {stderr}");
    }

    [Fact(Skip = "Requires NuGet packages; run manually after dotnet restore in fixture")]
    public void SyntheticFixture_TestsPass()
    {
        // Run actual dotnet test on the synthetic fixture
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "test tests/Orion.UnitTests/Orion.UnitTests.csproj --nologo -v q",
            WorkingDirectory = FixtureDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi);
        Assert.NotNull(proc);
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(120000);

        Assert.True(proc.ExitCode == 0, $"Tests failed: {stderr}");
    }

    // CLI-22: `titi tests record` runs all test projects with coverage, ingests
    // TRX + Cobertura, and builds the edge index in .titi/test-cache/edges/.
    [Fact(Skip = "Slow (runs dotnet test with coverage); run with: dotnet test --filter Category=Integration")]
    public void TitiTestsRecord_AgainstSyntheticFixture_BuildsEdgeIndex()
    {
        // Clean any prior titi state so this is a first-invocation recording.
        var titiDir = Path.Combine(FixtureDir, ".titi");
        if (Directory.Exists(titiDir)) Directory.Delete(titiDir, recursive: true);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{ProjectPath}\" -- tests record",
            WorkingDirectory = FixtureDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(psi);
        Assert.NotNull(proc);
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(300000);

        Assert.True(proc.ExitCode == 0, $"Exit code {proc.ExitCode}: {stderr}");

        var edgesPath = Path.Combine(titiDir, "test-cache", "edges", "edges.json");
        Assert.True(File.Exists(edgesPath), $"edge index not found at {edgesPath}");

        var fingerprintPath = Path.Combine(titiDir, "test-cache", "fingerprint");
        Assert.True(File.Exists(fingerprintPath), "fingerprint not written for incremental skip");

        // The fixture's tests exercise library code (Orion.Core.Data.Parser/Foo,
        // Orion.Auth.AuthService, Orion.Storage.Repository), so the recorded
        // edge index MUST be non-empty (validates the full TD-03 pipeline:
        // graph -> test run with coverage -> TRX+Cobertura -> EdgeBuilder).
        var edgesJson = File.ReadAllText(edgesPath);
        var edges = System.Text.Json.JsonSerializer.Deserialize<List<JsonElement>>(edgesJson
            .StartsWith('[') ? edgesJson : "[]") ?? new();
        Assert.True(edges.Count > 0, $"expected non-empty edge index, got {edges.Count}");
    }
}
