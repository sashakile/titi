// Pack validation — dotnet pack + dotnet tool install + smoke test

namespace titi.Tests;

using System.Diagnostics;

public class PackValidationTests
{
    static readonly string SrcDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../src/titi"));

    static readonly string DistDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../../dist/nupkg"));

    static (string Stdout, string Stderr, int ExitCode) RunDotnet(
        string args, string workingDir, int timeoutMs = 60_000)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = args,
            WorkingDirectory = workingDir,
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
    /// Pack the project, install the tool in a clean temp environment,
    /// and run titi --help to verify the tool is functional.
    /// </summary>
    [Fact]
    public void Pack_InstallAndRunHelp_Success()
    {
        // Arrange: clean up any stale nupkgs
        if (Directory.Exists(DistDir))
            Directory.Delete(DistDir, recursive: true);
        Directory.CreateDirectory(DistDir);

        // Pack
        var (packOut, packErr, packExit) = RunDotnet(
            $"pack \"{SrcDir}\" --configuration Release -o \"{DistDir}\"",
            SrcDir, 120_000);

        Assert.True(packExit == 0,
            $"dotnet pack failed (exit {packExit}): {packErr}");

        // Find the .nupkg
        var nupkgFiles = Directory.GetFiles(DistDir, "*.nupkg");
        Assert.NotEmpty(nupkgFiles);
        var nupkg = nupkgFiles[0];
        Assert.True(File.Exists(nupkg), $"No .nupkg found in {DistDir}");

        // Create a clean temp environment
        var tempDir = Path.Combine(Path.GetTempPath(), "titi-pack-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tempDir);

            // Install the tool from the generated package
            var (installOut, installErr, installExit) = RunDotnet(
                $"tool install titi --add-source \"{DistDir}\" --tool-path \"{tempDir}\"",
                tempDir, 60_000);

            Assert.True(installExit == 0,
                $"dotnet tool install failed (exit {installExit}): {installErr}");

            // Verify the tool binary exists
            var toolExe = Path.Combine(tempDir, "titi");
            Assert.True(File.Exists(toolExe),
                $"titi binary not found at {toolExe}");

            // Run titi --help from the clean environment
            var (helpOut, helpErr, helpExit) = TitiTestRunner.RunTiti(
                ["--help"], tempDir, 30_000);

            Assert.True(helpExit == 0,
                $"titi --help failed (exit {helpExit}): {helpErr}");
            Assert.Contains("titi", helpOut, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
