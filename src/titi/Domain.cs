// Core domain model for titi
// Maps to Section 2 of architecture-schemas.md
// All types are public immutable value objects

namespace titi;

/// <summary>NuGet version range policy</summary>
public enum VersionPolicy { Strict, SemverCompatible, Force }

/// <summary>How a dependency edge is resolved</summary>
public enum ReferenceMode { Source, Binary, Suppressed }

/// <summary>Why a swap was retained in binary mode</summary>
public enum RetainedReason { NoLocalSource, VersionMismatch, TfmIncompatible, CyclePrevention }

/// <summary>Diagnostic error codes</summary>
public enum ErrorCode
{
    GraphBuildFailed = 1,
    CycleDetected = 2,
    VersionMismatch = 3,
    TfmIncompatible = 4,
    NoLocalSource = 5,
    CacheCorrupt = 6,
    MsbuildNotFound = 7,
    GitNotAvailable = 8,
    ConfigInvalid = 9,
}

// ── Domain Records ──────────────────────────────────────────────

public record SemanticVersion(int Major, int Minor, int Patch, string? Prerelease, string? Metadata);

public record Tfm(string Moniker, string Framework, double Version);

public record PackageRef(string PackageId, string VersionRange, string? PrivateAssets, string? ExcludeAssets);

public record ProjectRef(string Path, bool IsTransitive);

public record ProjectDescriptor(
    string Path,
    string PackageId,
    SemanticVersion Version,
    Tfm[] TargetFrameworks,
    bool IsPackable,
    bool IsTestProject,
    PackageRef[] PackageRefs,
    ProjectRef[] ProjectRefs,
    Dictionary<string, string> Properties
);

public record GraphEdge(string From, string To, ReferenceMode Mode, string? VersionRange, bool IsTransitive);

public record GraphNode(ProjectDescriptor Project, GraphEdge[] Dependencies, GraphEdge[] Dependents, int Depth);

public record MonorepoGraph(
    Dictionary<string, GraphNode> Nodes,
    string[] TopologicalOrder,
    string RepoRoot,
    DateTime BuiltAt,
    Dictionary<string, string> Fingerprints
);

public record CycleReport(string[] Cycle, GraphEdge[] EdgesToPreserve, string Diagnostic);

// ── Swap Engine Records ────────────────────────────────────────

public record SwapRequest(
    string[] Targets,
    VersionPolicy VersionPolicy,
    bool IncludeTransitive,
    bool Force
);

public record SwappedRef(string PackageId, string FromVersion, string LocalSourcePath, SemanticVersion LocalVersion, string[] Consumers);

public record RetainedRef(string PackageId, RetainedReason Reason, string Detail);

public record SwapResult(SwappedRef[] Swapped, RetainedRef[] Retained, CycleReport[] Cycles, MSBuildContext MsbuildContext);

public record MSBuildContext(
    string InTitiContext,
    string TitiPrefix,
    string TitiSourceRoot,
    Dictionary<string, string> AdditionalProps
);

// ── CLI Records ─────────────────────────────────────────────────

public record OpenCommandInput(string Target, VersionPolicy? VersionPolicy, bool? IncludeTransitive, bool? Force, bool? NoLaunch);

public record OpenCommandOutput(string SolutionPath, SwapResult SwapResult, int ProjectCount, bool LaunchedIde);

public record AffectedCommandInput(string? Base, string? Head, string? Format, bool? IncludeTests);

public record AffectedCommandOutput(AffectedSet Affected, string Format);

// ── Solution Records ────────────────────────────────────────────

public record SolutionProjectEntry(string Path, string ProjectGuid, string DisplayName, string? FolderPath);

public record SolutionFolder(string Name, string Guid, string? ParentFolder);

public record SolutionSpec(
    string Format,
    string OutputPath,
    SolutionProjectEntry[] Projects,
    SolutionFolder[] Folders,
    Dictionary<string, string> GlobalProperties
);

// ── Config Records ──────────────────────────────────────────────

public record CacheConfig(bool Enabled, string Directory, int MaxAge, string[] GlobalTriggers);

public record TestTierConfig(string[] Unit, string[] Package, string[] Integration, string[] Compatibility, string DefaultTier);

public record IdeConfig(string LaunchCommand, string[] Args, bool AutoOpen);

public record CiConfig(string[] FullRegressionBranches, int MaxParallelism, string OutputFormat);

public record TitiConfig(
    string Prefix,
    string[] SourceRoot,
    VersionPolicy VersionPolicy,
    CacheConfig Cache,
    TestTierConfig TestTiers,
    IdeConfig Ide,
    CiConfig Ci
)
{
    /// <summary>Test detection configuration (TID-6)</summary>
    public TestDetectionConfig TestDetection { get; init; } = new();

    /// <summary>
    /// Default well-known test-SDK package IDs used for test-project detection
    /// when IsTestProject is not explicitly set in the .csproj.
    /// </summary>
    public static readonly string[] DefaultTestSdkIds =
    [
        "xunit",
        "xunit.runner.visualstudio",
        "NUnit",
        "NUnit3TestAdapter",
        "MSTest.TestAdapter",
        "MSTest.TestFramework",
        "Microsoft.NET.Test.Sdk",
        "Microsoft.VisualStudio.TestPlatform.TestFramework",
        "Microsoft.VisualStudio.TestPlatform.TestFramework.Extensions",
        "Shouldly",
        "FluentAssertions",
        "Moq",
        "NSubstitute",
        "coverlet.collector",
        "coverlet.msbuild",
    ];

    /// <summary>
    /// Optional override of test-SDK package IDs for test-project detection.
    /// When null or empty, <see cref="DefaultTestSdkIds"/> is used.
    /// </summary>
    public string[]? TestSdkIds { get; init; }

    /// <summary>Resolved test-SDK IDs (user override or default).</summary>
    public string[] EffectiveTestSdkIds => TestSdkIds ?? DefaultTestSdkIds;
}

// ── TID-6: Test Detection Config ────────────────────────────────

public enum CoverageFormat { Cobertura, OpenCover }

public record TestDetectionConfig(
    bool Enabled,
    string VstestPath,
    bool CollectCoverage,
    CoverageFormat CoverageFormat,
    string CacheDir,
    double FallbackThreshold,
    int AlwaysRunEvictionThreshold,
    int BatchSize,
    string[] ExcludePatterns
)
{
    public static readonly TestDetectionConfig Default = new(
        Enabled: false,
        VstestPath: "dotnet",
        CollectCoverage: false,
        CoverageFormat: CoverageFormat.Cobertura,
        CacheDir: ".titi/test-cache",
        FallbackThreshold: 0.7,
        AlwaysRunEvictionThreshold: 5,
        BatchSize: 100,
        ExcludePatterns: []
    );

    // Parameterless constructor for init-only usage
    public TestDetectionConfig() : this(
        false, "dotnet", false, CoverageFormat.Cobertura,
        ".titi/test-cache", 0.7, 5, 100, []
    ) { }
}

// ── Error Record ────────────────────────────────────────────────

public record TitiError(ErrorCode Code, string Message, Dictionary<string, object> Context, string[] Suggestions);

// ── TID-1: Test-Item Domain Types ──────────────────────────────

/// <summary>Supported test frameworks</summary>
public enum TestFramework { Xunit, Nunit, Mstest }

/// <summary>Execution tiers for test projects</summary>
public enum TestTier { Unit, Package, Integration, Compatibility }

/// <summary>Last recorded outcome of a test item</summary>
public enum TestOutcome { None, Passed, Failed, Skipped, NotRun }

/// <summary>Origin of a test-to-source dependency edge</summary>
public enum EdgeOrigin { Static, Runtime, Manual }

/// <summary>Why a test is always selected to run</summary>
public enum AlwaysRunReason { LastRunFailed, NewlyAdded, NoHistory, MustRun, Quarantined }

/// <summary>Why selection fell back to project-level</summary>
public enum FallbackReason { ConfidenceBelowThreshold, UnresolvedFile, AdapterFailure, EnvironmentChange }

/// <summary>Status of a missed-selection incident</summary>
public enum IncidentStatus { Candidate, Promoted, Dismissed }

/// <summary>An individual test method identified for selection</summary>
public record TestItem(
    string TestId,
    string AssemblyPath,
    string ClassName,
    string MethodName,
    TestFramework Framework,
    TestTier Tier,
    string? SourceFile,
    TestOutcome LastOutcome,
    long MeanDurationMs,
    string[] Tags
);

/// <summary>A dependency edge from a test item to a source file</summary>
public record TestToSourceEdge(
    string From,
    string To,
    EdgeOrigin Origin,
    long Weight,
    (int Start, int End)[] LineRanges
);

/// <summary>A recorded incident where a test was missed by selection</summary>
public record MissedSelectionIncident(
    string ChangedContent,
    string MissedTestId,
    DateTime Timestamp,
    IncidentStatus Status
);

/// <summary>Result of selecting a test item for execution</summary>
public record TestSelectionResult(
    string TestId,
    bool Selected,
    (string Kind, string Description)[] Reasons,
    double Confidence,
    FallbackReason? FallbackReason
);

// ── Modified Records (with test-item support) ───────────────────

/// <summary>Tiered test set with both projects and individual test items</summary>
public record TieredTestSet(
    ProjectDescriptor[] Unit,
    ProjectDescriptor[] Package,
    ProjectDescriptor[] Integration,
    ProjectDescriptor[] Compatibility
)
{
    /// <summary>Per-tier test items (keyed by TestTier)</summary>
    public Dictionary<TestTier, TestItem[]> Items { get; init; } = new();
}

/// <summary>Affected set with optional per-test selection results</summary>
public record AffectedSet(
    string[] ChangedFiles,
    ProjectDescriptor[] DirectlyAffected,
    ProjectDescriptor[] TransitivelyAffected,
    TieredTestSet AffectedTests
)
{
    /// <summary>Per-test selection results (populated when test edges are available)</summary>
    public TestSelectionResult[] SelectedTests { get; init; } = [];

    /// <summary>Changed files that were successfully mapped to affected projects</summary>
    public string[] ResolvedFiles { get; init; } = [];
}

// ── Version Management Domain Types (VN-07) ────────────────────

/// <summary>Bump classification for API surface changes.</summary>
public enum BumpClassification { InternalOnly, Additive, Breaking }

/// <summary>Bump increment types for changeset files.</summary>
public enum BumpType { Patch, Minor, Major }

/// <summary>A changeset file describing an intentional version bump.</summary>
public record Changeset(
    string Package,
    BumpType Bump,
    string Description,
    string FilePath
);

/// <summary>Result of an API surface comparison between two assemblies.</summary>
public record ApiCompatResult(
    BumpClassification Classification,
    string[]? BreakingChanges,
    string? Diagnostic
);

/// <summary>Version plan entry for a single package.</summary>
public record VersionPlanEntry(
    string PackageId,
    string BaselineVersion,
    string NewVersion,
    BumpType AppliedBump,
    BumpClassification Classification,
    bool IsPropagated,
    string[]? Diagnostics
);

/// <summary>Complete version plan for a cascading bump run.</summary>
public record VersionPlan(
    VersionPlanEntry[] Entries,
    string[]? Issues,
    bool HasErrors
);

/// <summary>CPM detection result for a repository.</summary>
public record CpmConfig(
    bool Enabled,
    bool TransitivePinningEnabled,
    bool HasPackagesProps,
    string? PackagesPropsPath,
    string[]? PackageVersions,
    string[]? PackageVersionOverrides,
    string? Diagnostic,
    bool RestoreUseLegacyDependencyResolver
);

/// <summary>Default CPM config when no Directory.Packages.props exists.</summary>
public record CpmConfigDefaults
{
    public static readonly CpmConfig Instance = new(
        Enabled: false,
        TransitivePinningEnabled: false,
        HasPackagesProps: false,
        PackagesPropsPath: null,
        PackageVersions: null,
        PackageVersionOverrides: null,
        Diagnostic: "No Directory.Packages.props found — CPM not enabled",
        RestoreUseLegacyDependencyResolver: false
    );
}
