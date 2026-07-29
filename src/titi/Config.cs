// titi.config — Load/validate titi.config.json; apply defaults when file absent

namespace titi.Config;

using System.Text.Json;
using System.Text.Json.Nodes;

public static class ConfigLoader
{
    static readonly TitiConfig Defaults = new(
        Prefix: "",
        SourceRoot: ["src/"],
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

    /// <summary>Supported top-level config keys (all others are rejected).</summary>
    static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "prefix", "source-root", "sourceroot", "source-roots",
        "version-policy", "versionpolicy",
        "test-detection-enabled", "fallback-threshold"
    };

    /// <summary>Load config from repo root. Missing file returns defaults (not an error).</summary>
    public static (TitiConfig? Config, TitiError? Error) Load(string repoRoot)
    {
        var configPath = Path.Combine(repoRoot, "titi.config.json");

        if (!File.Exists(configPath))
            return (Defaults, null);

        try
        {
            var raw = File.ReadAllText(configPath);
            var (config, err) = Parse(raw, configPath);
            if (config != null)
                ValidateSourceRoots(repoRoot, config);
            return (config, err);
        }
        catch (Exception ex)
        {
            var err = new TitiError(
                ErrorCode.ConfigInvalid,
                $"E009: Failed to read config file: {ex.Message}",
                new() { ["command"] = "config", ["target"] = configPath, ["phase"] = "config" },
                ["Ensure titi.config.json is a valid UTF-8 text file"]
            );
            return (null, err);
        }
    }

    static (TitiConfig? Config, TitiError? Error) Parse(string raw, string path)
    {
        try
        {
            var config = ParseJson(raw, path);
            return (config, null);
        }
        catch (Exception ex)
        {
            var err = new TitiError(
                ErrorCode.ConfigInvalid,
                $"E009: Invalid config at {path}: {ex.Message}",
                new() { ["command"] = "config", ["target"] = path, ["phase"] = "config", ["parseError"] = ex.Message },
                ["Check titi.config.json syntax (valid JSON format)"]
            );
            return (null, err);
        }
    }

    static TitiConfig ParseJson(string raw, string path)
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("Config root must be a JSON object");

        // Reject unsupported top-level keys
        var unsupported = new List<string>();
        foreach (var prop in root.EnumerateObject())
        {
            if (!KnownKeys.Contains(prop.Name))
                unsupported.Add(prop.Name);
        }
        if (unsupported.Count > 0)
        {
            var listed = string.Join(", ", unsupported.OrderBy(k => k));
            throw new FormatException($"Unsupported config key(s): {listed}");
        }

        var prefix = GetString(root, "prefix") ?? "";
        var sourceRoot = ParseSourceRoots(root);
        var versionPolicyStr = GetString(root, "version-policy") ?? GetString(root, "versionPolicy") ?? "semver-compatible";
        var detectionEnabled = GetBool(root, "test-detection-enabled") ?? false;
        var fallbackThreshold = ParseFallbackThreshold(GetDouble(root, "fallback-threshold"));

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
            Prefix: prefix,
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

    static string? GetString(JsonElement obj, string key)
    {
        if (obj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.String)
            return el.GetString();
        return null;
    }

    static bool? GetBool(JsonElement obj, string key)
    {
        if (obj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
            return el.GetBoolean();
        return null;
    }

    static double? GetDouble(JsonElement obj, string key)
    {
        if (obj.TryGetProperty(key, out var el) && el.ValueKind == JsonValueKind.Number)
            return el.GetDouble();
        return null;
    }

    /// <summary>
    /// Parse source-root from config. Accepts either:
    ///   "source-root": "src/"               (single string, backwards compatible)
    ///   "source-roots": ["src/", "test/"]    (array of strings)
    /// Default: ["src/"]
    /// </summary>
    static string[] ParseSourceRoots(JsonElement root)
    {
        // Try plural array form first
        if (root.TryGetProperty("source-roots", out var arrayEl) && arrayEl.ValueKind == JsonValueKind.Array)
        {
            var strings = new List<string>();
            foreach (var item in arrayEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    strings.Add(item.GetString()!);
            }
            if (strings.Count > 0)
                return [.. strings];
        }

        // Fall back to singular string form
        var single = GetString(root, "source-root") ?? GetString(root, "sourceRoot");
        if (single != null)
            return [single];

        // Default
        return ["src/"];
    }

    static double? ParseFallbackThreshold(double? raw)
    {
        if (raw == null) return null;
        if (raw < 0.0 || raw > 1.0)
            throw new FormatException("test-detection.fallback-threshold must be a number between 0 and 1");
        return raw;
    }

    /// <summary>Validate source root paths: reject absolutes, warn if missing.</summary>
    static void ValidateSourceRoots(string repoRoot, TitiConfig config)
    {
        foreach (var sr in config.SourceRoot)
        {
            if (Path.IsPathRooted(sr))
            {
                Console.Error.WriteLine($"warning: source-root '{sr}' is an absolute path — use a repo-relative path instead");
            }

            var fullPath = Path.GetFullPath(Path.Combine(repoRoot, sr));
            if (!Directory.Exists(fullPath))
            {
                Console.Error.WriteLine($"warning: source-root '{sr}' does not exist in the repository ({fullPath})");
            }
        }
    }
}
