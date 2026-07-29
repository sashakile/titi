// TIT-1: Shared helper for integration tests — run titi CLI binary directly
// Uses the pre-built binary (not `dotnet run`) to avoid build-warning fragility.

namespace titi.Tests;

using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// Helper methods for running the titi CLI binary in integration tests.
/// Resolves the built binary path from the project output directory and
/// validates that stdout starts with valid JSON before deserializing.
/// </summary>
public static class TitiTestRunner
{
    /// <summary>Path to the built titi executable.</summary>
    public static readonly string TitiBinaryPath;

    static TitiTestRunner()
    {
        var baseDir = AppContext.BaseDirectory;
        // Navigate from test output to src/titi/bin/$(Configuration)/net10.0/
        var srcDir = Path.GetFullPath(Path.Combine(baseDir, "../../../../../src/titi"));
        var configuration = IsReleaseBuild(baseDir) ? "Release" : "Debug";
        TitiBinaryPath = Path.GetFullPath(Path.Combine(srcDir, "bin", configuration, "net10.0", "titi"));
    }

    static bool IsReleaseBuild(string baseDir)
    {
        // Heuristic: Release builds deploy to a Release directory
        return baseDir.Contains("Release", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Run an arbitrary dotnet command and return stdout, stderr, and exit code.
    /// </summary>
    public static (string Stdout, string Stderr, int ExitCode) RunDotnet(
        string args, string workingDirectory, int timeoutMs = 60_000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(startInfo);
        if (proc == null) return ("", "", -1);

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(timeoutMs);
        return (stdout, stderr, proc.ExitCode);
    }

    /// <summary>
    /// Run titi with the given arguments and return stdout, stderr, and exit code.
    /// Uses the pre-built binary directly (no dotnet run) to avoid build warnings
    /// polluting stdout.
    /// </summary>
    public static (string Stdout, string Stderr, int ExitCode) RunTiti(
        string[] args, string workingDirectory, int timeoutMs = 30_000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = TitiBinaryPath,
            Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var proc = Process.Start(startInfo);
        if (proc == null)
            return ("", "", -1);

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(timeoutMs);

        return (stdout, stderr, proc.ExitCode);
    }

    /// <summary>
    /// Parse stdout as JSON, throwing a descriptive error if stdout doesn't
    /// start with a valid JSON token (e.g., it contains build warnings).
    /// </summary>
    public static JsonDocument ParseJsonStdout(string stdout)
    {
        var trimmed = stdout.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            // Show first line of non-JSON output to help debug build warnings
            var firstLine = trimmed.Length > 0
                ? trimmed[..Math.Min(trimmed.Length, 200)].ToString()
                : "(empty output)";
            throw new FormatException(
                $"Expected JSON output but got: {firstLine}");
        }

        return JsonDocument.Parse(stdout);
    }

    /// <summary>
    /// Run titi and parse its JSON stdout in one call. Throws on non-JSON
    /// output or non-zero exit code.
    /// </summary>
    public static (JsonDocument Output, string Stderr) RunTitiAndParseJson(
        string[] args, string workingDirectory, int timeoutMs = 30_000)
    {
        var (stdout, stderr, exitCode) = RunTiti(args, workingDirectory, timeoutMs);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"titi exited with code {exitCode}: {stderr}");
        }

        return (ParseJsonStdout(stdout), stderr);
    }
}
