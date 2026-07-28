// TID-2.6: Cache test-item lists in .titi/test-cache/items/
// Invalidation based on content fingerprint of .csproj + source files (.cs).
// Follows the same fingerprint pattern as ComputeSourceFingerprint in Core.cs.

namespace titi.TestDiscovery;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using titi.Serialization;

public static class DiscoveryCache
{
    /// <summary>
    /// Get cached test items for a project. If a valid cache entry exists
    /// matching <paramref name="fingerprint"/>, loads from cache. Otherwise
    /// invokes <paramref name="discover"/> to run discovery, stores the
    /// result, and returns it.
    /// </summary>
    public static TestItem[] GetOrDiscover(
        string cacheDir,
        string packageId,
        string fingerprint,
        Func<TestItem[]> discover)
    {
        var cached = Load(cacheDir, packageId, fingerprint);
        if (cached != null)
            return cached;

        var items = discover();
        Store(cacheDir, packageId, fingerprint, items);
        return items;
    }

    /// <summary>
    /// Compute a content-based fingerprint for a test project directory.
    /// Includes the .csproj file content hash and all .cs files in the
    /// project directory (recursively). Missing files are hashed as
    /// "missing:{path}" so a deletion also invalidates the cache.
    /// </summary>
    public static string ComputeFingerprint(string projectDir, string projectPath)
    {
        using var sha = SHA256.Create();
        var hashes = new List<string>();

        // Hash the .csproj itself
        hashes.Add(HashFile(sha, projectPath));

        // Hash all .cs files in the project directory (recursive)
        if (Directory.Exists(projectDir))
        {
            foreach (var src in Directory.EnumerateFiles(projectDir, "*.cs", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.Ordinal))
            {
                hashes.Add(HashFile(sha, src));
            }
        }

        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', hashes));
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    /// <summary>
    /// Load cached test items for a package id, validated against a
    /// fingerprint. Returns null when the cache is missing, stale, or
    /// corrupt — the caller should run fresh discovery.
    /// </summary>
    public static TestItem[]? Load(string cacheDir, string packageId, string fingerprint)
    {
        if (string.IsNullOrEmpty(cacheDir) || string.IsNullOrEmpty(packageId))
            return null;

        var itemDir = Path.Combine(cacheDir, "items", Sanitize(packageId));
        var fingerprintPath = Path.Combine(itemDir, "fingerprint");
        var itemsPath = Path.Combine(itemDir, "items.json");

        if (!File.Exists(fingerprintPath) || !File.Exists(itemsPath))
            return null;

        try
        {
            var storedFingerprint = File.ReadAllText(fingerprintPath).Trim();
            if (storedFingerprint != fingerprint)
                return null;

            var json = File.ReadAllText(itemsPath);
            return JsonSerializer.Deserialize(json, TitiJsonContext.Default.TestItemArray);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Store test items in the cache under a package id and fingerprint.
    /// Creates cache directories as needed.
    /// </summary>
    public static void Store(string cacheDir, string packageId, string fingerprint, TestItem[] items)
    {
        if (string.IsNullOrEmpty(cacheDir) || string.IsNullOrEmpty(packageId))
            return;

        var itemDir = Path.Combine(cacheDir, "items", Sanitize(packageId));
        Directory.CreateDirectory(itemDir);

        var fingerprintPath = Path.Combine(itemDir, "fingerprint");
        var itemsPath = Path.Combine(itemDir, "items.json");

        File.WriteAllText(itemsPath, JsonSerializer.Serialize(items, TitiJsonContext.Default.TestItemArray));
        File.WriteAllText(fingerprintPath, fingerprint);
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static string HashFile(SHA256 sha, string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(fs));
        }
        catch
        {
            return $"missing:{path}";
        }
    }

    /// <summary>
    /// Sanitize a package id for use as a directory name. Package ids
    /// use dots (e.g. "Orion.UnitTests") which are valid in file paths
    /// on all target platforms, but we also handle edge cases like
    /// slashes or null chars.
    /// </summary>
    private static string Sanitize(string s)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(s.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrEmpty(sanitized) ? "_" : sanitized;
    }
}
