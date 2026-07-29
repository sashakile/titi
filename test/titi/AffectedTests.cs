// Tests for titi.affected and git integration

namespace titi.Tests;

using titi.Core;

public class AffectedTests
{
    /// <summary>Test that BuildAffectedSet computes correct affected set from git changes.</summary>
    [Fact]
    public void BuildAffectedSet_WithChangedFile_MapsToProject()
    {
        // Arrange
        var changedFiles = new[] { "src/Orion.Core.Data/Models/Foo.cs" };
        var graph = BuildSampleGraph();

        // Act
        var affected = titi.Affected.Analyzer.BuildAffectedSet(changedFiles, graph);

        // Assert
        Assert.Contains(affected.DirectlyAffected, p => p.PackageId == "Orion.Core.Data");
        Assert.Contains(affected.ChangedFiles, f => f == "src/Orion.Core.Data/Models/Foo.cs");
    }

    /// <summary>Test that transitively affected projects are included.</summary>
    [Fact]
    public void BuildAffectedSet_WithChangedFile_IncludesTransitiveDependents()
    {
        // Arrange
        var changedFiles = new[] { "src/Orion.Core.Data/Models/Foo.cs" };
        var graph = BuildSampleGraph();

        // Act
        var affected = titi.Affected.Analyzer.BuildAffectedSet(changedFiles, graph);

        // Assert
        Assert.Contains(affected.TransitivelyAffected, p => p.PackageId == "Orion.App");
    }

    /// <summary>Test that no changes returns empty affected set.</summary>
    [Fact]
    public void BuildAffectedSet_NoChanges_ReturnsEmpty()
    {
        // Arrange
        var changedFiles = Array.Empty<string>();
        var graph = BuildSampleGraph();

        // Act
        var affected = titi.Affected.Analyzer.BuildAffectedSet(changedFiles, graph);

        // Assert
        Assert.Empty(affected.DirectlyAffected);
        Assert.Empty(affected.TransitivelyAffected);
    }

    /// <summary>Test fallback when git is unavailable — all projects affected.</summary>
    [Fact]
    public void BuildAffectedSet_GitUnavailable_AllProjectsAffected()
    {
        // Arrange
        var graph = BuildSampleGraph();

        // Act
        var affected = titi.Affected.Analyzer.BuildAffectedSet(null, graph);

        // Assert
        Assert.Equal(2, affected.DirectlyAffected.Length);
    }

    static MonorepoGraph BuildSampleGraph()
    {
        var coreDesc = new ProjectDescriptor(
            Path: "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj",
            PackageId: "Orion.Core.Data",
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: true,
            IsTestProject: false,
            PackageRefs: [],
            ProjectRefs: [],
            Properties: new() { ["SourceDir"] = "/repo/src/Orion.Core.Data" }
        );

        var appDesc = new ProjectDescriptor(
            Path: "/repo/src/Orion.App/Orion.App.csproj",
            PackageId: "Orion.App",
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: false,
            IsTestProject: false,
            PackageRefs: [],
            ProjectRefs: [new ProjectRef("/repo/src/Orion.Core.Data/Orion.Core.Data.csproj", false)],
            Properties: new() { ["SourceDir"] = "/repo/src/Orion.App" }
        );

        var coreNode = new GraphNode(
            Project: coreDesc,
            Dependencies: [],
            Dependents: [
                new GraphEdge(
                    From: "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj",
                    To: "/repo/src/Orion.App/Orion.App.csproj",
                    Mode: ReferenceMode.Binary,
                    VersionRange: null,
                    IsTransitive: false
                )
            ],
            Depth: 0
        );

        var appNode = new GraphNode(
            Project: appDesc,
            Dependencies: [
                new GraphEdge(
                    From: "/repo/src/Orion.App/Orion.App.csproj",
                    To: "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj",
                    Mode: ReferenceMode.Binary,
                    VersionRange: null,
                    IsTransitive: false
                )
            ],
            Dependents: [],
            Depth: 1
        );

        return new MonorepoGraph(
            Nodes: new()
            {
                ["/repo/src/Orion.Core.Data/Orion.Core.Data.csproj"] = coreNode,
                ["/repo/src/Orion.App/Orion.App.csproj"] = appNode,
            },
            TopologicalOrder: ["/repo/src/Orion.Core.Data/Orion.Core.Data.csproj", "/repo/src/Orion.App/Orion.App.csproj"],
            RepoRoot: "/repo",
            BuiltAt: DateTime.UtcNow,
            Fingerprints: []
        );
    }

    /// <summary>
    /// RED: GraphBuilder stores dependents with From=dependency, To=consumer
    /// (Graph.cs:52-69). AffectedAnalyzer reads dep.From as the consumer
    /// (Affected.cs:117-119), but dep.From is the dependency. This test uses
    /// GraphBuilder-style edge orientation to expose the bug.
    /// </summary>
    [Fact]
    public void BuildAffectedSet_GraphBuilderStyle_TransitiveDependentsFound()
    {
        var libDesc = new ProjectDescriptor(
            Path: "/repo/libs/Lib/Lib.csproj",
            PackageId: "Lib",
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: true, IsTestProject: false,
            PackageRefs: [], ProjectRefs: [],
            Properties: new() { ["SourceDir"] = "/repo/libs/Lib" }
        );
        var appDesc = new ProjectDescriptor(
            Path: "/repo/apps/App/App.csproj",
            PackageId: "App",
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: false, IsTestProject: false,
            PackageRefs: [],
            ProjectRefs: [new ProjectRef("/repo/libs/Lib/Lib.csproj", false)],
            Properties: new() { ["SourceDir"] = "/repo/apps/App" }
        );

        // Dependents edge: matches GraphBuilder direction (From=dependency, To=consumer)
        var libEdge = new GraphEdge(
            From: "/repo/libs/Lib/Lib.csproj",
            To: "/repo/apps/App/App.csproj",
            Mode: ReferenceMode.Binary, VersionRange: null, IsTransitive: false
        );
        var libNode = new GraphNode(
            Project: libDesc, Dependencies: [],
            Dependents: [libEdge], Depth: 0
        );
        var appNode = new GraphNode(
            Project: appDesc,
            Dependencies: [
                new GraphEdge(
                    From: "/repo/apps/App/App.csproj",
                    To: "/repo/libs/Lib/Lib.csproj",
                    Mode: ReferenceMode.Binary, VersionRange: null, IsTransitive: false
                )
            ],
            Dependents: [], Depth: 1
        );

        var graph = new MonorepoGraph(
            Nodes: new()
            {
                ["/repo/libs/Lib/Lib.csproj"] = libNode,
                ["/repo/apps/App/App.csproj"] = appNode,
            },
            TopologicalOrder: ["/repo/libs/Lib/Lib.csproj", "/repo/apps/App/App.csproj"],
            RepoRoot: "/repo",
            BuiltAt: DateTime.UtcNow,
            Fingerprints: []
        );

        var changedFiles = new[] { "libs/Lib/SomeFile.cs" };
        var affected = titi.Affected.Analyzer.BuildAffectedSet(changedFiles, graph);

        Assert.Contains(affected.DirectlyAffected, p => p.PackageId == "Lib");
        // RED: App should be transitively affected — it depends on Lib
        Assert.Contains(affected.TransitivelyAffected, p => p.PackageId == "App");
    }
}
