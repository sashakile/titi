// Tests for titi.solution — .slnx generation

namespace titi.Tests;

using titi.Solution;

public class SolutionTests
{
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

    [Fact]
    public void Generate_WithSwappedProject_CreatesSwapTargets()
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

            var targetsPath = Path.Combine(tmpDir, "swap", "Swap.targets");
            Assert.True(File.Exists(targetsPath));

            var content = File.ReadAllText(targetsPath);
            Assert.Contains("<Project>", content);
            Assert.Contains("ItemGroup Condition", content);
            Assert.Contains("$(InTitiContext)", content);
            Assert.Contains("PackageReference Remove", content);
            Assert.Contains("Orion.Core.Data", content);
            Assert.Contains("ProjectReference Include", content);
            Assert.Contains("/repo/src/Orion.Core.Data/Orion.Core.Data.csproj", content);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_WithSwappedProject_SlnxIncludesCustomAfterMicrosoftCommonTargets()
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
            var content = File.ReadAllText(slnxPath);
            Assert.Contains("CustomAfterMicrosoftCommonTargets", content);
            var expectedTargetsPath = Path.GetFullPath(Path.Combine(tmpDir, "swap", "Swap.targets"));
            Assert.Contains(expectedTargetsPath, content);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_WithNoSwaps_DoesNotCreateSwapTargets()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);

            var result = new SwapResult(
                Swapped: [],
                Retained: [
                    new RetainedRef("Orion.External.Foo", RetainedReason.NoLocalSource, "Not found")
                ],
                Cycles: [],
                MsbuildContext: new MSBuildContext("true", "Orion.", "./", [])
            );

            var (slnxPath, err) = SolutionGenerator.Generate(result, tmpDir, "test-package");

            Assert.Null(err);
            var targetsPath = Path.Combine(tmpDir, "swap", "Swap.targets");
            Assert.False(File.Exists(targetsPath), "No swap targets file when no swaps occur");

            // .slnx should still reference the targets path (no-op when file missing)
            var content = File.ReadAllText(slnxPath);
            Assert.Contains("CustomAfterMicrosoftCommonTargets", content);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void Generate_WithMultipleSwaps_IncludesAllInTargets()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "titi-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(tmpDir);

            var result = new SwapResult(
                Swapped: [
                    new SwappedRef("Orion.Core.Data", "1.0.0",
                        "/repo/src/Orion.Core.Data/Orion.Core.Data.csproj",
                        new SemanticVersion(1, 0, 0, null, null), ["/repo/src/Orion.App/Orion.App.csproj"]),
                    new SwappedRef("Orion.Storage", "2.0.0",
                        "/repo/libs/Orion.Storage/Orion.Storage.csproj",
                        new SemanticVersion(2, 0, 0, null, null), ["/repo/src/Orion.App/Orion.App.csproj"])
                ],
                Retained: [],
                Cycles: [],
                MsbuildContext: new MSBuildContext("true", "Orion.", "./", [])
            );

            var (slnxPath, err) = SolutionGenerator.Generate(result, tmpDir, "Orion.Core.Data");

            Assert.Null(err);

            var targetsPath = Path.Combine(tmpDir, "swap", "Swap.targets");
            var content = File.ReadAllText(targetsPath);

            Assert.Contains("Orion.Core.Data", content);
            Assert.Contains("Orion.Storage", content);
            Assert.Contains("/repo/src/Orion.Core.Data/Orion.Core.Data.csproj", content);
            Assert.Contains("/repo/libs/Orion.Storage/Orion.Storage.csproj", content);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }
}