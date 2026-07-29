// Tests for titi-k2x.8: reject coverage paths outside sourceRoot

namespace titi.Tests;

public class CoverageCanonicalizationTests
{
    const string Template = """
    <?xml version="1.0" encoding="UTF-8"?>
    <coverage line-rate="0.5" branch-rate="0.5" lines-covered="1" lines-valid="2" branches-covered="0" branches-valid="0" complexity="1.0" version="0.1" timestamp="1234567890">
      <sources>
        <source>/repo/src</source>
      </sources>
      <packages>
        <package name="P" line-rate="0.5" branch-rate="0.5" complexity="1.0">
          <classes>
            <class name="C" filename="{0}" line-rate="0.5" branch-rate="0.5" complexity="1.0">
              <methods>
                <method name="M" signature="()" line-rate="0.5" branch-rate="0.5">
                  <lines>
                    <line number="10" hits="3" branch="false"/>
                  </lines>
                </method>
              </methods>
              <lines>
                <line number="10" hits="3" branch="false"/>
              </lines>
            </class>
          </classes>
        </package>
      </packages>
    </coverage>
    """;

    static string CoberturaWithFilename(string filename) =>
        string.Format(Template, filename);

    [Fact]
    public void ParseCobertura_PathOutsideSourceRoot_ProducesNoEdge()
    {
        // A class filename that escapes sourceRoot via '..' must not produce
        // an edge that could cross-match files outside the repo source tree.
        var xml = CoberturaWithFilename("../outside.cs");

        var edges = titi.Coverage.Parser.ParseCobertura(xml, "/repo/src");

        // A coverage path escaping sourceRoot via '..' must not produce an edge
        // that could cross-match files outside the repo source tree.
        Assert.False(
            Array.Exists(edges, e => e.To.EndsWith("outside.cs")),
            "a coverage path escaping sourceRoot via '..' must produce no edge");
    }

    [Fact]
    public void ParseCobertura_AbsolutePathInsideSourceRoot_IsCanonicalized()
    {
        // An absolute filename that resolves inside sourceRoot is accepted and
        // canonicalized to a repo-relative-ish form usable by selection.
        var xml = CoberturaWithFilename("/repo/src/Orion.Core.Data/Models/Foo.cs");

        var edges = titi.Coverage.Parser.ParseCobertura(xml, "/repo/src");

        Assert.NotEmpty(edges);
        // The edge target must be rooted under sourceRoot and canonical (no '..').
        Assert.All(edges, e =>
        {
            Assert.True(e.To.StartsWith("/repo/src"),
                $"edge.To '{e.To}' must remain under sourceRoot");
            Assert.False(e.To.Contains(".."),
                $"edge.To '{e.To}' must not contain '..' segments");
        });
    }

    [Fact]
    public void ParseCobertura_RelativePathInsideSourceRoot_IsAccepted()
    {
        // The normal case: a sourceRoot-relative filename (as emitted by coverlet).
        var xml = CoberturaWithFilename("Orion.Core.Data/Models/Foo.cs");

        var edges = titi.Coverage.Parser.ParseCobertura(xml, "/repo/src");

        Assert.NotEmpty(edges);
        Assert.All(edges, e =>
        {
            Assert.True(e.To.StartsWith("/repo/src"),
                $"edge.To '{e.To}' must be rooted under sourceRoot");
            Assert.False(e.To.Contains(".."));
        });
    }
}
