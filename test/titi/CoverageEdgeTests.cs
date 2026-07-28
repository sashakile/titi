// Tests for TID-3: Cobertura XML → TestToSourceEdge parsing

namespace titi.Tests;

public class CoverageEdgeTests
{
    const string SampleCobertura = """
    <?xml version="1.0" encoding="UTF-8"?>
    <coverage line-rate="0.5" branch-rate="0.5" lines-covered="10" lines-valid="20" branches-covered="2" branches-valid="4" complexity="1.0" version="0.1" timestamp="1234567890">
      <sources>
        <source>/repo/src</source>
      </sources>
      <packages>
        <package name="Orion.Core.Data" line-rate="0.5" branch-rate="0.5" complexity="1.0">
          <classes>
            <class name="Orion.Core.Data.Models.Foo" filename="Orion.Core.Data/Models/Foo.cs" line-rate="0.5" branch-rate="0.5" complexity="1.0">
              <methods>
                <method name="Bar" signature="()" line-rate="0.5" branch-rate="0.5">
                  <lines>
                    <line number="10" hits="3" branch="false"/>
                  </lines>
                </method>
              </methods>
              <lines>
                <line number="10" hits="3" branch="false"/>
                <line number="25" hits="0" branch="false"/>
              </lines>
            </class>
          </classes>
        </package>
      </packages>
    </coverage>
    """;

    [Fact]
    public void ParseCobertura_WithValidXml_ReturnsEdges()
    {
        var edges = titi.Coverage.Parser.ParseCobertura(SampleCobertura, "/repo/src");

        Assert.NotEmpty(edges);
        // Should have file-level edges for touched source files
        Assert.Contains(edges, e => e.To.Contains("Foo.cs"));
    }

    [Fact]
    public void ParseCobertura_EdgesHaveStaticOriginAndFullWeight()
    {
        var edges = titi.Coverage.Parser.ParseCobertura(SampleCobertura, "/repo/src");

        Assert.All(edges, e =>
        {
            Assert.Equal(EdgeOrigin.Static, e.Origin);
            Assert.Equal(1_000_000, e.Weight);
        });
    }

    [Fact]
    public void ParseCobertura_WithMultipleClasses_ReturnsEdgesForEach()
    {
        var xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <coverage line-rate="0.5" branch-rate="0.5" lines-covered="10" lines-valid="20" branches-covered="2" branches-valid="4" complexity="1.0" version="0.1">
          <sources>
            <source>/repo/src</source>
          </sources>
          <packages>
            <package name="Orion.Core.Data" line-rate="0.5" branch-rate="0.5" complexity="1.0">
              <classes>
                <class name="Foo" filename="Foo.cs" line-rate="0.5" branch-rate="0.5" complexity="1.0">
                  <methods><method name="Bar" signature="()" line-rate="0.5" branch-rate="0.5">
                    <lines><line number="10" hits="1" branch="false"/></lines>
                  </method></methods>
                  <lines><line number="10" hits="1" branch="false"/></lines>
                </class>
                <class name="Baz" filename="Baz.cs" line-rate="0.5" branch-rate="0.5" complexity="1.0">
                  <methods><method name="Qux" signature="()" line-rate="0.5" branch-rate="0.5">
                    <lines><line number="5" hits="2" branch="false"/></lines>
                  </method></methods>
                  <lines><line number="5" hits="2" branch="false"/></lines>
                </class>
              </classes>
            </package>
          </packages>
        </coverage>
        """;

        var edges = titi.Coverage.Parser.ParseCobertura(xml, "/repo/src");

        Assert.Equal(2, edges.Length);
        var sources = edges.Select(e => e.To).ToHashSet();
        Assert.Contains(sources, s => s.EndsWith("Foo.cs"));
        Assert.Contains(sources, s => s.EndsWith("Baz.cs"));
    }

    [Fact]
    public void ParseCobertura_WithEmptyCoverage_ReturnsEmpty()
    {
        var xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <coverage line-rate="1" branch-rate="1" lines-covered="0" lines-valid="0" branches-covered="0" branches-valid="0" complexity="0" version="0.1">
          <sources><source>/repo</source></sources>
          <packages/>
        </coverage>
        """;

        var edges = titi.Coverage.Parser.ParseCobertura(xml, "/repo/src");
        Assert.Empty(edges);
    }

    [Fact]
    public void ParseCobertura_WithInvalidXml_ReturnsEmpty()
    {
        var edges = titi.Coverage.Parser.ParseCobertura("not valid xml", "/repo");
        Assert.Empty(edges);
    }

    [Fact]
    public void BuildEdgesFromCoverage_TestIdMapping_IncludesMethodName()
    {
        var edges = titi.Coverage.Parser.ParseCobertura(SampleCobertura, "/repo/src");
        // The from field should reference the test/method that covered this source
        Assert.All(edges, e => Assert.False(string.IsNullOrEmpty(e.From)));
    }
}
