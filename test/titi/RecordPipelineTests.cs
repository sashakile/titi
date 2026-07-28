// Tests for TID-3b: titi tests record pipeline helpers (CLI-22)

namespace titi.Tests;

public class RecordPlannerTests
{
    static ProjectDescriptor TestProject(string path, string id) => new(
        Path: path, PackageId: id,
        Version: new SemanticVersion(1, 0, 0, null, null),
        TargetFrameworks: [new Tfm("net10.0", "net10.0", 10.0)],
        IsPackable: false, IsTestProject: true,
        PackageRefs: [], ProjectRefs: [], Properties: new());

    static ProjectDescriptor LibProject(string path, string id) => TestProject(path, id) with { IsTestProject = false };

    [Fact]
    public void PlanTestRuns_FiltersToTestProjectsOnly()
    {
        var projects = new ProjectDescriptor[]
        {
            LibProject("/repo/libs/A/A.csproj", "A"),
            TestProject("/repo/tests/T1/T1.csproj", "T1"),
            LibProject("/repo/libs/B/B.csproj", "B"),
            TestProject("/repo/tests/T2/T2.csproj", "T2"),
        };

        var plans = titi.RecordPlanner.PlanTestRuns(projects, resultsRoot: "/repo/.titi/test-cache/runs");

        Assert.Equal(2, plans.Length);
        Assert.All(plans, p => Assert.EndsWith(".csproj", p.ProjectPath));
        Assert.Contains(plans, p => p.ProjectPath.EndsWith("T1.csproj"));
        Assert.Contains(plans, p => p.ProjectPath.EndsWith("T2.csproj"));
    }

    [Fact]
    public void PlanTestRuns_EachPlanHasUniqueResultsDir()
    {
        var projects = new ProjectDescriptor[]
        {
            TestProject("/repo/tests/T1/T1.csproj", "T1"),
            TestProject("/repo/tests/T2/T2.csproj", "T2"),
        };

        var plans = titi.RecordPlanner.PlanTestRuns(projects, resultsRoot: "/repo/.titi/test-cache/runs");

        Assert.Equal(2, plans.Length);
        var dirs = plans.Select(p => p.ResultsDir).ToHashSet();
        Assert.Equal(2, dirs.Count);
        Assert.All(plans, p => Assert.StartsWith("/repo/.titi/test-cache/runs", p.ResultsDir));
    }

    [Fact]
    public void PlanTestRuns_ArgumentsIncludeCollectCoverageAndTrxLogger()
    {
        var projects = new ProjectDescriptor[]
        {
            TestProject("/repo/tests/T1/T1.csproj", "T1"),
        };

        var plans = titi.RecordPlanner.PlanTestRuns(projects, resultsRoot: "/repo/.titi/test-cache/runs");

        var plan = Assert.Single(plans);
        Assert.Contains("""--collect "XPlat Code Coverage" """, plan.Arguments);
        Assert.Contains("--logger trx", plan.Arguments);
        Assert.Contains($"--results-directory \"{plan.ResultsDir}\"", plan.Arguments);
        Assert.Contains($"\"{plan.ProjectPath}\"", plan.Arguments);
    }

    [Fact]
    public void PlanTestRuns_NoTestProjects_ReturnsEmpty()
    {
        var projects = new ProjectDescriptor[]
        {
            LibProject("/repo/libs/A/A.csproj", "A"),
        };

        Assert.Empty(titi.RecordPlanner.PlanTestRuns(projects, resultsRoot: "/repo/.titi/test-cache/runs"));
    }
}

public class ArtifactLocatorTests
{
    [Fact]
    public void FindArtifacts_LocatesTrxAndCobertura()
    {
        using var tmp = new TempDir();
        // dotnet test writes the TRX at the results-directory root, and the
        // Cobertura coverage file inside a GUID-named subdirectory.
        File.WriteAllText(Path.Combine(tmp.Path, "run.trx"), "<TestRun/>");
        var sub = Directory.CreateDirectory(Path.Combine(tmp.Path, "abc123-guid"));
        File.WriteAllText(Path.Combine(sub.FullName, "coverage.cobertura.xml"), "<coverage/>");

        var (trx, cobertura) = titi.ArtifactLocator.FindArtifacts(tmp.Path);

        Assert.NotNull(trx);
        Assert.EndsWith(".trx", trx);
        Assert.NotNull(cobertura);
        Assert.EndsWith("coverage.cobertura.xml", cobertura);
    }

    [Fact]
    public void FindArtifacts_NoTrx_ReturnsNulls()
    {
        using var tmp = new TempDir();
        var (trx, cobertura) = titi.ArtifactLocator.FindArtifacts(tmp.Path);
        Assert.Null(trx);
        Assert.Null(cobertura);
    }

    [Fact]
    public void FindArtifacts_TrxOnly_CoberturaNull()
    {
        using var tmp = new TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "run.trx"), "<TestRun/>");

        var (trx, cobertura) = titi.ArtifactLocator.FindArtifacts(tmp.Path);

        Assert.NotNull(trx);
        Assert.Null(cobertura);
    }

    [Fact]
    public void FindArtifacts_NonExistentDir_ReturnsNulls()
    {
        var (trx, cobertura) = titi.ArtifactLocator.FindArtifacts("/no/such/dir/anywhere");
        Assert.Null(trx);
        Assert.Null(cobertura);
    }

    sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "titi-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
    }
}
