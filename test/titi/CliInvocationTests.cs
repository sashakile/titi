// CLI invocation tests — table-driven tests covering command surface,
// exit codes, and usage error handling

namespace titi.Tests;

public class CliInvocationTests
{
    static readonly string FixtureDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../test/fixtures/sample-monorepo"));

    /// <summary>Commands that should succeed with exit code 0 against the fixture.</summary>
    public static TheoryData<string[], string> PassingCases => new()
    {
        // --help variations
        { ["--help"], "titi" },
        { ["-h"], "titi" },
        { Array.Empty<string>(), "titi" },
        // clean (no .titi/ yet, but should exit 0)
        { ["clean"], "" },
    };

    /// <summary>Commands that should fail with exit code 2 (usage errors).</summary>
    public static TheoryData<string[], string> UsageErrorCases => new()
    {
        // Unknown command
        { ["nonexistent-command"], "Unknown command" },
        // Unknown flag on root (dispatches to UnknownCommand catch-all)
        { ["--bogus-flag"], "Unknown command" },
    };

    [Theory]
    [MemberData(nameof(PassingCases))]
    public void KnownCommand_Exit0(string[] args, string contains)
    {
        var (stdout, stderr, exitCode) = TitiTestRunner.RunTiti(args, FixtureDir);
        Assert.True(exitCode == 0,
            $"Expected exit 0 for [{string.Join(", ", args)}] but got {exitCode}. Stderr: {stderr}");
        if (!string.IsNullOrEmpty(contains))
            Assert.True(
                stdout.Contains(contains, StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains(contains, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(UsageErrorCases))]
    public void UsageError_Exit2(string[] args, string contains)
    {
        var (stdout, stderr, exitCode) = TitiTestRunner.RunTiti(args, FixtureDir);
        Assert.Equal(2, exitCode);
        Assert.Contains(contains, stderr, StringComparison.OrdinalIgnoreCase);
    }
}
