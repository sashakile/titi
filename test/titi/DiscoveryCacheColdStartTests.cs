// Tests for titi-k2x.5: Cold adapter startup must not cache false empty discovery

namespace titi.Tests;

using titi.TestDiscovery;

public class DiscoveryCacheColdStartTests
{
    static TestItem MakeItem(string testId, string cls, string method) => new(
        TestId: testId,
        AssemblyPath: "asm.dll",
        ClassName: cls,
        MethodName: method,
        Framework: TestFramework.Xunit,
        Tier: TestTier.Unit,
        SourceFile: null,
        LastOutcome: TestOutcome.None,
        MeanDurationMs: 0,
        Tags: []
    );

    [Fact]
    public void ColdStart_EmptyDiscover_DoesNotWriteFingerprint()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "titi-test-cache-" + Guid.NewGuid());
        try
        {
            var result = DiscoveryCache.GetOrDiscover(
                cacheDir,
                "Test.Package",
                "abc123",
                () => []);

            Assert.Empty(result);

            // Cold start with empty result should NOT write fingerprint or items.json
            var itemDir = Path.Combine(cacheDir, "items", "Test.Package");
            Assert.False(Directory.Exists(itemDir),
                "Cold empty discovery must not create cache directory");
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void ColdStart_EmptyDiscover_DoesNotWriteItems()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "titi-test-cache-" + Guid.NewGuid());
        try
        {
            DiscoveryCache.GetOrDiscover(
                cacheDir,
                "Test.Package",
                "abc123",
                () => []);

            var itemsPath = Path.Combine(cacheDir, "items", "Test.Package", "items.json");
            Assert.False(File.Exists(itemsPath),
                "Cold empty discovery must not write items.json");
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void WarmCache_WithDiscoveryResults_StoresAndReturns()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "titi-test-cache-" + Guid.NewGuid());
        try
        {
            var result = DiscoveryCache.GetOrDiscover(
                cacheDir,
                "HasTests.Pkg",
                "def456",
                () => [MakeItem("FQN::M1", "NS.C", "M1")]);

            Assert.Single(result);
            Assert.Equal("NS.C", result[0].ClassName);

            // Should have written cache
            var itemDir = Path.Combine(cacheDir, "items", "HasTests.Pkg");
            Assert.True(Directory.Exists(itemDir));
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void WarmCache_WithExistingItems_ReadsWithoutCallback()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "titi-test-cache-" + Guid.NewGuid());
        try
        {
            // First call: store items
            DiscoveryCache.GetOrDiscover(
                cacheDir, "Cached.Pkg", "same",
                () => [MakeItem("FQN::M1", "NS.C", "M1")]);

            // Second call with same fingerprint: should read from cache, not invoke callback
            bool callbackInvoked = false;
            var result = DiscoveryCache.GetOrDiscover(cacheDir, "Cached.Pkg", "same", () =>
            {
                callbackInvoked = true;
                return [];
            });

            Assert.Single(result);
            Assert.False(callbackInvoked, "Callback should not be invoked on cache hit");
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void ColdStart_WithResults_StoresSuccessfully()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "titi-test-cache-" + Guid.NewGuid());
        try
        {
            // Even on cold start, when discover returns actual items, store is legitimate
            var result = DiscoveryCache.GetOrDiscover(
                cacheDir,
                "Real.Pkg",
                "ghi789",
                () => [
                    MakeItem("FQN::Foo", "NS.A", "Foo"),
                    MakeItem("FQN::Bar", "NS.A", "Bar"),
                ]);

            Assert.Equal(2, result.Length);

            // Cache should be written
            var itemDir = Path.Combine(cacheDir, "items", "Real.Pkg");
            Assert.True(Directory.Exists(itemDir));
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public void Cache_MissWithEmpty_LeavesPriorCacheIntact()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), "titi-test-cache-" + Guid.NewGuid());
        try
        {
            // First call: store real items
            DiscoveryCache.GetOrDiscover(
                cacheDir, "Mixed.Pkg", "v1",
                () => [MakeItem("FQN::M1", "NS.C", "M1")]);

            // Second call: cache miss (different fingerprint), callback returns empty
            // Should NOT overwrite prior cache
            var result = DiscoveryCache.GetOrDiscover(cacheDir, "Mixed.Pkg", "v2", () => []);

            Assert.Empty(result);

            // Prior cache should still be intact
            var prior = DiscoveryCache.Load(cacheDir, "Mixed.Pkg", "v1");
            Assert.NotNull(prior);
            Assert.Single(prior);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }
}
