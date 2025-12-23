using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace KukiFinance.Services;

public static class RegisterService
{
    /// <summary>
    /// Loads a register from a CSV file, applies categories, inserts an opening balance row, and calculates running balances.
    /// Supports custom amount logic (e.g., for AMEX) via getAmountForBalance.
    /// </summary>
    public static List<TEntry> LoadRegister<TEntry>(
        string registerFile,
        string categoryFile,
        decimal openingBalance,
        Func<string[], TEntry> entryFactory,
        Func<TEntry, DateTime> getDate,
        Func<TEntry, decimal> getAmount,
        Action<TEntry, decimal> setBalance,
        TEntry openingEntry,
        Func<TEntry, decimal>? getAmountForBalance = null // Optional: for AMEX or other special cases
    )
    {
        // Read categories (if needed by the caller)
        var categories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(categoryFile))
        {
            foreach (var parts in File.ReadAllLines(categoryFile)
                .Skip(1)
                .Select(line => line.Split(','))
                .Where(parts => parts.Length >= 2))
            {
                var key = parts[0].Trim();
                var value = parts[1].Trim();
                if (!string.IsNullOrEmpty(key) && !categories.ContainsKey(key))
                    categories[key] = value;
            }
        }

        var entries = new List<TEntry>();

        if (File.Exists(registerFile))
        {
            foreach (var line in File.ReadAllLines(registerFile).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 2) // Allow for registers with fewer columns
                    continue;

                var entry = entryFactory(parts);
                entries.Add(entry);
            }
        }

        entries = entries.OrderBy(getDate).ToList();

        // Insert opening balance row at the top
        entries.Insert(0, openingEntry);

        // Use the custom getAmountForBalance if provided, otherwise use getAmount
        var amountFunc = getAmountForBalance ?? getAmount;

        // Set the balance for the opening entry
        setBalance(entries[0], getAmount(entries[0]));

        // Calculate running balances for the rest
        for (int i = 1; i < entries.Count; i++)
        {
            var prevBalance = (entries[i - 1] as dynamic).Balance;
            var currAmount = amountFunc(entries[i]);
            setBalance(entries[i], prevBalance + currAmount);
        }

        return entries;
    }
}