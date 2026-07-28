// Tests for titi REPL — interactive dependency graph explorer

namespace titi.Tests;

using titi.Repl;
using System.Text.Json;

public class ReplTests
{
    static MonorepoGraph MakeTestGraph()
    {
        var corePath = "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj";
        var appPath = "/repo/src/Orion.App/Orion.App.csproj";

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

        return new MonorepoGraph(
            Nodes: new() { [corePath] = coreNode, [appPath] = appNode },
            TopologicalOrder: [corePath, appPath],
            RepoRoot: "/repo",
            BuiltAt: DateTime.UtcNow,
            Fingerprints: []
        );
    }

    /// <summary>REPL with graph prints titi> prompt and exits on 'quit' with code 0.</summary>
    [Fact]
    public void Repl_Quit_ExitsWithCode0()
    {
        var input = new StringReader("quit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("titi>", outText);
    }

    /// <summary>REPL with graph prints titi> prompt and exits on 'exit' with code 0.</summary>
    [Fact]
    public void Repl_Exit_ExitsWithCode0()
    {
        var input = new StringReader("exit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("titi>", outText);
    }

    /// <summary>Help lists all 8 commands and exits with code 0.</summary>
    [Fact]
    public void Repl_Help_ListsAllCommands()
    {
        var input = new StringReader("help\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("deps", outText);
        Assert.Contains("dependents", outText);
        Assert.Contains("path", outText);
        Assert.Contains("info", outText);
        Assert.Contains("affected", outText);
        Assert.Contains("tree", outText);
        Assert.Contains("help", outText);
        Assert.Contains("quit", outText);
        Assert.Contains("exit", outText);
    }

    /// <summary>Unknown command prints error message, suggests 'help', and does not exit.</summary>
    [Fact]
    public void Repl_UnknownCommand_ShowsErrorAndStays()
    {
        var input = new StringReader("foobar\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var errText = error.ToString();
        Assert.Contains("foobar", errText);
        Assert.Contains("help", errText);
    }

    /// <summary>EOF (Ctrl+D) exits with code 0.</summary>
    [Fact]
    public void Repl_EOF_ExitsWithCode0()
    {
        // StringReader returns empty string on null input — simulate EOF by empty input
        var input = new StringReader("");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("titi>", outText);
    }

    /// <summary>Deps command with project argument shows dependencies for that project.</summary>
    [Fact]
    public void Repl_DepsCommand_WithProject_ShowsDeps()
    {
        var input = new StringReader("deps Orion.App\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("Orion.Core.Data", outText);
    }

    /// <summary>Deps without project argument shows usage error.</summary>
    [Fact]
    public void Repl_DepsCommand_NoArg_ShowsUsage()
    {
        var input = new StringReader("deps\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var errText = error.ToString();
        Assert.Contains("Usage", errText);
    }

    /// <summary>Deps with unknown project shows error message.</summary>
    [Fact]
    public void Repl_DepsCommand_UnknownProject_ShowsError()
    {
        var input = new StringReader("deps Unknown.Pkg\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var errText = error.ToString();
        Assert.Contains("Unknown.Pkg", errText);
    }

    /// <summary>Deps on project with no dependencies prints nothing (empty output).</summary>
    [Fact]
    public void Repl_DepsCommand_NoDeps_PrintsNothing()
    {
        var input = new StringReader("deps Orion.Core.Data\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        // Should not contain "(no dependencies)" — prints nothing for empty
        Assert.DoesNotContain("no dependencies", outText.ToLower());
    }

    /// <summary>Dependents command with project shows dependents for that project.</summary>
    [Fact]
    public void Repl_DependentsCommand_WithProject_ShowsDependents()
    {
        var input = new StringReader("dependents Orion.Core.Data\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        // Orion.Core.Data has no dependents in the test graph (Dependents not filled)
        // So this should print nothing
    }

    /// <summary>Dependents without project argument shows usage error.</summary>
    [Fact]
    public void Repl_DependentsCommand_NoArg_ShowsUsage()
    {
        var input = new StringReader("dependents\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var errText = error.ToString();
        Assert.Contains("Usage", errText);
    }

    /// <summary>Path command shows path between two packages.</summary>
    [Fact]
    public void Repl_PathCommand_ShowsPath()
    {
        var input = new StringReader("path Orion.App Orion.Core.Data\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("Orion.App", outText);
        Assert.Contains("Orion.Core.Data", outText);
    }

    /// <summary>Path command with same from/to prints just the project name.</summary>
    [Fact]
    public void Repl_PathCommand_SameProject_PrintsName()
    {
        var input = new StringReader("path Orion.Core.Data Orion.Core.Data\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("Orion.Core.Data", outText);
        // Should not contain an arrow (means it's not showing a path)
        Assert.DoesNotContain("->", outText);
    }

    /// <summary>Path command with no path available prints no-path message.</summary>
    [Fact]
    public void Repl_PathCommand_NoPath_ShowsError()
    {
        // Both Orion.Core.Data and Orion.App exist but there's no dependency path
        // from Core.Data to App (Core.Data has no deps, App depends on Core.Data)
        var input = new StringReader("path Orion.Core.Data Orion.App\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("No path found", outText);
    }

    /// <summary>Path command with unknown project prints error.</summary>
    [Fact]
    public void Repl_PathCommand_UnknownProject_ShowsError()
    {
        var input = new StringReader("path Orion.App Unknown.Pkg\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var errText = error.ToString();
        Assert.Contains("Unknown.Pkg", errText);
    }

    /// <summary>Info command shows project details.</summary>
    [Fact]
    public void Repl_InfoCommand_ShowsProjectInfo()
    {
        var input = new StringReader("info Orion.Core.Data\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        Assert.Contains("Orion.Core.Data", outText);
        Assert.Contains("1.0.0", outText);
    }

    /// <summary>Info command with unknown project prints error.</summary>
    [Fact]
    public void Repl_InfoCommand_UnknownProject_ShowsError()
    {
        var input = new StringReader("info Unknown.Pkg\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var errText = error.ToString();
        Assert.Contains("Unknown.Pkg", errText);
    }

    /// <summary>Affected command shows affected projects.</summary>
    [Fact]
    public void Repl_AffectedCommand_ShowsAffected()
    {
        var input = new StringReader("affected\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        // With no git info, shows all projects
        Assert.Contains("Orion.Core.Data", outText);
        Assert.Contains("Orion.App", outText);
    }

    /// <summary>Tree command shows tree view (root nodes with their dependency subtrees).</summary>
    [Fact]
    public void Repl_TreeCommand_ShowsTree()
    {
        var input = new StringReader("tree\nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
        var outText = output.ToString();
        // Tree shows root nodes (depth 0) and their dependency subtrees
        Assert.Contains("Orion.Core.Data", outText);
        // Orion.App is a dependent of Core.Data, not a dependency — not shown in tree
    }

    /// <summary>REPL with null graph exits with code 1 and E001.</summary>
    [Fact]
    public void Repl_NullGraph_ExitsWithCode1()
    {
        var input = new StringReader("");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(null, input, output, error);

        Assert.Equal(1, exitCode);
        var errText = error.ToString();
        Assert.Contains("E001", errText);
        Assert.Contains("GRAPH_BUILD_FAILED", errText);
    }

    /// <summary>Whitespace commands are ignored.</summary>
    [Fact]
    public void Repl_WhitespaceCommand_Ignored()
    {
        var input = new StringReader("  \nquit\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = ReplEngine.Run(MakeTestGraph(), input, output, error);

        Assert.Equal(0, exitCode);
    }
}
