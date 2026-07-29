// TID-3b: Plan test runs + incremental recording helpers.
// Pure projection from the project set to per-test-project run plans. Each
// test project gets a unique results directory (so concurrent/sequential runs
// don't clobber each other) and the dotnet-test argument string that enables
// both the TRX logger and XPlat Cobertura coverage collection.
//
// TID-11: Per-project fingerprinting for incremental edge updates (3.6).
// Tracks source-fingerprints per project so TestsRecordCommand only re-runs
// projects whose source files actually changed.

namespace titi;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using titi.Serialization;

public record TestRunPlan(string ProjectPath, string PackageId, string ResultsDir, string Arguments);

public static class RecordPlanner
{
    // ── Test run planning ─────────────────────────────────────────

    /// <summary>
    /// Build a run plan for every test project (IsTestProject = true) in
    /// <paramref name="projects"/>. Non-test projects are skipped. Each plan
    /// targets a unique results directory under <paramref name="resultsRoot"/>.
    /// </summary>
    public static TestRunPlan[] PlanTestRuns(IEnumerable<ProjectDescriptor> projects, string resultsRoot)
    {
        var testProjects = projects.Where(p => p.IsTestProject).ToArray();
        if (testProjects.Length == 0)
            return [];

        var plans = new List<TestRunPlan>(testProjects.Length);
        foreach (var p in testProjects)
        {
            var resultsDir = Path.Combine(resultsRoot, Guid.NewGuid().ToString("N"));
            var args = $"test \"{p.Path}\" --collect \"XPlat Code Coverage\" --logger trx --results-directory \"{resultsDir}\"";
            plans.Add(new TestRunPlan(p.Path, p.PackageId, resultsDir, args));
        }
        return plans.ToArray();
    }

    // ── Incremental recording helpers (TID-11 / 3.6) ──────────────

    /// <summary>
    /// Compute which test projects have changed since the last recording.
    /// Compares <paramref name="priorFingerprints"/> against the current
    /// source fingerprint for each project. Projects not in the prior set
    /// (newly added) are included. Non-test projects are ignored.
    /// </summary>
    public static ProjectDescriptor[] ComputeChangedProjects(
        Dictionary<string, string> priorFingerprints,
        IEnumerable<ProjectDescriptor> projects,
        Func<ProjectDescriptor, string>? computeFingerprint = null)
    {
        computeFingerprint ??= ComputeDefaultFingerprint;

        return projects
            .Where(p => p.IsTestProject)
            .Where(p =>
            {
                var currentFp = computeFingerprint(p);
                return !priorFingerprints.TryGetValue(p.PackageId, out var priorFp)
                    || priorFp != currentFp;
            })
            .ToArray();
    }

    /// <summary>
    /// Load per-project fingerprints from <c>edges/project-fingerprints.json</c>.
    /// Returns empty dict if the file doesn't exist or is corrupt.
    /// </summary>
    public static Dictionary<string, string> LoadProjectFingerprints(string cacheDir)
    {
        var path = ProjectFingerprintsPath(cacheDir);
        if (!File.Exists(path))
            return [];

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, TitiJsonContext.Default.DictionaryStringString) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Save per-project fingerprints to <c>edges/project-fingerprints.json</c>.
    /// Creates the edges directory if it doesn't exist.
    /// </summary>
    public static void SaveProjectFingerprints(string cacheDir, Dictionary<string, string> fingerprints)
    {
        var path = ProjectFingerprintsPath(cacheDir);
        var dir = Path.GetDirectoryName(path);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(fingerprints, TitiJsonContext.Default.DictionaryStringString));
    }

    /// <summary>
    /// Clean up fingerprints for projects that are no longer in the graph.
    /// Returns the pruned dictionary.
    /// </summary>
    public static Dictionary<string, string> PruneStaleFingerprints(
        Dictionary<string, string> fingerprints,
        IEnumerable<ProjectDescriptor> currentProjects)
    {
        var currentIds = currentProjects
            .Where(p => p.IsTestProject)
            .Select(p => p.PackageId)
            .ToHashSet();

        var pruned = new Dictionary<string, string>();
        foreach (var kv in fingerprints)
        {
            if (currentIds.Contains(kv.Key))
                pruned[kv.Key] = kv.Value;
        }
        return pruned;
    }

    /// <summary>
    /// Derive the per-project edge file path from a stable package-ID key.
    /// Used by write, read, and cleanup paths so they stay consistent.
    /// </summary>
    public static string EdgeFilePath(string projectsDir, string packageId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var key = new string(packageId.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return Path.Combine(projectsDir, $"{key}.edn");
    }

    /// <summary>
    /// Derive the stable filename key for a package ID.
    /// </summary>
    public static string EdgeFileKey(string packageId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(packageId.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static string ProjectFingerprintsPath(string cacheDir) =>
        Path.Combine(cacheDir, "edges", "project-fingerprints.json");

    /// <summary>Default fingerprint computation: SHA256 over .csproj + .cs files.</summary>
    private static string ComputeDefaultFingerprint(ProjectDescriptor project)
    {
        var projDir = Path.GetDirectoryName(project.Path) ?? "";
        return TestDiscovery.DiscoveryCache.ComputeFingerprint(projDir, project.Path);
    }
}
