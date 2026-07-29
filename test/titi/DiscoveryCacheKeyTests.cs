// Tests for titi-k2x.4: bounded collision-resistant discovery-cache keys

namespace titi.Tests;

using titi.TestDiscovery;

public class DiscoveryCacheKeyTests
{
    static TestItem MakeItem(string testId, string cls) => new(
        TestId: testId,
        AssemblyPath: "asm.dll",
        ClassName: cls,
        MethodName: "M",
        Framework: TestFramework.Xunit,
        Tier: TestTier.Unit,
        SourceFile: null,
        LastOutcome: TestOutcome.None,
        MeanDurationMs: 0,
        Tags: []);

    static string TempDir() =>
        Path.Combine(Path.GetTempPath(), "titi-key-cache-" + Guid.NewGuid());

    [Fact]
    public void Keys_NeverEscapeItemsDirectory()
    {
        var cacheDir = TempDir();
        try
        {
            DiscoveryCache.Store(cacheDir, "..", "fp", [MakeItem("a", "C1")]);

            var itemsRoot = Path.Combine(cacheDir, "items");
            Assert.True(Directory.Exists(itemsRoot),
                "items/ directory must exist after Store");

            var fullItemsRoot = Path.GetFullPath(itemsRoot);
            // Every directory entry directly under items/ must be a real descendant.
            foreach (var dir in Directory.EnumerateDirectories(itemsRoot))
            {
                var full = Path.GetFullPath(dir);
                Assert.StartsWith(
                    fullItemsRoot + Path.DirectorySeparatorChar, full,
                    StringComparison.Ordinal);
            }

            // The cache for ".." must load back independently.
            var loaded = DiscoveryCache.Load(cacheDir, "..", "fp");
            Assert.NotNull(loaded);
            Assert.Equal("C1", loaded![0].ClassName);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void Keys_DistinctPackageIds_DoNotCollide()
    {
        var cacheDir = TempDir();
        try
        {
            DiscoveryCache.Store(cacheDir, "a/b", "fp", [MakeItem("a1", "Slash")]);
            DiscoveryCache.Store(cacheDir, "a_b", "fp", [MakeItem("a2", "Underscore")]);

            var slash = DiscoveryCache.Load(cacheDir, "a/b", "fp");
            var under = DiscoveryCache.Load(cacheDir, "a_b", "fp");

            Assert.NotNull(slash);
            Assert.NotNull(under);
            Assert.Equal("Slash", slash![0].ClassName);
            Assert.Equal("Underscore", under![0].ClassName);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void Keys_AreBoundedLength_ForLongPackageIds()
    {
        var cacheDir = TempDir();
        try
        {
            var longId = new string('x', 5000);
            DiscoveryCache.Store(cacheDir, longId, "fp", [MakeItem("a", "C1")]);

            var itemsRoot = Path.Combine(cacheDir, "items");
            var dirs = Directory.EnumerateDirectories(itemsRoot).ToArray();
            Assert.Single(dirs);
            var name = Path.GetFileName(dirs[0]);
            Assert.True(name.Length <= 128,
                $"key length {name.Length} must be bounded (<=128) for long package ids");
            Assert.False(name.Contains(Path.DirectorySeparatorChar),
                "key must not contain path separators");
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

}
