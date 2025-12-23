using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using KukiFinance.Models;

namespace KukiFinance.Helpers
{
    public static class RegisterExporter
    {
        public static void ExportRegisterWithBalance(
            List<RegistryEntry> entries,
            string currentCsvPath,
            bool includeCheckNumber = false)
        {
            using (var writer = new StreamWriter(currentCsvPath))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                // Write headers
                if (includeCheckNumber)
                {
                    csv.WriteField("DATE");
                    csv.WriteField("DESCRIPTION");
                    csv.WriteField("CATEGORY");
                    csv.WriteField("CHECK NUMBER");
                    csv.WriteField("AMOUNT");
                    csv.WriteField("BALANCE");
                }
                else
                {
                    csv.WriteField("DATE");
                    csv.WriteField("DESCRIPTION");
                    csv.WriteField("CATEGORY");
                    csv.WriteField("AMOUNT");
                    csv.WriteField("BALANCE");
                }
                csv.NextRecord();

                // Write data
                foreach (var entry in entries.OrderBy(e => e.Date ?? DateTime.MinValue))
                {
                    csv.WriteField((entry.Date ?? DateTime.MinValue).ToString("yyyy-MM-dd"));
                    csv.WriteField(entry.Description ?? "");
                    csv.WriteField(entry.Category ?? "");
                    if (includeCheckNumber)
                        csv.WriteField(entry.CheckNumber ?? "");
                    csv.WriteField(entry.Amount ?? 0m);
                    csv.WriteField(entry.Balance);
                    csv.NextRecord();
                }
            }
        }

    public class DisplayRegisterRow
        {
            public string Date { get; set; }
            public string Description { get; set; }
            public string Category { get; set; }
            public string CheckNumber { get; set; } // Only used for BMO Check
            public decimal Amount { get; set; }
            public decimal Balance { get; set; }
        }
    }
}