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
}