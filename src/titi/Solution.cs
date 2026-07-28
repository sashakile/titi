// titi.solution — .slnx solution file generation
// Generates .slnx XML directly (standard Microsoft.Build.Traversal format)

namespace titi.Solution;

using System.Xml.Linq;

public static class SolutionGenerator
{
    static readonly XNamespace SolNs = "http://schemas.microsoft.com/developer/solution/2024";

    /// <summary>Generate a .slnx file for a SwapResult. Only swapped closure projects included.</summary>
    public static (string Path, TitiError? Error) Generate(SwapResult result, string outputDir, string packageId)
    {
        try
        {
            var dir = Path.Combine(outputDir, "solutions");
            Directory.CreateDirectory(dir);

            var outputPath = Path.Combine(dir, $"{packageId}.slnx");
            var tmpPath = outputPath + ".tmp";

            var doc = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(SolNs + "Solution",

                    // Global properties
                    new XElement(SolNs + "Properties",
                        new XElement(SolNs + "Property", new XAttribute("Name", "InTitiContext"), result.MsbuildContext.InTitiContext),
                        new XElement(SolNs + "Property", new XAttribute("Name", "TitiPrefix"), result.MsbuildContext.TitiPrefix),
                        new XElement(SolNs + "Property", new XAttribute("Name", "TitiSourceRoot"), result.MsbuildContext.TitiSourceRoot)
                    ),

                    // Swapped projects
                    result.Swapped.Length > 0
                        ? new XElement(SolNs + "Folder", new XAttribute("Name", "Swapped"),
                            result.Swapped.Select(s =>
                                new XElement(SolNs + "Project",
                                    new XAttribute("Path", s.LocalSourcePath),
                                    new XAttribute("DisplayName", s.PackageId)
                                )
                            )
                        )
                        : null,

                    // Retained projects (informational)
                    result.Retained.Length > 0
                        ? new XElement(SolNs + "Folder", new XAttribute("Name", "Retained"),
                            result.Retained.Select(r =>
                                new XElement(SolNs + "Project",
                                    new XAttribute("Path", $"[{r.Reason}] {r.PackageId}"),
                                    new XAttribute("DisplayName", r.PackageId)
                                )
                            )
                        )
                        : null
                )
            );

            // Write atomically: tmp → rename
            doc.Save(tmpPath);
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            File.Move(tmpPath, outputPath);

            return (outputPath, null);
        }
        catch (Exception ex)
        {
            var err = new TitiError(
                ErrorCode.GraphBuildFailed,
                $"Failed to generate solution: {ex.Message}",
                new() { ["command"] = "solution", ["target"] = packageId, ["phase"] = "solution-gen" },
                ["Check file permissions and disk space"]
            );
            return ("", err);
        }
    }
}
