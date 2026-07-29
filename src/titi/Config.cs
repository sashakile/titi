// titi.config — Load/validate titi.config.edn; apply defaults when file absent
// Uses ClojureCLR's EDN reader for parsing, with C# fallback

namespace titi.Config;

using System.Text.RegularExpressions;

public static class ConfigLoader
{
    static readonly TitiConfig Defaults = new(
        Prefix: "",
        SourceRoot: "src/",
        VersionPolicy: VersionPolicy.SemverCompatible,
        Cache: new CacheConfig(
            Enabled: true,
            Directory: ".titi/",
            MaxAge: 3600,
            GlobalTriggers: ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"]
        ),
        TestTiers: new TestTierConfig(
            Unit: ["**/*.UnitTests.csproj"],
            Package: ["**/*.PackageTests.csproj"],
            Integration: ["**/*.IntegrationTests.csproj"],
            Compatibility: ["**/*.CompatTests.csproj"],
            DefaultTier: "integration"
        ),
        Ide: new IdeConfig(
            LaunchCommand: "",
            Args: [],
            AutoOpen: false
        ),
        Ci: new CiConfig(
            FullRegressionBranches: ["main", "release/*"],
            MaxParallelism: 0,
            OutputFormat: "text"
        )
    );

    /// <summary>Load config from repo root. Missing file returns defaults (not an error).</summary>
    public static (TitiConfig? Config, TitiError? Error) Load(string repoRoot)
    {
        var configPath = Path.Combine(repoRoot, "titi.config.edn");

        if (!File.Exists(configPath))
            return (Defaults, null);

        try
        {
            var raw = File.ReadAllText(configPath);
            return Parse(raw, configPath);
        }
        catch (Exception ex)
        {
            var err = new TitiError(
                ErrorCode.ConfigInvalid,
                $"E009: Failed to read config file: {ex.Message}",
                new() { ["command"] = "config", ["target"] = configPath, ["phase"] = "config" },
                ["Ensure titi.config.edn is a valid UTF-8 text file"]
            );
            return (null, err);
        }
    }

    static (TitiConfig? Config, TitiError? Error) Parse(string raw, string path)
    {
        try
        {
            // Minimal EDN parser for titi.config.edn
            var config = ParseEdn(raw, path);
            return (config, null);
        }
        catch (Exception ex)
        {
            var err = new TitiError(
                ErrorCode.ConfigInvalid,
                $"E009: Invalid config at {path}: {ex.Message}",
                new() { ["command"] = "config", ["target"] = path, ["phase"] = "config", ["parseError"] = ex.Message },
                ["Check titi.config.edn syntax (valid EDN format)"]
            );
            return (null, err);
        }
    }

    static TitiConfig ParseEdn(string raw, string path)
    {
        // Strip comments
        raw = Regex.Replace(raw, @";[^\n]*", "");


        // Basic EDN validation: must start with { and end with }
        var trimmed = raw.Trim();
        if (!trimmed.StartsWith('{') || !trimmed.EndsWith('}'))
            throw new FormatException("Config file must contain a valid EDN map");

        var prefix = ExtractString(raw, ":prefix");
        var sourceRoot = ExtractString(raw, ":source-root") ?? ExtractString(raw, ":sourceRoot") ?? "src/";
        var versionPolicyStr = ExtractKeyword(raw, ":version-policy") ?? ExtractKeyword(raw, ":versionPolicy") ?? "semver-compatible";
        var detectionEnabled = ExtractKeyword(raw, ":test-detection-enabled") == "true";
        var fallbackThreshold = ParseFallbackThreshold(ExtractKeyword(raw, ":fallback-threshold"));

        var versionPolicy = versionPolicyStr switch
        {
            "strict" => VersionPolicy.Strict,
            "force" => VersionPolicy.Force,
            _ => VersionPolicy.SemverCompatible,
        };

        var testDetection = detectionEnabled
            ? TestDetectionConfig.Default with { Enabled = true, FallbackThreshold = fallbackThreshold ?? 0.7 }
            : new TestDetectionConfig();

        return new TitiConfig(
            Prefix: prefix ?? "",
            SourceRoot: sourceRoot,
            VersionPolicy: versionPolicy,
            Cache: new CacheConfig(true, ".titi/", 3600, ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"]),
            TestTiers: Defaults.TestTiers,
            Ide: Defaults.Ide,
            Ci: Defaults.Ci
        )
        {
            TestDetection = testDetection
        };
    }

    static string? ExtractString(string raw, string key)
    {
        var match = Regex.Match(raw, $@"{key}\s+""([^""]*)""");
        return match.Success ? match.Groups[1].Value : null;
    }

    static string? ExtractKeyword(string raw, string key)
    {
        var match = Regex.Match(raw, $@"{key}\s+([a-zA-Z0-9_.-]+)");
        return match.Success ? match.Groups[1].Value : null;
    }

    static double? ParseFallbackThreshold(string? raw)
    {
        if (raw == null) return null;
        if (!double.TryParse(raw,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out var threshold) || threshold < 0.0 || threshold > 1.0)
            throw new FormatException("test-detection.fallback-threshold must be a number between 0 and 1");
        return threshold;
    }
}
