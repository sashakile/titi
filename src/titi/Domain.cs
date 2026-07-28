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

public record AffectedSet(
    string[] ChangedFiles,
    ProjectDescriptor[] DirectlyAffected,
    ProjectDescriptor[] TransitivelyAffected,
    TieredTestSet AffectedTests
);

public record TieredTestSet(
    ProjectDescriptor[] Unit,
    ProjectDescriptor[] Package,
    ProjectDescriptor[] Integration,
    ProjectDescriptor[] Compatibility
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
    string SourceRoot,
    VersionPolicy VersionPolicy,
    CacheConfig Cache,
    TestTierConfig TestTiers,
    IdeConfig Ide,
    CiConfig Ci
);

// ── Error Record ────────────────────────────────────────────────

public record TitiError(ErrorCode Code, string Message, Dictionary<string, object> Context, string[] Suggestions);
