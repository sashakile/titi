// TID-5: Test CLI commands — formatters and dispatch

namespace titi.TestCli;

using System.Text.Json;

public static class Formatter
{
    public static string FormatTestItems(TestItem[] items)
    {
        var list = items.Select(i => new
        {
            testId = i.TestId,
            className = i.ClassName,
            methodName = i.MethodName,
            framework = i.Framework.ToString().ToLower(),
            tier = i.Tier.ToString().ToLower(),
            sourceFile = i.SourceFile,
        });

        return JsonSerializer.Serialize(new { tests = list }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FormatAffectedUpgrade(AffectedSet affected)
    {
        var selectedTests = affected.SelectedTests.Select(st => new
        {
            testId = st.TestId,
            selected = st.Selected,
            reasons = st.Reasons.Select(r => new { kind = r.Kind, description = r.Description }),
            confidence = st.Confidence,
            fallbackReason = st.FallbackReason?.ToString()
        }).ToArray();

        var allResolved = affected.ChangedFiles.Length > 0
            ? affected.DirectlyAffected.Length
            : 0;
        var confidence = affected.ChangedFiles.Length > 0
            ? (double)allResolved / affected.ChangedFiles.Length
            : 1.0;

        var output = new
        {
            changedFiles = affected.ChangedFiles,
            directlyAffected = affected.DirectlyAffected.Select(p => new { p.PackageId, p.Path }),
            transitivelyAffected = affected.TransitivelyAffected.Select(p => new { p.PackageId, p.Path }),
            totalAffected = affected.DirectlyAffected.Length + affected.TransitivelyAffected.Length,
            selectedTests = selectedTests,
            confidence = confidence,
        };

        return JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true });
    }
}
