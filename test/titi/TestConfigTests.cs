// Tests for TID-6: TestDetectionConfig

namespace titi.Tests;

using titi.Config;

public class TestConfigTests
{
    [Fact]
    public void TestDetectionConfig_HasDefaults()
    {
        var config = new TestDetectionConfig();
        Assert.False(config.Enabled);
        Assert.Equal("dotnet", config.VstestPath);
        Assert.Equal(0.7, config.FallbackThreshold);
        Assert.Equal(".titi/test-cache", config.CacheDir);
    }

    [Fact]
    public void TitiConfig_IncludesTestDetection()
    {
        var td = new TestDetectionConfig(
            Enabled: true,
            VstestPath: "dotnet",
            CollectCoverage: true,
            CoverageFormat: CoverageFormat.Cobertura,
            CacheDir: ".titi/test-cache",
            FallbackThreshold: 0.8,
            AlwaysRunEvictionThreshold: 5,
            BatchSize: 100,
            ExcludePatterns: ["**/obj/**"]
        );

        // Verify it's embedded in TitiConfig via init
        var config = new TitiConfig(
            Prefix: "Test.", SourceRoot: "src/", VersionPolicy: VersionPolicy.SemverCompatible,
            Cache: new CacheConfig(true, ".titi/", 3600, []),
            TestTiers: new TestTierConfig([], [], [], [], "unit"),
            Ide: new IdeConfig("", [], false),
            Ci: new CiConfig([], 0, "text")
        )
        {
            TestDetection = td
        };

        Assert.True(config.TestDetection.Enabled);
        Assert.Equal(0.8, config.TestDetection.FallbackThreshold);
        Assert.Contains("**/obj/**", config.TestDetection.ExcludePatterns);
    }
}
