// Tests for titi.solution — .slnx generation

namespace titi.Tests;

using titi.Solution;

public class SolutionTests
{
    /// <summary>Generate solution from SwapResult with one swapped project.</summary>
    [Fact]
    public void Generate_WithSwappedProject_CreatesSlnxFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);

            var result = new SwapResult(
                Swapped: [
                    new SwappedRef("Orion.Core.Data", "1.0.0",
                        "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj",
                        new SemanticVersion(1, 0, 0, null, null), ["/repo/src/Orion.App/Orion.App.csproj"])
                ],
                Retained: [],
                Cycles: [],
                MsbuildContext: new MSBuildContext("true", "Orion.", "./", [])
            );

            var (slnxPath, err) = SolutionGenerator.Generate(result, tmpDir, "Orion.Core.Data");

            Assert.Null(err);
            Assert.NotNull(slnxPath);
            Assert.True(File.Exists(slnxPath));

            var content = File.ReadAllText(slnxPath);
            Assert.Contains("InTitiContext", content);
            Assert.Contains("true", content);
            Assert.Contains("Orion.Core.Data", content);
            Assert.Contains("Swapped", content);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    /// <summary>Generate solution with retained projects includes Retained folder.</summary>
    [Fact]
    public void Generate_WithRetainedProjects_IncludesRetainedFolder()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);

            var result = new SwapResult(
                Swapped: [],
                Retained: [
                    new RetainedRef("Orion.External.Foo", RetainedReason.NoLocalSource, "No local .csproj found")
                ],
                Cycles: [],
                MsbuildContext: new MSBuildContext("true", "Orion.", "./", [])
            );

            var (slnxPath, err) = SolutionGenerator.Generate(result, tmpDir, "test-package");

            Assert.Null(err);
            var content = File.ReadAllText(slnxPath);
            Assert.Contains("Retained", content);
            Assert.Contains("NoLocalSource", content);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    /// <summary>Atomic write: .slnx.tmp is not left behind.</summary>
    [Fact]
    public void Generate_AtomicWrite_NoTmpFileLeft()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);

            var result = new SwapResult(
                Swapped: [],
                Retained: [],
                Cycles: [],
                MsbuildContext: new MSBuildContext("true", "", "./", [])
            );

            var (slnxPath, _) = SolutionGenerator.Generate(result, tmpDir, "empty-test");
            var tmpPath = slnxPath + ".tmp";

            Assert.True(File.Exists(slnxPath));
            Assert.False(File.Exists(tmpPath), "Temporary file should be deleted after atomic write");
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}
