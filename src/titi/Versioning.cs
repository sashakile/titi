// titi.versioning — NBGV integration, version detection, and version plan computation
// VN-01: Read version from version.json per project

namespace titi.Versioning;

using System.Text.Json;
using System.Text.Json.Serialization;

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