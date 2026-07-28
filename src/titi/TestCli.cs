// TID-5: Test CLI commands — formatters and dispatch

namespace titi.TestCli;

using System.Text.Json;
using titi.Serialization;

public static class Formatter
{
    public static string FormatTestItems(TestItem[] items)
    {
        var list = items.Select(i => new TestItemEntry(
            TestId: i.TestId,
            ClassName: i.ClassName,
            MethodName: i.MethodName,
            Framework: i.Framework.ToString().ToLower(),
            Tier: i.Tier.ToString().ToLower(),
            SourceFile: i.SourceFile
        )).ToArray();

        var wrapper = new TestItemList(list);
        return JsonSerializer.Serialize(wrapper, TitiJsonContext.Default.TestItemList);
    }

    public static string FormatAffectedUpgrade(AffectedSet affected)
    {
        var selectedTests = affected.SelectedTests.Select(st => new SelectedTestEntry(
            TestId: st.TestId,
            Selected: st.Selected,
            Reasons: st.Reasons.Select(r => new SelectionReasonEntry(
                Kind: r.Kind,
                Description: r.Description
            )).ToArray(),
            Confidence: st.Confidence,
            FallbackReason: st.FallbackReason?.ToString()
        )).ToArray();

        var allResolved = affected.ChangedFiles.Length > 0
            ? affected.DirectlyAffected.Length
            : 0;
        var confidence = affected.ChangedFiles.Length > 0
            ? (double)allResolved / affected.ChangedFiles.Length
            : 1.0;

        var output = new AffectedSetOutput(
            ChangedFiles: affected.ChangedFiles,
            DirectlyAffected: affected.DirectlyAffected.Select(p => new ProjectRefEntry(
                PackageId: p.PackageId,
                Path: p.Path
            )).ToArray(),
            TransitivelyAffected: affected.TransitivelyAffected.Select(p => new ProjectRefEntry(
                PackageId: p.PackageId,
                Path: p.Path
            )).ToArray(),
            TotalAffected: affected.DirectlyAffected.Length + affected.TransitivelyAffected.Length,
            SelectedTests: selectedTests,
            Confidence: confidence
        );

        return JsonSerializer.Serialize(output, TitiJsonContext.Default.AffectedSetOutput);
    }
}
