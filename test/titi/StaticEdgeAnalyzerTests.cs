// Tests for TID-0ej: Pure static dependency edge analysis
// Levels 1 (project references) and 2 (using statements)

namespace titi.Tests;

public class StaticEdgeAnalyzerTests
{
    // ── Helpers ──────────────────────────────────────────────────

    /// <summary>Build a minimal MonorepoGraph from project descriptors.</summary>
    static MonorepoGraph MakeGraph((string Path, string PackageId, bool IsTestProject)[] projects,
        (string From, string To)[]? dependencies = null)
    {
        var nodes = new Dictionary<string, GraphNode>();
        var topo = new List<string>();

        foreach (var (path, pkgId, isTest) in projects)
        {
            var desc = new ProjectDescriptor(
                path, pkgId,
                new SemanticVersion(1, 0, 0, null, null),
                [], false, isTest, [], [], new()
            );
            nodes[path] = new GraphNode(
                Project: desc,
                Dependencies: [],
                Dependents: [],
                Depth: 0
            );
            topo.Add(path);
        }

        // Add dependency edges
        if (dependencies != null)
        {
            foreach (var (from, to) in dependencies)
            {
                if (nodes.TryGetValue(from, out var fromNode))
                {
                    var deps = fromNode.Dependencies.Append(
                        new GraphEdge(from, to, ReferenceMode.Binary, null, false)
                    ).ToArray();
                    nodes[from] = fromNode with { Dependencies = deps };
                }
                if (nodes.TryGetValue(to, out var toNode))
                {
                    var dependents = toNode.Dependents.Append(
                        new GraphEdge(to, from, ReferenceMode.Binary, null, false)
                    ).ToArray();
                    nodes[to] = toNode with { Dependents = dependents };
                }
            }
        }

        return new MonorepoGraph(
            Nodes: nodes,
            TopologicalOrder: topo.ToArray(),
            RepoRoot: "/repo",
            BuiltAt: DateTime.UtcNow,
            Fingerprints: new()
        );
    }

    /// <summary>Create test items for a project.</summary>
    static TestItem[] MakeTestItems(string pkgId, string[] methodNames)
    {
        return methodNames.Select((m, i) => new TestItem(
            TestId: $"{pkgId}::{pkgId}.Tests.{m}",
            AssemblyPath: $"/asm/{pkgId}.dll",
            ClassName: $"{pkgId}.Tests",
            MethodName: m,
            Framework: TestFramework.Xunit,
            Tier: TestTier.Unit,
            SourceFile: $"{pkgId}/Tests/{m}.cs",
            LastOutcome: TestOutcome.None,
            MeanDurationMs: 0,
            Tags: []
        )).ToArray();
    }

    /// <summary>Create a test fixture directory for source file introspection.</summary>
    static (string FixtureDir, string TearDownToken) CreateFixtureDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"static-edge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return (dir, dir);
    }

    static void CleanupFixtureDir(string dir)
    {
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    // ── Level 1: Project references ──────────────────────────────

    [Fact]
    public void Level1_NoTestProjects_ReturnsEmpty()
    {
        var graph = MakeGraph([
            ("/repo/src/Lib/Lib.csproj", "Lib", false),
        ]);

        var edges = StaticEdgeAnalyzer.AnalyzeProjectReferences(graph, null);

        Assert.Empty(edges);
    }

    [Fact]
    public void Level1_TestProjectWithNoDeps_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var testDir = Path.Combine(fixtureDir, "tests", "MyTest");
            Directory.CreateDirectory(testDir);
            var testProjPath = Path.Combine(testDir, "MyTest.csproj");

            var graph = MakeGraph([
                (testProjPath, "MyTest", true),
            ]);

            var edges = StaticEdgeAnalyzer.AnalyzeProjectReferences(graph, null);

            Assert.Empty(edges);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level1_TestProjectWithSourceDeps_WithoutTestItems_UsesPkgPrefix()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "Lib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Foo.cs"), "namespace Lib; public class Foo { }");
            var srcProjPath = Path.Combine(srcDir, "Lib.csproj");

            var testDir = Path.Combine(fixtureDir, "tests", "LibTest");
            Directory.CreateDirectory(testDir);
            var testProjPath = Path.Combine(testDir, "LibTest.csproj");

            var graph = MakeGraph([
                (srcProjPath, "Lib", false),
                (testProjPath, "LibTest", true),
            ], dependencies: [(testProjPath, srcProjPath)]);

            var edges = StaticEdgeAnalyzer.AnalyzeProjectReferences(graph, null);

            // Should produce edges with $pkg: prefix
            Assert.NotEmpty(edges);
            Assert.All(edges, e => Assert.StartsWith("$pkg:", e.From));
            Assert.All(edges, e => Assert.Equal(EdgeOrigin.Static, e.Origin));
            Assert.All(edges, e => Assert.Equal(500_000, e.Weight));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level1_TestProjectWithSourceDeps_WithTestItems_EmitsMethodLevelEdges()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "Lib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Foo.cs"), "namespace Lib; public class Foo { }");
            var srcProjPath = Path.Combine(srcDir, "Lib.csproj");

            var testDir = Path.Combine(fixtureDir, "tests", "LibTest");
            Directory.CreateDirectory(testDir);
            var testProjPath = Path.Combine(testDir, "LibTest.csproj");

            var graph = MakeGraph([
                (srcProjPath, "Lib", false),
                (testProjPath, "LibTest", true),
            ], dependencies: [(testProjPath, srcProjPath)]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["LibTest"] = MakeTestItems("LibTest", ["TestParse", "TestParseEmpty"])
            };

            var edges = StaticEdgeAnalyzer.AnalyzeProjectReferences(graph, discoveredTests);

            // Should produce method-level edges
            Assert.NotEmpty(edges);
            Assert.All(edges, e => Assert.StartsWith("LibTest::", e.From));
            Assert.All(edges, e => Assert.Equal(EdgeOrigin.Static, e.Origin));
            Assert.All(edges, e => Assert.Equal(500_000, e.Weight));

            // Check specific test methods appear
            var fromIds = edges.Select(e => e.From).Distinct().ToArray();
            Assert.Contains(fromIds, id => id.Contains("TestParse"));
            Assert.Contains(fromIds, id => id.Contains("TestParseEmpty"));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level1_MultiLevelReferences_OnlyDirectDeps()
    {
        // TestProject -> LibA -> LibB
        // Edges should only connect TestProject -> LibA (not LibB)
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDirA = Path.Combine(fixtureDir, "src", "LibA");
            Directory.CreateDirectory(srcDirA);
            File.WriteAllText(Path.Combine(srcDirA, "Foo.cs"), "namespace LibA; public class Foo { }");
            var srcProjPathA = Path.Combine(srcDirA, "LibA.csproj");

            var srcDirB = Path.Combine(fixtureDir, "src", "LibB");
            Directory.CreateDirectory(srcDirB);
            File.WriteAllText(Path.Combine(srcDirB, "Bar.cs"), "namespace LibB; public class Bar { }");
            var srcProjPathB = Path.Combine(srcDirB, "LibB.csproj");

            var testDir = Path.Combine(fixtureDir, "tests", "LibTest");
            Directory.CreateDirectory(testDir);
            var testProjPath = Path.Combine(testDir, "LibTest.csproj");

            var graph = MakeGraph([
                (srcProjPathA, "LibA", false),
                (srcProjPathB, "LibB", false),
                (testProjPath, "LibTest", true),
            ], dependencies: [
                (testProjPath, srcProjPathA),
                (srcProjPathA, srcProjPathB),
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["LibTest"] = MakeTestItems("LibTest", ["Test1"])
            };

            var edges = StaticEdgeAnalyzer.AnalyzeProjectReferences(graph, discoveredTests);

            // All edges should go to LibA source files (not LibB)
            Assert.NotEmpty(edges);
            Assert.All(edges, e => Assert.Contains("LibA", e.To, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level1_SkipsDepsToOtherTestProjects()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "Lib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Foo.cs"), "namespace Lib; public class Foo { }");
            var srcProjPath = Path.Combine(srcDir, "Lib.csproj");

            var testDir = Path.Combine(fixtureDir, "tests", "LibTest");
            Directory.CreateDirectory(testDir);
            var testProjPath = Path.Combine(testDir, "LibTest.csproj");

            // OtherTest is a test project with a .cs file — should NOT get edges
            // because the analysis skips test→test dependencies.
            var otherTestDir = Path.Combine(fixtureDir, "tests", "OtherTest");
            Directory.CreateDirectory(otherTestDir);
            File.WriteAllText(Path.Combine(otherTestDir, "OtherTest.cs"), "namespace OtherTest; public class OtherTest { }");
            var otherTestProjPath = Path.Combine(otherTestDir, "OtherTest.csproj");

            var graph = MakeGraph([
                (srcProjPath, "Lib", false),
                (testProjPath, "LibTest", true),
                (otherTestProjPath, "OtherTest", true),
            ], dependencies: [
                (testProjPath, srcProjPath),
                (testProjPath, otherTestProjPath),
            ]);

            var edges = StaticEdgeAnalyzer.AnalyzeProjectReferences(graph, null);

            // Should have edges (to Lib), but NOT to OtherTest
            Assert.NotEmpty(edges);
            Assert.DoesNotContain(edges, e => e.To.Contains("OtherTest"));
            Assert.All(edges, e => Assert.Contains("Lib", e.To));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level1_EmptyGraph_ReturnsEmpty()
    {
        var graph = MakeGraph([]);
        var edges = StaticEdgeAnalyzer.AnalyzeProjectReferences(graph, null);
        Assert.Empty(edges);
    }

    // ── Level 2: Using statement analysis ────────────────────────

    [Fact]
    public void Level2_NoTestProjects_ReturnsEmpty()
    {
        var graph = MakeGraph([
            ("/repo/src/Lib/Lib.csproj", "Lib", false),
        ]);

        var edges = StaticEdgeAnalyzer.AnalyzeUsingStatements(graph, null);

        Assert.Empty(edges);
    }

    [Fact]
    public void Level2_NoSourceProjects_ReturnsEmpty()
    {
        var graph = MakeGraph([
            ("/repo/tests/MyTest/MyTest.csproj", "MyTest", true),
        ]);

        var edges = StaticEdgeAnalyzer.AnalyzeUsingStatements(graph, null);

        Assert.Empty(edges);
    }

    [Fact]
    public void Level2_UsingMatchesNamespace_ProducesEdges()
    {
        // Create a real temp directory with source files
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            // Create source project with a source file
            var srcDir = Path.Combine(fixtureDir, "src", "Orion.Core.Data");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "DataModels.cs"),
                """
                namespace Orion.Core.Data;

                public record DataModel(int Id);
                """);

            // Create test project with using directive
            var testDir = Path.Combine(fixtureDir, "tests", "Orion.UnitTests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "DataTests.cs"),
                """
                using Orion.Core.Data;
                using Xunit;

                namespace Orion.UnitTests;

                public class DataTests
                {
                    [Fact]
                    public void TestDataModel() => Assert.NotNull(new DataModel(1));
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "Orion.Core.Data.csproj");
            var testProjPath = Path.Combine(testDir, "Orion.UnitTests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "Orion.Core.Data", false),
                (testProjPath, "Orion.UnitTests", true),
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["Orion.UnitTests"] = new[]
                {
                    new TestItem(
                        "Orion.UnitTests::Orion.UnitTests.DataTests.TestDataModel",
                        "/asm/Orion.UnitTests.dll",
                        "Orion.UnitTests.DataTests",
                        "TestDataModel",
                        TestFramework.Xunit, TestTier.Unit,
                        Path.Combine(testDir, "DataTests.cs"),
                        TestOutcome.None, 0, [])
                }
            };

            var edges = StaticEdgeAnalyzer.AnalyzeUsingStatements(graph, discoveredTests);

            // Should find the using→namespace match
            Assert.NotEmpty(edges);
            Assert.All(edges, e => Assert.Equal(EdgeOrigin.Static, e.Origin));
            Assert.All(edges, e => Assert.Equal(800_000, e.Weight));

            // The 'To' should be the source file path
            Assert.Contains(edges, e =>
                e.To.Contains("DataModels.cs") ||
                e.To.Contains("Orion.Core.Data"));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level2_FiltersSystemUsings()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "MyLib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Service.cs"),
                """
                namespace MyLib;

                public class Service { }
                """);

            var testDir = Path.Combine(fixtureDir, "tests", "MyLib.Tests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "ServiceTests.cs"),
                """
                using System;
                using System.Collections.Generic;
                using Xunit;
                using MyLib;

                namespace MyLib.Tests;

                public class ServiceTests
                {
                    [Fact]
                    public void TestService() => Assert.NotNull(new Service());
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "MyLib.csproj");
            var testProjPath = Path.Combine(testDir, "MyLib.Tests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "MyLib", false),
                (testProjPath, "MyLib.Tests", true),
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["MyLib.Tests"] = new[]
                {
                    new TestItem(
                        "MyLib.Tests::MyLib.Tests.ServiceTests.TestService",
                        "/asm/MyLib.Tests.dll",
                        "MyLib.Tests.ServiceTests",
                        "TestService",
                        TestFramework.Xunit, TestTier.Unit,
                        Path.Combine(testDir, "ServiceTests.cs"),
                        TestOutcome.None, 0, [])
                }
            };

            var edges = StaticEdgeAnalyzer.AnalyzeUsingStatements(graph, discoveredTests);

            // Should match MyLib (not System, Xunit)
            Assert.NotEmpty(edges);
            Assert.All(edges, e => Assert.Contains("MyLib", e.To));
            Assert.DoesNotContain(edges, e =>
                e.To.Contains("System", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level2_NoTestItems_UsesFilePrefix()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "Orion.Core.Data");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "DataModels.cs"),
                """
                namespace Orion.Core.Data;

                public record DataModel(int Id);
                """);

            var testDir = Path.Combine(fixtureDir, "tests", "Orion.UnitTests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "DataTests.cs"),
                """
                using Orion.Core.Data;
                using Xunit;

                namespace Orion.UnitTests;

                public class DataTests
                {
                    [Fact]
                    public void TestDataModel() => Assert.NotNull(new DataModel(1));
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "Orion.Core.Data.csproj");
            var testProjPath = Path.Combine(testDir, "Orion.UnitTests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "Orion.Core.Data", false),
                (testProjPath, "Orion.UnitTests", true),
            ]);

            var edges = StaticEdgeAnalyzer.AnalyzeUsingStatements(graph, null);

            // Should use $file: prefix when no test items
            Assert.NotEmpty(edges);
            Assert.All(edges, e => Assert.StartsWith("$file:", e.From));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    // ── AnalyzeAll (merged) ──────────────────────────────────────

    [Fact]
    public void AnalyzeAll_EmptyGraph_ReturnsEmpty()
    {
        var graph = MakeGraph([]);
        var edges = StaticEdgeAnalyzer.AnalyzeAll(graph, null);
        Assert.Empty(edges);
    }

    [Fact]
    public void AnalyzeAll_NoTestProjects_ReturnsEmpty()
    {
        var graph = MakeGraph([
            ("/repo/src/Lib/Lib.csproj", "Lib", false),
        ]);

        var edges = StaticEdgeAnalyzer.AnalyzeAll(graph, null);
        Assert.Empty(edges);
    }

    [Fact]
    public void AnalyzeAll_DeduplicatesL1AndL2()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "MyLib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Service.cs"),
                """
                namespace MyLib;

                public class Service { }
                """);

            var testDir = Path.Combine(fixtureDir, "tests", "MyLib.Tests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "ServiceTests.cs"),
                """
                using MyLib;
                using Xunit;

                namespace MyLib.Tests;

                public class ServiceTests
                {
                    [Fact]
                    public void TestService() => Assert.NotNull(new Service());
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "MyLib.csproj");
            var testProjPath = Path.Combine(testDir, "MyLib.Tests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "MyLib", false),
                (testProjPath, "MyLib.Tests", true),
            ], dependencies: [
                (testProjPath, srcProjPath),  // Level 1 would catch this
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["MyLib.Tests"] = new[]
                {
                    new TestItem(
                        "MyLib.Tests::MyLib.Tests.ServiceTests.TestService",
                        "/asm/MyLib.Tests.dll",
                        "MyLib.Tests.ServiceTests",
                        "TestService",
                        TestFramework.Xunit, TestTier.Unit,
                        Path.Combine(testDir, "ServiceTests.cs"),
                        TestOutcome.None, 0, [])
                }
            };

            var edges = StaticEdgeAnalyzer.AnalyzeAll(graph, discoveredTests);

            // Both L1 and L2 would produce edges, but deduplication should
            // keep only unique (From, To) pairs. L2 (800_000 weight) should
            // win over L1 (500_000 weight) for the same pair.
            var distinctPairs = edges.Select(e => (e.From, e.To, e.Weight)).ToArray();

            // Should have at least one edge
            Assert.NotEmpty(edges);

            // Verify L3 weight (950k) is preferred — `new Service()` constructor
            // call is caught by L3's NewTypeRegex, overrides L2/L1 for same pair
            var highWeightEdges = edges.Where(e => e.Weight == 950_000).ToArray();
            Assert.NotEmpty(highWeightEdges);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    // ── Persistence ──────────────────────────────────────────────

    [Fact]
    public void PersistAndLoad_RoundTrips()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var cacheDir = Path.Combine(fixtureDir, ".titi", "test-cache");
            var edges = new TestToSourceEdge[]
            {
                new("TestProj::Test1", "/repo/src/Lib/Foo.cs", EdgeOrigin.Static, 500_000, []),
                new("TestProj::Test2", "/repo/src/Lib/Bar.cs", EdgeOrigin.Static, 800_000, []),
            };

            StaticEdgeAnalyzer.PersistStaticEdges(cacheDir, edges);

            var loaded = StaticEdgeAnalyzer.LoadPersistedStaticEdges(cacheDir);

            Assert.Equal(2, loaded.Length);
            Assert.Contains(loaded, e => e.From == "TestProj::Test1" && e.To == "/repo/src/Lib/Foo.cs");
            Assert.Contains(loaded, e => e.From == "TestProj::Test2" && e.To == "/repo/src/Lib/Bar.cs");
            Assert.All(loaded, e => Assert.Equal(EdgeOrigin.Static, e.Origin));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void PersistAndLoad_EmptyEdges_DoesNotWriteFile()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var cacheDir = Path.Combine(fixtureDir, ".titi", "test-cache");
            StaticEdgeAnalyzer.PersistStaticEdges(cacheDir, []);

            var loaded = StaticEdgeAnalyzer.LoadPersistedStaticEdges(cacheDir);
            Assert.Empty(loaded);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void LoadPersisted_MissingFile_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var cacheDir = Path.Combine(fixtureDir, ".titi", "test-cache");
            var loaded = StaticEdgeAnalyzer.LoadPersistedStaticEdges(cacheDir);
            Assert.Empty(loaded);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void LoadPersisted_CorruptFile_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var cacheDir = Path.Combine(fixtureDir, ".titi", "test-cache");
            var edgesDir = Path.Combine(cacheDir, "edges");
            Directory.CreateDirectory(edgesDir);
            File.WriteAllText(Path.Combine(edgesDir, "static-edges.json"), "not valid json");

            var loaded = StaticEdgeAnalyzer.LoadPersistedStaticEdges(cacheDir);
            Assert.Empty(loaded);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    // ── EnumerateSourceFiles ─────────────────────────────────────

    [Fact]
    public void EnumerateSourceFiles_SkipsObjAndBin()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            // Create source files
            File.WriteAllText(Path.Combine(fixtureDir, "Foo.cs"), "");
            File.WriteAllText(Path.Combine(fixtureDir, "Bar.cs"), "");

            // Create files in obj/ and bin/ (should be excluded)
            Directory.CreateDirectory(Path.Combine(fixtureDir, "obj"));
            Directory.CreateDirectory(Path.Combine(fixtureDir, "bin"));
            File.WriteAllText(Path.Combine(fixtureDir, "obj", "Generated.cs"), "");
            File.WriteAllText(Path.Combine(fixtureDir, "bin", "Temp.cs"), "");

            var files = StaticEdgeAnalyzer.EnumerateSourceFiles(fixtureDir);

            Assert.Contains(files, f => f.EndsWith("Foo.cs"));
            Assert.Contains(files, f => f.EndsWith("Bar.cs"));
            Assert.DoesNotContain(files, f => f.Contains("obj"));
            Assert.DoesNotContain(files, f => f.Contains("bin"));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void EnumerateSourceFiles_NonExistentDir_ReturnsEmpty()
    {
        var files = StaticEdgeAnalyzer.EnumerateSourceFiles("/nonexistent/path");
        Assert.Empty(files);
    }

    // ── ParseUsingDirectives ─────────────────────────────────────

    [Fact]
    public void ParseUsingDirectives_StandardUsings()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var filePath = Path.Combine(fixtureDir, "test.cs");
            File.WriteAllText(filePath,
                """
                using System;
                using System.Collections.Generic;
                using Xunit;
                using MyProject.Data;
                using MyProject.Services;

                namespace MyProject.Tests;

                public class Test
                {
                    [Fact]
                    public void Test1() { }
                }
                """);

            var usings = StaticEdgeAnalyzer.ParseUsingDirectives(filePath);

            // System.* and Xunit are filtered out
            Assert.DoesNotContain(usings, u => u.StartsWith("System"));
            Assert.DoesNotContain(usings, u => u == "Xunit");

            // MyProject.* should remain
            Assert.Contains("MyProject.Data", usings);
            Assert.Contains("MyProject.Services", usings);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void ParseUsingDirectives_SkipsUsingAlias()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var filePath = Path.Combine(fixtureDir, "test.cs");
            File.WriteAllText(filePath,
                """
                using Projection = System.Linq.Expressions;
                using MyProject.Data;

                namespace MyProject.Tests;

                public class Test
                {
                    [Fact]
                    public void Test1() { }
                }
                """);

            var usings = StaticEdgeAnalyzer.ParseUsingDirectives(filePath);

            // Alias directive should be skipped
            Assert.DoesNotContain(usings, u => u.Contains("="));
            Assert.DoesNotContain(usings, u => u == "Projection");

            // Normal using should still be present
            Assert.Contains("MyProject.Data", usings);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void ParseUsingDirectives_SkipsSingleSegmentUsingStatic()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var filePath = Path.Combine(fixtureDir, "test.cs");
            File.WriteAllText(filePath,
                """
                using static System.Math;
                using MyProject.Data;

                namespace MyProject.Tests;

                public class Test
                {
                    [Fact]
                    public void Test1() { }
                }
                """);

            var usings = StaticEdgeAnalyzer.ParseUsingDirectives(filePath);

            // System.Math is filtered out (System prefix), but the extraction
            // should produce "System" (not "System.Math") for the using static case
            Assert.DoesNotContain(usings, u => u == "System.Math");
            Assert.DoesNotContain(usings, u => u == "System"); // filtered by prefix
            Assert.Contains("MyProject.Data", usings);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void ParseUsingDirectives_SkipsSingleSegmentUsingStaticType()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var filePath = Path.Combine(fixtureDir, "test.cs");
            File.WriteAllText(filePath,
                """
                using static Foo;
                using MyProject.Data;

                namespace MyProject.Tests;

                public class Test
                {
                    [Fact]
                    public void Test1() { }
                }
                """);

            var usings = StaticEdgeAnalyzer.ParseUsingDirectives(filePath);

            // Single-segment `using static Foo` should be skipped
            Assert.DoesNotContain(usings, u => u == "Foo");
            Assert.Contains("MyProject.Data", usings);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void ParseUsingDirectives_NoUsings_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var filePath = Path.Combine(fixtureDir, "empty.cs");
            File.WriteAllText(filePath,
                """
                namespace NoUsing;

                public class Foo { }
                """);

            var usings = StaticEdgeAnalyzer.ParseUsingDirectives(filePath);
            Assert.Empty(usings);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void ParseUsingDirectives_UnreadableFile_ReturnsEmpty()
    {
        var usings = StaticEdgeAnalyzer.ParseUsingDirectives("/nonexistent/file.cs");
        Assert.Empty(usings);
    }

    // ── FindTestFileForItem ──────────────────────────────────────

    [Fact]
    public void FindTestFileForItem_BySourceField()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var testFile = Path.Combine(fixtureDir, "MyTest.cs");
            File.WriteAllText(testFile,
                """
                using Xunit;
                namespace Tests;
                public class MyTest
                {
                    [Fact]
                    public void Test1() { }
                }
                """);

            var item = new TestItem(
                "TestProj::Tests.MyTest.Test1",
                "/asm/TestProj.dll",
                "Tests.MyTest",
                "Test1",
                TestFramework.Xunit, TestTier.Unit,
                testFile,  // source file set
                TestOutcome.None, 0, []);

            var result = StaticEdgeAnalyzer.FindTestFileForItem(
                item, [testFile], fixtureDir);

            Assert.Equal(testFile, result);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void FindTestFileForItem_ByClassName()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var testFile = Path.Combine(fixtureDir, "MyTest.cs");
            File.WriteAllText(testFile,
                """
                using Xunit;
                namespace Tests;
                public class MyTest
                {
                    [Fact]
                    public void Test1() { }
                }
                """);

            var item = new TestItem(
                "TestProj::Tests.MyTest.Test1",
                "/asm/TestProj.dll",
                "Tests.MyTest",
                "Test1",
                TestFramework.Xunit, TestTier.Unit,
                null,  // no source file
                TestOutcome.None, 0, []);

            var result = StaticEdgeAnalyzer.FindTestFileForItem(
                item, [testFile], fixtureDir);

            Assert.Equal(testFile, result);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void FindTestFileForItem_NoMatch_ReturnsNull()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var testFile = Path.Combine(fixtureDir, "MyTest.cs");
            File.WriteAllText(testFile,
                """
                using Xunit;
                namespace Tests;
                public class MyTest
                {
                    [Fact]
                    public void Test1() { }
                }
                """);

            // ClassName doesn't match any file's class name
            var item = new TestItem(
                "TestProj::Tests.UnknownClass.Test1",
                "/asm/TestProj.dll",
                "Tests.UnknownClass",
                "Test1",
                TestFramework.Xunit, TestTier.Unit,
                null,  // no source file
                TestOutcome.None, 0, []);

            var result = StaticEdgeAnalyzer.FindTestFileForItem(
                item, [testFile], fixtureDir);

            Assert.Null(result);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    // ── BuildNamespaceMap ────────────────────────────────────────

    [Fact]
    public void BuildNamespaceMap_ExtractsNamespaces()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "MyLib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Service.cs"),
                """
                namespace MyLib;

                public class Service { }
                """);
            File.WriteAllText(Path.Combine(srcDir, "Models.cs"),
                """
                namespace MyLib.Models;

                public record Foo(int Id);
                """);

            var srcProjPath = Path.Combine(srcDir, "MyLib.csproj");
            var graph = MakeGraph([
                (srcProjPath, "MyLib", false),
            ]);

            var nsMap = StaticEdgeAnalyzer.BuildNamespaceMap(graph);

            Assert.True(nsMap.ContainsKey("MyLib"));
            Assert.True(nsMap.ContainsKey("MyLib.Models"));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void BuildNamespaceMap_NoSourceFiles_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "EmptyLib");
            Directory.CreateDirectory(srcDir);

            var srcProjPath = Path.Combine(srcDir, "EmptyLib.csproj");
            var graph = MakeGraph([
                (srcProjPath, "EmptyLib", false),
            ]);

            var nsMap = StaticEdgeAnalyzer.BuildNamespaceMap(graph);
            Assert.Empty(nsMap);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    // ── Level 3: Method call graph ───────────────────────────────

    [Fact]
    public void Level3_NoTestProjects_ReturnsEmpty()
    {
        var graph = MakeGraph([
            ("/repo/src/Lib/Lib.csproj", "Lib", false),
        ]);

        var edges = StaticEdgeAnalyzer.AnalyzeMethodCalls(graph, null);

        Assert.Empty(edges);
    }

    [Fact]
    public void Level3_NoSourceProjects_ReturnsEmpty()
    {
        var graph = MakeGraph([
            ("/repo/tests/MyTest/MyTest.csproj", "MyTest", true),
        ]);

        var edges = StaticEdgeAnalyzer.AnalyzeMethodCalls(graph, null);

        Assert.Empty(edges);
    }

    [Fact]
    public void Level3_StaticMethodCall_ProducesEdgeToCorrectFile()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "Orion.Core.Data");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Parser.cs"),
                """
                namespace Orion.Core.Data;

                public static class Parser
                {
                    public static Foo Parse(string input) => new Foo(1, "a");
                }

                public record Foo(int Id, string Name);
                """);

            var testDir = Path.Combine(fixtureDir, "tests", "Orion.UnitTests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "ParserTests.cs"),
                """
                using Orion.Core.Data;
                using Xunit;

                namespace Orion.UnitTests;

                public class ParserTests
                {
                    [Fact]
                    public void TestParse()
                    {
                        var foo = Parser.Parse("42:widget");
                        Assert.NotNull(foo);
                    }
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "Orion.Core.Data.csproj");
            var testProjPath = Path.Combine(testDir, "Orion.UnitTests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "Orion.Core.Data", false),
                (testProjPath, "Orion.UnitTests", true),
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["Orion.UnitTests"] = new[]
                {
                    new TestItem(
                        "Orion.UnitTests::Orion.UnitTests.ParserTests.TestParse",
                        "/asm/Orion.UnitTests.dll",
                        "Orion.UnitTests.ParserTests",
                        "TestParse",
                        TestFramework.Xunit, TestTier.Unit,
                        Path.Combine(testDir, "ParserTests.cs"),
                        TestOutcome.None, 0, [])
                }
            };

            var edges = StaticEdgeAnalyzer.AnalyzeMethodCalls(graph, discoveredTests);

            Assert.NotEmpty(edges);
            Assert.All(edges, e => Assert.Equal(EdgeOrigin.Static, e.Origin));
            Assert.All(edges, e => Assert.Equal(950_000, e.Weight));

            Assert.Contains(edges, e => e.To.Contains("Parser.cs"));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level3_MultipleMethodCalls_ProducesEdgesToMultipleFiles()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "Orion.Services");
            Directory.CreateDirectory(srcDir);

            File.WriteAllText(Path.Combine(srcDir, "AuthService.cs"),
                """
                namespace Orion.Services;

                public static class AuthService
                {
                    public static bool ValidateCredentials(string u, string p) => true;
                    public static string IssueToken(string u) => "tok";
                }
                """);

            File.WriteAllText(Path.Combine(srcDir, "Parser.cs"),
                """
                namespace Orion.Services;

                public static class Parser
                {
                    public static int Parse(string input) => 42;
                }
                """);

            var testDir = Path.Combine(fixtureDir, "tests", "Orion.Tests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "CombinedTests.cs"),
                """
                using Orion.Services;
                using Xunit;

                namespace Orion.Tests;

                public class CombinedTests
                {
                    [Fact]
                    public void TestBoth()
                    {
                        var result = Parser.Parse("42");
                        var token = AuthService.IssueToken("alice");
                        Assert.NotNull(token);
                    }
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "Orion.Services.csproj");
            var testProjPath = Path.Combine(testDir, "Orion.Tests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "Orion.Services", false),
                (testProjPath, "Orion.Tests", true),
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["Orion.Tests"] = new[]
                {
                    new TestItem(
                        "Orion.Tests::Orion.Tests.CombinedTests.TestBoth",
                        "/asm/Orion.Tests.dll",
                        "Orion.Tests.CombinedTests",
                        "TestBoth",
                        TestFramework.Xunit, TestTier.Unit,
                        Path.Combine(testDir, "CombinedTests.cs"),
                        TestOutcome.None, 0, [])
                }
            };

            var edges = StaticEdgeAnalyzer.AnalyzeMethodCalls(graph, discoveredTests);

            Assert.NotEmpty(edges);
            Assert.Contains(edges, e => e.To.Contains("Parser.cs"));
            Assert.Contains(edges, e => e.To.Contains("AuthService.cs"));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level3_ConstructorCall_ProducesEdge()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "Orion.Storage");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Repository.cs"),
                """
                namespace Orion.Storage;

                public class Repository
                {
                    public void Save(string key, string value) { }
                    public string? Load(string key) => null;
                }
                """);

            var testDir = Path.Combine(fixtureDir, "tests", "Orion.Tests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "RepoTest.cs"),
                """
                using Orion.Storage;
                using Xunit;

                namespace Orion.Tests;

                public class RepoTest
                {
                    [Fact]
                    public void TestNewRepo()
                    {
                        var repo = new Repository();
                        Assert.NotNull(repo);
                    }
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "Orion.Storage.csproj");
            var testProjPath = Path.Combine(testDir, "Orion.Tests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "Orion.Storage", false),
                (testProjPath, "Orion.Tests", true),
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["Orion.Tests"] = new[]
                {
                    new TestItem(
                        "Orion.Tests::Orion.Tests.RepoTest.TestNewRepo",
                        "/asm/Orion.Tests.dll",
                        "Orion.Tests.RepoTest",
                        "TestNewRepo",
                        TestFramework.Xunit, TestTier.Unit,
                        Path.Combine(testDir, "RepoTest.cs"),
                        TestOutcome.None, 0, [])
                }
            };

            var edges = StaticEdgeAnalyzer.AnalyzeMethodCalls(graph, discoveredTests);

            Assert.NotEmpty(edges);
            Assert.Contains(edges, e => e.To.Contains("Repository.cs"));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level3_NoMatchingCalls_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "MyLib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Service.cs"),
                """
                namespace MyLib;
                public class Service
                {
                    public void DoWork() { }
                }
                """);

            var testDir = Path.Combine(fixtureDir, "tests", "MyLib.Tests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "ServiceTests.cs"),
                """
                using Xunit;
                namespace MyLib.Tests;
                public class ServiceTests
                {
                    [Fact]
                    public void Test1()
                    {
                        Assert.True(true);
                    }
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "MyLib.csproj");
            var testProjPath = Path.Combine(testDir, "MyLib.Tests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "MyLib", false),
                (testProjPath, "MyLib.Tests", true),
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["MyLib.Tests"] = new[]
                {
                    new TestItem(
                        "MyLib.Tests::MyLib.Tests.ServiceTests.Test1",
                        "/asm/MyLib.Tests.dll",
                        "MyLib.Tests.ServiceTests",
                        "Test1",
                        TestFramework.Xunit, TestTier.Unit,
                        Path.Combine(testDir, "ServiceTests.cs"),
                        TestOutcome.None, 0, [])
                }
            };

            var edges = StaticEdgeAnalyzer.AnalyzeMethodCalls(graph, discoveredTests);

            Assert.Empty(edges);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void Level3_EmptyGraph_ReturnsEmpty()
    {
        var graph = MakeGraph([]);
        var edges = StaticEdgeAnalyzer.AnalyzeMethodCalls(graph, null);
        Assert.Empty(edges);
    }

    // ── BuildMethodMap ───────────────────────────────────────────

    [Fact]
    public void BuildMethodMap_ExtractsMethodNames()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "MyLib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Service.cs"),
                """
                namespace MyLib;
                public class Service
                {
                    public void DoWork() { }
                    public static string GetName() => "svc";
                    internal int Calculate() => 42;
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "MyLib.csproj");
            var graph = MakeGraph([
                (srcProjPath, "MyLib", false),
            ]);

            var methodMap = StaticEdgeAnalyzer.BuildMethodMap(graph);

            Assert.Contains("DoWork", methodMap);
            Assert.Contains("GetName", methodMap);
            Assert.Contains("Calculate", methodMap);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void BuildMethodMap_NoSourceFiles_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "EmptyLib");
            Directory.CreateDirectory(srcDir);

            var srcProjPath = Path.Combine(srcDir, "EmptyLib.csproj");
            var graph = MakeGraph([
                (srcProjPath, "EmptyLib", false),
            ]);

            var methodMap = StaticEdgeAnalyzer.BuildMethodMap(graph);
            Assert.Empty(methodMap);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    // ── BuildTypeMap ─────────────────────────────────────────────

    [Fact]
    public void BuildTypeMap_ExtractsTypeNames()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "MyLib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Service.cs"),
                """
                namespace MyLib;
                public class Service { }
                public record Foo(int Id);
                """);

            var srcProjPath = Path.Combine(srcDir, "MyLib.csproj");
            var graph = MakeGraph([
                (srcProjPath, "MyLib", false),
            ]);

            var typeMap = StaticEdgeAnalyzer.BuildTypeMap(graph);

            Assert.Contains("Service", typeMap);
            Assert.Contains("Foo", typeMap);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void BuildTypeMap_NoSourceFiles_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "EmptyLib");
            Directory.CreateDirectory(srcDir);

            var srcProjPath = Path.Combine(srcDir, "EmptyLib.csproj");
            var graph = MakeGraph([
                (srcProjPath, "EmptyLib", false),
            ]);

            var typeMap = StaticEdgeAnalyzer.BuildTypeMap(graph);
            Assert.Empty(typeMap);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    // ── ParseMethodCalls ─────────────────────────────────────────

    [Fact]
    public void ParseMethodCalls_FindsStaticMethodCall()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var testFile = Path.Combine(fixtureDir, "test.cs");
            File.WriteAllText(testFile,
                """
                using Xunit;
                namespace Tests;
                public class Test
                {
                    [Fact]
                    public void Test1()
                    {
                        var result = Parser.Parse("input");
                        Assert.NotNull(result);
                    }
                }
                """);

            var methodToFiles = new Dictionary<string, HashSet<string>>
            {
                ["Parse"] = new HashSet<string> { "/repo/src/Lib/Parser.cs" }
            };
            var classToFiles = new Dictionary<string, HashSet<string>>
            {
                ["Parser"] = new HashSet<string> { "/repo/src/Lib/Parser.cs" }
            };

            var matched = StaticEdgeAnalyzer.ParseMethodCalls(testFile, methodToFiles, classToFiles);

            Assert.Contains("/repo/src/Lib/Parser.cs", matched);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void ParseMethodCalls_FindsNewTypeCall()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var testFile = Path.Combine(fixtureDir, "test.cs");
            File.WriteAllText(testFile,
                """
                using Xunit;
                namespace Tests;
                public class Test
                {
                    [Fact]
                    public void Test1()
                    {
                        var repo = new Repository();
                        Assert.NotNull(repo);
                    }
                }
                """);

            var methodToFiles = new Dictionary<string, HashSet<string>>();
            var classToFiles = new Dictionary<string, HashSet<string>>
            {
                ["Repository"] = new HashSet<string> { "/repo/src/Lib/Repository.cs" }
            };

            var matched = StaticEdgeAnalyzer.ParseMethodCalls(testFile, methodToFiles, classToFiles);

            Assert.Contains("/repo/src/Lib/Repository.cs", matched);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void ParseMethodCalls_NoMatches_ReturnsEmpty()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var testFile = Path.Combine(fixtureDir, "test.cs");
            File.WriteAllText(testFile,
                """
                using Xunit;
                namespace Tests;
                public class Test
                {
                    [Fact]
                    public void Test1()
                    {
                        Assert.True(true);
                    }
                }
                """);

            var methodToFiles = new Dictionary<string, HashSet<string>>
            {
                ["Parse"] = new HashSet<string> { "/repo/src/Lib/Parser.cs" }
            };
            var classToFiles = new Dictionary<string, HashSet<string>>
            {
                ["Parser"] = new HashSet<string> { "/repo/src/Lib/Parser.cs" }
            };

            var matched = StaticEdgeAnalyzer.ParseMethodCalls(testFile, methodToFiles, classToFiles);

            Assert.Empty(matched);
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }

    [Fact]
    public void ParseMethodCalls_UnreadableFile_ReturnsEmpty()
    {
        var matched = StaticEdgeAnalyzer.ParseMethodCalls(
            "/nonexistent/file.cs",
            new Dictionary<string, HashSet<string>>(),
            new Dictionary<string, HashSet<string>>());
        Assert.Empty(matched);
    }

    // ── AnalyzeAll with L3 (merge) ───────────────────────────────

    [Fact]
    public void AnalyzeAll_L3PreferredOverL2AndL1()
    {
        var (fixtureDir, tearDown) = CreateFixtureDir();
        try
        {
            var srcDir = Path.Combine(fixtureDir, "src", "MyLib");
            Directory.CreateDirectory(srcDir);
            File.WriteAllText(Path.Combine(srcDir, "Service.cs"),
                """
                namespace MyLib;
                public static class Service
                {
                    public static string GetName() => "svc";
                }
                """);
            File.WriteAllText(Path.Combine(srcDir, "Other.cs"),
                """
                namespace MyLib;
                public static class Other
                {
                    public static int Calculate() => 42;
                }
                """);

            var testDir = Path.Combine(fixtureDir, "tests", "MyLib.Tests");
            Directory.CreateDirectory(testDir);
            File.WriteAllText(Path.Combine(testDir, "ServiceTests.cs"),
                """
                using MyLib;
                using Xunit;
                namespace MyLib.Tests;
                public class ServiceTests
                {
                    [Fact]
                    public void TestGetName()
                    {
                        var name = Service.GetName();
                        Assert.NotNull(name);
                    }
                }
                """);

            var srcProjPath = Path.Combine(srcDir, "MyLib.csproj");
            var testProjPath = Path.Combine(testDir, "MyLib.Tests.csproj");

            var graph = MakeGraph([
                (srcProjPath, "MyLib", false),
                (testProjPath, "MyLib.Tests", true),
            ], dependencies: [
                (testProjPath, srcProjPath),
            ]);

            var discoveredTests = new Dictionary<string, TestItem[]>
            {
                ["MyLib.Tests"] = new[]
                {
                    new TestItem(
                        "MyLib.Tests::MyLib.Tests.ServiceTests.TestGetName",
                        "/asm/MyLib.Tests.dll",
                        "MyLib.Tests.ServiceTests",
                        "TestGetName",
                        TestFramework.Xunit, TestTier.Unit,
                        Path.Combine(testDir, "ServiceTests.cs"),
                        TestOutcome.None, 0, [])
                }
            };

            var edges = StaticEdgeAnalyzer.AnalyzeAll(graph, discoveredTests);

            Assert.Contains(edges, e => e.To.Contains("Service.cs"));

            var l3Weights = edges.Where(e => e.Weight == 950_000).ToArray();
            Assert.NotEmpty(l3Weights);
            Assert.All(l3Weights, e => Assert.Contains("Service.cs", e.To));
        }
        finally
        {
            CleanupFixtureDir(tearDown);
        }
    }
}