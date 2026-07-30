// Tests for titi.swap — swap engine, cycle detection, partial transitive

namespace titi.Tests;

using titi.Swap;

public class SwapTests
{
    // Helper: build a simple 2-node graph (core + app)
    // graphDir is the root directory for graph node paths (e.g. a temp dir)
    static (MonorepoGraph Graph, string CorePath, string AppPath) BuildTwoNodeGraph(string graphDir)
    {
        var corePath = Path.Combine(graphDir, "Orion.Core.Data", "Orion.Core.Data.csproj");
        var appPath = Path.Combine(graphDir, "Orion.App", "Orion.App.csproj");

        var coreDesc = new ProjectDescriptor(
            Path: corePath,
            PackageId: "Orion.Core.Data",
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: true,
            IsTestProject: false,
            PackageRefs: [],
            ProjectRefs: [],
            Properties: new()
        );

        var appDesc = new ProjectDescriptor(
            Path: appPath,
            PackageId: "Orion.App",
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
            IsPackable: false,
            IsTestProject: false,
            PackageRefs: [
                new PackageRef("Orion.Core.Data", "1.0.0", null, null)
            ],
            ProjectRefs: [],
            Properties: new()
        );

        var coreNode = new GraphNode(coreDesc, [], [], 0);
        var appNode = new GraphNode(
            appDesc,
            Dependencies: [
                new GraphEdge(appPath, corePath, ReferenceMode.Binary, "1.0.0", false)
            ],
            Dependents: [],
            Depth: 1
        );

        var graph = new MonorepoGraph(
            Nodes: new() { [corePath] = coreNode, [appPath] = appNode },
            TopologicalOrder: [corePath, appPath],
            RepoRoot: graphDir,
            BuiltAt: DateTime.UtcNow,
            Fingerprints: []
        );
        return (graph, corePath, appPath);
    }

    [Fact]
    public void Compute_NoLocalSource_ReturnsRetained()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            var (graph, _, _) = BuildTwoNodeGraph(tmpDir);
            var target = "Orion.External.Foo"; // not in graph

            var result = SwapEngine.Compute(
                graph, [target], VersionPolicy.SemverCompatible, true, "Orion.", tmpDir
            );

            Assert.Single(result.Retained);
            Assert.Equal(RetainedReason.NoLocalSource, result.Retained[0].Reason);
            Assert.Empty(result.Swapped);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Compute_WithLocalSource_ReturnsSwapped()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            var (graph, corePath, _) = BuildTwoNodeGraph(tmpDir);
            var coreDir = Path.GetDirectoryName(corePath)!;
            Directory.CreateDirectory(coreDir);
            File.WriteAllText(corePath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>Orion.Core.Data</PackageId></PropertyGroup></Project>");

            var result = SwapEngine.Compute(
                graph, ["Orion.Core.Data"], VersionPolicy.SemverCompatible, true, "Orion.", tmpDir
            );

            Assert.Single(result.Swapped);
            Assert.Equal("Orion.Core.Data", result.Swapped[0].PackageId);
            Assert.Equal(corePath, result.Swapped[0].LocalSourcePath);
            Assert.Empty(result.Retained);
            Assert.Equal("true", result.MsbuildContext.InTitiContext);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Compute_CycleDetected_ReturnsRetainedWithCyclePrevention()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);

            // A→B (binary), B→C (binary), C→A (binary)
            // Swapping B: consumers are A and C. C→B (ProjectRef) while B→C exists = cycle C→B→C
            var pathA = Path.Combine(tmpDir, "ProjA", "A.csproj");
            var pathB = Path.Combine(tmpDir, "ProjB", "B.csproj");
            var pathC = Path.Combine(tmpDir, "ProjC", "C.csproj");

            var descA = new ProjectDescriptor(
                pathA, "ProjA", new SemanticVersion(1, 0, 0, null, null),
                [new Tfm("net10.0", "net", 10.0)], true, false,
                [new PackageRef("ProjB", "1.0.0", null, null)], [], new()
            );
            var descB = new ProjectDescriptor(
                pathB, "ProjB", new SemanticVersion(1, 0, 0, null, null),
                [new Tfm("net10.0", "net", 10.0)], true, false,
                [new PackageRef("ProjC", "1.0.0", null, null)], [], new()
            );
            var descC = new ProjectDescriptor(
                pathC, "ProjC", new SemanticVersion(1, 0, 0, null, null),
                [new Tfm("net10.0", "net", 10.0)], true, false,
                [new PackageRef("ProjA", "1.0.0", null, null)], [], new()
            );

            var nodeA = new GraphNode(descA,
                [new GraphEdge(pathA, pathB, ReferenceMode.Binary, "1.0.0", false)],
                [new GraphEdge(pathC, pathA, ReferenceMode.Binary, "1.0.0", false)], 0);
            var nodeB = new GraphNode(descB,
                [new GraphEdge(pathB, pathC, ReferenceMode.Binary, "1.0.0", false)],
                [new GraphEdge(pathA, pathB, ReferenceMode.Binary, "1.0.0", false)], 1);
            var nodeC = new GraphNode(descC,
                [new GraphEdge(pathC, pathA, ReferenceMode.Binary, "1.0.0", false)],
                [new GraphEdge(pathB, pathC, ReferenceMode.Binary, "1.0.0", false)], 2);

            var graph = new MonorepoGraph(
                Nodes: new() { [pathA] = nodeA, [pathB] = nodeB, [pathC] = nodeC },
                TopologicalOrder: [pathA, pathB, pathC],
                RepoRoot: tmpDir,
                BuiltAt: DateTime.UtcNow,
                Fingerprints: []
            );

            var projBDir = Path.GetDirectoryName(pathB)!;
            Directory.CreateDirectory(projBDir);
            File.WriteAllText(pathB,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>ProjB</PackageId></PropertyGroup></Project>");

            var result = SwapEngine.Compute(
                graph, ["ProjB"], VersionPolicy.SemverCompatible, true, "", tmpDir
            );

            Assert.NotEmpty(result.Cycles);
            Assert.Contains(result.Retained, r => r.PackageId == "ProjB" && r.Reason == RetainedReason.CyclePrevention);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Compute_PartialTransitive_SomeSwappedSomeRetained()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);
            var (graph, corePath, _) = BuildTwoNodeGraph(tmpDir);
            var coreDir = Path.GetDirectoryName(corePath)!;
            Directory.CreateDirectory(coreDir);
            File.WriteAllText(corePath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>Orion.Core.Data</PackageId></PropertyGroup></Project>");

            var result = SwapEngine.Compute(
                graph,
                ["Orion.Core.Data", "Orion.External.Foo"],
                VersionPolicy.SemverCompatible,
                true,
                "Orion.",
                tmpDir
            );

            Assert.Contains(result.Swapped, s => s.PackageId == "Orion.Core.Data");
            Assert.Contains(result.Retained, r => r.PackageId == "Orion.External.Foo");
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Compute_NonStandardDirectoryLayout_UsesGraphPath()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);

            // Graph node path is in a non-standard layout: libs/Orion.Core.Data/
            var corePath = Path.Combine(tmpDir, "libs", "Orion.Core.Data", "Orion.Core.Data.csproj");
            var appPath = Path.Combine(tmpDir, "src", "Orion.App", "Orion.App.csproj");

            var coreDesc = new ProjectDescriptor(
                Path: corePath,
                PackageId: "Orion.Core.Data",
                Version: new SemanticVersion(1, 0, 0, null, null),
                TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
                IsPackable: true, IsTestProject: false, PackageRefs: [], ProjectRefs: [], Properties: new()
            );

            var appDesc = new ProjectDescriptor(
                Path: appPath,
                PackageId: "Orion.App",
                Version: new SemanticVersion(1, 0, 0, null, null),
                TargetFrameworks: [new Tfm("net10.0", "net", 10.0)],
                IsPackable: false, IsTestProject: false,
                PackageRefs: [new PackageRef("Orion.Core.Data", "1.0.0", null, null)],
                ProjectRefs: [], Properties: new()
            );

            var coreNode = new GraphNode(coreDesc, [], [], 0);
            var appNode = new GraphNode(appDesc,
                Dependencies: [new GraphEdge(appPath, corePath, ReferenceMode.Binary, "1.0.0", false)],
                Dependents: [], Depth: 1
            );

            var graph = new MonorepoGraph(
                Nodes: new() { [corePath] = coreNode, [appPath] = appNode },
                TopologicalOrder: [corePath, appPath],
                RepoRoot: tmpDir,
                BuiltAt: DateTime.UtcNow,
                Fingerprints: []
            );

            // Create .csproj at the graph's path (libs/Orion.Core.Data/)
            var coreDir = Path.GetDirectoryName(corePath)!;
            Directory.CreateDirectory(coreDir);
            File.WriteAllText(corePath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>Orion.Core.Data</PackageId></PropertyGroup></Project>");

            var result = SwapEngine.Compute(
                graph, ["Orion.Core.Data"], VersionPolicy.SemverCompatible, true, "Orion.", tmpDir
            );

            // Should find the project at libs/Orion.Core.Data/ via graph lookup
            Assert.Single(result.Swapped);
            Assert.Equal(corePath, result.Swapped[0].LocalSourcePath);
            Assert.Empty(result.Retained);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, recursive: true);
        }
    }
}
