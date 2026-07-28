using System;
using System.Collections.Generic;

namespace Orion.Storage;

/// <summary>Repository exercised by unit tests for coverage attribution.</summary>
public class Repository
{
    private readonly Dictionary<string, string> _store = new();

    public void Save(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
        _store[key] = value;
    }

    public string? Load(string key) => _store.TryGetValue(key, out var v) ? v : null;

    public int Count => _store.Count;
}
