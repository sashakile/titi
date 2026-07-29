// Tests for titi.config — EDN loading, defaults, error handling

namespace titi.Tests;

using titi.Config;

public class ConfigTests
{
    /// <summary>Missing config file returns defaults (not an error).</summary>

    // ── test-detection.fallback-threshold ───────────────────────

    // ── test-detection.fallback-threshold ───────────────────────

    [Fact]
    public void Config_WithFallbackThreshold_ParsesCorrectly()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-config-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.edn"),
                "{:prefix \"Orion.\"\n" +
                "  :test-detection-enabled true\n" +
                "  :fallback-threshold 0.9}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(err);
            Assert.NotNull(config);
            Assert.True(config.TestDetection.Enabled);
            Assert.Equal(0.9, config.TestDetection.FallbackThreshold, precision: 2);
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Config_FallbackThresholdBelowZero_Rejected()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-config-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.edn"),
                "{:prefix \"Orion.\"\n" +
                "  :fallback-threshold -0.1}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(config);
            Assert.NotNull(err);
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Config_FallbackThresholdAboveOne_Rejected()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-config-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.edn"),
                "{:prefix \"Orion.\"\n" +
                "  :fallback-threshold 1.5}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(config);
            Assert.NotNull(err);
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Config_FallbackThresholdDefault_Is07()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-config-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            // No test-detection section — use defaults
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.edn"),
                "{:prefix \"Orion.\"}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(err);
            Assert.NotNull(config);
            Assert.Equal(0.7, config.TestDetection.FallbackThreshold, precision: 2);
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Load_NoConfigFile_ReturnsDefaults()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(err);
            Assert.NotNull(config);
            Assert.Equal("src/", config.SourceRoot);
            Assert.Equal("", config.Prefix);
            Assert.Equal(VersionPolicy.SemverCompatible, config.VersionPolicy);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    /// <summary>Valid EDN config file parses correctly.</summary>
    [Fact]
    public void Load_ValidEdn_ParsesCorrectly()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.edn"),
                @"{:prefix ""Orion.""
                  :source-root ""src/""
                  :version-policy :semver-compatible}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(err);
            Assert.NotNull(config);
            Assert.Equal("Orion.", config.Prefix);
            Assert.Equal("src/", config.SourceRoot);
            Assert.Equal(VersionPolicy.SemverCompatible, config.VersionPolicy);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    /// <summary>Invalid EDN file returns E009 error.</summary>
    [Fact]
    public void Load_InvalidEdn_ReturnsE009()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.edn"),
                "this is not valid edn {{{broken");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(config);
            Assert.NotNull(err);
            Assert.Equal(ErrorCode.ConfigInvalid, err.Code);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    /// <summary>Config with only prefix parses and uses defaults for other fields.</summary>
    [Fact]
    public void Load_PartialConfig_UsesDefaults()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.edn"),
                @"{:prefix ""MyCo.""}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(err);
            Assert.NotNull(config);
            Assert.Equal("MyCo.", config.Prefix);
            Assert.Equal("src/", config.SourceRoot); // default
            Assert.Equal(VersionPolicy.SemverCompatible, config.VersionPolicy); // default
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
