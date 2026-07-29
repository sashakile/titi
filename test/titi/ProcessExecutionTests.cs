// Tests for subprocess execution (titi-k2x.3): timeout enforcement and
// deadlock-safe concurrent stream draining.

namespace titi.Tests;

using titi.Core;

public class ProcessExecutionTests
{
    private const string TestDir = "/tmp";

    [Fact]
    public void RunDotnet_TimedOutProcess_ReturnsTimeout()
    {
        // RED: Run a process that exceeds the timeout. The current
        // implementation ignores WaitForExit's return value and still
        // accesses ExitCode on a potentially running child.
        // Use a 1-second timeout with a process that sleeps 5 seconds.
        var result = Program.RunProcess(
            fileName: "bash",
            arguments: "-c \"echo start; sleep 5; echo done\"",
            workingDir: TestDir,
            timeoutMs: 1000);

        // Should timeout and return error, but currently might hang
        // or return Ok with ExitCode if the process finishes.
        Assert.False(result.Ok, "Process should time out");
        Assert.Contains("timed out", result.Stderr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunProcess_LargeConcurrentOutput_DoesNotDeadlock()
    {
        // RED: A child that writes 5000 lines concurrently to both stdout
        // and stderr can deadlock with synchronous stream reads.
        var result = Program.RunProcess(
            fileName: "bash",
            arguments: "-c \"for i in $(seq 1 5000); do echo s_$i; echo e_$i >&2; done; echo DONE\"",
            workingDir: TestDir,
            timeoutMs: 30_000);

        // Should complete without deadlock.
        Assert.True(result.Ok);
        Assert.Contains("DONE", result.Stdout ?? "");
    }

    [Fact]
    public void RunProcess_TimeoutExceeded_ProcessKilled()
    {
        // RED: After timeout, the child process should be terminated.
        var result = Program.RunProcess(
            fileName: "bash",
            arguments: "-c \"echo start; sleep 10; echo done\"",
            workingDir: TestDir,
            timeoutMs: 500);

        Assert.False(result.Ok);
        // Process should not still be running after timeout.
        Assert.DoesNotContain("done", result.Stdout ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunProcess_NormalCompletion_ReturnsOutput()
    {
        var result = Program.RunProcess(
            fileName: "echo",
            arguments: "hello world",
            workingDir: TestDir,
            timeoutMs: 5000);

        Assert.True(result.Ok);
        Assert.Contains("hello world", result.Stdout ?? "");
    }
}
