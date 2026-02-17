using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace eFinance.Importing
{
    public static class CsvReader
    {
        public static IEnumerable<CsvRow> Read(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("File path is required.", nameof(filePath));

            using var fs = File.OpenRead(filePath);
            using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            // Read header
            var headerLine = sr.ReadLine();
            if (string.IsNullOrWhiteSpace(headerLine))
                yield break;

            // Split + normalize headers
            var rawHeaders = SplitCsvLine(headerLine);
            var headers = rawHeaders
                .Select(h => CsvRow.Normalize(h))
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .ToList();

            if (headers.Count == 0)
                yield break;

            while (!sr.EndOfStream)
            {
                var line = sr.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var cells = SplitCsvLine(line);

                var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

                // Map header -> cell; if the row is short, fill nulls
                for (int i = 0; i < headers.Count; i++)
                {
                    var key = headers[i];
                    string? value = i < cells.Count ? cells[i] : null;

                    // Keep raw value but trim common whitespace noise
                    dict[key] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                }

                yield return new CsvRow(dict);
            }
        }

        // Minimal CSV splitter: handles quoted fields and commas inside quotes.
        private static List<string> SplitCsvLine(string line)
        {
            var result = new List<string>();
            if (line is null) return result;

            var cur = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    // Escaped quote inside quoted field ("")
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cur.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }

                    continue;
                }

                if (c == ',' && !inQuotes)
                {
                    result.Add(cur.ToString());
                    cur.Clear();
                    continue;
                }

                cur.Append(c);
            }

            result.Add(cur.ToString());
            return result;
        }
    }
}
