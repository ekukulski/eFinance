using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;

namespace eFinance.Services;

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
    /// Loads a register from a CSV file, inserts an opening row, sorts by date,
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
        Func<TEntry, decimal>? getAmountForBalance = null)
    {
        // Validate required args
        if (string.IsNullOrWhiteSpace(registerFile))
            throw new ArgumentException("Register file path is blank.", nameof(registerFile));

        if (!File.Exists(registerFile))
            throw new FileNotFoundException($"Register file not found: {registerFile}", registerFile);

        if (entryFactory is null) throw new ArgumentNullException(nameof(entryFactory));
        if (getDate is null) throw new ArgumentNullException(nameof(getDate));
        if (getAmount is null) throw new ArgumentNullException(nameof(getAmount));
        if (getBalance is null) throw new ArgumentNullException(nameof(getBalance));
        if (setBalance is null) throw new ArgumentNullException(nameof(setBalance));

        // categoryFile is currently unused here; keep the parameter for compatibility
        _ = categoryFile;

        // Read rows -> entries
        var entries = ReadEntries(registerFile, entryFactory);

        // Sort and insert opening entry
        entries = entries.OrderBy(getDate).ToList();
        entries.Insert(0, openingEntry);

        // Calculate running balance
        ApplyRunningBalance(
            entries,
            openingBalance,
            getBalance,
            setBalance,
            getAmountForBalance ?? getAmount);

        return entries;
    }

    private static List<TEntry> ReadEntries<TEntry>(
        string registerFile,
        Func<string[], TEntry> entryFactory)
    {
        var entries = new List<TEntry>();

        using var reader = new StreamReader(registerFile);
        using var csv = new CsvReader(reader, CsvConfig);

        while (csv.Read())
        {
            // csv.Parser.Record is string[]? in nullable annotations
            var record = csv.Parser.Record;
            if (record is null || record.Length == 0)
                continue;

            try
            {
                entries.Add(entryFactory(record));
            }
            catch (Exception ex)
            {
                throw BuildRowParseException(registerFile, csv, ex);
            }
        }

        return entries;
    }

    private static Exception BuildRowParseException(
        string registerFile,
        CsvReader csv,
        Exception ex)
    {
        var fileName = Path.GetFileName(registerFile);

        // CsvHelper annotations may mark Context/Parser nullable; don’t crash while reporting errors
        var rowNumber = csv.Context?.Parser?.Row ?? -1;
        var rowLabel = rowNumber >= 0
            ? rowNumber.ToString(CultureInfo.InvariantCulture)
            : "unknown";

        var message = $"Failed to parse row {rowLabel} in {fileName}: {ex.Message}";
        return new InvalidDataException(message, ex);
    }

    private static void ApplyRunningBalance<TEntry>(
        IList<TEntry> entries,
        decimal openingBalance,
        Func<TEntry, decimal> getBalance,
        Action<TEntry, decimal> setBalance,
        Func<TEntry, decimal> amountForBalance)
    {
        if (entries.Count == 0)
            return;

        setBalance(entries[0], openingBalance);

        for (int i = 1; i < entries.Count; i++)
        {
            var prevBalance = getBalance(entries[i - 1]);
            var currAmount = amountForBalance(entries[i]);
            setBalance(entries[i], prevBalance + currAmount);
        }
    }
}
