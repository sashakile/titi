// TID-4a: Persist per-test run history to .titi/test-cache/history.json (TD-06 / 4.6).
//
// EDN shape (written and read by this module):
//   {"<test-id>" [{:test-id "<test-id>" :outcome :passed|:failed|:skipped
//                  :duration-ms <num> :timestamp "<iso-8601>"} ...]
//    ...}
// Top-level map: test-id -> vector of entries, most-recent-last. The EDN
// reader here is a minimal recursive-descent parser for the subset this module
// emits (maps, vectors, strings, keywords, numbers, nil) — sufficient for a
// faithful round-trip of titi's own history output.

namespace titi;

using System.Globalization;
using System.Text;
using titi.Safety;

public static class HistoryStore
{
    public const int DefaultMaxEntriesPerTest = 100;
    public const long DefaultMaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// Append one entry per TRX result to the history, evicting the oldest
    /// entries beyond <paramref name="maxPerTest"/>. Pure: returns a new dict.
    /// </summary>
    public static Dictionary<string, TestRunEntry[]> AppendResults(
        Dictionary<string, TestRunEntry[]> history,
        Coverage.TrxTestResult[] results,
        DateTime timestamp,
        int maxPerTest = DefaultMaxEntriesPerTest)
    {
        if (results.Length == 0)
            return new Dictionary<string, TestRunEntry[]>(history);

        var updated = new Dictionary<string, TestRunEntry[]>(history);
        foreach (var r in results)
        {
            var entry = new TestRunEntry(r.TestName, r.Outcome, r.DurationMs, timestamp);
            if (!updated.TryGetValue(r.TestName, out var existing) || existing.Length == 0)
            {
                updated[r.TestName] = new[] { entry };
                continue;
            }

            // most-recent-last: append, then evict oldest if over retention.
            var combined = new TestRunEntry[existing.Length + 1];
            Array.Copy(existing, combined, existing.Length);
            combined[^1] = entry;
            if (combined.Length > maxPerTest)
            {
                var trimmed = new TestRunEntry[maxPerTest];
                Array.Copy(combined, combined.Length - maxPerTest, trimmed, 0, maxPerTest);
                combined = trimmed;
            }
            updated[r.TestName] = combined;
        }
        return updated;
    }

    /// <summary>Serialize history to an EDN map string.</summary>
    public static string SerializeEdn(Dictionary<string, TestRunEntry[]> history)
    {
        var sb = new StringBuilder();
        sb.Append('{');
        var first = true;
        foreach (var kv in history.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (kv.Value.Length == 0) continue;
            if (!first) sb.Append(' ');
            first = false;
            sb.Append(EdnString(kv.Key)).Append(' ').Append('[');
            for (var i = 0; i < kv.Value.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                var e = kv.Value[i];
                sb.Append('{');
                sb.Append(":test-id ").Append(EdnString(e.TestId)).Append(' ');
                sb.Append(":outcome ").Append(OutcomeKeyword(e.Outcome)).Append(' ');
                sb.Append(":duration-ms ").Append(e.DurationMs.ToString(CultureInfo.InvariantCulture)).Append(' ');
                sb.Append(":timestamp ").Append(EdnString(e.Timestamp.ToString("o", CultureInfo.InvariantCulture)));
                sb.Append('}');
            }
            sb.Append(']');
        }
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Parse an EDN history map (as emitted by <see cref="SerializeEdn"/>).
    /// Malformed input yields an empty dictionary rather than throwing.
    /// </summary>
    public static Dictionary<string, TestRunEntry[]> ParseEdn(string edn)
    {
        var result = new Dictionary<string, TestRunEntry[]>();
        if (string.IsNullOrWhiteSpace(edn))
            return result;

        var p = new EdnParser(edn);
        try
        {
            if (!p.SkipWs() || p.Peek() != '{')
                return result;
            var top = p.ParseValue() as Dictionary<object, object>;
            if (top == null)
                return result;

            foreach (var kv in top)
            {
                var testId = kv.Key as string;
                if (testId == null) continue;
                var vec = kv.Value as List<object>;
                if (vec == null) continue;

                var entries = new List<TestRunEntry>();
                foreach (var item in vec)
                {
                    if (item is not Dictionary<object, object> m) continue;
                    var id = m[":test-id"] as string ?? testId;
                    var outcome = KeywordToOutcome(m[":outcome"] as string);
                    var dur = AsLong(m[":duration-ms"]);
                    var ts = DateTime.Parse((string)(m[":timestamp"] ?? ""), CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                    entries.Add(new TestRunEntry(id, outcome, dur, ts));
                }
                if (entries.Count > 0)
                    result[testId] = entries.ToArray();
            }
        }
        catch
        {
            return new Dictionary<string, TestRunEntry[]>();
        }
        return result;
    }

    /// <summary>
    /// Compaction (TD-06): if the serialized history exceeds
    /// <paramref name="maxSizeBytes"/>, drop entries beyond
    /// <paramref name="maxEntriesPerTest"/> for every test, keeping the most
    /// recent. Pure: returns a new dict.
    /// </summary>
    public static Dictionary<string, TestRunEntry[]> CompactIfOversized(
        Dictionary<string, TestRunEntry[]> history,
        int maxEntriesPerTest = DefaultMaxEntriesPerTest,
        long maxSizeBytes = DefaultMaxSizeBytes)
    {
        var serialized = SerializeEdn(history);
        if (Encoding.UTF8.GetByteCount(serialized) <= maxSizeBytes)
            return new Dictionary<string, TestRunEntry[]>(history);

        var compacted = new Dictionary<string, TestRunEntry[]>();
        foreach (var kv in history)
        {
            if (kv.Value.Length <= maxEntriesPerTest)
            {
                compacted[kv.Key] = kv.Value;
                continue;
            }
            var trimmed = new TestRunEntry[maxEntriesPerTest];
            Array.Copy(kv.Value, kv.Value.Length - maxEntriesPerTest, trimmed, 0, maxEntriesPerTest);
            compacted[kv.Key] = trimmed;
        }
        return compacted;
    }

    // ── EDN helpers ───────────────────────────────────────────────

    static string OutcomeKeyword(TestOutcome o) => o switch
    {
        TestOutcome.Passed => ":passed",
        TestOutcome.Failed => ":failed",
        TestOutcome.Skipped => ":skipped",
        _ => ":not-run",
    };

    static TestOutcome KeywordToOutcome(string? kw) => kw switch
    {
        ":passed" => TestOutcome.Passed,
        ":failed" => TestOutcome.Failed,
        ":skipped" => TestOutcome.Skipped,
        _ => TestOutcome.NotRun,
    };

    static string EdnString(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static long AsLong(object? v) => v switch
    {
        long l => l,
        double d => (long)d,
        int i => i,
        _ => 0,
    };

    // Minimal recursive-descent EDN reader for the subset HistoryStore emits.
    private sealed class EdnParser
    {
        private readonly string _s;
        private int _i;
        public EdnParser(string s) { _s = s; }

        public bool SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
            return _i < _s.Length;
        }
        public char Peek() => _s[_i];

        public object? ParseValue()
        {
            if (!SkipWs()) return null;
            var c = _s[_i];
            if (c == '{') return ParseMap();
            if (c == '[') return ParseVector();
            if (c == '"') return ParseString();
            if (c == ':') return ParseKeyword();
            if (c == 'n') return ParseNil();
            return ParseNumberOrToken();
        }

        Dictionary<object, object> ParseMap()
        {
            _i++; // {
            var m = new Dictionary<object, object>();
            while (SkipWs() && _s[_i] != '}')
            {
                var key = ParseValue();
                var val = ParseValue();
                if (key != null) m[key] = val ?? "<nil>";
            }
            if (_i < _s.Length && _s[_i] == '}') _i++;
            return m;
        }

        List<object> ParseVector()
        {
            _i++; // [
            var v = new List<object>();
            while (SkipWs() && _s[_i] != ']')
            {
                var item = ParseValue();
                v.Add(item ?? "<nil>");
            }
            if (_i < _s.Length && _s[_i] == ']') _i++;
            return v;
        }

        string ParseString()
        {
            _i++; // opening quote
            var sb = new StringBuilder();
            while (_i < _s.Length && _s[_i] != '"')
            {
                if (_s[_i] == '\\' && _i + 1 < _s.Length)
                {
                    var n = _s[_i + 1];
                    sb.Append(n switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', _ => n });
                    _i += 2;
                }
                else sb.Append(_s[_i++]);
            }
            if (_i < _s.Length && _s[_i] == '"') _i++;
            return sb.ToString();
        }

        string ParseKeyword() { var t = ParseToken(1); return ":" + t; }

        object ParseNil()
        {
            if (_i + 3 <= _s.Length && _s.Substring(_i, 3) == "nil" && (_i + 3 == _s.Length || IsDelim(_s[_i + 3])))
            { _i += 3; return "<nil>"; }
            return ParseNumberOrToken();
        }

        object ParseNumberOrToken()
        {
            var start = _i;
            while (_i < _s.Length && !IsDelim(_s[_i]) && !char.IsWhiteSpace(_s[_i])) _i++;
            var tok = _s.Substring(start, _i - start);
            if (long.TryParse(tok, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            return tok;
        }

        string ParseToken(int skip)
        {
            _i += skip;
            var start = _i;
            while (_i < _s.Length && !IsDelim(_s[_i]) && !char.IsWhiteSpace(_s[_i])) _i++;
            return _s.Substring(start, _i - start);
        }

        static bool IsDelim(char c) => c is ',' or '}' or ']' or '{' or '[' or '"' or ':';
    }
}
