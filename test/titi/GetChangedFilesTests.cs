// Tests for titi-k2x.7: distinguish a successful empty git diff from a git failure

namespace titi.Tests;

using System.Diagnostics;
using titi.Affected;

public class GetChangedFilesTests
{
    static string MakeTempGitRepo()
    {
        var dir = Path.Combine(Path.GetTempPath(), "titi-gitdiff-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        Git(dir, "init -q");
        Git(dir, "config user.email test@test");
        Git(dir, "config user.name test");
        // Initial commit so HEAD exists
        File.WriteAllText(Path.Combine(dir, "README.md"), "init");
        Git(dir, "add README.md");
        Git(dir, "commit -q -m init");
        return dir;
    }

    static (string stdout, string stderr, int exit) Git(string dir, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = dir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (stdout, stderr, p.ExitCode);
    }

    [Fact]
    public void EmptyDiff_Success_ReturnsEmptyArray_NotNull()
    {
        // HEAD..HEAD is a valid, successful diff with zero changed files.
        var repo = MakeTempGitRepo();
        try
        {
            var (files, err) = Analyzer.GetChangedFiles(repo, "HEAD");

            Assert.Null(err);
            // The distinction that matters: a successful empty diff is an empty
            // array, NOT null. null is reserved for the unavailable-git/fallback
            // signal so affected analysis doesn't select every project on a
            // clean no-change invocation.
            Assert.NotNull(files);
            Assert.Empty(files);
        }
        finally
        {
            if (Directory.Exists(repo)) Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public void EmptyDiff_BuildsZeroAffectedProjects_NotAll()
    {
        // A successful empty diff must produce an empty AffectedSet, not the
        // fallback "all projects affected" set that a git failure triggers.
        var repo = MakeTempGitRepo();
        try
        {
            var (files, err) = Analyzer.GetChangedFiles(repo, "HEAD");
            Assert.Null(err);
            Assert.NotNull(files);
            Assert.Empty(files);

            // BuildAffectedSet: empty array => empty; null => all projects.
            var graph = SampleGraph();
            var affected = Analyzer.BuildAffectedSet(files, graph);

            Assert.Empty(affected.DirectlyAffected);
            Assert.Empty(affected.TransitivelyAffected);
        }
        finally
        {
            if (Directory.Exists(repo)) Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public void GitFailure_BadRef_ReturnsNullFilesWithError()
    {
        // A non-existent base ref is an actual git failure: must surface as null
        // (the documented fallback signal) with a non-null error.
        var repo = MakeTempGitRepo();
        try
        {
            var (files, err) = Analyzer.GetChangedFiles(repo, "does-not-exist-ref");

            Assert.NotNull(err);
            Assert.Null(files);
        }
        finally
        {
            if (Directory.Exists(repo)) Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public void GitFailure_FallbackSelectsAllProjects()
    {
        // On a real git failure the null signal must drive the documented
        // fallback (all projects directly affected).
        var repo = MakeTempGitRepo();
        try
        {
            var (files, err) = Analyzer.GetChangedFiles(repo, "does-not-exist-ref");
            Assert.Null(files);

            var graph = SampleGraph();
            var affected = Analyzer.BuildAffectedSet(files, graph);

            Assert.Equal(graph.Nodes.Count, affected.DirectlyAffected.Length);
        }
        finally
        {
            if (Directory.Exists(repo)) Directory.Delete(repo, recursive: true);
        }
    }

    static titi.MonorepoGraph SampleGraph()
    {
        // Minimal two-node graph; reused to assert fallback-vs-empty distinction.
        var projA = new titi.ProjectDescriptor(
            Path: "src/Proj.A/Proj.A.csproj",
            PackageId: "Proj.A",
            Version: new titi.SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [],
            IsPackable: false,
            IsTestProject: false,
            PackageRefs: [],
            ProjectRefs: [],
            Properties: new() { ["SourceDir"] = "src/Proj.A" });
        var projB = new titi.ProjectDescriptor(
            Path: "src/Proj.B/Proj.B.csproj",
            PackageId: "Proj.B",
            Version: new titi.SemanticVersion(1, 0, 0, null, null),
            TargetFrameworks: [],
            IsPackable: false,
            IsTestProject: false,
            PackageRefs: [],
            ProjectRefs: [],
            Properties: new() { ["SourceDir"] = "src/Proj.B" });

        var nodes = new Dictionary<string, titi.GraphNode>
        {
            ["src/Proj.A/Proj.A.csproj"] = new(projA, [], [], 0),
            ["src/Proj.B/Proj.B.csproj"] = new(projB, [], [], 0),
        };
        return new titi.MonorepoGraph(nodes, [], "", DateTime.MinValue, new());
    }
}
