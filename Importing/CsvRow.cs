using System;
using System.Collections.Generic;
using System.Linq;

namespace eFinance.Importing
{
    public sealed class CsvRow
    {
        private readonly Dictionary<string, string?> _values;

        public CsvRow(Dictionary<string, string?> values, int lineNumber, string rawLine)
        {
            _values = values ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            LineNumber = lineNumber;
            RawLine = rawLine ?? "";
        }

        public int LineNumber { get; }
        public string RawLine { get; }

        public string? Get(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _values.TryGetValue(name, out var v) ? v : null;
        }

        // Your importers already call GetFirst(...), keep it
        public string? GetFirst(params string[] names)
        {
            foreach (var n in names ?? Array.Empty<string>())
            {
                var v = Get(n);
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            }
            return null;
        }

        // Helpful for debugging
        public override string ToString()
            => $"Line {LineNumber}: {RawLine}";
    }
}