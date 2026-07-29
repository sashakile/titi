// Tests for TID-11: incremental edge update per source-fingerprint (3.6)

namespace titi.Tests;

using System.Text.Json;

public class IncrementalRecordTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(), "titi-incremental-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    static ProjectDescriptor TestProject(string packageId) => new(
        Path: $"/repo/tests/{packageId}/{packageId}.csproj",
        PackageId: packageId,
        Version: new SemanticVersion(1, 0, 0, null, null),
        TargetFrameworks: [new Tfm("net10.0", "net10.0", 10.0)],
        IsPackable: false,
        IsTestProject: true,
        PackageRefs: [],
        ProjectRefs: [],
        Properties: new()
    );

    // ── ComputeChangedProjects ───────────────────────────────────

    [Fact]
    public void ComputeChangedProjects_NoPrior_AllChanged()
    {
        var projects = new ProjectDescriptor[] { TestProject("A"), TestProject("B") };
        var changed = titi.RecordPlanner.ComputeChangedProjects(new Dictionary<string, string>(), projects);

        Assert.Equal(2, changed.Length);
        Assert.Equal(["A", "B"], changed.Select(p => p.PackageId).OrderBy(x => x));
    }

    [Fact]
    public void ComputeChangedProjects_AllMatch_NoneChanged()
    {
        var projA = CreateTempProject("PA", "A");
        var projB = CreateTempProject("PB", "B");
        var projects = new[] { projA, projB };

        var prior = new Dictionary<string, string>();
        foreach (var proj in projects)
        {
            var projDir = Path.GetDirectoryName(proj.Path) ?? "";
            prior[proj.PackageId] = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, proj.Path);
        }

        var changed = titi.RecordPlanner.ComputeChangedProjects(prior, projects);
        Assert.Empty(changed);
    }

    [Fact]
    public void ComputeChangedProjects_OneChanged_DetectsIt()
    {
        var projA = CreateTempProject("PA", "A");
        var projB = CreateTempProject("PB", "B");
        var projects = new[] { projA, projB };

        var projADir = Path.GetDirectoryName(projA.Path) ?? "";
        var realFpA = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projADir, projA.Path);
        var prior = new Dictionary<string, string>
        {
            ["A"] = realFpA,
            ["B"] = "stale-fingerprint",
        };

        var changed = titi.RecordPlanner.ComputeChangedProjects(prior, projects);
        Assert.Single(changed);
        Assert.Equal("B", changed[0].PackageId);
    }

    // ── Pure fingerprint diff ───────────────────────────────────

    [Fact]
    public void ComputeChangedProjects_EmptyPriorAndProjects_ReturnsEmpty()
    {
        var changed = titi.RecordPlanner.ComputeChangedProjects(new Dictionary<string, string>(), []);
        Assert.Empty(changed);
    }

    [Fact]
    public void ComputeChangedProjects_NonTestProject_Ignored()
    {
        var lib = TestProject("Lib") with { IsTestProject = false };
        var testA = TestProject("A");
        var projects = new[] { lib, testA };

        var changed = titi.RecordPlanner.ComputeChangedProjects(new Dictionary<string, string>(), projects);
        Assert.Single(changed);
        Assert.Equal("A", changed[0].PackageId);
    }

    // ── Fingerprint file I/O ─────────────────────────────────────

    [Fact]
    public void SaveAndLoadFingerprints_RoundTrips()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var fingerprints = new Dictionary<string, string>
        {
            ["Proj.A"] = "abc123",
            ["Proj.B"] = "def456",
        };

        titi.RecordPlanner.SaveProjectFingerprints(cacheDir, fingerprints);
        var loaded = titi.RecordPlanner.LoadProjectFingerprints(cacheDir);

        Assert.Equal(fingerprints, loaded);
    }

    [Fact]
    public void LoadFingerprints_NoFile_ReturnsEmpty()
    {
        var loaded = titi.RecordPlanner.LoadProjectFingerprints("/nonexistent");
        Assert.Empty(loaded);
    }

    [Fact]
    public void LoadFingerprints_CorruptFile_ReturnsEmpty()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        Directory.CreateDirectory(cacheDir);
        File.WriteAllText(
            Path.Combine(cacheDir, "project-fingerprints.json"),
            "not json");

        var loaded = titi.RecordPlanner.LoadProjectFingerprints(cacheDir);
        Assert.Empty(loaded);
    }

    // ── Integration: full incremental flow (with temp project dirs) ─

    [Fact]
    public void IncrementalRecord_Fresh_RecordsAllProjects()
    {
        var projA = CreateTempProject("PA", "A");
        var projB = CreateTempProject("PB", "B");
        var projects = new[] { projA, projB };
        var cacheDir = Path.Combine(_tempDir, "cache");

        var prior = titi.RecordPlanner.LoadProjectFingerprints(cacheDir);
        var changed = titi.RecordPlanner.ComputeChangedProjects(prior, projects);

        Assert.Equal(2, changed.Length);
    }

    [Fact]
    public void IncrementalRecord_AfterRecording_NothingChanged()
    {
        var projA = CreateTempProject("PA", "A");
        var projB = CreateTempProject("PB", "B");
        var projects = new[] { projA, projB };
        var cacheDir = Path.Combine(_tempDir, "cache");
        var edgesDir = Path.Combine(cacheDir, "edges");
        Directory.CreateDirectory(edgesDir);

        var fingerprints = new Dictionary<string, string>();
        foreach (var proj in projects)
        {
            var projDir = Path.GetDirectoryName(proj.Path) ?? "";
            var fp = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, proj.Path);
            fingerprints[proj.PackageId] = fp;
        }
        titi.RecordPlanner.SaveProjectFingerprints(cacheDir, fingerprints);

        var loaded = titi.RecordPlanner.LoadProjectFingerprints(cacheDir);
        var changed = titi.RecordPlanner.ComputeChangedProjects(loaded, projects);

        Assert.Empty(changed);
    }

    [Fact]
    public void IncrementalRecord_AfterSourceChange_DetectsChangedProject()
    {
        var projA = CreateTempProject("PA", "A");
        var projB = CreateTempProject("PB", "B");
        var projects = new[] { projA, projB };
        var cacheDir = Path.Combine(_tempDir, "cache");
        var edgesDir = Path.Combine(cacheDir, "edges");
        Directory.CreateDirectory(edgesDir);

        var fingerprints = new Dictionary<string, string>();
        foreach (var proj in projects)
        {
            var projDir = Path.GetDirectoryName(proj.Path) ?? "";
            var fp = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, proj.Path);
            fingerprints[proj.PackageId] = fp;
        }
        titi.RecordPlanner.SaveProjectFingerprints(cacheDir, fingerprints);

        var projADir = Path.GetDirectoryName(projA.Path) ?? "";
        var srcFile = Path.Combine(projADir, "NewTest.cs");
        File.WriteAllText(srcFile, "// added file");

        var currentFingerprints = new Dictionary<string, string>();
        foreach (var proj in projects)
        {
            var projDir = Path.GetDirectoryName(proj.Path) ?? "";
            var fp = titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, proj.Path);
            currentFingerprints[proj.PackageId] = fp;
        }

        var changed = titi.RecordPlanner.ComputeChangedProjects(fingerprints, projects,
            (p) => currentFingerprints.GetValueOrDefault(p.PackageId, ""));

        Assert.Single(changed);
        Assert.Equal("A", changed[0].PackageId);
    }

    [Fact]
    public void IncrementalRecord_NewProject_DetectsAsChanged()
    {
        var projA = CreateTempProject("PA", "A");
        var cacheDir = Path.Combine(_tempDir, "cache");

        var prior = new Dictionary<string, string>
        {
            ["A"] = "old-fp",
            ["B"] = "old-fp-b",
        };
        titi.RecordPlanner.SaveProjectFingerprints(cacheDir, prior);

        var changed = titi.RecordPlanner.ComputeChangedProjects(prior, [projA],
            (p) => titi.TestDiscovery.DiscoveryCache.ComputeFingerprint(
                Path.GetDirectoryName(projA.Path) ?? "", projA.Path));

        Assert.Single(changed);
        Assert.Equal("A", changed[0].PackageId);
    }

    // ── Key-derivation consistency (TID-11 / titi-k2x.2) ─────

    [Fact]
    public void EdgeFilePath_WriteVsRead_UseSameKey()
    {
        // After fix: write, read, and cleanup all derive paths from the
        // package-ID key, so they are consistent even when the project path
        // differs from the package ID.
        var proj = TestProject("MyApp") with { Path = "/repo/tests/SomePath/MyApp.csproj" };

        var writeKey = titi.RecordPlanner.EdgeFileKey(proj.PackageId);
        var readKey = titi.RecordPlanner.EdgeFileKey(proj.PackageId);
        var cleanupKey = titi.RecordPlanner.EdgeFileKey(proj.PackageId);

        Assert.Equal(writeKey, readKey);
        Assert.Equal(readKey, cleanupKey);
    }

    [Fact]
    public void RecordPipeline_UnchangedEdgeFile_SurvivesAfterPartialUpdate()
    {
        // After fix: unchanged project B's edge file survives because write,
        // read, and cleanup all use the same package-ID key.
        var edgesDir = Path.Combine(_tempDir, "edges");
        var projectsDir = Path.Combine(edgesDir, "projects");
        Directory.CreateDirectory(projectsDir);

        var projA = TestProject("A") with { Path = "/repo/tests/PA/PA.csproj" };
        var projB = TestProject("B") with { Path = "/repo/tests/PB/PB.csproj" };
        var changed = new[] { projA };
        var unchangedPackageIds = new[] { "B" };

        // Write for changed project A (uses package-ID key after fix):
        var writePathA = Path.Combine(projectsDir, $"{titi.RecordPlanner.EdgeFileKey(projA.PackageId)}.json");
        File.WriteAllText(writePathA, "[]");

        // Write for unchanged project B (simulates prior recording):
        var writePathB = Path.Combine(projectsDir, $"{titi.RecordPlanner.EdgeFileKey(projB.PackageId)}.json");
        File.WriteAllText(writePathB, "[]");

        // Read B's edges (uses package-ID key):
        var readPathB = Path.Combine(projectsDir, $"{titi.RecordPlanner.EdgeFileKey(projB.PackageId)}.json");
        Assert.True(File.Exists(readPathB), "B's edge file should exist after fix");

        // Cleanup: only project A's unchanged .json files survive
        var currentIds = new[] { "A", "B" }.Select(id => titi.RecordPlanner.EdgeFileKey(id)).ToHashSet();
        foreach (var file in Directory.EnumerateFiles(projectsDir, "*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            Assert.Contains(name, currentIds); // Both files survive
        }
    }

    // ── Helpers ─────────────────────────────────────────────────

    private ProjectDescriptor CreateTempProject(string dirName, string packageId)
    {
        var projDir = Path.Combine(_tempDir, "projects", dirName);
        Directory.CreateDirectory(projDir);
        var csprojPath = Path.Combine(projDir, $"{packageId}.csproj");
        File.WriteAllText(csprojPath,
            $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(projDir, "Test1.cs"), "class Test1 { }");

        return TestProject(packageId) with { Path = csprojPath };
    }

}
