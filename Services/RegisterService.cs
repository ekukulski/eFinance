using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;

namespace KukiFinance.Services;

public static class RegisterService
{
    private static readonly CsvConfiguration CsvConfig = new(CultureInfo.InvariantCulture)
    {
        HasHeaderRecord = true,
        IgnoreBlankLines = true,
        TrimOptions = TrimOptions.Trim,
        MissingFieldFound = null,
        HeaderValidated = null,
        BadDataFound = null
    };

    /// <summary>
    /// Loads a register from a CSV file, reads categories (optional), inserts an opening row,
    /// and calculates running balances.
    ///
    /// Supports custom amount logic (e.g., for AMEX) via getAmountForBalance.
    /// </summary>
    public static List<TEntry> LoadRegister<TEntry>(
        string registerFile,
        string categoryFile,
        decimal openingBalance,
        Func<string[], TEntry> entryFactory,
        Func<TEntry, DateTime> getDate,
        Func<TEntry, decimal> getAmount,
        Func<TEntry, decimal> getBalance,
        Action<TEntry, decimal> setBalance,
        TEntry openingEntry,
        Func<TEntry, decimal>? getAmountForBalance = null
    )
    {
        // 1) Validate inputs early (better errors)
        if (string.IsNullOrWhiteSpace(registerFile))
            throw new ArgumentException("Register file path is blank.", nameof(registerFile));

        if (!File.Exists(registerFile))
            throw new FileNotFoundException($"Register file not found: {registerFile}", registerFile);

        // 2) Read register rows using CsvHelper (handles quotes/commas correctly)
        var entries = ReadRows(registerFile, entryFactory);

        // 3) Sort, insert opening entry
        entries = entries.OrderBy(getDate).ToList();
        entries.Insert(0, openingEntry);

        // 4) Running balance calculation
        var amountForBalance = getAmountForBalance ?? getAmount;

        setBalance(entries[0], openingBalance);

        for (int i = 1; i < entries.Count; i++)
        {
            var prevBalance = getBalance(entries[i - 1]);
            var currAmount = amountForBalance(entries[i]);
            setBalance(entries[i], prevBalance + currAmount);
        }

        return entries;
    }

    private static Dictionary<string, string> ReadCategoriesIfPresent(string categoryFile)
    {
        var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(categoryFile) || !File.Exists(categoryFile))
            return categories;

        using var reader = new StreamReader(categoryFile);
        using var csv = new CsvReader(reader, CsvConfig);

        // Expecting headers: at least two columns. We read raw rows so your current format works.
        while (csv.Read())
        {
            var row = csv.Parser.Record;
            if (row is null || row.Length < 2) continue;

            var key = row[0]?.Trim();
            var value = row[1]?.Trim();

            if (string.IsNullOrEmpty(key)) continue;
            if (!categories.ContainsKey(key))
                categories[key] = value ?? string.Empty;
        }

        return categories;
    }

    private static List<TEntry> ReadRows<TEntry>(string registerFile, Func<string[], TEntry> entryFactory)
    {
        var entries = new List<TEntry>();

        using var reader = new StreamReader(registerFile);
        using var csv = new CsvReader(reader, CsvConfig);

        while (csv.Read())
        {
            var row = csv.Parser.Record;
            if (row is null || row.Length == 0) continue;

            try
            {
                var entry = entryFactory(row);
                entries.Add(entry);
            }
            catch (Exception ex)
            {
                // Make row-level failures diagnosable
                throw new InvalidDataException(
                    $"Failed to parse row {csv.Context.Parser.Row} in {Path.GetFileName(registerFile)}: {ex.Message}",
                    ex);
            }
        }

        return entries;
    }
}
