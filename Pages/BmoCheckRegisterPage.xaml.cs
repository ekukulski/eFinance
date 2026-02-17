using eFinance.Services;
using eFinance.Models;
using eFinance.Services;
using Microsoft.Maui.Controls;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using eFinance.Constants;
using eFinance.Helpers;
using eFinance.Models;

namespace eFinance.Pages
{
    public partial class BmoCheckRegisterPage : ContentPage
    {
        private readonly string registerFile = FilePathHelper.GeteFinancePath("BMOCheck.csv");
        private readonly string currentFile = FilePathHelper.GeteFinancePath("BMOCheckCurrent.csv");
        private readonly string transactionsFile = FilePathHelper.GeteFinancePath("transactionsCheck.csv");
        private readonly string categoryFile = FilePathHelper.GeteFinancePath("Category.csv");
        private readonly string numberFile = FilePathHelper.GeteFinancePath("CheckNumber.csv");
        private readonly decimal openingBalance = OpeningBalances.Get("BmoCheck");

        private readonly RegisterViewModel viewModel = new();

        public BmoCheckRegisterPage()
        {
            InitializeComponent();
            BindingContext = viewModel;
            LoadRegister();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            ReplaceDdaCheckDescriptions();
            LoadRegister();
        }

        /// <summary>
        /// Replaces "DDA CHECK" descriptions in BMOCheck.csv with the corresponding description from CheckNumber.csv.
        /// </summary>
        private void ReplaceDdaCheckDescriptions()
        {
            var checkNumberLookup = File.Exists(numberFile)
                ? File.ReadAllLines(numberFile)
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .Select(line => line.Split(','))
                    .Where(parts => parts.Length >= 2)
                    .ToDictionary(
                        parts => new string(parts[0].Where(char.IsDigit).ToArray()),
                        parts => parts[1].Trim(),
                        StringComparer.OrdinalIgnoreCase
                    )
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var allLines = File.Exists(registerFile)
                ? File.ReadAllLines(registerFile).ToList()
                : new List<string>();

            if (allLines.Count <= 1) return;

            bool changed = false;

            for (int i = 1; i < allLines.Count; i++)
            {
                var parts = allLines[i].Split(',');
                if (parts.Length < 5) continue;

                for (int j = 0; j < parts.Length; j++)
                    parts[j] = parts[j].Trim();

                if (parts[1].Equals("DDA CHECK", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(parts[4]))
                {
                    var checkNumber = new string(parts[4].Where(char.IsDigit).ToArray());
                    if (checkNumberLookup.TryGetValue(checkNumber, out var newDescription) &&
                        !string.IsNullOrWhiteSpace(newDescription))
                    {
                        parts[1] = newDescription;
                        allLines[i] = string.Join(",", parts);
                        changed = true;
                    }
                }
            }

            if (changed)
                File.WriteAllLines(registerFile, allLines);
        }

        private void LoadRegister()
        {
            if (!File.Exists(registerFile))
            {
                viewModel.Entries.Clear();
                viewModel.CurrentBalance = 0m;
                return;
            }

            List<RegistryEntry> records;
            try
            {
                using var reader = new StreamReader(registerFile);
                using var csv = new CsvReader(reader, new CsvConfiguration(CultureInfo.InvariantCulture)
                {
                    HeaderValidated = null,
                    MissingFieldFound = null,
                    BadDataFound = null
                });

                csv.Context.RegisterClassMap<RegistryEntryMap>();
                records = csv.GetRecords<RegistryEntry>().ToList();
            }
            catch
            {
                // If the CSV is malformed, keep UI stable rather than crashing the page.
                viewModel.Entries.Clear();
                viewModel.CurrentBalance = 0m;
                return;
            }

            var categoryMap = LoadCategoryMap();

            viewModel.Entries.Clear();

            // Determine opening-row date: one day before first record date if present, otherwise today.
            var firstDate = records
                .Select(r => r.Date)
                .Where(d => d.HasValue)
                .Select(d => d!.Value)
                .OrderBy(d => d)
                .FirstOrDefault();

            var openingDate = firstDate != default ? firstDate.AddDays(-1) : DateTime.Today;

            viewModel.Entries.Add(new RegistryEntry
            {
                Date = openingDate,
                Description = "OPENING BALANCE",
                Category = "Equity",
                CheckNumber = "",
                Amount = openingBalance,
                Balance = openingBalance
            });

            decimal runningBalance = openingBalance;

            foreach (var entry in records
                         .Where(e => e.Date.HasValue)
                         .OrderBy(e => e.Date!.Value))
            {
                var descKey = entry.Description ?? string.Empty;
                entry.Category = categoryMap.TryGetValue(descKey, out var cat) ? cat : string.Empty;

                runningBalance += entry.Amount ?? 0m;
                entry.Balance = runningBalance;

                viewModel.Entries.Add(entry);
            }

            // Include records that have no date (optional): push them to bottom, keep balance consistent.
            foreach (var entry in records.Where(e => !e.Date.HasValue))
            {
                var descKey = entry.Description ?? string.Empty;
                entry.Category = categoryMap.TryGetValue(descKey, out var cat) ? cat : string.Empty;

                runningBalance += entry.Amount ?? 0m;
                entry.Balance = runningBalance;

                viewModel.Entries.Add(entry);
            }

            viewModel.CurrentBalance = runningBalance;

            RegisterExporter.ExportRegisterWithBalance(
                viewModel.Entries.ToList(),
                currentFile,
                includeCheckNumber: false
            );

            viewModel.FilterEntries();
        }

        private Dictionary<string, string> LoadCategoryMap()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(categoryFile))
                return map;

            foreach (var line in File.ReadAllLines(categoryFile).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
                    map[key] = value;
            }

            return map;
        }

        // --- ForecastExpenseEntry and Forecasting Method ---

        public class ForecastExpenseEntry
        {
            public required string Account { get; set; }
            public required string Frequency { get; set; }
            public required string Month { get; set; }
            public int Day { get; set; }
            public required string Category { get; set; }
            public decimal Amount { get; set; }
            public DateTime Date { get; set; }
        }

        public List<(DateTime date, string category, decimal amount)> GetBmoCheckForecastedExpenses(int monthsAhead = 12)
        {
            var forecastFile = FilePathHelper.GeteFinancePath("ForecastExpenses.csv");
            var forecasted = new List<(DateTime date, string category, decimal amount)>();
            if (!File.Exists(forecastFile)) return forecasted;

            var today = DateTime.Today;
            var startDate = today.AddMonths(-3);
            var endDate = today.AddMonths(monthsAhead);

            var allItems = new List<ForecastExpenseEntry>();
            foreach (var line in File.ReadAllLines(forecastFile).Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length < 5) continue;

                if (parts.Length >= 6)
                {
                    allItems.Add(new ForecastExpenseEntry
                    {
                        Account = parts[0].Trim(),
                        Frequency = parts[1].Trim(),
                        Month = parts[2].Trim(),
                        Day = int.TryParse(parts[3], out var d) ? d : 1,
                        Category = parts[4].Trim(),
                        Amount = decimal.TryParse(parts[5], out var amt) ? amt : 0m
                    });
                }
                else
                {
                    allItems.Add(new ForecastExpenseEntry
                    {
                        Account = "BMO Check",
                        Frequency = parts[0].Trim(),
                        Month = parts[1].Trim(),
                        Day = int.TryParse(parts[2], out var d2) ? d2 : 1,
                        Category = parts[3].Trim(),
                        Amount = decimal.TryParse(parts[4], out var amt2) ? amt2 : 0m
                    });
                }
            }

            var expanded = new List<ForecastExpenseEntry>();
            foreach (var item in allItems)
            {
                if (string.IsNullOrWhiteSpace(item.Frequency)) continue;

                if (item.Frequency.Equals("Once", StringComparison.OrdinalIgnoreCase))
                {
                    int monthNum = DateTime.TryParseExact(item.Month, "MMMM", CultureInfo.CurrentCulture, DateTimeStyles.None, out var mdt)
                        ? mdt.Month
                        : startDate.Month;
                    int year = startDate.Year;
                    int dayOfMonth = Math.Min(item.Day, DateTime.DaysInMonth(year, monthNum));
                    var date = new DateTime(year, monthNum, dayOfMonth);
                    if (date >= startDate && date <= endDate)
                    {
                        expanded.Add(new ForecastExpenseEntry
                        {
                            Account = item.Account,
                            Frequency = item.Frequency,
                            Month = item.Month,
                            Day = dayOfMonth,
                            Category = item.Category,
                            Amount = item.Amount,
                            Date = date
                        });
                    }
                }
                else if (item.Frequency.Equals("Monthly", StringComparison.OrdinalIgnoreCase))
                {
                    for (var dt = new DateTime(startDate.Year, startDate.Month, 1); dt <= endDate; dt = dt.AddMonths(1))
                    {
                        int dayOfMonth = Math.Min(item.Day, DateTime.DaysInMonth(dt.Year, dt.Month));
                        var date = new DateTime(dt.Year, dt.Month, dayOfMonth);
                        if (date >= startDate && date <= endDate)
                        {
                            expanded.Add(new ForecastExpenseEntry
                            {
                                Account = item.Account,
                                Frequency = item.Frequency,
                                Month = item.Month,
                                Day = dayOfMonth,
                                Category = item.Category,
                                Amount = item.Amount,
                                Date = date
                            });
                        }
                    }
                }
                else if (item.Frequency.Equals("Annual", StringComparison.OrdinalIgnoreCase))
                {
                    int monthNum = DateTime.TryParseExact(item.Month, "MMMM", CultureInfo.CurrentCulture, DateTimeStyles.None, out var mdt) ? mdt.Month : startDate.Month;
                    for (int y = startDate.Year; y <= endDate.Year; y++)
                    {
                        int dayOfMonth = Math.Min(item.Day, DateTime.DaysInMonth(y, monthNum));
                        var date = new DateTime(y, monthNum, dayOfMonth);
                        if (date >= startDate && date <= endDate)
                        {
                            expanded.Add(new ForecastExpenseEntry
                            {
                                Account = item.Account,
                                Frequency = item.Frequency,
                                Month = item.Month,
                                Day = dayOfMonth,
                                Category = item.Category,
                                Amount = item.Amount,
                                Date = date
                            });
                        }
                    }
                }
                else if (item.Frequency.EndsWith("Months", StringComparison.OrdinalIgnoreCase))
                {
                    int freqMonths = int.TryParse(item.Frequency.Split(' ')[0], out var n) ? n : 1;
                    int startMonth = item.Month.Equals("All", StringComparison.OrdinalIgnoreCase)
                        ? startDate.Month
                        : DateTime.TryParseExact(item.Month, "MMMM", CultureInfo.CurrentCulture, DateTimeStyles.None, out var smdt) ? smdt.Month : startDate.Month;

                    var firstDate = new DateTime(startDate.Year, startMonth, Math.Min(item.Day, DateTime.DaysInMonth(startDate.Year, startMonth)));
                    if (firstDate < startDate) firstDate = firstDate.AddMonths(freqMonths);

                    for (var dt = firstDate; dt <= endDate; dt = dt.AddMonths(freqMonths))
                    {
                        int dayOfMonth = Math.Min(item.Day, DateTime.DaysInMonth(dt.Year, dt.Month));
                        var date = new DateTime(dt.Year, dt.Month, dayOfMonth);
                        if (date >= startDate && date <= endDate)
                        {
                            expanded.Add(new ForecastExpenseEntry
                            {
                                Account = item.Account,
                                Frequency = item.Frequency,
                                Month = item.Month,
                                Day = dayOfMonth,
                                Category = item.Category,
                                Amount = item.Amount,
                                Date = date
                            });
                        }
                    }
                }
            }

            foreach (var e in expanded.Where(x => x.Account.Equals("BMO Check", StringComparison.OrdinalIgnoreCase)))
            {
                if (e.Day <= 0) continue;
                DateTime candidate = ExpandedDateFor(e, today, endDate, expanded);
                if (candidate >= today && candidate <= endDate)
                    forecasted.Add((candidate, e.Category, e.Amount));
            }

            var derivedPayments = ComputeCardPaymentsForBmoFromExpanded(expanded, today, endDate);
            foreach (var dp in derivedPayments)
                forecasted.Add((dp.date, dp.category, dp.amount));

            return forecasted.OrderBy(f => f.date).ToList();
        }

        private DateTime ExpandedDateFor(ForecastExpenseEntry item, DateTime nowLocal, DateTime horizonLocal, List<ForecastExpenseEntry> expanded)
        {
            if (item.Frequency.Equals("Once", StringComparison.OrdinalIgnoreCase))
            {
                int monthNum = DateTime.TryParseExact(item.Month, "MMMM", CultureInfo.CurrentCulture, DateTimeStyles.None, out var mdt)
                    ? mdt.Month
                    : nowLocal.Month;
                int year = nowLocal.Year;
                int dayOfMonth = Math.Min(item.Day, DateTime.DaysInMonth(year, monthNum));
                return new DateTime(year, monthNum, dayOfMonth);
            }

            for (var dt = new DateTime(nowLocal.Year, nowLocal.Month, 1); dt <= horizonLocal; dt = dt.AddMonths(1))
            {
                int dayOfMonth = Math.Min(item.Day, DateTime.DaysInMonth(dt.Year, dt.Month));
                var forecastDate = new DateTime(dt.Year, dt.Month, dayOfMonth);
                if (expanded.Any(x =>
                    x.Account == item.Account &&
                    x.Category == item.Category &&
                    x.Amount == item.Amount &&
                    x.Day == dayOfMonth &&
                    (DateTime.TryParseExact(x.Month, "MMMM", CultureInfo.CurrentCulture, DateTimeStyles.None, out var mdt2) ? mdt2.Month : dt.Month) == dt.Month))
                    return forecastDate;
            }
            return nowLocal;
        }

        private DateTime GetBillingStartForCard(string cardName, int dueYear, int dueMonth)
        {
            if (cardName.Equals("AMEX", StringComparison.OrdinalIgnoreCase))
            {
                var startMonthDate = new DateTime(dueYear, dueMonth, 1).AddMonths(-2);
                int dom = Math.Min(25, DateTime.DaysInMonth(startMonthDate.Year, startMonthDate.Month));
                return new DateTime(startMonthDate.Year, startMonthDate.Month, dom);
            }

            var startMonth = new DateTime(dueYear, dueMonth, 1).AddMonths(-1);
            int dom2 = Math.Min(2, DateTime.DaysInMonth(startMonth.Year, startMonth.Month));
            return new DateTime(startMonth.Year, startMonth.Month, dom2);
        }

        private DateTime GetBillingEndForCard(string cardName, int dueYear, int dueMonth)
        {
            if (cardName.Equals("AMEX", StringComparison.OrdinalIgnoreCase))
            {
                var endMonthDate = new DateTime(dueYear, dueMonth, 1).AddMonths(-1);
                int dom = Math.Min(24, DateTime.DaysInMonth(endMonthDate.Year, endMonthDate.Month));
                return new DateTime(endMonthDate.Year, endMonthDate.Month, dom);
            }

            int dom2 = Math.Min(1, DateTime.DaysInMonth(dueYear, dueMonth));
            return new DateTime(dueYear, dueMonth, dom2);
        }

        private Dictionary<string, Dictionary<DateTime, decimal>> ComputeFutureCardStatementAmountsFromExpanded(
            List<ForecastExpenseEntry> expanded,
            DateTime horizonStart,
            DateTime horizonEnd)
        {
            var today = horizonStart.Date;

            var cards = new[]
            {
                new { Name = "AMEX",       DueDay = 8  },
                new { Name = "Visa",       DueDay = 26 },
                new { Name = "MasterCard", DueDay = 14 }
            };

            var dueItems = new List<(string card, DateTime dueDate, DateTime cutoffDate)>();
            var cursor = new DateTime(today.Year, today.Month, 1);
            var endMonth = new DateTime(horizonEnd.Year, horizonEnd.Month, 1);

            while (cursor <= endMonth)
            {
                int y = cursor.Year;
                int m = cursor.Month;

                foreach (var c in cards)
                {
                    int dueDay = Math.Min(c.DueDay, DateTime.DaysInMonth(y, m));
                    var dueDate = new DateTime(y, m, dueDay);

                    if (dueDate <= today) continue;
                    if (dueDate > horizonEnd) continue;

                    var cutoff = GetBillingEndForCard(c.Name, y, m);
                    dueItems.Add((c.Name, dueDate.Date, cutoff.Date));
                }

                cursor = cursor.AddMonths(1);
            }

            var result = new Dictionary<string, Dictionary<DateTime, decimal>>(StringComparer.OrdinalIgnoreCase);
            if (dueItems.Count == 0) return result;

            var simEnd = dueItems.Max(x => x.dueDate).Date;

            var forecastByCardByDate = expanded
                .Where(e => !string.IsNullOrWhiteSpace(e.Account))
                .GroupBy(e => e.Account, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => x.Date.Date)
                          .ToDictionary(gg => gg.Key, gg => gg.Sum(x => x.Amount)));

            var cutoffToDueDates = dueItems
                .GroupBy(x => (x.card, x.cutoffDate))
                .ToDictionary(g => g.Key, g => g.Select(x => x.dueDate).Distinct().ToList());

            var dueToCutoff = dueItems.ToDictionary(x => (x.card, x.dueDate), x => x.cutoffDate);

            var running = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in cards)
            {
                var rows = ReadCardRegister(c.Name);
                var balToday = GetLastBalanceOnOrBefore(rows, today);
                if (balToday.HasValue)
                {
                    running[c.Name] = balToday.Value;
                }
                else
                {
                    var firstDate = rows.Any() ? rows.Min(r => r.Date) : today;
                    running[c.Name] = SumTransactionsBetweenExcludingPayments(rows, firstDate, today);
                }

                result[c.Name] = new Dictionary<DateTime, decimal>();
            }

            var statementAtCutoff = new Dictionary<(string card, DateTime cutoff), decimal>();

            for (var d = today.AddDays(1); d <= simEnd; d = d.AddDays(1))
            {
                foreach (var c in cards)
                {
                    if (forecastByCardByDate.TryGetValue(c.Name, out var byDate) &&
                        byDate.TryGetValue(d.Date, out var amt))
                    {
                        running[c.Name] += amt;
                    }

                    var cutoffKey = (c.Name, d.Date);
                    if (cutoffToDueDates.TryGetValue(cutoffKey, out var dueDates))
                    {
                        var stmt = Math.Max(0m, -running[c.Name]);
                        statementAtCutoff[(c.Name, d.Date)] = stmt;

                        foreach (var dd in dueDates)
                            result[c.Name][dd] = stmt;
                    }

                    var dueKey = (c.Name, d.Date);
                    if (dueToCutoff.TryGetValue(dueKey, out var cutoff))
                    {
                        if (statementAtCutoff.TryGetValue((c.Name, cutoff), out var statementAtCutoffAmount))
                        {
                            var needed = Math.Max(0m, -running[c.Name]);
                            var payAmt = Math.Min(statementAtCutoffAmount, needed);

                            if (payAmt > 0m)
                            {
                                running[c.Name] += payAmt;
                                if (running[c.Name] > 0m)
                                    running[c.Name] = 0m;
                            }

                            result[c.Name][d.Date] = payAmt;
                        }
                    }
                }
            }

            return result;
        }

        private List<(DateTime date, string category, decimal amount)> ComputeCardPaymentsForBmoFromExpanded(
            List<ForecastExpenseEntry> expanded,
            DateTime from,
            DateTime to)
        {
            var results = new List<(DateTime date, string category, decimal amount)>();

            var cards = new[]
            {
                new { Name = "AMEX",       DueDay = 8  },
                new { Name = "Visa",       DueDay = 26 },
                new { Name = "MasterCard", DueDay = 14 }
            };

            var today = DateTime.Today;

            var simulated = ComputeFutureCardStatementAmountsFromExpanded(expanded, today, to);

            foreach (var card in cards)
            {
                var cursor = new DateTime(from.Year, from.Month, 1);
                var endMonth = new DateTime(to.Year, to.Month, 1);

                while (cursor <= endMonth)
                {
                    int year = cursor.Year;
                    int month = cursor.Month;

                    int dueDay = Math.Min(card.DueDay, DateTime.DaysInMonth(year, month));
                    var dueDate = new DateTime(year, month, dueDay).Date;

                    if (dueDate < from.Date || dueDate > to.Date || dueDate <= today)
                    {
                        cursor = cursor.AddMonths(1);
                        continue;
                    }

                    DateTime billingStart = GetBillingStartForCard(card.Name, year, month);
                    DateTime billingEnd = GetBillingEndForCard(card.Name, year, month);

                    decimal amountDue;

                    if (dueDate.Year == today.Year && dueDate.Month == today.Month)
                    {
                        var cardTx = ReadCardRegister(card.Name);

                        var balanceOnOrBefore = GetLastBalanceOnOrBefore(cardTx, billingEnd);
                        if (balanceOnOrBefore.HasValue)
                        {
                            amountDue = Math.Max(0m, -balanceOnOrBefore.Value);
                        }
                        else
                        {
                            DateTime effectiveEnd = billingEnd;
                            var lastTxDate = cardTx.Any() ? cardTx.Max(t => t.Date) : (DateTime?)null;
                            if (lastTxDate.HasValue && lastTxDate.Value < billingEnd)
                                effectiveEnd = lastTxDate.Value;

                            decimal txSum = SumTransactionsBetweenExcludingPayments(cardTx, billingStart, effectiveEnd);

                            var forecastSum = expanded
                                .Where(f => f.Account.Equals(card.Name, StringComparison.OrdinalIgnoreCase)
                                            && f.Date >= billingStart && f.Date <= billingEnd)
                                .Sum(f => f.Amount);

                            amountDue = Math.Abs(txSum + forecastSum);
                        }
                    }
                    else
                    {
                        if (simulated.TryGetValue(card.Name, out var byDue) &&
                            byDue.TryGetValue(dueDate, out var stmt))
                        {
                            amountDue = stmt;
                        }
                        else
                        {
                            var forecastSum = expanded
                                .Where(f => f.Account.Equals(card.Name, StringComparison.OrdinalIgnoreCase)
                                            && f.Date >= billingStart && f.Date <= billingEnd)
                                .Sum(f => f.Amount);

                            amountDue = Math.Abs(forecastSum);
                        }
                    }

                    if (amountDue > 0m)
                        results.Add((dueDate, $"Card Payment - {card.Name}", -amountDue));

                    cursor = cursor.AddMonths(1);
                }
            }

            return results;
        }

        private record CardRow(DateTime Date, decimal Amount, decimal? Balance, string Description);

        private List<CardRow> ReadCardRegister(string cardName)
        {
            string fileName = cardName switch
            {
                "AMEX" => "AMEXCurrent.csv",
                "Visa" => "VisaCurrent.csv",
                "MasterCard" => "MasterCardCurrent.csv",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(fileName))
                return new List<CardRow>();

            string basePath = FilePathHelper.GeteFinancePath("");
            string csvFile = Path.Combine(basePath, fileName);
            if (!File.Exists(csvFile)) return new List<CardRow>();

            var lines = File.ReadAllLines(csvFile)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToArray();

            if (lines.Length < 2) return new List<CardRow>();

            var headers = lines[0].Split(',');
            int dateIdx = Array.FindIndex(headers, h => h.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));
            int amtIdx = Array.FindIndex(headers, h => h.Trim().Equals("Amount", StringComparison.OrdinalIgnoreCase));
            int balIdx = Array.FindIndex(headers, h => h.Trim().Equals("Balance", StringComparison.OrdinalIgnoreCase));
            int descIdx = Array.FindIndex(headers, h =>
                h.Trim().Equals("Description", StringComparison.OrdinalIgnoreCase) ||
                h.Trim().Equals("Desc", StringComparison.OrdinalIgnoreCase) ||
                h.Trim().Equals("Memo", StringComparison.OrdinalIgnoreCase) ||
                h.Trim().Equals("Category", StringComparison.OrdinalIgnoreCase));

            if (dateIdx < 0 || amtIdx < 0)
                return new List<CardRow>();

            var result = new List<CardRow>();
            string[] formats = { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy" };

            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length <= Math.Max(dateIdx, amtIdx)) continue;

                if (!DateTime.TryParseExact(parts[dateIdx].Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    continue;

                if (!decimal.TryParse(parts[amtIdx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                    continue;

                decimal? bal = null;
                if (balIdx >= 0 && parts.Length > balIdx &&
                    decimal.TryParse(parts[balIdx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    bal = b;
                }

                string desc = (descIdx >= 0 && parts.Length > descIdx) ? parts[descIdx].Trim() : string.Empty;

                result.Add(new CardRow(dt, amt, bal, desc));
            }

            return result.OrderBy(r => r.Date).ToList();
        }

        private decimal? GetLastBalanceOnOrBefore(List<CardRow> rows, DateTime date)
        {
            var withBalance = rows
                .Where(r => r.Balance.HasValue && r.Date <= date)
                .OrderBy(r => r.Date)
                .ToList();

            if (withBalance.Count == 0) return null;
            return withBalance[^1].Balance;
        }

        private decimal SumTransactionsBetweenExcludingPayments(List<CardRow> rows, DateTime start, DateTime end)
        {
            var paymentKeywords = new[]
            {
                "payment", "pmt", "pay", "paid", "online transfer", "bill pay", "billpay", "transfer",
                "autopay", "auto pay", "ddacheck", "dda check"
            };

            static bool ContainsAny(string haystack, IEnumerable<string> needles)
            {
                foreach (var n in needles)
                    if (haystack.Contains(n, StringComparison.OrdinalIgnoreCase))
                        return true;
                return false;
            }

            var sum = rows
                .Where(r => r.Date >= start && r.Date <= end)
                .Where(r => !string.IsNullOrWhiteSpace(r.Description) && !ContainsAny(r.Description, paymentKeywords))
                .Sum(r => r.Amount);

            return sum;
        }

        private async void AddTransactionsButton_Clicked(object sender, EventArgs e)
        {
            if (!File.Exists(transactionsFile))
            {
                await DisplayAlert("Error", "No new transactions file found.", "OK");
                return;
            }

            var newLines = File.ReadAllLines(transactionsFile)
                .Skip(1)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (newLines.Count > 0)
                File.AppendAllLines(registerFile, newLines);

            var header = File.ReadLines(transactionsFile).FirstOrDefault() ?? string.Empty;
            File.WriteAllText(transactionsFile, header + Environment.NewLine);

            LoadRegister();

            await DisplayAlert("Success", "New transactions added.", "OK");
        }

        private async void ManualTransactionEntryButton_Clicked(object sender, EventArgs e)
        {
            string dateStr = await DisplayPromptAsync("Manual Entry", "Enter date (MM/dd/yyyy):");
            if (string.IsNullOrWhiteSpace(dateStr) ||
                !DateTime.TryParseExact(dateStr.Trim(), new[] { "MM/dd/yyyy", "M/d/yyyy" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                await DisplayAlert("Invalid", "Please enter a valid date (MM/dd/yyyy).", "OK");
                return;
            }

            string description = await DisplayPromptAsync("Manual Entry", "Enter description:");
            if (string.IsNullOrWhiteSpace(description))
            {
                await DisplayAlert("Invalid", "Please enter a description.", "OK");
                return;
            }

            string amountStr = await DisplayPromptAsync("Manual Entry", "Enter amount:");
            if (string.IsNullOrWhiteSpace(amountStr) ||
                !decimal.TryParse(amountStr.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amount))
            {
                await DisplayAlert("Invalid", "Please enter a valid amount.", "OK");
                return;
            }

            string csvLine = $"{date:MM/dd/yyyy},{description.Trim()},{amount.ToString(CultureInfo.InvariantCulture)},USD,,,,,";

            if (!File.Exists(registerFile))
            {
                string header = "POSTED DATE,DESCRIPTION,AMOUNT,CURRENCY,TRANSACTION REFERENCE NUMBER,FI TRANSACTION REFERENCE,TYPE,CREDIT/DEBIT,ORIGINAL AMOUNT";
                File.WriteAllText(registerFile, header + Environment.NewLine);
            }

            File.AppendAllText(registerFile, csvLine + Environment.NewLine);

            LoadRegister();

            await DisplayAlert("Success", "Manual transaction added.", "OK");
        }

        private void RegisterCollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No selection logic needed
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            if (RegisterCollectionView.SelectedItem is not RegistryEntry selectedEntry)
            {
                await DisplayAlert("Edit", "Please select a row to edit.", "OK");
                return;
            }

            string newDescription = await DisplayPromptAsync(
                "Edit Description",
                "Enter new description:",
                initialValue: selectedEntry.Description);

            if (string.IsNullOrWhiteSpace(newDescription) ||
                string.Equals(newDescription, selectedEntry.Description, StringComparison.Ordinal))
            {
                return;
            }

            newDescription = newDescription.Trim();
            selectedEntry.Description = newDescription;

            var categoryMap = LoadCategoryMap();
            selectedEntry.Category = categoryMap.TryGetValue(newDescription, out var newCategory) ? newCategory : string.Empty;

            // Update the CSV file line (best-effort match)
            UpdateRegisterDescriptionInFile(selectedEntry, newDescription);

            LoadRegister();
        }

        private void UpdateRegisterDescriptionInFile(RegistryEntry selectedEntry, string newDescription)
        {
            if (!File.Exists(registerFile)) return;

            var allLines = File.ReadAllLines(registerFile).ToList();
            int startIdx = (allLines.Count > 0 && allLines[0].Contains("DESCRIPTION", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

            for (int i = startIdx; i < allLines.Count; i++)
            {
                var parts = allLines[i].Split(',');
                if (parts.Length < 3) continue;

                if (!TryParseRegisterDate(parts[0], out var date)) continue;
                if (!TryParseDecimal(parts[2], out var amount)) continue;

                var entryDate = selectedEntry.Date;
                var entryAmount = selectedEntry.Amount;

                // Compare safely against nullable model fields
                if (entryDate.HasValue && entryAmount.HasValue &&
                    date.Date == entryDate.Value.Date &&
                    amount == entryAmount.Value)
                {
                    if (parts.Length > 1)
                    {
                        parts[1] = newDescription;
                        allLines[i] = string.Join(",", parts);
                    }
                    break;
                }
            }

            File.WriteAllLines(registerFile, allLines);
        }

        private static bool TryParseRegisterDate(string raw, out DateTime date)
        {
            var s = (raw ?? string.Empty).Trim();
            var formats = new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd" };

            return DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                   || DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static bool TryParseDecimal(string raw, out decimal value)
        {
            var s = (raw ?? string.Empty).Trim();
            return decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out value);
        }

        private async void CopyDescriptionButton_Clicked(object sender, EventArgs e)
        {
            if (RegisterCollectionView.SelectedItem is not RegistryEntry entry)
            {
                await DisplayAlert("Copy", "Please select a row to copy.", "OK");
                return;
            }

            await Clipboard.Default.SetTextAsync(entry.Description ?? string.Empty);
            await DisplayAlert("Copied", "Description copied to clipboard.", "OK");
        }

        private async void DeleteTransactionButton_Clicked(object sender, EventArgs e)
        {
            if (RegisterCollectionView.SelectedItem is not RegistryEntry entry)
            {
                await DisplayAlert("Delete", "Please select a row to delete.", "OK");
                return;
            }

            bool confirm = await DisplayAlert("Delete", "Are you sure you want to delete this transaction?", "Yes", "No");
            if (!confirm) return;

            viewModel.Entries.Remove(entry);

            if (!File.Exists(registerFile))
            {
                LoadRegister();
                return;
            }

            var allLines = File.ReadAllLines(registerFile).ToList();
            int startIdx = (allLines.Count > 0 && allLines[0].Contains("DESCRIPTION", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

            for (int i = startIdx; i < allLines.Count; i++)
            {
                var parts = allLines[i].Split(',');
                if (parts.Length < 3) continue;

                if (!TryParseRegisterDate(parts[0], out var date)) continue;
                if (!TryParseDecimal(parts[2], out var amt)) continue;

                if (entry.Date.HasValue && entry.Amount.HasValue &&
                    date.Date == entry.Date.Value.Date &&
                    amt == entry.Amount.Value)
                {
                    // If entry has a check number, try to match it against column 5 if present
                    if (!string.IsNullOrWhiteSpace(entry.CheckNumber) && parts.Length > 4)
                    {
                        var fileCheck = parts[4].Trim();
                        var entryCheck = entry.CheckNumber.Trim();
                        if (!string.Equals(fileCheck, entryCheck, StringComparison.OrdinalIgnoreCase))
                            continue;
                    }

                    allLines.RemoveAt(i);
                    break;
                }
            }

            File.WriteAllLines(registerFile, allLines);

            LoadRegister();
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
