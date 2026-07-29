// Tests for test-SDK package-reference detection (titi-4p5)

namespace titi.Tests;

using titi.Interop;
using titi.Config;
using static titi.Interop.MsBuildSetup;

public class TestSdkDetectionTests
{
    // ── HasTestSdkRef ───────────────────────────────────────────

    [Fact]
    public void HasTestSdkRef_WithXunitPackage_ReturnsTrue()
    {
        var refs = new[] { new PackageRef("xunit", "2.9.*", null, null) };
        Assert.True(HasTestSdkRef(refs));
    }

    [Fact]
    public void HasTestSdkRef_WithNUnitPackage_ReturnsTrue()
    {
        var refs = new[] { new PackageRef("NUnit", "4.0.*", null, null) };
        Assert.True(HasTestSdkRef(refs));
    }

    [Fact]
    public void HasTestSdkRef_WithMSTestAdapter_ReturnsTrue()
    {
        var refs = new[] { new PackageRef("MSTest.TestAdapter", "3.0.*", null, null) };
        Assert.True(HasTestSdkRef(refs));
    }

    [Fact]
    public void HasTestSdkRef_WithMicrosoftNetTestSdk_ReturnsTrue()
    {
        var refs = new[] { new PackageRef("Microsoft.NET.Test.Sdk", "17.*", null, null) };
        Assert.True(HasTestSdkRef(refs));
    }

    [Fact]
    public void HasTestSdkRef_WithSerilogPackage_ReturnsFalse()
    {
        var refs = new[] { new PackageRef("Serilog", "3.0.*", null, null) };
        Assert.False(HasTestSdkRef(refs));
    }

    [Fact]
    public void HasTestSdkRef_WithEmptyRefs_ReturnsFalse()
    {
        Assert.False(HasTestSdkRef([]));
    }

    [Fact]
    public void HasTestSdkRef_WithNullRefs_ReturnsFalse()
    {
        Assert.False(HasTestSdkRef(null!));
    }

    [Fact]
    public void HasTestSdkRef_WithCustomSdkIds_UsesThem()
    {
        var refs = new[] { new PackageRef("MyCustomTestFramework", "1.0", null, null) };
        var customIds = new[] { "MyCustomTestFramework" };
        Assert.True(HasTestSdkRef(refs, customIds));
    }

    [Fact]
    public void HasTestSdkRef_WithCustomSdkIds_DoesNotMatchByDefault()
    {
        var refs = new[] { new PackageRef("MyCustomTestFramework", "1.0", null, null) };
        Assert.False(HasTestSdkRef(refs, DefaultTestSdkIds));
    }

    [Fact]
    public void HasTestSdkRef_IsCaseInsensitive()
    {
        var refs = new[] { new PackageRef("XUNIT", "2.9.*", null, null) };
        Assert.True(HasTestSdkRef(refs));
    }

    // ── config parsing: test-sdk-ids ────────────────────────────

    [Fact]
    public void Config_WithTestSdkIds_OverridesDefault()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-config-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.json"),
                "{\"prefix\": \"Orion.\",\n" +
                "  \"test-sdk-ids\": [\"MyFramework.Test\", \"Other.Test\"]}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(err);
            Assert.NotNull(config);
            Assert.Equal(["MyFramework.Test", "Other.Test"], config.TestSdkIds);
            Assert.Equal(["MyFramework.Test", "Other.Test"], config.EffectiveTestSdkIds);
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Config_WithoutTestSdkIds_UsesDefault()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-config-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.json"),
                "{\"prefix\": \"Orion.\"}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(err);
            Assert.NotNull(config);
            Assert.Null(config.TestSdkIds);
            Assert.Equal(DefaultTestSdkIds, config.EffectiveTestSdkIds);
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Config_WithEmptyTestSdkIdsArray_UsesDefault()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-config-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "titi.config.json"),
                "{\"prefix\": \"Orion.\",\n" +
                "  \"test-sdk-ids\": []}");

            var (config, err) = ConfigLoader.Load(tmpDir);
            Assert.Null(err);
            Assert.NotNull(config);
            // Empty array resolves to null, which means use default
            Assert.Null(config.TestSdkIds);
            Assert.Equal(DefaultTestSdkIds, config.EffectiveTestSdkIds);
        }
        finally
        {
            if (Directory.Exists(tmpDir))
                Directory.Delete(tmpDir, recursive: true);
        }
    }
}
