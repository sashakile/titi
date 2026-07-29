// Tests for TID-7: Synthetic fixture integration

namespace titi.Tests;

using System.Text.Json;

public class SyntheticFixtureTests
{
    static readonly string FixtureDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../test/fixtures/synthetic-monorepo"));

    [Fact]
    [Trait("Category", "Integration")]
    public void TitiOpen_WithSyntheticFixture_FindsAllProjects()
    {
        var (output, stderr) = TitiTestRunner.RunTitiAndParseJson(
            ["open", "Orion.Core.Data"], FixtureDir);

        Assert.True(output.RootElement.TryGetProperty("projectCount", out var count));
        Assert.True(count.GetInt32() > 0);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SyntheticFixture_ProjectsBuild()
    {
        // Verify the synthetic fixture itself builds
        var (stdout, stderr, exitCode) = TitiTestRunner.RunDotnet(
            "build tests/Orion.UnitTests/Orion.UnitTests.csproj --nologo -v q",
            FixtureDir, 120_000);

        Assert.True(exitCode == 0, $"Build failed (exit {exitCode}): {stderr}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void SyntheticFixture_TestsPass()
    {
        // Run actual dotnet test on the synthetic fixture
        var (stdout, stderr, exitCode) = TitiTestRunner.RunDotnet(
            "test tests/Orion.UnitTests/Orion.UnitTests.csproj --nologo -v q",
            FixtureDir, 120_000);

        Assert.True(exitCode == 0, $"Tests failed (exit {exitCode}): {stderr}");
    }

    // CLI-22: `titi tests record` runs all test projects with coverage, ingests
    // TRX + Cobertura, and builds the edge index in .titi/test-cache/edges/.
    [Fact]
    [Trait("Category", "Integration")]
    public void TitiTestsRecord_AgainstSyntheticFixture_BuildsEdgeIndex()
    {
        // Clean any prior titi state so this is a first-invocation recording.
        var titiDir = Path.Combine(FixtureDir, ".titi");
        if (Directory.Exists(titiDir)) Directory.Delete(titiDir, recursive: true);

        var (stdout, stderr, exitCode) = TitiTestRunner.RunTiti(
            ["tests", "record"], FixtureDir, 300_000);

        Assert.True(exitCode == 0, $"Exit code {exitCode}: {stderr}");

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
