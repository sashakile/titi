// titi.versioning — NBGV integration, version detection, and version plan computation
// VN-01: Read version from version.json per project

namespace titi.Versioning;

using System.Text.Json;
using System.Text.Json.Serialization;
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

            return new CpmConfig(
                Enabled: cpmEnabled,
                TransitivePinningEnabled: pinningEnabled,
                HasPackagesProps: true,
                PackagesPropsPath: packagesPropsPath,
                PackageVersions: packageVersions,
                PackageVersionOverrides: packageVersionOverrides,
                Diagnostic: diagnostics.Count > 0 ? string.Join("; ", diagnostics) : null
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
                Diagnostic: $"Failed to parse Directory.Packages.props: {ex.Message}"
            );
        }
    }
}

public static class VersionDetector
{
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