// Tests for TID-2jy: TFM parsing against real-world monikers

namespace titi.Tests;

public class TfmParsingTests
{
    static string[] ParseTfms(string raw) =>
        Interop.MsBuildSetup.ParseTfms(raw).Select(t => t.Moniker).ToArray();

    [Fact]
    public void Parses_Simple_Net10() =>
        Assert.Contains("net10.0", ParseTfms("net10.0"));

    [Fact]
    public void Parses_MultiTarget_Semicolon() =>
        Assert.Equal(["net9.0", "net10.0"], ParseTfms("net9.0;net10.0"));

    [Fact]
    public void Handles_TrailingSemicolon_WithoutCrash()
    {
        // Real csproj files may have trailing semicolons
        var result = ParseTfms("net10.0;");
        Assert.Contains("net10.0", result);
    }

    [Fact]
    public void Handles_PlatformSuffix_WithoutCrash()
    {
        // e.g. net10.0-windows, net10.0-android
        var result = ParseTfms("net10.0-windows");
        Assert.Contains("net10.0-windows", result);
    }

    [Fact]
    public void Handles_MultiTarget_WithPlatformSuffixes()
    {
        var result = ParseTfms("net10.0;net10.0-windows");
        Assert.Contains("net10.0", result);
        Assert.Contains("net10.0-windows", result);
    }

    [Fact]
    public void Handles_Netstandard()
    {
        var result = ParseTfms("netstandard2.0");
        Assert.Contains("netstandard2.0", result);
    }

    [Fact]
    public void Handles_Netcoreapp()
    {
        var result = ParseTfms("netcoreapp3.1");
        Assert.Contains("netcoreapp3.1", result);
    }

    [Fact]
    public void Handles_MixedModernAndLegacy()
    {
        var result = ParseTfms("net48;net8.0");
        Assert.Contains("net48", result);
        Assert.Contains("net8.0", result);
    }

    [Fact]
    public void Handles_EmptyString() =>
        Assert.Empty(ParseTfms(""));

    [Fact]
    public void Handles_NullString() =>
        Assert.Empty(ParseTfms(null!));

    [Fact]
    public void Handles_WhitespaceOnly()
    {
        // Whitespace-only input should not crash
        var result = ParseTfms("  ");
        Assert.Empty(result);
    }

    [Fact]
    public void Handles_TrailingWhitespaceSemicolon()
    {
        // "net10.0; " → has empty trailing segment after trim
        var result = ParseTfms("net10.0; ");
        Assert.Contains("net10.0", result);
    }

    [Fact]
    public void Handles_WhitespaceBetweenMonikers()
    {
        var result = ParseTfms("net9.0; net10.0");
        Assert.Contains("net9.0", result);
        Assert.Contains("net10.0", result);
    }

    [Fact]
    public void RealWorld_Serilog_DoesNotCrash()
    {
        // Serilog targets net8.0 and net48 simultaneously
        var result = ParseTfms("net8.0;net48");
        Assert.Contains("net8.0", result);
        Assert.Contains("net48", result);
    }

    [Fact]
    public void RealWorld_EFCore_DoesNotCrash()
    {
        // EFCore may use net10.0 with platform-specific TFMs
        var result = ParseTfms("net10.0;net10.0-android;net10.0-ios");
        Assert.Contains("net10.0", result);
        Assert.Contains("net10.0-android", result);
        Assert.Contains("net10.0-ios", result);
    }

    [Fact]
    public void UnknownMoniker_UsesOtherFramework()
    {
        // Monikers not matching net* should parse without crash
        var result = ParseTfms("native");
        Assert.Contains("native", result);
    }
}
