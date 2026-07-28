using System;

namespace Orion.Core.Data;

/// <summary>Minimal parser exercised by unit tests for coverage attribution.</summary>
public static class Parser
{
    public static Foo Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            throw new ArgumentException("input must be non-empty", nameof(input));

        var parts = input.Split(':', System.StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !int.TryParse(parts[0], out var id))
            throw new FormatException($"expected '<id>:<name>', got '{input}'");

        return new Foo(id, parts[1]);
    }

    public static bool TryParse(string input, out Foo? result)
    {
        try { result = Parse(input); return true; }
        catch { result = null; return false; }
    }
}
