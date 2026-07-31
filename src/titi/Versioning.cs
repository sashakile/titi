// titi.versioning — NBGV integration, version detection, version resolution, and version plan computation
// VN-01: Read version from version.json per project
// VN-02: NuGet lowest-applicable-version resolution

namespace titi.Versioning;

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

/// <summary>NBGV version.json file format (subset relevant to titi).</summary>
public record NbgvVersionFile(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("assemblyVersion")] string? AssemblyVersion,
    [property: JsonPropertyName("publicReleaseRefSpec")] string? PublicReleaseRefSpec
);

/// <summary>Version info for a single project in the dependency graph.</summary>
public record ProjectVersionInfo(
    string PackageId,
    string ProjectPath,
    string? VersionJsonPath,
    string? CurrentVersion,
    string? AssemblyVersion,
    bool IsManaged
);

/// <summary>Result of a version detection run.</summary>
public record VersionDetectResult(
    ProjectVersionInfo[] Projects,
    int ManagedCount,
    int UnmanagedCount
);

// ── NuGet Version Resolution (VN-02) ────────────────────────────

/// <summary>Internal parsed version for comparison.</summary>
internal readonly record struct ParsedVersion(
    int Major,
    int Minor,
    int Patch,
    string? Prerelease,
    string? Metadata
)
{
    public override string ToString()
    {
        var s = $"{Major}.{Minor}.{Patch}";
        if (Prerelease != null) s += $"-{Prerelease}";
        if (Metadata != null) s += "+" + Metadata;
        return s;
    }
}

/// <summary>Which version field is floating in a version spec.</summary>
internal enum FloatingField { None, Patch, Minor, Major, Prerelease }

/// <summary>Resolved version spec — either a range or a floating pattern.</summary>
internal readonly record struct VersionRange(
    ParsedVersion? MinVersion,
    ParsedVersion? MaxVersion,
    bool IsMinInclusive,
    bool IsMaxInclusive,
    FloatingField Floating,
    string? FixedPrereleasePrefix
);

/// <summary>Resolve floating NuGet versions to the lowest applicable version from a feed.</summary>
public static class NuGetVersionResolver
{
    static readonly Regex ExactVersionRx = new(
        @"^(\d+)\.(\d+)(?:\.(\d+))?(?:-(\S+?))?(?:\+(\S+?))?$",
        RegexOptions.Compiled);

    static readonly Regex FloatVersionRx = new(
        @"^(\d+)\.(\d+)\.(\*|\d+)(?:-(\*|[\w.-]*\*))?$",
        RegexOptions.Compiled);

    static readonly Regex FloatMinorRx = new(
        @"^(\d+)\.\*$",
        RegexOptions.Compiled);

    static readonly Regex FloatMajorRx = new(
        @"^\*$",
        RegexOptions.Compiled);

    static readonly Regex RangeRx = new(
        @"^([\[(])\s*(.*?)\s*,\s*(.*?)\s*([\])])$",
        RegexOptions.Compiled);

    static readonly Regex ExactRangeRx = new(
        @"^\[\s*([^\]]+)\s*\]$",
        RegexOptions.Compiled);

    static readonly Regex SemVerRangeRx = new(
        @"^([~^])(\d+)\.(\d+)(?:\.(\d+))?$",
        RegexOptions.Compiled);

    /// <summary>Check if a version spec is a floating version pattern.</summary>
    public static bool IsFloating(string versionSpec)
    {
        if (string.IsNullOrEmpty(versionSpec)) return false;
        return versionSpec.Contains('*');
    }

    /// <summary>
    /// Resolve a version spec to the lowest applicable version from the provided list.
    /// Supports exact versions, floating versions (1.0.*, 1.*, *), NuGet ranges
    /// ([1.0.0,2.0.0), (1.0.0,)), and SemVer ranges (^1.0.0, ~1.0.0).
    /// </summary>
    public static string? Resolve(string versionSpec, IReadOnlyList<string> availableVersions)
    {
        if (string.IsNullOrEmpty(versionSpec) || availableVersions.Count == 0)
            return null;

        var range = ParseVersionSpec(versionSpec);
        if (range == null) return null;

        var r = range.Value;

        // For exact version (no floating, no range syntax), check if it's available
        if (r.Floating == FloatingField.None && r.MinVersion != null && r.MaxVersion == null
            && r.IsMinInclusive && !HasRangeSyntax(versionSpec))
        {
            return availableVersions.Any(v => VersionEquals(v, r.MinVersion.Value))
                ? versionSpec
                : null;
        }

        // Parse all available versions, filter, find lowest
        var parsed = availableVersions
            .Select(v => (Raw: v, Parsed: ParseVersion(v)))
            .Where(x => x.Parsed != null)
            .Select(x => (x.Raw, Parsed: x.Parsed!.Value))
            .Where(x => IsInRange(x.Parsed, r))
            .OrderBy(x => x.Parsed.Major)
            .ThenBy(x => x.Parsed.Minor)
            .ThenBy(x => x.Parsed.Patch)
            .ThenBy(x => x.Parsed.Prerelease ?? "\uFFFF") // stable sorts last
            .ToList();

        return parsed.Count > 0 ? FormatVersion(parsed[0].Parsed) : null;
    }

    /// <summary>Parse a version string into a ParsedVersion.</summary>
    internal static ParsedVersion? ParseVersion(string version)
    {
        var m = ExactVersionRx.Match(version);
        if (!m.Success) return null;

        return new ParsedVersion(
            Major: int.Parse(m.Groups[1].Value),
            Minor: int.Parse(m.Groups[2].Value),
            Patch: m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0,
            Prerelease: m.Groups[4].Success ? m.Groups[4].Value : null,
            Metadata: m.Groups[5].Success ? m.Groups[5].Value : null
        );
    }

    /// <summary>Format a ParsedVersion back to a version string.</summary>
    internal static string FormatVersion(ParsedVersion v)
    {
        var s = $"{v.Major}.{v.Minor}.{v.Patch}";
        if (v.Prerelease != null) s += "-" + v.Prerelease;
        return s;
    }

    /// <summary>Check if a version equals a parsed version.</summary>
    static bool VersionEquals(string version, ParsedVersion target)
    {
        var p = ParseVersion(version);
        if (p == null) return false;
        var v = p.Value;
        return v.Major == target.Major
            && v.Minor == target.Minor
            && v.Patch == target.Patch
            && v.Prerelease == target.Prerelease;
    }

    /// <summary>Check if a version string has range syntax (brackets, ^, ~).</summary>
    static bool HasRangeSyntax(string spec)
    {
        if (spec.Length == 0) return false;
        var c = spec[0];
        return c == '[' || c == '(' || c == '^' || c == '~';
    }

    /// <summary>Parse a version spec into a range for filtering.</summary>
    internal static VersionRange? ParseVersionSpec(string spec)
    {
        if (string.IsNullOrEmpty(spec)) return null;

        // NuGet exact range: [1.0.0]
        var exactM = ExactRangeRx.Match(spec);
        if (exactM.Success)
        {
            var p = ParseVersion(exactM.Groups[1].Value.Trim());
            if (p == null) return null;
            // [1.0.0] means exactly 1.0.0 — bound both min and max
            return new VersionRange(
                MinVersion: p, MaxVersion: p,
                IsMinInclusive: true, IsMaxInclusive: true,
                Floating: FloatingField.None, FixedPrereleasePrefix: null);
        }

        // SemVer range: ^1.0.0 or ~1.0.0
        var semverM = SemVerRangeRx.Match(spec);
        if (semverM.Success)
        {
            var major = int.Parse(semverM.Groups[2].Value);
            var minor = int.Parse(semverM.Groups[3].Value);
            var patch = semverM.Groups[4].Success ? int.Parse(semverM.Groups[4].Value) : 0;

            var minVer = new ParsedVersion(major, minor, patch, null, null);

            ParsedVersion? maxVer;
            if (semverM.Groups[1].Value == "^")
            {
                // ^1.0.0 = >=1.0.0, <2.0.0
                maxVer = new ParsedVersion(major + 1, 0, 0, null, null);
            }
            else
            {
                // ~1.0.0 = >=1.0.0, <1.1.0
                maxVer = new ParsedVersion(major, minor + 1, 0, null, null);
            }

            return new VersionRange(
                MinVersion: minVer, MaxVersion: maxVer,
                IsMinInclusive: true, IsMaxInclusive: false,
                Floating: FloatingField.None, FixedPrereleasePrefix: null);
        }

        // NuGet range: [1.0.0, 2.0.0), (1.0.0, ), etc.
        var rangeM = RangeRx.Match(spec);
        if (rangeM.Success)
        {
            var isMinInclusive = rangeM.Groups[1].Value == "[";
            var isMaxInclusive = rangeM.Groups[4].Value == "]";

            ParsedVersion? minVer = null;
            var minStr = rangeM.Groups[2].Value.Trim();
            if (minStr.Length > 0)
            {
                var p = ParseVersion(minStr);
                if (p == null) return null;
                minVer = p;
            }

            ParsedVersion? maxVer = null;
            var maxStr = rangeM.Groups[3].Value.Trim();
            if (maxStr.Length > 0)
            {
                var p = ParseVersion(maxStr);
                if (p == null) return null;
                maxVer = p;
            }

            return new VersionRange(
                MinVersion: minVer, MaxVersion: maxVer,
                IsMinInclusive: isMinInclusive, IsMaxInclusive: isMaxInclusive,
                Floating: FloatingField.None, FixedPrereleasePrefix: null);
        }

        // Floating version: 1.0.*, 1.*, *, 1.0.0-*, 1.0.0-beta.*
        if (spec.Contains('*'))
        {
            // Check for floating prerelease: 1.0.0-* or 1.0.0-beta.*
            var dashIdx = spec.IndexOf('-');
            if (dashIdx >= 0)
            {
                var versionPart = spec[..dashIdx];
                var prereleasePart = spec[(dashIdx + 1)..];

                var vp = ParseVersion(versionPart);
                if (vp == null) return null;
                var pv = vp.Value;

                // Check if the prerelease has * in it
                if (prereleasePart == "*" || prereleasePart.EndsWith(".*"))
                {
                    var fixedPrefix = prereleasePart == "*" ? "" : prereleasePart[..^2];
                    // trim trailing dot from prefix
                    if (fixedPrefix.EndsWith(".")) fixedPrefix = fixedPrefix[..^1];
                    return new VersionRange(
                        MinVersion: new ParsedVersion(pv.Major, pv.Minor, pv.Patch, null, null),
                        MaxVersion: null,
                        IsMinInclusive: true, IsMaxInclusive: false,
                        Floating: FloatingField.Prerelease,
                        FixedPrereleasePrefix: fixedPrefix.Length > 0 ? fixedPrefix : null);
                }

                return null;
            }

            // Check for 1.0.* (float patch)
            var floatM = FloatVersionRx.Match(spec);
            if (floatM.Success)
            {
                var major = int.Parse(floatM.Groups[1].Value);
                var minor = int.Parse(floatM.Groups[2].Value);

                return new VersionRange(
                    MinVersion: new ParsedVersion(major, minor, 0, null, null),
                    MaxVersion: null,
                    IsMinInclusive: true, IsMaxInclusive: false,
                    Floating: FloatingField.Patch, FixedPrereleasePrefix: null);
            }

            // Check for 1.* (float minor)
            var minorM = FloatMinorRx.Match(spec);
            if (minorM.Success)
            {
                var major = int.Parse(minorM.Groups[1].Value);
                return new VersionRange(
                    MinVersion: new ParsedVersion(major, 0, 0, null, null),
                    MaxVersion: null,
                    IsMinInclusive: true, IsMaxInclusive: false,
                    Floating: FloatingField.Minor, FixedPrereleasePrefix: null);
            }

            // Check for * (float major)
            if (FloatMajorRx.IsMatch(spec))
            {
                return new VersionRange(
                    MinVersion: null, MaxVersion: null,
                    IsMinInclusive: true, IsMaxInclusive: false,
                    Floating: FloatingField.Major, FixedPrereleasePrefix: null);
            }

            return null;
        }

        // Bare version: 1.0.0 (exact pin)
        var bare = ParseVersion(spec);
        if (bare != null)
        {
            return new VersionRange(
                MinVersion: bare, MaxVersion: null,
                IsMinInclusive: true, IsMaxInclusive: false,
                Floating: FloatingField.None, FixedPrereleasePrefix: null);
        }

        return null;
    }

    /// <summary>Check if a parsed version is within a version range.</summary>
    static bool IsInRange(ParsedVersion version, VersionRange range)
    {
        // Check min bound
        if (range.MinVersion != null)
        {
            var min = range.MinVersion.Value;

            if (range.Floating == FloatingField.Prerelease)
            {
                // For floating prerelease, min version is the base version (1.0.0).
                // Match only major.minor.patch — any prerelease of the base version qualifies.
                if (version.Major != min.Major || version.Minor != min.Minor || version.Patch != min.Patch)
                    return false;
                if (version.Prerelease == null)
                    return false; // must have a prerelease label
            }
            else if (range.Floating != FloatingField.None)
            {
                // For floating major/minor/patch, the fixed prefix must match
                if (!MatchesFloatingPrefix(version, min, range.Floating))
                    return false;
            }
            else
            {
                // Standard version range comparison
                var cmp = CompareVersions(version, min);
                if (range.IsMinInclusive ? cmp < 0 : cmp <= 0)
                    return false;
            }
        }

        // Check max bound
        if (range.MaxVersion != null)
        {
            var max = range.MaxVersion.Value;
            var cmp = CompareVersions(version, max);
            if (range.IsMaxInclusive ? cmp > 0 : cmp >= 0)
                return false;
        }

        // Check prerelease floating prefix (for prerelease patterns like 1.0.0-beta.*)
        if (range.Floating == FloatingField.Prerelease && range.FixedPrereleasePrefix != null)
        {
            if (version.Prerelease == null)
                return false;
            if (!version.Prerelease.StartsWith(range.FixedPrereleasePrefix))
                return false;
        }

        return true;
    }

    /// <summary>Check if version matches the fixed prefix of a floating range.</summary>
    static bool MatchesFloatingPrefix(ParsedVersion version, ParsedVersion prefix, FloatingField floating)
    {
        if (version.Major != prefix.Major) return false;
        if (floating == FloatingField.Major) return true;
        if (version.Minor != prefix.Minor) return false;
        if (floating == FloatingField.Minor) return true;
        // FloatingField.Patch: patch must match? No, patch is floating so any patch is fine
        if (floating == FloatingField.Patch) return true;
        return false;
    }

    /// <summary>Compare two parsed versions. Returns negative, zero, or positive.</summary>
    static int CompareVersions(ParsedVersion a, ParsedVersion b)
    {
        var majorCmp = a.Major.CompareTo(b.Major);
        if (majorCmp != 0) return majorCmp;

        var minorCmp = a.Minor.CompareTo(b.Minor);
        if (minorCmp != 0) return minorCmp;

        var patchCmp = a.Patch.CompareTo(b.Patch);
        if (patchCmp != 0) return patchCmp;

        // Prerelease: no prerelease > any prerelease (stable > prerelease)
        if (a.Prerelease == null && b.Prerelease != null) return 1;
        if (a.Prerelease != null && b.Prerelease == null) return -1;

        if (a.Prerelease == null && b.Prerelease == null) return 0;

        return string.Compare(a.Prerelease, b.Prerelease, StringComparison.Ordinal);
    }

    /// <summary>Sort key for prerelease labels: null (stable) sorts after all labels (semver).</summary>
}

// ── Baseline Assembly Acquisition (VN-08) ───────────────────────

/// <summary>Result of a baseline assembly acquisition attempt.</summary>
public record BaselineAcquisitionResult(
    string? AssemblyPath,
    string? Version,
    string? Diagnostic
);

/// <summary>Acquire baseline assemblies from NuGet feeds for ApiCompat comparison.</summary>
public static class BaselineAcquirer
{
    /// <summary>Determine the baseline version (highest stable version below the current version).</summary>
    public static string? DetermineBaselineVersion(string currentVersion, IReadOnlyList<string> availableVersions)
    {
        if (string.IsNullOrEmpty(currentVersion) || availableVersions.Count == 0)
            return null;

        var current = NuGetVersionResolver.ParseVersion(currentVersion);
        if (current == null) return null;

        var cv = current.Value;

        // Find the highest stable version below the current version
        var parsed = availableVersions
            .Select(v => (Raw: v, Parsed: NuGetVersionResolver.ParseVersion(v)))
            .Where(x => x.Parsed != null)
            .Select(x => (x.Raw, Parsed: x.Parsed!.Value))
            .Where(x => x.Parsed.Prerelease == null) // only stable versions
            .Where(x => CompareVersionsInternal(x.Parsed, cv) < 0) // below current
            .OrderByDescending(x => x.Parsed.Major)
            .ThenByDescending(x => x.Parsed.Minor)
            .ThenByDescending(x => x.Parsed.Patch)
            .ToList();

        return parsed.Count > 0 ? parsed[0].Raw : null;
    }

    /// <summary>Acquire a baseline assembly from a NuGet feed.</summary>
    public static async Task<BaselineAcquisitionResult> AcquireBaselineAsync(
        string packageId,
        string baselineVersion,
        string feedUrl,
        string cacheDir,
        CancellationToken ct = default)
    {
        try
        {
            var nupkgPath = await DownloadPackageAsync(packageId, baselineVersion, feedUrl, cacheDir, ct).ConfigureAwait(false);
            if (nupkgPath == null)
                return new BaselineAcquisitionResult(null, null, "Failed to download package from feed");

            var assemblyPath = await ExtractAssemblyAsync(nupkgPath, cacheDir, ct).ConfigureAwait(false);
            if (assemblyPath == null)
                return new BaselineAcquisitionResult(null, null, "No .dll found in package");

            return new BaselineAcquisitionResult(assemblyPath, baselineVersion, null);
        }
        catch (HttpRequestException ex)
        {
            return new BaselineAcquisitionResult(null, null, $"Feed unreachable: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            return new BaselineAcquisitionResult(null, null, "Download timed out");
        }
        catch (Exception ex)
        {
            return new BaselineAcquisitionResult(null, null, $"Acquisition failed: {ex.Message}");
        }
    }

    /// <summary>Query available versions from a NuGet v3 feed.</summary>
    public static async Task<IReadOnlyList<string>> QueryAvailableVersionsAsync(
        string packageId, string feedUrl, CancellationToken ct = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var id = packageId.ToLowerInvariant();
            // Use the v3-flatcontainer protocol directly
            var versionsUrl = feedUrl.TrimEnd('/') + $"/v3-flatcontainer/{id}/index.json";
            var response = await client.GetAsync(versionsUrl, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("versions", out var versions))
            {
                return versions.EnumerateArray().Select(v => v.GetString()!).Where(s => s != null).ToList();
            }

            return [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Download a NuGet package from the v3-flatcontainer endpoint.</summary>
    static async Task<string?> DownloadPackageAsync(
        string packageId, string version, string feedUrl, string cacheDir, CancellationToken ct)
    {
        var id = packageId.ToLowerInvariant();
        var ver = version.ToLowerInvariant();
        var nupkgName = $"{id}.{ver}.nupkg";
        var cachePath = Path.Combine(cacheDir, "baselines", nupkgName);

        if (File.Exists(cachePath))
            return cachePath;

        Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var downloadUrl = feedUrl.TrimEnd('/') + $"/v3-flatcontainer/{id}/{ver}/{nupkgName}";

        var response = await client.GetAsync(downloadUrl, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var fileStream = File.Create(cachePath);
        await stream.CopyToAsync(fileStream, ct).ConfigureAwait(false);

        return cachePath;
    }

    /// <summary>Extract the assembly (.dll) from a .nupkg file.</summary>
    static async Task<string?> ExtractAssemblyAsync(string nupkgPath, string cacheDir, CancellationToken ct)
    {
        var extractDir = Path.Combine(cacheDir, "baselines", "extracted", Path.GetFileNameWithoutExtension(nupkgPath));

        if (Directory.Exists(extractDir))
        {
            var cached = Directory.GetFiles(extractDir, "*.dll", SearchOption.AllDirectories).FirstOrDefault();
            if (cached != null) return cached;
        }

        Directory.CreateDirectory(extractDir);

        await Task.Run(() =>
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(nupkgPath, extractDir, overwriteFiles: true);
        }, ct).ConfigureAwait(false);

        // Find the main assembly (prefer the one in lib/{tfm}/ or the root)
        var dlls = Directory.GetFiles(extractDir, "*.dll", SearchOption.AllDirectories)
            .Where(d =>
            {
                var name = Path.GetFileNameWithoutExtension(d);
                return !name.StartsWith("System.") && !name.StartsWith("Microsoft.") && !name.StartsWith("mscorlib");
            })
            .OrderBy(d => d.Contains("/lib/") ? 0 : 1)
            .ThenBy(d => d.Length) // prefer shorter paths (closer to root)
            .ToList();

        return dlls.FirstOrDefault();
    }

    /// <summary>Compare two ParsedVersions (internal helper, avoids exposing NuGetVersionResolver.CompareVersions).</summary>
    static int CompareVersionsInternal(ParsedVersion a, ParsedVersion b)
    {
        var majorCmp = a.Major.CompareTo(b.Major);
        if (majorCmp != 0) return majorCmp;
        var minorCmp = a.Minor.CompareTo(b.Minor);
        if (minorCmp != 0) return minorCmp;
        var patchCmp = a.Patch.CompareTo(b.Patch);
        if (patchCmp != 0) return patchCmp;
        return 0;
    }
}

// ── Cascading Bump Algorithm (VN-07) ────────────────────────────

/// <summary>Read and parse changeset files from the .changesets/ directory.</summary>
public static class ChangesetReader
{
    /// <summary>Read all changeset files from the repository root's .changesets/ directory.</summary>
    public static (Changeset[] Valid, Changeset[] Invalid) ReadChangesets(string repoRoot)
    {
        var changesetsDir = Path.Combine(repoRoot, ".changesets");
        if (!Directory.Exists(changesetsDir))
            return ([], []);

        var valid = new List<Changeset>();
        var invalid = new List<Changeset>();

        foreach (var file in Directory.GetFiles(changesetsDir, "*.yaml"))
        {
            try
            {
                var content = File.ReadAllText(file);
                var parsed = ParseChangeset(content, file);
                if (parsed != null)
                    valid.Add(parsed);
                else
                    invalid.Add(new Changeset("", BumpType.Patch, $"Failed to parse: {file}", file));
            }
            catch (Exception ex)
            {
                invalid.Add(new Changeset("", BumpType.Patch, $"Error reading {file}: {ex.Message}", file));
            }
        }

        return (valid.ToArray(), invalid.ToArray());
    }

    /// <summary>Parse a single changeset YAML file content.</summary>
    internal static Changeset? ParseChangeset(string content, string filePath)
    {
        var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? package = null;
        string? bump = null;
        string? description = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("package:", StringComparison.OrdinalIgnoreCase))
                package = line["package:".Length..].Trim();
            else if (line.StartsWith("bump:", StringComparison.OrdinalIgnoreCase))
                bump = line["bump:".Length..].Trim();
            else if (line.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                description = line["description:".Length..].Trim();
        }

        if (string.IsNullOrEmpty(package) || string.IsNullOrEmpty(bump))
            return null;

        var bumpType = bump.ToLowerInvariant() switch
        {
            "patch" => BumpType.Patch,
            "minor" => BumpType.Minor,
            "major" => BumpType.Major,
            _ => (BumpType?)null
        };

        if (bumpType == null)
            return null;

        return new Changeset(package, bumpType.Value, description ?? "", filePath);
    }

    /// <summary>Aggregate changesets by package, returning the highest bump per package.</summary>
    internal static Dictionary<string, BumpType> AggregateByPackage(Changeset[] changesets)
    {
        var result = new Dictionary<string, BumpType>(StringComparer.OrdinalIgnoreCase);

        foreach (var cs in changesets)
        {
            if (result.TryGetValue(cs.Package, out var existing))
            {
                // Higher bump wins
                if ((int)cs.Bump > (int)existing)
                    result[cs.Package] = cs.Bump;
            }
            else
            {
                result[cs.Package] = cs.Bump;
            }
        }

        return result;
    }
}

/// <summary>Compute version plans using the cascading bump algorithm.</summary>
public static class CascadingBumpEngine
{
    /// <summary>
    /// Compute a version plan for the given graph and changesets.
    /// Uses ApiCompat when baseline assemblies are available; falls back to BREAKING
    /// when no baseline exists or the feed is unreachable.
    /// </summary>
    public static VersionPlan Compute(
        MonorepoGraph graph,
        Dictionary<string, BumpType> packageBumps,
        Func<string, string, BumpClassification>? apiCompatProvider = null)
    {
        var entries = new List<VersionPlanEntry>();
        var issues = new List<string>();

        // Phase 1: Identify changed packable projects
        var changedProjects = new Dictionary<string, (BumpType DesiredBump, BumpClassification Classification)>();
        foreach (var (pkgId, bump) in packageBumps)
        {
            // Find the project in the graph
            var node = graph.Nodes.Values.FirstOrDefault(n =>
                n.Project.PackageId.Equals(pkgId, StringComparison.OrdinalIgnoreCase));

            if (node == null)
            {
                issues.Add($"Changeset references unknown package '{pkgId}' — skipping");
                continue;
            }

            if (!node.Project.IsPackable)
            {
                issues.Add($"Changeset references non-packable project '{pkgId}' — skipping");
                continue;
            }

            // Determine classification via ApiCompat or fallback
            // Without ApiCompat, treat as Breaking for safety per spec
            var classification = BumpClassification.Breaking;
            if (apiCompatProvider != null)
            {
                try
                {
                    classification = apiCompatProvider(pkgId, "");
                }
                catch
                {
                    classification = BumpClassification.Breaking;
                    issues.Add($"ApiCompat failed for '{pkgId}' — treating as BREAKING");
                }
            }

            changedProjects[pkgId] = (bump, classification);
        }

        // Phase 2: Topological propagation
        var bumpedPackages = new Dictionary<string, (BumpType Bump, BumpClassification Classification, bool IsPropagated)>();

        foreach (var pkgId in graph.TopologicalOrder)
        {
            var node = graph.Nodes[pkgId];
            var pkgNodeId = node.Project.PackageId;

            if (changedProjects.TryGetValue(pkgNodeId, out var change))
            {
                // This project has a direct changeset
                var bump = change.DesiredBump;
                var classification = change.Classification;
                bumpedPackages[pkgNodeId] = (bump, classification, false);

                // Propagate to dependents
                if (classification == BumpClassification.InternalOnly)
                {
                    // No propagation for internal-only changes
                }
                else
                {
                    var propBump = classification == BumpClassification.Breaking ? BumpType.Major : BumpType.Minor;
                    PropagateBump(graph, pkgNodeId, propBump, bumpedPackages, changedProjects);
                }
            }
        }

        // Phase 3: Build version plan
        foreach (var (pkgId, info) in bumpedPackages)
        {
            var node = graph.Nodes.Values.First(n => n.Project.PackageId == pkgId);
            var currentVersion = node.Project.Version;
            var baselineVersion = $"{currentVersion.Major}.{currentVersion.Minor}.{currentVersion.Patch}";

            var newVersion = ApplyBump(baselineVersion, info.Bump);

            entries.Add(new VersionPlanEntry(
                PackageId: pkgId,
                BaselineVersion: baselineVersion,
                NewVersion: newVersion,
                AppliedBump: info.Bump,
                Classification: info.Classification,
                IsPropagated: info.IsPropagated,
                Diagnostics: null
            ));
        }

        return new VersionPlan(
            Entries: entries.ToArray(),
            Issues: issues.Count > 0 ? issues.ToArray() : null,
            HasErrors: issues.Count > 0
        );
    }

    /// <summary>Propagate a bump type to direct dependents.</summary>
    static void PropagateBump(
        MonorepoGraph graph,
        string fromPackageId,
        BumpType bump,
        Dictionary<string, (BumpType Bump, BumpClassification Classification, bool IsPropagated)> bumpedPackages,
        Dictionary<string, (BumpType DesiredBump, BumpClassification Classification)> changedProjects)
    {
        // Find all direct dependents of this package
        var fromNode = graph.Nodes.Values.FirstOrDefault(n => n.Project.PackageId == fromPackageId);
        if (fromNode == null) return;

        var fromPath = fromNode.Project.Path;

        foreach (var (path, node) in graph.Nodes)
        {
            // Check if this node depends on the fromPackage
            var hasDep = node.Project.ProjectRefs.Any(r =>
            {
                // Resolve the project ref path to a package ID
                if (graph.Nodes.TryGetValue(r.Path, out var depNode))
                    return depNode.Project.PackageId.Equals(fromPackageId, StringComparison.OrdinalIgnoreCase);
                return false;
            });

            if (!hasDep) continue;

            var pkgId = node.Project.PackageId;

            // If this node already has a direct changeset, its own bump takes precedence
            if (changedProjects.ContainsKey(pkgId))
                continue;

            // If already bumped with a higher or equal bump, skip
            if (bumpedPackages.TryGetValue(pkgId, out var existing))
            {
                if ((int)bump <= (int)existing.Bump)
                    continue;
            }

            // Apply the propagated bump
            var classification = bump == BumpType.Major
                ? BumpClassification.Breaking
                : BumpClassification.Additive;

            bumpedPackages[pkgId] = (bump, classification, true);

            // Recurse: this dependent now propagates further
            PropagateBump(graph, pkgId, bump, bumpedPackages, changedProjects);
        }
    }

    /// <summary>Apply a bump type to a version string.</summary>
    internal static string ApplyBump(string version, BumpType bump)
    {
        var parts = version.Split('.');
        if (parts.Length < 3) return version;

        if (!int.TryParse(parts[0], out var major)) return version;
        if (!int.TryParse(parts[1], out var minor)) return version;
        if (!int.TryParse(parts[2], out var patch)) return version;

        return bump switch
        {
            BumpType.Major => $"{major + 1}.0.0",
            BumpType.Minor => $"{major}.{minor + 1}.0",
            BumpType.Patch => $"{major}.{minor}.{patch + 1}",
            _ => version
        };
    }

    /// <summary>Convert a BumpType to a BumpClassification for propagation.</summary>
    internal static BumpClassification BumpToClassification(BumpType bump)
    {
        return bump switch
        {
            BumpType.Major => BumpClassification.Breaking,
            BumpType.Minor => BumpClassification.Additive,
            BumpType.Patch => BumpClassification.InternalOnly,
            _ => BumpClassification.InternalOnly
        };
    }
}

public static class CpmDetector
{
    /// <summary>Detect Central Package Management (CPM) configuration from the repo root.</summary>
    public static CpmConfig Detect(string repoRoot)
    {
        var packagesPropsPath = Path.Combine(repoRoot, "Directory.Packages.props");

        if (!File.Exists(packagesPropsPath))
            return CpmConfigDefaults.Instance;

        try
        {
            var doc = XDocument.Load(packagesPropsPath);
            var root = doc.Root;
            if (root == null)
                return CpmConfigDefaults.Instance with { Diagnostic = "Directory.Packages.props has no root element" };

            var ns = root.GetDefaultNamespace();

            // Check ManagePackageVersionsCentrally
            var centrallyManaged = root.Descendants(ns + "ManagePackageVersionsCentrally").FirstOrDefault();
            var cpmEnabled = centrallyManaged?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

            // Check CentralPackageTransitivePinningEnabled
            var transitivePinning = root.Descendants(ns + "CentralPackageTransitivePinningEnabled").FirstOrDefault();
            var pinningEnabled = transitivePinning?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

            // Check RestoreUseLegacyDependencyResolver (NuGet 6.12 CPM regression workaround)
            var legacyResolver = root.Descendants(ns + "RestoreUseLegacyDependencyResolver").FirstOrDefault();
            var legacyResolverEnabled = legacyResolver?.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false;

            // Extract package versions defined in CPM
            var packageVersions = root.Descendants(ns + "PackageVersion")
                .Select(pv => pv.Attribute("Include")?.Value)
                .Where(v => v != null)
                .Cast<string>()
                .ToArray();

            var packageVersionOverrides = root.Descendants(ns + "PackageVersionOverride")
                .Select(pv => pv.Attribute("Include")?.Value)
                .Where(v => v != null)
                .Cast<string>()
                .ToArray();

            var diagnostics = new List<string>();
            if (!cpmEnabled)
                diagnostics.Add("ManagePackageVersionsCentrally is not set to true");
            if (!pinningEnabled)
                diagnostics.Add("CentralPackageTransitivePinningEnabled is not set to true (recommended for monorepos)");
            if (pinningEnabled && !legacyResolverEnabled)
                diagnostics.Add("RestoreUseLegacyDependencyResolver is not set to true — NuGet 6.12 CPM regression workaround required when transitive pinning is enabled");

            return new CpmConfig(
                Enabled: cpmEnabled,
                TransitivePinningEnabled: pinningEnabled,
                HasPackagesProps: true,
                PackagesPropsPath: packagesPropsPath,
                PackageVersions: packageVersions,
                PackageVersionOverrides: packageVersionOverrides,
                Diagnostic: diagnostics.Count > 0 ? string.Join("; ", diagnostics) : null,
                RestoreUseLegacyDependencyResolver: legacyResolverEnabled
            );
        }
        catch (Exception ex)
        {
            return new CpmConfig(
                Enabled: false,
                TransitivePinningEnabled: false,
                HasPackagesProps: true,
                PackagesPropsPath: packagesPropsPath,
                PackageVersions: null,
                PackageVersionOverrides: null,
                Diagnostic: $"Failed to parse Directory.Packages.props: {ex.Message}",
                RestoreUseLegacyDependencyResolver: false
            );
        }
    }
}

/// <summary>Result of an AssemblyVersion check for a single project.</summary>
public record AssemblyVersionCheck(
    string PackageId,
    string ProjectPath,
    string? CurrentAssemblyVersion,
    string? ExpectedAssemblyVersion,
    bool IsCorrect
);

public static class VersionDetector
{
    /// <summary>Check AssemblyVersion pattern for all projects in the graph.</summary>
    public static AssemblyVersionCheck[] CheckAssemblyVersions(MonorepoGraph graph)
    {
        var checks = new List<AssemblyVersionCheck>();

        foreach (var (path, node) in graph.Nodes)
        {
            var proj = node.Project;
            if (!proj.IsPackable)
                continue;

            proj.Properties.TryGetValue("AssemblyVersion", out var currentAv);
            proj.Properties.TryGetValue("Version", out var version);

            // Determine expected AssemblyVersion: major.0.0.0
            string? expectedAv = null;
            if (version != null)
            {
                var parts = version.Split('.');
                if (parts.Length > 0 && int.TryParse(parts[0], out var major))
                    expectedAv = $"{major}.0.0.0";
            }

            var isCorrect = currentAv == null || (expectedAv != null && currentAv == expectedAv);

            checks.Add(new AssemblyVersionCheck(
                PackageId: proj.PackageId,
                ProjectPath: path,
                CurrentAssemblyVersion: currentAv,
                ExpectedAssemblyVersion: expectedAv,
                IsCorrect: isCorrect
            ));
        }

        return checks.ToArray();
    }

    /// <summary>Detect NBGV-managed versions for all packable projects in the graph.</summary>
    public static VersionDetectResult Detect(MonorepoGraph graph)
    {
        var projects = new List<ProjectVersionInfo>();

        foreach (var (path, node) in graph.Nodes)
        {
            var proj = node.Project;
            if (!proj.IsPackable && !proj.IsTestProject)
                continue;

            var projDir = Path.GetDirectoryName(path);
            if (projDir == null)
                continue;

            var versionJsonPath = Path.Combine(projDir, "version.json");
            string? currentVersion = null;
            string? assemblyVersion = null;
            bool isManaged = false;

            if (File.Exists(versionJsonPath))
            {
                isManaged = true;
                try
                {
                    var json = File.ReadAllText(versionJsonPath);
                    var nbgv = JsonSerializer.Deserialize<NbgvVersionFile>(json);
                    currentVersion = nbgv?.Version;
                    assemblyVersion = nbgv?.AssemblyVersion;
                }
                catch (JsonException)
                {
                    // Malformed version.json — treat as unmanaged
                    isManaged = false;
                }
            }

            projects.Add(new ProjectVersionInfo(
                PackageId: proj.PackageId,
                ProjectPath: path,
                VersionJsonPath: isManaged ? versionJsonPath : null,
                CurrentVersion: currentVersion,
                AssemblyVersion: assemblyVersion,
                IsManaged: isManaged
            ));
        }

        return new VersionDetectResult(
            Projects: projects.ToArray(),
            ManagedCount: projects.Count(p => p.IsManaged),
            UnmanagedCount: projects.Count(p => !p.IsManaged)
        );
    }
}