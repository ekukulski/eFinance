using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using KukiFinance.Helpers;

namespace KukiFinance.Constants
{
    public class OpeningBalanceEntry
    {
        public DateTime Date { get; set; }
        public string Account { get; set; }
        public decimal Balance { get; set; }
    }

    public static class OpeningBalances
    {
        private static readonly string CsvPath = FilePathHelper.GetKukiFinancePath("OpeningBalances.csv");

        public static List<OpeningBalanceEntry> GetAllEntries()
        {
            var list = new List<OpeningBalanceEntry>();
            if (File.Exists(CsvPath))
            {
                foreach (var line in File.ReadAllLines(CsvPath).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var parts = line.Split(',').Select(p => p.Trim()).ToArray();
                    // Debug: Uncomment the next line to see what's being parsed
                    // System.Diagnostics.Debug.WriteLine($"Line: {line} | Parts: {string.Join("|", parts)}");

                    if (parts.Length >= 3 &&
                        DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) &&
                        !string.IsNullOrWhiteSpace(parts[1]) &&
                        decimal.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out var bal))
                    {
                        list.Add(new OpeningBalanceEntry
                        {
                            Date = date,
                            Account = parts[1],
                            Balance = bal
                        });
                    }
                }
            }
            return list;
        }

        public static decimal Get(string account)
        {
            var entry = GetAllEntries().FirstOrDefault(e =>
                string.Equals(e.Account?.Trim(), account?.Trim(), StringComparison.OrdinalIgnoreCase));
            return entry?.Balance ?? 0m;
        }

        public static DateTime? GetDate(string account)
        {
            var entry = GetAllEntries().FirstOrDefault(e =>
                string.Equals(e.Account?.Trim(), account?.Trim(), StringComparison.OrdinalIgnoreCase));
            return entry?.Date;
        }

        public static void Update(string account, DateTime newDate, decimal newBalance)
        {
            if (!File.Exists(CsvPath))
                return;

            var lines = File.ReadAllLines(CsvPath).ToList();
            if (lines.Count == 0)
                return;

            // Assume first line is header
            for (int i = 1; i < lines.Count; i++)
            {
                var parts = lines[i].Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length >= 3 && string.Equals(parts[1], account?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    parts[0] = newDate.ToString("yyyy-MM-dd");
                    parts[2] = newBalance.ToString(CultureInfo.InvariantCulture);
                    lines[i] = string.Join(",", parts);
                    break;
                }
            }
            File.WriteAllLines(CsvPath, lines);
        }
    }
}