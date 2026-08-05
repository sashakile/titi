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

    public static string FormatAffectedUpgrade(AffectedSet affected, double edgeFreshness = 1.0, int historyDepth = 10)
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

        // Use the documented confidence model (titi-e59): weighted combination
        // of resolution ratio, edge freshness, and history depth, instead of
        // the old project-count / file-count calculation.
        var resolvedFiles = affected.ResolvedFiles.Length > 0
            ? affected.ResolvedFiles
            : affected.ChangedFiles;
        var confidence = titi.Safety.Selection.ComputeConfidence(
            affected.ChangedFiles, resolvedFiles, edgeFreshness, historyDepth);

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
