// Tests for titi-k2x.18: ignore uncovered (hits=0) Cobertura lines

namespace titi.Tests;

using System.Linq;

public class CoverageHitFilterTests
{
    const string Prologue = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>";
    const string CoverTemplate = Prologue +
        "<coverage line-rate=\"{0}\" branch-rate=\"{1}\" lines-covered=\"{2}\"" +
        " lines-valid=\"{3}\" branches-covered=\"0\" branches-valid=\"0\"" +
        " complexity=\"1.0\" version=\"0.1\" timestamp=\"1234567890\">" +
        "<sources><source>/repo/src</source></sources><packages>" +
        "<package name=\"P\" line-rate=\"{0}\" branch-rate=\"{1}\" complexity=\"1.0\">" +
        "<classes><class name=\"{4}\" filename=\"{5}\" line-rate=\"{0}\"" +
        " branch-rate=\"{1}\" complexity=\"1.0\">{6}</class></classes></package></packages></coverage>";

    static string Cover(string lineRate, string branchRate, string linesCovered,
        string linesValid, string className, string filename, string methodsAndLines) =>
        string.Format(CoverTemplate, lineRate, branchRate, linesCovered, linesValid,
            className, filename, methodsAndLines);

    const string MethodWithLines = "<methods><method name=\"{0}\" signature=\"()\" " +
        "line-rate=\"{1}\" branch-rate=\"{1}\">{2}</method></methods>";
    const string Lines = "<lines>{0}</lines>";

    static string Method(string name, string lineRate, string linesXml) =>
        string.Format(MethodWithLines, name, lineRate, string.Format(Lines, linesXml));

    const string LineFmt = "<line number=\"{0}\" hits=\"{1}\" branch=\"false\"/>";

    static string Line(int number, int hits) =>
        string.Format(LineFmt, number, hits);

    [Fact]
    public void AllLinesZeroHits_ProducesNoEdge()
    {
        // A method where every line has hits=0 is uncovered: no edge should
        // be emitted, because representing it as "covered" would cause false-
        // positive test selections.
        var xml = Cover("0.0", "0.0", "0", "2", "C", "Foo.cs",
            Method("M", "0.0",
                Line(10, 0) + Line(20, 0)));

        var edges = titi.Coverage.Parser.ParseCobertura(xml, "/repo/src");

        Assert.False(
            Array.Exists(edges, e => e.From == "M"),
            "a method with all zero-hit lines must produce no edge");
    }

    [Fact]
    public void MixedHits_RetainsOnlyPositiveHitLines()
    {
        // A method with some hits=0 and some hits>0 lines: only the positive-
        // hit lines should appear in the edge's LineRanges.
        var xml = Cover("0.5", "0.5", "1", "2", "C", "Foo.cs",
            Method("M", "0.5",
                Line(10, 3) + Line(20, 0)));

        var edges = titi.Coverage.Parser.ParseCobertura(xml, "/repo/src");

        var matches = edges.Where(e => e.From == "M").ToArray();
        var edge = Assert.Single(matches);
        var ranges = edge.LineRanges;
        Assert.Single(ranges.Where(r => r.Start == 10 && r.End == 10));
        Assert.False(
            Array.Exists(ranges, r => r.Start == 20),
            "hits=0 line must not appear in LineRanges");
    }

    [Fact]
    public void FileLevel_AllZeroHits_ProducesNoEdge()
    {
        // A class with NO methods and all hits=0 on its lines must produce
        // no file-level edge.
        var xml = Cover("0.0", "0.0", "0", "1", "C", "NoMethods.cs",
            // No <methods> element; class-level <lines> inside <class>
            string.Format(Lines, Line(5, 0)));

        var edges = titi.Coverage.Parser.ParseCobertura(xml, "/repo/src");

        Assert.False(
            Array.Exists(edges, e => e.From == "C"),
            "a class with no methods and all zero-hit lines must produce no edge");
    }
}

