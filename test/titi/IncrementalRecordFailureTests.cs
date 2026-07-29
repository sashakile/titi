// Tests for titi-k2x.16: Preserve prior recording state when a changed project fails

namespace titi.Tests;

using titi.TestDiscovery;
using titi.Serialization;
using System.Text.Json;

public class IncrementalRecordFailureTests : IDisposable
{
    readonly string _tempDir;

    public IncrementalRecordFailureTests() =>
        _tempDir = Path.Combine(Path.GetTempPath(), "titi-fail-test-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ProjectDescriptor MakeTestProject(string dirName, string packageId)
    {
        var projDir = Path.Combine(_tempDir, "projects", dirName);
        Directory.CreateDirectory(projDir);
        var csprojPath = Path.Combine(projDir, $"{packageId}.csproj");
        File.WriteAllText(csprojPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(projDir, "Test1.cs"), "class Test1 { }");

        return new ProjectDescriptor(
            Path: csprojPath,
            PackageId: packageId,
            Version: new SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [new("net10.0", "net", 10.0)],
            IsPackable: false,
            IsTestProject: true,
            PackageRefs: [],
            ProjectRefs: [],
            Properties: new()
        );
    }

    [Fact]
    public void FailedProject_FingerprintKeptPrior()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var projA = MakeTestProject("projA", "ProjA");
        var projB = MakeTestProject("projB", "ProjB");

        var projADir = Path.GetDirectoryName(projA.Path) ?? "";
        var projBDir = Path.GetDirectoryName(projB.Path) ?? "";
        var fpA = DiscoveryCache.ComputeFingerprint(projADir, projA.Path);
        var fpB = DiscoveryCache.ComputeFingerprint(projBDir, projB.Path);

        var initial = new Dictionary<string, string> { ["ProjA"] = fpA, ["ProjB"] = fpB };
        RecordPlanner.SaveProjectFingerprints(cacheDir, initial);

        // Simulate: ProjA succeeded, ProjB failed
        // Only advance fingerprint for ProjA
        var recorded = new[] { "ProjA" };
        var advanced = new Dictionary<string, string> { ["ProjA"] = fpA };
        foreach (var kv in initial.Where(kv => !recorded.Contains(kv.Key)))
            advanced[kv.Key] = kv.Value;

        RecordPlanner.SaveProjectFingerprints(cacheDir, advanced);

        var loaded = RecordPlanner.LoadProjectFingerprints(cacheDir);
        Assert.Equal(fpA, loaded["ProjA"]);
        Assert.Equal(fpB, loaded["ProjB"]);
    }

    [Fact]
    public void FailedProject_PriorEdgeFilePreserved()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var edgesDir = Path.Combine(cacheDir, "edges", "projects");
        var projA = MakeTestProject("projA", "ProjA");
        var projB = MakeTestProject("projB", "ProjB");

        // Write a prior edge file for ProjB (from a prior successful recording)
        Directory.CreateDirectory(edgesDir);
        var priorEdgePath = RecordPlanner.EdgeFilePath(edgesDir, "ProjB");
        var priorEdges = new[] { new EdgeEntry("ProjB.Test1", "FileB.cs", 1, 1, []) };
        File.WriteAllText(priorEdgePath,
            JsonSerializer.Serialize(priorEdges, TitiJsonContext.Default.EdgeEntryArray));

        // Simulate recording: ProjA succeeds (writes new edges), ProjB fails (no write)
        var projAEdgePath = RecordPlanner.EdgeFilePath(edgesDir, "ProjA");
        var newEdges = new[] { new EdgeEntry("ProjA.Test1", "FileA.cs", 1, 1, []) };
        File.WriteAllText(projAEdgePath,
            JsonSerializer.Serialize(newEdges, TitiJsonContext.Default.EdgeEntryArray));

        // ProjB's prior edge file must remain untouched
        Assert.True(File.Exists(priorEdgePath));
        var loadedPrior = JsonSerializer.Deserialize(
            File.ReadAllText(priorEdgePath), TitiJsonContext.Default.ListJsonEdge);
        Assert.Single(loadedPrior!);
        Assert.Equal("ProjB.Test1", loadedPrior![0].From);
    }

    [Fact]
    public void SuccessfulProject_FingerprintAdvances()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var projA = MakeTestProject("projA", "ProjA");
        var projADir = Path.GetDirectoryName(projA.Path) ?? "";

        var priorFp = DiscoveryCache.ComputeFingerprint(projADir, projA.Path);
        RecordPlanner.SaveProjectFingerprints(cacheDir, new() { ["ProjA"] = priorFp });

        // Change source to get a new fingerprint
        File.WriteAllText(Path.Combine(projADir, "Test2.cs"), "class Test2 { }");
        var newFp = DiscoveryCache.ComputeFingerprint(projADir, projA.Path);
        Assert.NotEqual(priorFp, newFp);

        // Simulate successful recording: advance fingerprint
        RecordPlanner.SaveProjectFingerprints(cacheDir, new() { ["ProjA"] = newFp });

        var loaded = RecordPlanner.LoadProjectFingerprints(cacheDir);
        Assert.Equal(newFp, loaded["ProjA"]);
    }
}
