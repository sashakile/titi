// TID-3: Cobertura XML → TestToSourceEdge parsing

namespace titi.Coverage;

using System.Xml.Linq;

public static class Parser
{
    /// <summary>Parse Cobertura XML coverage report into TestToSourceEdge records.</summary>
    /// <param name="xml">Raw Cobertura XML content.</param>
    /// <param name="sourceRoot">Source root directory (from Cobertura &lt;sources&gt;).</param>
    /// <returns>Array of file-level TestToSourceEdge records.</returns>
    public static TestToSourceEdge[] ParseCobertura(string xml, string sourceRoot)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var root = doc.Root;
            if (root == null || root.Name != "coverage")
                return [];

            var edges = new List<TestToSourceEdge>();

            foreach (var pkg in root.Descendants("package"))
            {
                foreach (var cls in pkg.Descendants("class"))
                {
                    var filename = cls.Attribute("filename")?.Value;
                    if (string.IsNullOrEmpty(filename))
                        continue;

                    var sourcePath = Path.Combine(sourceRoot, filename);

                    // Collect line ranges
                    var lineRanges = new List<(int Start, int End)>();
                    foreach (var line in cls.Descendants("line"))
                    {
                        var numStr = line.Attribute("number")?.Value;
                        if (int.TryParse(numStr, out var num))
                        {
                            lineRanges.Add((num, num));
                        }
                    }

                    // Build edges per method (method name as the "from" field identifier)
                    foreach (var method in cls.Descendants("method"))
                    {
                        var methodName = method.Attribute("name")?.Value ?? "unknown";
                        var methodLines = new List<(int Start, int End)>();
                        foreach (var line in method.Descendants("line"))
                        {
                            var numStr = line.Attribute("number")?.Value;
                            if (int.TryParse(numStr, out var num))
                                methodLines.Add((num, num));
                        }

                        edges.Add(new TestToSourceEdge(
                            From: methodName,
                            To: sourcePath,
                            Origin: EdgeOrigin.Static,
                            Weight: 1_000_000,
                            LineRanges: methodLines.ToArray()
                        ));
                    }

                    // If no methods, create a file-level edge using class name
                    if (!cls.Descendants("method").Any())
                    {
                        var className = cls.Attribute("name")?.Value ?? "unknown";
                        edges.Add(new TestToSourceEdge(
                            From: className,
                            To: sourcePath,
                            Origin: EdgeOrigin.Static,
                            Weight: 1_000_000,
                            LineRanges: lineRanges.ToArray()
                        ));
                    }
                }
            }

            return edges.ToArray();
        }
        catch
        {
            return [];
        }
    }
}
