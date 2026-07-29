// Tests for TID-2.6: Test-item list caching in .titi/test-cache/items/

namespace titi.Tests;

using System.Text.Json;
using titi.TestDiscovery;

public class TestItemCacheTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "titi-cache-test-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private static readonly TestItem[] SampleItems =
    [
        new("Ns.Class.Test1", "/asm.dll", "Ns.Class", "Test1", TestFramework.Xunit, TestTier.Unit, "/src/test1.cs", TestOutcome.None, 0, []),
        new("Ns.Class.Test2", "/asm.dll", "Ns.Class", "Test2", TestFramework.Nunit, TestTier.Integration, "/src/test2.cs", TestOutcome.Passed, 120, ["smoke"]),
    ];

    private static readonly TestItem[] SampleItemsV2 =
    [
        new("Ns.Class.TestA", "/asm.dll", "Ns.Class", "TestA", TestFramework.Xunit, TestTier.Unit, null, TestOutcome.None, 0, []),
    ];

    // ── Fingerprint computation ──────────────────────────────────

    [Fact]
    public void ComputeFingerprint_SameFiles_ProducesSameHash()
    {
        var projDir = Path.Combine(_tempDir, "proj");
        Directory.CreateDirectory(projDir);
        var csproj = Path.Combine(projDir, "test.csproj");
        var source = Path.Combine(projDir, "Test1.cs");
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(source, "class Test1 { }");

        var fp1 = DiscoveryCache.ComputeFingerprint(projDir, csproj);
        var fp2 = DiscoveryCache.ComputeFingerprint(projDir, csproj);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputeFingerprint_DifferentSources_ProducesDifferentHash()
    {
        var projDir = Path.Combine(_tempDir, "proj");
        Directory.CreateDirectory(projDir);
        var csproj = Path.Combine(projDir, "test.csproj");
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        File.WriteAllText(Path.Combine(projDir, "Test1.cs"), "class Test1 { }");
        var fp1 = DiscoveryCache.ComputeFingerprint(projDir, csproj);

        File.WriteAllText(Path.Combine(projDir, "Test2.cs"), "class Test2 { }");
        var fp2 = DiscoveryCache.ComputeFingerprint(projDir, csproj);

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void ComputeFingerprint_CsprojChange_ProducesDifferentHash()
    {
        var projDir = Path.Combine(_tempDir, "proj");
        Directory.CreateDirectory(projDir);
        var csproj = Path.Combine(projDir, "test.csproj");
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(projDir, "Test1.cs"), "class Test1 { }");

        var fp1 = DiscoveryCache.ComputeFingerprint(projDir, csproj);

        // Change .csproj content
        File.WriteAllText(csproj, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup></Project>");
        var fp2 = DiscoveryCache.ComputeFingerprint(projDir, csproj);

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void ComputeFingerprint_MissingCsproj_DoesNotThrow()
    {
        var projDir = Path.Combine(_tempDir, "proj");
        Directory.CreateDirectory(projDir);
        var missing = Path.Combine(projDir, "nope.csproj");

        var fp = DiscoveryCache.ComputeFingerprint(projDir, missing);
        Assert.False(string.IsNullOrEmpty(fp));
    }

    // ── Cache load/store ─────────────────────────────────────────

    [Fact]
    public void Store_ThenLoad_ReturnsSameItems()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var fingerprint = "abc123";

        DiscoveryCache.Store(cacheDir, "test-proj", fingerprint, SampleItems);

        var loaded = DiscoveryCache.Load(cacheDir, "test-proj", fingerprint);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Length);
        Assert.Equal("Ns.Class.Test1", loaded[0].TestId);
        Assert.Equal("Ns.Class.Test2", loaded[1].TestId);
    }

    [Fact]
    public void Load_WrongFingerprint_ReturnsNull()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        DiscoveryCache.Store(cacheDir, "test-proj", "abc", SampleItems);

        var loaded = DiscoveryCache.Load(cacheDir, "test-proj", "xyz");
        Assert.Null(loaded);
    }

    [Fact]
    public void Load_NoCache_ReturnsNull()
    {
        var loaded = DiscoveryCache.Load("/nonexistent", "test-proj", "abc");
        Assert.Null(loaded);
    }

    [Fact]
    public void Load_CorruptItemsJson_ReturnsNull()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var fingerprint = "abc";
        DiscoveryCache.Store(cacheDir, "test-proj", fingerprint, SampleItems);

        // Corrupt the items file
        var itemsPath = Path.Combine(cacheDir, "items", "test-proj", "items.json");
        File.WriteAllText(itemsPath, "not json");

        var loaded = DiscoveryCache.Load(cacheDir, "test-proj", fingerprint);
        Assert.Null(loaded);
    }

    [Fact]
    public void Store_CreatesCacheDir()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        Assert.False(Directory.Exists(cacheDir));

        DiscoveryCache.Store(cacheDir, "test-proj", "abc", SampleItems);

        Assert.True(Directory.Exists(Path.Combine(cacheDir, "items", "test-proj")));
        Assert.True(File.Exists(Path.Combine(cacheDir, "items", "test-proj", "items.json")));
        Assert.True(File.Exists(Path.Combine(cacheDir, "items", "test-proj", "fingerprint")));
    }

    [Fact]
    public void Load_ValidCache_ItemsSerializedAsEdnFormat()
    {
        // Verify the stored format matches what SelectionLoader expects
        var cacheDir = Path.Combine(_tempDir, "cache");
        DiscoveryCache.Store(cacheDir, "test-proj", "abc", SampleItems);

        var itemsJson = File.ReadAllText(Path.Combine(cacheDir, "items", "test-proj", "items.json"));
        var parsed = JsonSerializer.Deserialize<JsonElement>(itemsJson);
        Assert.Equal(JsonValueKind.Array, parsed.ValueKind);
        Assert.Equal(2, parsed.GetArrayLength());
    }

    [Fact]
    public void Store_ThenStore_Overwrites()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");

        DiscoveryCache.Store(cacheDir, "test-proj", "abc", SampleItems);
        DiscoveryCache.Store(cacheDir, "test-proj", "def", SampleItemsV2);

        // Old fingerprint should not load
        Assert.Null(DiscoveryCache.Load(cacheDir, "test-proj", "abc"));

        // New fingerprint should load new items
        var loaded = DiscoveryCache.Load(cacheDir, "test-proj", "def");
        Assert.NotNull(loaded);
        Assert.Single(loaded);
        Assert.Equal("Ns.Class.TestA", loaded[0].TestId);
    }

    [Fact]
    public void Load_DifferentProject_DoesNotCrossContaminate()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");

        DiscoveryCache.Store(cacheDir, "proj-a", "fp-a", SampleItems);
        DiscoveryCache.Store(cacheDir, "proj-b", "fp-b", SampleItemsV2);

        var loadedA = DiscoveryCache.Load(cacheDir, "proj-a", "fp-a");
        var loadedB = DiscoveryCache.Load(cacheDir, "proj-b", "fp-b");

        Assert.Equal(2, loadedA!.Length);
        Assert.Single(loadedB!);
    }

    // ── GetOrDiscover integration ────────────────────────────────

    [Fact]
    public void GetOrDiscover_WithCache_ReturnsCached()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var fingerprint = "abc";
        DiscoveryCache.Store(cacheDir, "test-proj", fingerprint, SampleItems);

        var invoked = false;
        var result = DiscoveryCache.GetOrDiscover(
            cacheDir, "test-proj", fingerprint,
            () => { invoked = true; return SampleItemsV2; });

        Assert.False(invoked, "should not invoke discovery when cache is fresh");
        Assert.Equal(2, result.Length);
        Assert.Equal("Ns.Class.Test1", result[0].TestId);
    }

    [Fact]
    public void GetOrDiscover_NoCache_InvokesDiscovery()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var fingerprint = "abc";

        var invoked = false;
        var result = DiscoveryCache.GetOrDiscover(
            cacheDir, "test-proj", fingerprint,
            () => { invoked = true; return SampleItems; });

        Assert.True(invoked, "should invoke discovery when cache is missing");
        Assert.Equal(2, result.Length);

        // Should also have cached the result
        var loaded = DiscoveryCache.Load(cacheDir, "test-proj", fingerprint);
        Assert.NotNull(loaded);
    }

    [Fact]
    public void GetOrDiscover_WrongFingerprint_InvokesDiscovery()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        DiscoveryCache.Store(cacheDir, "test-proj", "old-fp", SampleItems);

        var invoked = false;
        var result = DiscoveryCache.GetOrDiscover(
            cacheDir, "test-proj", "new-fp",
            () => { invoked = true; return SampleItemsV2; });

        Assert.True(invoked);
        Assert.Single(result);
        Assert.Equal("Ns.Class.TestA", result[0].TestId);

        // New fingerprint should be cached
        var loaded = DiscoveryCache.Load(cacheDir, "test-proj", "new-fp");
        Assert.NotNull(loaded);
        Assert.Single(loaded);
    }

    [Fact]
    public void GetOrDiscover_EmptyDiscovery_DoesNotCache()
    {
        var cacheDir = Path.Combine(_tempDir, "cache");
        var fingerprint = "abc";

        var result = DiscoveryCache.GetOrDiscover(cacheDir, "test-proj", fingerprint, () => []);

        Assert.Empty(result);

        // Cold empty discovery must not be cached — storing empty would poison
        // the cache and suppress future real discovery until the fingerprint changes.
        var loaded = DiscoveryCache.Load(cacheDir, "test-proj", fingerprint);
        Assert.Null(loaded);
    }
}
