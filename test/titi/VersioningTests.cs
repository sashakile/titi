// Tests for titi.versioning — NBGV integration, version detection

namespace titi.Tests;

using titi.Versioning;

public class VersioningTests
{
    static GraphNode MakeNode(string path, string packageId, bool isPackable, bool isTestProject)
    {
        var desc = new ProjectDescriptor(
            Path: path,
            PackageId: packageId,
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: isPackable,
            IsTestProject: isTestProject,
            PackageRefs: [], ProjectRefs: [], Properties: new()
        );
        return new GraphNode(desc, [], [], 0);
    }

    static MonorepoGraph MakeGraph(params (string Path, GraphNode Node)[] nodes)
    {
        var dict = new Dictionary<string, GraphNode>();
        var order = new List<string>();
        foreach (var (path, node) in nodes)
        {
            dict[path] = node;
            order.Add(path);
        }
        return new MonorepoGraph(
            Nodes: dict,
            TopologicalOrder: order.ToArray(),
            RepoRoot: "/repo",
            BuiltAt: DateTime.UtcNow,
            Fingerprints: []
        );
    }

    [Fact]
    public void Detect_EmptyGraph_ReturnsEmpty()
    {
        var graph = MakeGraph();
        var result = VersionDetector.Detect(graph);
        Assert.Empty(result.Projects);
        Assert.Equal(0, result.ManagedCount);
        Assert.Equal(0, result.UnmanagedCount);
    }

    [Fact]
    public void Detect_ProjectWithoutVersionJson_ReturnsUnmanaged()
    {
        var path = "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj";
        var node = MakeNode(path, "Orion.Core.Data", true, false);
        var graph = MakeGraph((path, node));

        var result = VersionDetector.Detect(graph);

        var p = Assert.Single(result.Projects);
        Assert.False(p.IsManaged);
        Assert.Null(p.CurrentVersion);
        Assert.Null(p.VersionJsonPath);
        Assert.Equal(0, result.ManagedCount);
        Assert.Equal(1, result.UnmanagedCount);
    }

    [Fact]
    public void Detect_NonPackableNonTestProject_Skipped()
    {
        var path = "/repo/src/Orion.App/Orion.App.csproj";
        var node = MakeNode(path, "Orion.App", false, false);
        var graph = MakeGraph((path, node));

        var result = VersionDetector.Detect(graph);
        Assert.Empty(result.Projects);
    }

    [Fact]
    public void Detect_TestProject_IsDetected()
    {
        var path = "/repo/tests/Orion.UnitTests/Orion.UnitTests.csproj";
        var node = MakeNode(path, "Orion.UnitTests", false, true);
        var graph = MakeGraph((path, node));

        var result = VersionDetector.Detect(graph);
        Assert.Single(result.Projects);
    }

    [Fact]
    public void Detect_MixedProjects_ReturnsCorrectCounts()
    {
        var path1 = "/repo/src/Managed.Lib/Managed.Lib.csproj";
        var path2 = "/repo/src/Unmanaged.Lib/Unmanaged.Lib.csproj";
        var path3 = "/repo/app/App.csproj";
        var node1 = MakeNode(path1, "Managed.Lib", true, false);
        var node2 = MakeNode(path2, "Unmanaged.Lib", true, false);
        var node3 = MakeNode(path3, "App", false, false);
        var graph = MakeGraph((path1, node1), (path2, node2), (path3, node3));

        var result = VersionDetector.Detect(graph);

        Assert.Equal(2, result.Projects.Length);
        Assert.Equal(0, result.ManagedCount);
        Assert.Equal(2, result.UnmanagedCount);
    }

    [Fact]
    public void Check_CorrectAssemblyVersion_Passes()
    {
        var path = "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj";
        var node = MakeAvNode(path, "Orion.Core.Data", true, "3.7.2", "3.0.0.0");
        var graph = MakeGraph((path, node));

        var result = VersionDetector.CheckAssemblyVersions(graph);

        var check = Assert.Single(result);
        Assert.True(check.IsCorrect);
        Assert.Equal("3.0.0.0", check.CurrentAssemblyVersion);
        Assert.Equal("3.0.0.0", check.ExpectedAssemblyVersion);
    }

    [Fact]
    public void Check_IncorrectAssemblyVersion_Fails()
    {
        var path = "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj";
        var node = MakeAvNode(path, "Orion.Core.Data", true, "3.7.2", "3.7.2.0");
        var graph = MakeGraph((path, node));

        var result = VersionDetector.CheckAssemblyVersions(graph);

        var check = Assert.Single(result);
        Assert.False(check.IsCorrect);
        Assert.Equal("3.7.2.0", check.CurrentAssemblyVersion);
        Assert.Equal("3.0.0.0", check.ExpectedAssemblyVersion);
    }

    [Fact]
    public void Check_NoAssemblyVersionSet_Passes()
    {
        var path = "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj";
        var node = MakeAvNode(path, "Orion.Core.Data", true, "3.7.2", null);
        var graph = MakeGraph((path, node));

        var result = VersionDetector.CheckAssemblyVersions(graph);

        var check = Assert.Single(result);
        Assert.True(check.IsCorrect);
        Assert.Null(check.CurrentAssemblyVersion);
        Assert.Equal("3.0.0.0", check.ExpectedAssemblyVersion);
    }

    [Fact]
    public void Check_NonPackableProject_Skipped()
    {
        var path = "/repo/src/Orion.App/Orion.App.csproj";
        var node = MakeAvNode(path, "Orion.App", false, "1.0.0", "1.0.0.0");
        var graph = MakeGraph((path, node));

        var result = VersionDetector.CheckAssemblyVersions(graph);
        Assert.Empty(result);
    }

    [Fact]
    public void Check_MultipleProjects_AllChecked()
    {
        var path1 = "/repo/src/Good/Good.csproj";
        var path2 = "/repo/src/Bad/Bad.csproj";
        var node1 = MakeAvNode(path1, "Good.Lib", true, "1.2.3", "1.0.0.0");
        var node2 = MakeAvNode(path2, "Bad.Lib", true, "2.0.1", "2.0.1.0");
        var graph = MakeGraph((path1, node1), (path2, node2));

        var result = VersionDetector.CheckAssemblyVersions(graph);

        Assert.Equal(2, result.Length);
        Assert.True(result[0].IsCorrect);
        Assert.False(result[1].IsCorrect);
    }

    static GraphNode MakeAvNode(string path, string packageId, bool isPackable, string? version, string? assemblyVersion)
    {
        var props = new Dictionary<string, string>();
        if (version != null) props["Version"] = version;
        if (assemblyVersion != null) props["AssemblyVersion"] = assemblyVersion;

        var desc = new ProjectDescriptor(
            Path: path,
            PackageId: packageId,
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: isPackable,
            IsTestProject: false,
            PackageRefs: [], ProjectRefs: [], Properties: props
        );
        return new GraphNode(desc, [], [], 0);
    }
}

public class NuGetVersionResolverTests
{
    [Fact]
    public void Resolve_ExactVersion_ReturnsSame()
    {
        var result = NuGetVersionResolver.Resolve("1.0.0", ["1.0.0", "1.0.1", "1.1.0"]);
        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public void Resolve_FloatingMajorMinorPatch_ReturnsLowestMatch()
    {
        var result = NuGetVersionResolver.Resolve("1.0.*", ["1.0.0", "1.0.1", "1.0.10", "1.1.0"]);
        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public void Resolve_FloatingMajorMinor_ReturnsLowestMatch()
    {
        var result = NuGetVersionResolver.Resolve("1.*", ["1.0.0", "1.1.0", "1.10.0", "2.0.0"]);
        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public void Resolve_WildcardOnly_ReturnsLowestVersion()
    {
        var result = NuGetVersionResolver.Resolve("*", ["1.0.0", "2.0.0", "0.5.0"]);
        Assert.Equal("0.5.0", result);
    }

    [Fact]
    public void Resolve_NoMatch_ReturnsNull()
    {
        var result = NuGetVersionResolver.Resolve("2.*", ["1.0.0", "1.1.0"]);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_EmptyFeed_ReturnsNull()
    {
        var result = NuGetVersionResolver.Resolve("1.0.*", []);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_SortsVersionsNumerically()
    {
        var result = NuGetVersionResolver.Resolve("1.0.*", ["1.0.10", "1.0.2", "1.0.1"]);
        Assert.Equal("1.0.1", result);
    }

    [Fact]
    public void Resolve_PrereleaseVersions_CanMatch()
    {
        var result = NuGetVersionResolver.Resolve("1.0.0-beta.*", ["1.0.0-beta.1", "1.0.0-beta.2", "1.0.0"]);
        Assert.Equal("1.0.0-beta.1", result);
    }

    [Fact]
    public void Resolve_FloatingPrereleaseWithStar_ReturnsLowestMatch()
    {
        var result = NuGetVersionResolver.Resolve("1.0.0-*", ["1.0.0-alpha.1", "1.0.0-beta.1", "1.0.0"]);
        Assert.Equal("1.0.0-alpha.1", result);
    }

    [Fact]
    public void Resolve_ExactPrerelease_ReturnsOriginalSpec()
    {
        var result = NuGetVersionResolver.Resolve("1.0.0-beta.1", ["1.0.0-beta.1"]);
        Assert.Equal("1.0.0-beta.1", result);
    }

    [Fact]
    public void IsFloating_Star_ReturnsTrue()
    {
        Assert.True(NuGetVersionResolver.IsFloating("*"));
        Assert.True(NuGetVersionResolver.IsFloating("1.*"));
        Assert.True(NuGetVersionResolver.IsFloating("1.0.*"));
        Assert.True(NuGetVersionResolver.IsFloating("1.0.0-beta.*"));
        Assert.True(NuGetVersionResolver.IsFloating("1.0.0-*"));
    }

    [Fact]
    public void IsFloating_ExactVersion_ReturnsFalse()
    {
        Assert.False(NuGetVersionResolver.IsFloating("1.0.0"));
        Assert.False(NuGetVersionResolver.IsFloating("1.0.0-beta.1"));
        Assert.False(NuGetVersionResolver.IsFloating(""));
    }

    [Fact]
    public void Resolve_WithVersionRangeExact_ReturnsExactMatch()
    {
        var result = NuGetVersionResolver.Resolve("[1.0.0]", ["1.0.0", "1.0.1"]);
        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public void Resolve_WithVersionRange_ReturnsLowestInRange()
    {
        var result = NuGetVersionResolver.Resolve("[1.0.0, 2.0.0)", ["0.9.0", "1.0.0", "1.5.0", "2.0.0"]);
        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public void Resolve_WithOpenRange_ReturnsLowestMatch()
    {
        var result = NuGetVersionResolver.Resolve("(1.0.0, )", ["1.0.0", "1.5.0", "2.0.0"]);
        Assert.Equal("1.5.0", result);
    }

    [Fact]
    public void Resolve_WithRangeNoMatch_ReturnsNull()
    {
        var result = NuGetVersionResolver.Resolve("(2.0.0, )", ["1.0.0", "1.5.0"]);
        Assert.Null(result);
    }

    [Fact]
    public void Resolve_CaretVersion_ReturnsLowestMatching()
    {
        var result = NuGetVersionResolver.Resolve("^1.0.0", ["1.0.0", "1.5.0", "2.0.0"]);
        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public void Resolve_TildeVersion_ReturnsLowestMatching()
    {
        var result = NuGetVersionResolver.Resolve("~1.0.0", ["1.0.0", "1.0.5", "1.1.0"]);
        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public void Resolve_NonNuGetOrgFeed_ReturnsLowestMatch()
    {
        // Feed URL is just metadata for the resolver; version list drives resolution
        var result = NuGetVersionResolver.Resolve("1.0.*", ["1.0.0", "1.0.3", "1.0.7"]);
        Assert.Equal("1.0.0", result);
    }

    [Fact]
    public void Resolve_MixedPrereleaseAndRelease_LowestWins()
    {
        // Semver: prerelease sorts before release, so the lowest of 1.0.* is the prerelease
        var result = NuGetVersionResolver.Resolve("1.0.*", ["1.0.0-alpha.1", "1.0.0"]);
        Assert.Equal("1.0.0-alpha.1", result);
    }

    [Fact]
    public void Resolve_ThreePartPatch_LowestNumeric()
    {
        var result = NuGetVersionResolver.Resolve("1.0.*", ["1.0.11", "1.0.3", "1.0.20"]);
        Assert.Equal("1.0.3", result);
    }

    [Fact]
    public void Resolve_InvalidVersionSpec_ReturnsNull()
    {
        var result = NuGetVersionResolver.Resolve("not-a-version", ["1.0.0"]);
        Assert.Null(result);
    }
}

public class CpmDetectionTests
{
    [Fact]
    public void Detect_NoDirectoryPackagesProps_ReturnsDefault()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            var result = titi.Versioning.CpmDetector.Detect(tmpDir);

            Assert.False(result.Enabled);
            Assert.False(result.HasPackagesProps);
            Assert.Null(result.PackagesPropsPath);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains("No Directory.Packages.props", result.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Detect_WithCpmEnabled_DiscoversCorrectly()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            var packagesProps = """
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="Orion.Core.Data" Version="1.0.0" />
    <PackageVersion Include="Orion.Storage" Version="2.0.0" />
  </ItemGroup>
</Project>
""";
            File.WriteAllText(Path.Combine(tmpDir, "Directory.Packages.props"), packagesProps);

            var result = titi.Versioning.CpmDetector.Detect(tmpDir);

            Assert.True(result.Enabled);
            Assert.True(result.HasPackagesProps);
            Assert.True(result.TransitivePinningEnabled);
            Assert.NotNull(result.PackagesPropsPath);
            Assert.Contains("Orion.Core.Data", result.PackageVersions);
            Assert.Contains("Orion.Storage", result.PackageVersions);
            Assert.Equal(2, result.PackageVersions.Length);
            Assert.Null(result.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Detect_CpmWithoutTransitivePinning_ReportsDiagnostic()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            var packagesProps = """
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
""";
            File.WriteAllText(Path.Combine(tmpDir, "Directory.Packages.props"), packagesProps);

            var result = titi.Versioning.CpmDetector.Detect(tmpDir);

            Assert.True(result.Enabled);
            Assert.False(result.TransitivePinningEnabled);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains("CentralPackageTransitivePinningEnabled", result.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Detect_CpmNotEnabled_ReportsDiagnostic()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            var packagesProps = """
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
""";
            File.WriteAllText(Path.Combine(tmpDir, "Directory.Packages.props"), packagesProps);

            var result = titi.Versioning.CpmDetector.Detect(tmpDir);

            Assert.False(result.Enabled);
            Assert.True(result.HasPackagesProps);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains("ManagePackageVersionsCentrally", result.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Detect_MalformedPackagesProps_ReturnsErrorDiagnostic()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(Path.Combine(tmpDir, "Directory.Packages.props"), "<not-valid-xml>");

            var result = titi.Versioning.CpmDetector.Detect(tmpDir);

            Assert.False(result.Enabled);
            Assert.True(result.HasPackagesProps);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains("Failed to parse", result.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}