// Source-generated JsonSerializerContext for AOT-compatible serialization.
// All types used with JsonSerializer.Serialize/Deserialize across the codebase
// must be registered here. Anonymous types are replaced with named DTOs.
//
// Note: Adapter.cs uses reflection-based serialization (anonymous types) since
// the adapter subprocess does not need AOT compilation. Only CLI command
// serialization (TestCli, Core, DiscoveryCache, RecordPlanner, SelectionLoader)
// uses this context.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace titi.Serialization;

// ── Core formatter DTOs (TestCli.cs) ───────────────────────────

/// <summary>DTO for FormatTestItems output.</summary>
public record TestItemEntry(
    string TestId,
    string ClassName,
    string MethodName,
    string Framework,
    string Tier,
    string? SourceFile
);

/// <summary>Outer wrapper for FormatTestItems: { tests: [...] }.</summary>
public record TestItemList(TestItemEntry[] Tests);

/// <summary>DTO for a single reason in AffectedSet output.</summary>
public record SelectionReasonEntry(string Kind, string Description);

/// <summary>DTO for a single test selection result.</summary>
public record SelectedTestEntry(
    string TestId,
    bool Selected,
    SelectionReasonEntry[] Reasons,
    double Confidence,
    string? FallbackReason
);

/// <summary>DTO for a project reference in AffectedSet output.</summary>
public record ProjectRefEntry(string PackageId, string Path);

/// <summary>DTO for FormatAffectedUpgrade output.</summary>
public record AffectedSetOutput(
    string[] ChangedFiles,
    ProjectRefEntry[] DirectlyAffected,
    ProjectRefEntry[] TransitivelyAffected,
    int TotalAffected,
    SelectedTestEntry[] SelectedTests,
    double Confidence
);

// ── Core command DTOs (Core.cs) ────────────────────────────────

/// <summary>DTO for a swapped project in OpenCommand output.</summary>
public record SwappedEntry(string PackageId, string LocalSourcePath);

/// <summary>DTO for a retained project in OpenCommand output.</summary>
public record RetainedEntry(string PackageId, int Reason, string Detail);

/// <summary>DTO for OpenCommand output.</summary>
public record OpenCommandOutput(
    string SolutionPath,
    SwappedEntry[] Swapped,
    RetainedEntry[] Retained,
    int ProjectCount
);

/// <summary>DTO for edge serialization (replaces anonymous new { e.From, e.To, ... }).</summary>
public record EdgeEntry(
    string From,
    string To,
    int? Origin,
    long Weight,
    LineRangeEntry[]? LineRanges
);

/// <summary>DTO for a line range within an edge.</summary>
public record LineRangeEntry(int Start, int End);

// ── Version command DTOs (Core.cs/Versioning.cs) ────────────────

/// <summary>DTO for a single project version entry.</summary>
public record ProjectVersionEntry(
    string PackageId,
    string? CurrentVersion,
    bool IsManaged
);

/// <summary>DTO for version detect command output.</summary>
public record VersionDetectOutput(
    ProjectVersionEntry[] Projects,
    int ManagedCount,
    int UnmanagedCount
);

// ── Source-generated context ───────────────────────────────────

/// <summary>
/// Source-generated JsonSerializerContext for the titi codebase.
/// All types used in serialization must be registered here.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true
)]
[JsonSerializable(typeof(TestItem[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<SelectionLoader.JsonEdge>))]
[JsonSerializable(typeof(SelectionLoader.JsonEdge))]
[JsonSerializable(typeof(SelectionLoader.JsonLineRange))]
// TestCli formatters
[JsonSerializable(typeof(TestItemEntry))]
[JsonSerializable(typeof(TestItemList))]
[JsonSerializable(typeof(SelectionReasonEntry))]
[JsonSerializable(typeof(SelectedTestEntry))]
[JsonSerializable(typeof(ProjectRefEntry))]
[JsonSerializable(typeof(AffectedSetOutput))]
// Core command DTOs
[JsonSerializable(typeof(SwappedEntry))]
[JsonSerializable(typeof(RetainedEntry))]
[JsonSerializable(typeof(OpenCommandOutput))]
[JsonSerializable(typeof(EdgeEntry))]
[JsonSerializable(typeof(EdgeEntry[]))]
[JsonSerializable(typeof(LineRangeEntry))]
// Version command DTOs
[JsonSerializable(typeof(ProjectVersionEntry))]
[JsonSerializable(typeof(ProjectVersionEntry[]))]
[JsonSerializable(typeof(VersionDetectOutput))]
internal partial class TitiJsonContext : JsonSerializerContext
{
}
