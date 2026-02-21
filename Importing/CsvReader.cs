using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CsvHelper;
using CsvHelper.Configuration;

namespace eFinance.Importing
{
    public static class CsvReader
    {
        public static IEnumerable<CsvRow> Read(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                yield break;

            if (!File.Exists(filePath))
                yield break;

            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HasHeaderRecord = true,
                IgnoreBlankLines = true,

                // Prevent CsvHelper from throwing for small irregularities
                BadDataFound = null,
                MissingFieldFound = null,
                HeaderValidated = null,

                // Trim headers/fields
                TrimOptions = TrimOptions.Trim,

                // Normalize header matching (so "POSTED DATE" == "Posted Date" etc.)
                PrepareHeaderForMatch = args => (args.Header ?? "").Trim(),
            };

            using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            using var csv = new CsvHelper.CsvReader(reader, config);

            // Read header
            if (!csv.Read())
                yield break;

            csv.ReadHeader();
            var headers = csv.HeaderRecord ?? Array.Empty<string>();

            int line = 1; // header line

            while (csv.Read())
            {
                line++;

                var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                foreach (var h in headers)
                {
                    try
                    {
                        dict[h] = csv.GetField(h);
                    }
                    catch
                    {
                        dict[h] = null;
                    }
                }

                // RawRecord includes the exact original line text from the parser
                var raw = csv.Context?.Parser?.RawRecord ?? "";

                yield return new CsvRow(dict, line, raw);
            }
        }
    }
}