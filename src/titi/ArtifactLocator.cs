// TID-3b: Locate dotnet-test run artifacts (TRX + Cobertura) in a results dir.

namespace titi;

public static class ArtifactLocator
{
    /// <summary>
    /// Find the TRX file and Cobertura coverage file produced by a single
    /// `dotnet test --logger trx --collect "XPlat Code Coverage"` run in
    /// <paramref name="resultsDir"/>. The TRX is written at the results-
    /// directory root; the Cobertura file is written inside a GUID-named
    /// subdirectory as `coverage.cobertura.xml`. Returns (null, null) if the
    /// directory is missing or no TRX was produced.
    /// </summary>
    public static (string? TrxPath, string? CoberturaPath) FindArtifacts(string resultsDir)
    {
        if (!Directory.Exists(resultsDir))
            return (null, null);

        var trx = Directory.EnumerateFiles(resultsDir, "*.trx", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();
        if (trx == null)
            return (null, null);

        // Cobertura output lives in a subdirectory (e.g. <guid>/coverage.cobertura.xml).
        var cobertura = Directory.EnumerateFiles(resultsDir, "coverage.cobertura.xml", SearchOption.AllDirectories)
            .FirstOrDefault();

        return (trx, cobertura);
    }
}
