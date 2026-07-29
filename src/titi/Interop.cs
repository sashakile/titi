// titi.interop — MSBuild boundary layer
// MUST be loaded first. Calls MSBuildLocator.RegisterDefaults()
// before any MSBuild type is referenced.

using Microsoft.Build.Locator;
using Microsoft.Build.Graph;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Construction;
using Microsoft.Build.Execution;

namespace titi.Interop;

public static class MsBuildSetup
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }
        _initialized = true;
    }

    public static string[] DiscoverProjects(string sourceRoot, string prefix)
    {
        return DiscoverProjects([sourceRoot], prefix);
    }

    public static string[] DiscoverProjects(string[] sourceRoots, string prefix)
    {
        Initialize();
        return sourceRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path =>
            {
                try
                {
                    var proj = ProjectRootElement.Open(path);
                    var packageId = proj.Properties
                        .FirstOrDefault(p => p.Name == "PackageId")?.Value
                        ?? Path.GetFileNameWithoutExtension(path);
                    return string.IsNullOrEmpty(prefix) || packageId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();
    }

    public static ProjectGraph BuildGraph(string[] entryPoints)
    {
        Initialize();
        return new ProjectGraph(entryPoints);
    }

    public static ProjectDescriptor ConvertNode(ProjectGraphNode node)
    {
        var proj = node.ProjectInstance;
        var properties = new Dictionary<string, string>();
        foreach (var prop in proj.Properties)
            properties[prop.Name] = prop.EvaluatedValue;

        return new ProjectDescriptor(
            Path: proj.FullPath,
            PackageId: GetProperty(proj, "PackageId", Path.GetFileNameWithoutExtension(proj.FullPath)),
            Version: ParseVersion(GetProperty(proj, "Version", "1.0.0")),
            TargetFrameworks: ParseTfms(GetProperty(proj, "TargetFrameworks", "net10.0")),
            IsPackable: bool.TryParse(GetProperty(proj, "IsPackable", "true"), out var p) && p,
            IsTestProject: bool.TryParse(GetProperty(proj, "IsTestProject", "false"), out var t) && t,
            PackageRefs: GetItems(proj, "PackageReference").Select(ItemToPackageRef).ToArray(),
            ProjectRefs: GetItems(proj, "ProjectReference").Select(ItemToProjectRef).ToArray(),
            Properties: properties
        );
    }

    static string GetProperty(ProjectInstance proj, string name, string fallback)
    {
        try { return proj.GetPropertyValue(name) ?? fallback; }
        catch { return fallback; }
    }

    static IEnumerable<ProjectItemInstance> GetItems(ProjectInstance proj, string itemType)
    {
        try { return proj.GetItems(itemType); }
        catch { return []; }
    }

    static PackageRef ItemToPackageRef(ProjectItemInstance item)
    {
        return new PackageRef(
            PackageId: item.EvaluatedInclude,
            VersionRange: item.GetMetadataValue("Version") ?? "*",
            PrivateAssets: item.GetMetadataValue("PrivateAssets") switch { "" => null, var v => v },
            ExcludeAssets: item.GetMetadataValue("ExcludeAssets") switch { "" => null, var v => v }
        );
    }

    static ProjectRef ItemToProjectRef(ProjectItemInstance item)
    {
        return new ProjectRef(
            Path: item.EvaluatedInclude,
            IsTransitive: item.GetMetadataValue("IsTransitive")?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false
        );
    }

    static SemanticVersion ParseVersion(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new SemanticVersion(1, 0, 0, null, null);

        var dash = raw.IndexOf('-');
        var plus = raw.IndexOf('+');
        var verPart = dash >= 0 ? raw[..dash] : plus >= 0 ? raw[..plus] : raw;
        var prerelease = dash >= 0 ? (plus >= 0 ? raw[(dash + 1)..plus] : raw[(dash + 1)..]) : null;
        var metadata = plus >= 0 ? raw[(plus + 1)..] : null;

        var parts = verPart.Split('.');
        return new SemanticVersion(
            Major: parts.Length > 0 && int.TryParse(parts[0], out var ma) ? ma : 1,
            Minor: parts.Length > 1 && int.TryParse(parts[1], out var mi) ? mi : 0,
            Patch: parts.Length > 2 && int.TryParse(parts[2], out var pa) ? pa : 0,
            Prerelease: prerelease,
            Metadata: metadata
        );
    }

    internal static Tfm[] ParseTfms(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return [];
        return raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tfm =>
            {
                var fw = tfm.StartsWith("netstandard") ? "netstandard"
                       : tfm.StartsWith("netcoreapp") ? "netcoreapp"
                       : tfm.StartsWith("net") ? "net" : "other";
                int start = fw == "net" ? 3 : fw.Length;
                double ver = 10.0;
                if (start < tfm.Length)
                {
                    var verSpan = tfm.AsSpan(start);
                    // Strip platform suffix (e.g. -windows, -android, -ios)
                    int dash = verSpan.IndexOf('-');
                    if (dash >= 0)
                        verSpan = verSpan[..dash];
                    int plus = verSpan.IndexOf('+');
                    if (plus >= 0)
                        verSpan = verSpan[..plus];
                    double.TryParse(verSpan,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out ver);
                }
                return new Tfm(tfm, fw, ver);
            }).ToArray();
    }
}
