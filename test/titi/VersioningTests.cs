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
    <RestoreUseLegacyDependencyResolver>true</RestoreUseLegacyDependencyResolver>
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

    [Fact]
    public void Detect_WithLegacyDependencyResolver_DetectsWorkaround()
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
    <RestoreUseLegacyDependencyResolver>true</RestoreUseLegacyDependencyResolver>
  </PropertyGroup>
</Project>
""";
            File.WriteAllText(Path.Combine(tmpDir, "Directory.Packages.props"), packagesProps);

            var result = titi.Versioning.CpmDetector.Detect(tmpDir);

            Assert.True(result.RestoreUseLegacyDependencyResolver);
            Assert.Null(result.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Detect_WithoutLegacyDependencyResolver_ReportsMissing()
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
</Project>
""";
            File.WriteAllText(Path.Combine(tmpDir, "Directory.Packages.props"), packagesProps);

            var result = titi.Versioning.CpmDetector.Detect(tmpDir);

            Assert.False(result.RestoreUseLegacyDependencyResolver);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains("RestoreUseLegacyDependencyResolver", result.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Detect_LegacyDependencyResolverFalse_ReportsMissing()
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
    <RestoreUseLegacyDependencyResolver>false</RestoreUseLegacyDependencyResolver>
  </PropertyGroup>
</Project>
""";
            File.WriteAllText(Path.Combine(tmpDir, "Directory.Packages.props"), packagesProps);

            var result = titi.Versioning.CpmDetector.Detect(tmpDir);

            Assert.False(result.RestoreUseLegacyDependencyResolver);
            Assert.NotNull(result.Diagnostic);
            Assert.Contains("RestoreUseLegacyDependencyResolver", result.Diagnostic);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}

public class BaselineAcquirerTests
{
    [Fact]
    public void DetermineBaseline_BelowCurrent_ReturnsHighestStable()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "2.0.0", ["1.0.0", "1.5.0", "1.9.0", "2.0.0", "3.0.0"]);
        Assert.Equal("1.9.0", result);
    }

    [Fact]
    public void DetermineBaseline_NoVersionsBelow_ReturnsNull()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "1.0.0", ["1.0.0", "1.5.0"]);
        Assert.Null(result);
    }

    [Fact]
    public void DetermineBaseline_OnlyPrereleaseVersions_ReturnsNull()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "2.0.0", ["1.0.0-alpha", "1.5.0-beta", "1.9.0-rc.1"]);
        Assert.Null(result);
    }

    [Fact]
    public void DetermineBaseline_CurrentIsPrerelease_ReturnsHighestStableBelow()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "2.0.0-alpha", ["1.0.0", "1.5.0", "1.9.0", "2.0.0-alpha"]);
        Assert.Equal("1.9.0", result);
    }

    [Fact]
    public void DetermineBaseline_MixedVersions_IgnoresPrerelease()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "3.0.0", ["1.0.0", "1.5.0-beta", "2.0.0", "2.5.0-rc.1"]);
        Assert.Equal("2.0.0", result);
    }

    [Fact]
    public void DetermineBaseline_EmptyAvailable_ReturnsNull()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "1.0.0", []);
        Assert.Null(result);
    }

    [Fact]
    public void DetermineBaseline_InvalidCurrentVersion_ReturnsNull()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "not-a-version", ["1.0.0"]);
        Assert.Null(result);
    }

    [Fact]
    public void DetermineBaseline_NumericSorting_ReturnsHighestNotLatest()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "10.0.0", ["1.0.0", "9.0.0", "10.0.0", "2.0.0"]);
        Assert.Equal("9.0.0", result);
    }

    [Fact]
    public void DetermineBaseline_ExactMatchBelowCurrent_ReturnsIt()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "2.0.0", ["1.0.0", "1.9.9", "2.0.0"]);
        Assert.Equal("1.9.9", result);
    }

    [Fact]
    public void DetermineBaseline_CurrentVersionItself_Excluded()
    {
        var result = titi.Versioning.BaselineAcquirer.DetermineBaselineVersion(
            "2.0.0", ["1.0.0", "2.0.0"]);
        Assert.Equal("1.0.0", result);
    }
}

public class ChangesetReaderTests
{
    [Fact]
    public void ParseChangeset_ValidContent_ReturnsChangeset()
    {
        var content = """
package: Orion.Core.Data
bump: minor
description: Add async overloads
""";
        var result = titi.Versioning.ChangesetReader.ParseChangeset(content, "/test.yaml");
        Assert.NotNull(result);
        Assert.Equal("Orion.Core.Data", result.Package);
        Assert.Equal(BumpType.Minor, result.Bump);
        Assert.Equal("Add async overloads", result.Description);
    }

    [Fact]
    public void ParseChangeset_MissingPackage_ReturnsNull()
    {
        var content = "bump: minor\ndescription: test";
        Assert.Null(titi.Versioning.ChangesetReader.ParseChangeset(content, "/test.yaml"));
    }

    [Fact]
    public void ParseChangeset_InvalidBump_ReturnsNull()
    {
        var content = "package: Test\nbump: critical\ndescription: test";
        Assert.Null(titi.Versioning.ChangesetReader.ParseChangeset(content, "/test.yaml"));
    }

    [Fact]
    public void ParseChangeset_AllBumpTypes_ParsedCorrectly()
    {
        var major = titi.Versioning.ChangesetReader.ParseChangeset("package: Test\nbump: major", "/m.yaml");
        var minor = titi.Versioning.ChangesetReader.ParseChangeset("package: Test\nbump: minor", "/m.yaml");
        var patch = titi.Versioning.ChangesetReader.ParseChangeset("package: Test\nbump: patch", "/p.yaml");
        Assert.Equal(BumpType.Major, major!.Bump);
        Assert.Equal(BumpType.Minor, minor!.Bump);
        Assert.Equal(BumpType.Patch, patch!.Bump);
    }

    [Fact]
    public void AggregateByPackage_SinglePackage_HighestWins()
    {
        var changesets = new[]
        {
            new Changeset("Orion.Core", BumpType.Patch, "", "/a.yaml"),
            new Changeset("Orion.Core", BumpType.Minor, "", "/b.yaml"),
            new Changeset("Orion.Core", BumpType.Major, "", "/c.yaml"),
        };
        var result = titi.Versioning.ChangesetReader.AggregateByPackage(changesets);
        Assert.Equal(BumpType.Major, result["Orion.Core"]);
    }

    [Fact]
    public void AggregateByPackage_MultiplePackages_AllPresent()
    {
        var changesets = new[]
        {
            new Changeset("A", BumpType.Patch, "", "/a.yaml"),
            new Changeset("B", BumpType.Minor, "", "/b.yaml"),
        };
        var result = titi.Versioning.ChangesetReader.AggregateByPackage(changesets);
        Assert.Equal(BumpType.Patch, result["A"]);
        Assert.Equal(BumpType.Minor, result["B"]);
    }

    [Fact]
    public void AggregateByPackage_EmptyInput_ReturnsEmpty()
    {
        var result = titi.Versioning.ChangesetReader.AggregateByPackage([]);
        Assert.Empty(result);
    }
}

public class CascadingBumpEngineTests
{
    static GraphNode MakeNode(string path, string packageId, bool isPackable, string[]? projectRefs = null)
    {
        var desc = new ProjectDescriptor(
            Path: path,
            PackageId: packageId,
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: isPackable,
            IsTestProject: false,
            PackageRefs: [],
            ProjectRefs: (projectRefs ?? []).Select(p => new ProjectRef(p, false)).ToArray(),
            Properties: []
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
    public void Compute_NoChangesets_ReturnsEmptyPlan()
    {
        var graph = MakeGraph();
        var plan = titi.Versioning.CascadingBumpEngine.Compute(graph, []);
        Assert.Empty(plan.Entries);
        Assert.False(plan.HasErrors);
    }

    [Fact]
    public void Compute_SinglePackagePatch_BumpsCorrectly()
    {
        var path = "/repo/src/A/A.csproj";
        var node = MakeNode(path, "A", true);
        var graph = MakeGraph((path, node));
        var bumps = new Dictionary<string, BumpType> { ["A"] = BumpType.Patch };

        var plan = titi.Versioning.CascadingBumpEngine.Compute(graph, bumps);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("A", entry.PackageId);
        Assert.Equal("1.0.1", entry.NewVersion);
        Assert.Equal(BumpType.Patch, entry.AppliedBump);
        Assert.False(entry.IsPropagated);
    }

    [Fact]
    public void Compute_SinglePackageMinor_BumpsCorrectly()
    {
        var path = "/repo/src/A/A.csproj";
        var node = MakeNode(path, "A", true);
        var graph = MakeGraph((path, node));
        var bumps = new Dictionary<string, BumpType> { ["A"] = BumpType.Minor };

        var plan = titi.Versioning.CascadingBumpEngine.Compute(graph, bumps);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("1.1.0", entry.NewVersion);
        Assert.Equal(BumpType.Minor, entry.AppliedBump);
    }

    [Fact]
    public void Compute_SinglePackageMajor_BumpsCorrectly()
    {
        var path = "/repo/src/A/A.csproj";
        var node = MakeNode(path, "A", true);
        var graph = MakeGraph((path, node));
        var bumps = new Dictionary<string, BumpType> { ["A"] = BumpType.Major };

        var plan = titi.Versioning.CascadingBumpEngine.Compute(graph, bumps);

        var entry = Assert.Single(plan.Entries);
        Assert.Equal("2.0.0", entry.NewVersion);
        Assert.Equal(BumpType.Major, entry.AppliedBump);
    }

    [Fact]
    public void Compute_Propagation_MajorPropagatesToDependents()
    {
        // A -> B (A depends on B)
        var pathB = "/repo/src/B/B.csproj";
        var nodeB = MakeNode(pathB, "B", true);
        var pathA = "/repo/src/A/A.csproj";
        var nodeA = MakeNode(pathA, "A", true, [pathB]);
        // Topological order: B first, then A
        var graph = MakeGraph((pathB, nodeB), (pathA, nodeA));
        var bumps = new Dictionary<string, BumpType> { ["B"] = BumpType.Major };

        var plan = titi.Versioning.CascadingBumpEngine.Compute(graph, bumps);

        Assert.Equal(2, plan.Entries.Length);
        var bEntry = plan.Entries.First(e => e.PackageId == "B");
        var aEntry = plan.Entries.First(e => e.PackageId == "A");
        Assert.Equal("2.0.0", bEntry.NewVersion);
        Assert.False(bEntry.IsPropagated);
        Assert.Equal("2.0.0", aEntry.NewVersion);
        Assert.True(aEntry.IsPropagated);
    }

    [Fact]
    public void Compute_Propagation_PatchDoesNotPropagate()
    {
        // A -> B, but B has only patch changes
        var pathB = "/repo/src/B/B.csproj";
        var nodeB = MakeNode(pathB, "B", true);
        var pathA = "/repo/src/A/A.csproj";
        var nodeA = MakeNode(pathA, "A", true, [pathB]);
        var graph = MakeGraph((pathB, nodeB), (pathA, nodeA));
        // Use ApiCompat provider that returns InternalOnly for B
        var bumps = new Dictionary<string, BumpType> { ["B"] = BumpType.Patch };

        var plan = titi.Versioning.CascadingBumpEngine.Compute(graph, bumps,
            apiCompatProvider: (pkg, _) => pkg == "B" ? BumpClassification.InternalOnly : BumpClassification.Additive);

        Assert.Single(plan.Entries); // Only B, no propagation
        Assert.Equal("B", plan.Entries[0].PackageId);
    }

    [Fact]
    public void Compute_UnknownPackage_ReportsIssue()
    {
        var graph = MakeGraph();
        var bumps = new Dictionary<string, BumpType> { ["Unknown"] = BumpType.Minor };

        var plan = titi.Versioning.CascadingBumpEngine.Compute(graph, bumps);

        Assert.Empty(plan.Entries);
        Assert.True(plan.HasErrors);
        Assert.Contains("Unknown", plan.Issues![0]);
    }

    [Fact]
    public void ApplyBump_Patch_ReturnsCorrectVersion()
    {
        Assert.Equal("1.0.1", titi.Versioning.CascadingBumpEngine.ApplyBump("1.0.0", BumpType.Patch));
        Assert.Equal("1.2.4", titi.Versioning.CascadingBumpEngine.ApplyBump("1.2.3", BumpType.Patch));
    }

    [Fact]
    public void ApplyBump_Minor_ReturnsCorrectVersion()
    {
        Assert.Equal("1.1.0", titi.Versioning.CascadingBumpEngine.ApplyBump("1.0.0", BumpType.Minor));
        Assert.Equal("2.3.0", titi.Versioning.CascadingBumpEngine.ApplyBump("2.2.0", BumpType.Minor));
    }

    [Fact]
    public void ApplyBump_Major_ReturnsCorrectVersion()
    {
        Assert.Equal("2.0.0", titi.Versioning.CascadingBumpEngine.ApplyBump("1.0.0", BumpType.Major));
        Assert.Equal("3.0.0", titi.Versioning.CascadingBumpEngine.ApplyBump("2.9.9", BumpType.Major));
    }

    [Fact]
    public void ApplyBump_InvalidVersion_ReturnsOriginal()
    {
        Assert.Equal("not-a-version", titi.Versioning.CascadingBumpEngine.ApplyBump("not-a-version", BumpType.Patch));
    }

    [Fact]
    public void BumpToClassification_Conversion_OrdersCorrectly()
    {
        Assert.Equal(BumpClassification.InternalOnly, titi.Versioning.CascadingBumpEngine.BumpToClassification(BumpType.Patch));
        Assert.Equal(BumpClassification.Additive, titi.Versioning.CascadingBumpEngine.BumpToClassification(BumpType.Minor));
        Assert.Equal(BumpClassification.Breaking, titi.Versioning.CascadingBumpEngine.BumpToClassification(BumpType.Major));
    }

    [Fact]
    public void Compute_Propagation_ChainPropagatesCorrectly()
    {
        // A -> B -> C (A depends on B, B depends on C)
        var pathC = "/repo/src/C/C.csproj";
        var nodeC = MakeNode(pathC, "C", true);
        var pathB = "/repo/src/B/B.csproj";
        var nodeB = MakeNode(pathB, "B", true, [pathC]);
        var pathA = "/repo/src/A/A.csproj";
        var nodeA = MakeNode(pathA, "A", true, [pathB]);
        var graph = MakeGraph((pathC, nodeC), (pathB, nodeB), (pathA, nodeA));
        var bumps = new Dictionary<string, BumpType> { ["C"] = BumpType.Minor };

        var plan = titi.Versioning.CascadingBumpEngine.Compute(graph, bumps,
            apiCompatProvider: (pkg, _) => BumpClassification.Additive);

        Assert.Equal(3, plan.Entries.Length);
        Assert.Equal("1.1.0", plan.Entries.First(e => e.PackageId == "C").NewVersion);
        Assert.Equal("1.1.0", plan.Entries.First(e => e.PackageId == "B").NewVersion);
        Assert.Equal("1.1.0", plan.Entries.First(e => e.PackageId == "A").NewVersion);
    }
}