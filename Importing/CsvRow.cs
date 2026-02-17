using System.Collections.Generic;

namespace eFinance.Importing
{
    public sealed class CsvRow
    {
        private readonly Dictionary<string, string?> _values;

        public CsvRow(Dictionary<string, string?> values)
        {
            _values = values ?? new Dictionary<string, string?>();
        }

        public bool TryGet(string header, out string? value) =>
            _values.TryGetValue(Normalize(header), out value);

        // ✅ Add: Get single header (matches how some of your code is calling it)
        public string? Get(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
                return null;

            return _values.TryGetValue(Normalize(header), out var v) ? v : null;
        }

        // ✅ Add: Get with default
        public string Get(string header, string defaultValue)
        {
            var v = Get(header);
            return string.IsNullOrWhiteSpace(v) ? defaultValue : v!;
        }

        public string? GetFirst(params string[] headers)
        {
            foreach (var h in headers)
            {
                if (_values.TryGetValue(Normalize(h), out var v) && !string.IsNullOrWhiteSpace(v))
                    return v;
            }
            return null;
        }

        internal static string Normalize(string s) =>
            (s ?? string.Empty).Trim().Trim('"').ToLowerInvariant();
    }
}
