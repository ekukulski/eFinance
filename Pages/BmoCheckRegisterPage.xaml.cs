using KukiFinance.Models;
using KukiFinance.Services;
using Microsoft.Maui.Controls;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Globalization;
using CsvHelper;
using KukiFinance.Constants;
using KukiFinance.Helpers;

namespace KukiFinance.Pages
{
    public partial class BmoCheckRegisterPage : ContentPage
    {
        private readonly string registerFile = FilePathHelper.GetKukiFinancePath("BMOCheck.csv");
        private readonly string currentFile = FilePathHelper.GetKukiFinancePath("BMOCheckCurrent.csv");
        private readonly string transactionsFile = FilePathHelper.GetKukiFinancePath("transactionsCheck.csv");
        private readonly string categoryFile = FilePathHelper.GetKukiFinancePath("Category.csv");
        private readonly string numberFile = FilePathHelper.GetKukiFinancePath("CheckNumber.csv");
        private readonly decimal openingBalance = OpeningBalances.Get("BmoCheck");

        private readonly RegisterViewModel viewModel = new();

        public BmoCheckRegisterPage()
        {
            InitializeComponent();
            WindowCenteringService.CenterWindow(1435, 1375);
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
        /// Runs automatically when the page loads.
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
                        parts => parts[1].Trim()
                    )
                : new Dictionary<string, string>();

            var allLines = File.Exists(registerFile) ? File.ReadAllLines(registerFile).ToList() : new List<string>();
            bool changed = false;
            if (allLines.Count > 1)
            {
                for (int i = 1; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    if (parts.Length < 5) continue;

                    for (int j = 0; j < parts.Length; j++)
                        parts[j] = parts[j].Trim();

                    if (parts[1] == "DDA CHECK" && !string.IsNullOrWhiteSpace(parts[4]))
                    {
                        var checkNumber = new string(parts[4].Where(char.IsDigit).ToArray());
                        if (checkNumberLookup.TryGetValue(checkNumber, out var newDescription) && !string.IsNullOrWhiteSpace(newDescription))
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
            using (var reader = new StreamReader(registerFile))
            using (var csv = new CsvReader(reader, new CsvHelper.Configuration.CsvConfiguration(CultureInfo.InvariantCulture)
            {
                HeaderValidated = null,
                MissingFieldFound = null
            }))
            {
                csv.Context.RegisterClassMap<RegistryEntryMap>();
                records = csv.GetRecords<RegistryEntry>().ToList();
            }

            // Optionally, set category in code using your category file
            var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(categoryFile))
            {
                foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (!string.IsNullOrEmpty(key) && !categoryMap.ContainsKey(key))
                        categoryMap[key] = value;
                }
            }

            viewModel.Entries.Clear();

            // Insert the OPENING BALANCE row at the top
            viewModel.Entries.Add(new RegistryEntry
            {
                Date = records.Count > 0 ? (records[0].Date ?? DateTime.Today).AddDays(-1) : DateTime.Today,
                Description = "OPENING BALANCE",
                Category = "Equity",
                CheckNumber = "",
                Amount = openingBalance,
                Balance = openingBalance
            });

            decimal runningBalance = openingBalance;
            foreach (var entry in records.OrderBy(e => e.Date))
            {
                entry.Category = categoryMap.TryGetValue(entry.Description ?? "", out var cat) ? cat : "";
                runningBalance += entry.Amount ?? 0;
                entry.Balance = runningBalance;
                viewModel.Entries.Add(entry);
            }
            viewModel.CurrentBalance = runningBalance;

            // Export the current, display-ready register to BMOCheckCurrent.csv using the in-memory list
            RegisterExporter.ExportRegisterWithBalance(
                viewModel.Entries.ToList(),
                currentFile,
                includeCheckNumber: false
            );
            viewModel.FilterEntries();
        }

        // --- ForecastExpenseEntry and Forecasting Method ---

        public class ForecastExpenseEntry
        {
            public string Account { get; set; }
            public string Frequency { get; set; }
            public string Month { get; set; }
            public int Day { get; set; }
            public string Category { get; set; }
            public decimal Amount { get; set; }

            // concrete occurrence date for expanded items
            public DateTime Date { get; set; }
        }

        /// <summary>
        /// Returns a list of forecasted expenses for the BMO Check register, starting from today.
        /// Reads ForecastExpenses.csv which may include an Account column.
        /// Includes derived BMO payment entries for AMEX/Visa/MasterCard due dates (per configured billing windows).
        /// </summary>
        public List<(DateTime date, string category, decimal amount)> GetBmoCheckForecastedExpenses(int monthsAhead = 12)
        {
            var forecastFile = FilePathHelper.GetKukiFinancePath("ForecastExpenses.csv");
            var forecasted = new List<(DateTime date, string category, decimal amount)>();
            if (!File.Exists(forecastFile)) return forecasted;

            var today = DateTime.Today;
            var startDate = today.AddMonths(-3); // include past months so billing windows are covered
            var endDate = today.AddMonths(monthsAhead);

            // First collect all forecast items (account-aware)
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
                    // legacy format => default to BMO Check
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

            // Expand scheduled items into date instances starting at startDate
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
                    int startMonth = item.Month.Equals("All", StringComparison.OrdinalIgnoreCase) ? startDate.Month : DateTime.TryParseExact(item.Month, "MMMM", CultureInfo.CurrentCulture, DateTimeStyles.None, out var smdt) ? smdt.Month : startDate.Month;
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

            // Add expanded items assigned to BMO Check into forecasted results
            foreach (var e in expanded.Where(x => x.Account.Equals("BMO Check", StringComparison.OrdinalIgnoreCase)))
            {
                if (e.Day <= 0) continue;
                DateTime candidate = ExpandedDateFor(e, today, endDate, expanded);
                if (candidate >= today && candidate <= endDate)
                    forecasted.Add((candidate, e.Category, e.Amount));
            }

            // Now compute derived BMO payments for card accounts per billing windows
            var derivedPayments = ComputeCardPaymentsForBmoFromExpanded(expanded, today, endDate);
            foreach (var dp in derivedPayments)
                forecasted.Add((dp.date, dp.category, dp.amount));

            // sort by date
            return forecasted.OrderBy(f => f.date).ToList();
        }

        // local helper to determine the concrete date used for an expanded ForecastExpenseEntry
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

        // Compute card payments that should appear in BMO Check on card due dates.
        // For each card/month: determine billingStart..billingEnd, compute amount due:
        //  - Prefer using the card register Balance at billingEnd if available (last balance on or before billingEnd).
        //  - Otherwise sum transactions in the billing window (charges + credits) excluding detected payments.
        // If billingEnd is in the future, use the last available transaction date as effective end.
        // NOTE: MasterCard billing window updated to match Visa per requested behavior (start = 2nd of prior month, end = 1st of month).
        private List<(DateTime date, string category, decimal amount)> ComputeCardPaymentsForBmoFromExpanded(List<ForecastExpenseEntry> expanded, DateTime from, DateTime to)
        {
            var results = new List<(DateTime date, string category, decimal amount)>();
            var cards = new[]
            {
                new { Name = "AMEX", DueDay = 8 },
                new { Name = "Visa", DueDay = 26 },
                new { Name = "MasterCard", DueDay = 14 }
            };

            foreach (var card in cards)
            {
                var cursor = new DateTime(from.Year, from.Month, 1);
                var endMonth = new DateTime(to.Year, to.Month, 1);
                while (cursor <= endMonth)
                {
                    int year = cursor.Year;
                    int month = cursor.Month;
                    int dueDay = Math.Min(card.DueDay, DateTime.DaysInMonth(year, month));
                    var dueDate = new DateTime(year, month, dueDay);

                    DateTime billingStart, billingEnd;
                    if (card.Name == "AMEX")
                    {
                        // AMEX billing: start = 25th of month (M-2), end = 24th of month (M-1)
                        var startMonthDate = new DateTime(year, month, 1).AddMonths(-2);
                        var endMonthDate = new DateTime(year, month, 1).AddMonths(-1);
                        int startDay = Math.Min(25, DateTime.DaysInMonth(startMonthDate.Year, startMonthDate.Month));
                        int endDay = Math.Min(24, DateTime.DaysInMonth(endMonthDate.Year, endMonthDate.Month));
                        billingStart = new DateTime(startMonthDate.Year, startMonthDate.Month, startDay);
                        billingEnd = new DateTime(endMonthDate.Year, endMonthDate.Month, endDay);
                    }
                    else if (card.Name == "Visa")
                    {
                        // Visa billing: start = 2nd of month (M-1), end = 1st of month M
                        var startMonthDate = new DateTime(year, month, 1).AddMonths(-1);
                        var endMonthDate = new DateTime(year, month, 1);
                        int startDay = Math.Min(2, DateTime.DaysInMonth(startMonthDate.Year, startMonthDate.Month));
                        int endDay = Math.Min(1, DateTime.DaysInMonth(endMonthDate.Year, endMonthDate.Month));
                        billingStart = new DateTime(startMonthDate.Year, startMonthDate.Month, startDay);
                        billingEnd = new DateTime(endMonthDate.Year, endMonthDate.Month, endDay);
                    }
                    else // MasterCard updated to same billing window as Visa (per request)
                    {
                        // MasterCard billing: start = 2nd of month (M-1), end = 1st of month M
                        var startMonthDate = new DateTime(year, month, 1).AddMonths(-1);
                        var endMonthDate = new DateTime(year, month, 1);
                        int startDay = Math.Min(2, DateTime.DaysInMonth(startMonthDate.Year, startMonthDate.Month));
                        int endDay = Math.Min(1, DateTime.DaysInMonth(endMonthDate.Year, endMonthDate.Month));
                        billingStart = new DateTime(startMonthDate.Year, startMonthDate.Month, startDay);
                        billingEnd = new DateTime(endMonthDate.Year, endMonthDate.Month, endDay);
                    }

                    // Forecast additions for the card inside the billing window should be included when statement not yet posted.
                    var forecastSum = expanded
                        .Where(f => f.Account.Equals(card.Name, StringComparison.OrdinalIgnoreCase)
                                    && f.Date >= billingStart && f.Date <= billingEnd)
                        .Sum(f => f.Amount);

                    // Read card register transactions and balances
                    var cardTx = ReadCardRegister(card.Name);

                    decimal amountDue = 0m;

                    // Try to obtain balance on or before billingEnd
                    var balanceOnOrBefore = GetLastBalanceOnOrBefore(cardTx, billingEnd);
                    if (balanceOnOrBefore.HasValue)
                    {
                        // Use absolute value of balance as the approximate amount due
                        amountDue = Math.Abs(balanceOnOrBefore.Value);
                    }
                    else
                    {
                        // No balance column or no balance row found — sum transactions between billingStart..billingEnd
                        DateTime effectiveEnd = billingEnd;
                        var lastTxDate = cardTx.Any() ? cardTx.Max(t => t.Date) : (DateTime?)null;
                        if (lastTxDate.HasValue && lastTxDate.Value < billingEnd)
                            effectiveEnd = lastTxDate.Value;

                        decimal txSum = SumTransactionsBetweenExcludingPayments(cardTx, billingStart, effectiveEnd);

                        // Include forecasted occurrences (forecastSum) that might not be in card register yet
                        decimal combined = txSum + forecastSum;

                        // Use absolute combined amount as amount due
                        amountDue = Math.Abs(combined);
                    }

                    if (amountDue > 0m)
                    {
                        // BMO payment is a withdrawal from BMO → negative amount in the BMO register
                        results.Add((dueDate, $"Card Payment - {card.Name}", -amountDue));
                    }

                    cursor = cursor.AddMonths(1);
                }
            }

            return results;
        }

        // Represents a row from a card register current CSV
        private record CardRow(DateTime Date, decimal Amount, decimal? Balance, string Description);

        // Read card register CSV into list of CardRow
        private List<CardRow> ReadCardRegister(string cardName)
        {
            string basePath = FilePathHelper.GetKukiFinancePath("");
            string fileName = cardName switch
            {
                "AMEX" => "AMEXCurrent.csv",
                "Visa" => "VisaCurrent.csv",
                "MasterCard" => "MasterCardCurrent.csv",
                _ => null
            };
            if (fileName == null) return new List<CardRow>();

            string csvFile = Path.Combine(basePath, fileName);
            if (!File.Exists(csvFile)) return new List<CardRow>();

            var lines = File.ReadAllLines(csvFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
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
                if (balIdx >= 0 && parts.Length > balIdx && decimal.TryParse(parts[balIdx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                    bal = b;

                string desc = descIdx >= 0 && parts.Length > descIdx ? parts[descIdx].Trim() : string.Empty;

                result.Add(new CardRow(dt, amt, bal, desc));
            }

            return result.OrderBy(r => r.Date).ToList();
        }

        // Try to get last Balance value on or before date from card rows
        private decimal? GetLastBalanceOnOrBefore(List<CardRow> rows, DateTime date)
        {
            var withBalance = rows.Where(r => r.Balance.HasValue && r.Date <= date).OrderBy(r => r.Date).ToList();
            if (!withBalance.Any()) return null;
            return withBalance.Last().Balance;
        }

        // Sum transactions between inclusive dates, excluding rows that appear to be payments (heuristic)
        private decimal SumTransactionsBetweenExcludingPayments(List<CardRow> rows, DateTime start, DateTime end)
        {
            var paymentKeywords = new[]
            {
                "payment", "pmt", "pay", "paid", "online transfer", "bill pay", "billpay", "transfer",
                "autopay", "auto pay", "ddacheck", "dda check"
            };

            bool IsPayment(string desc)
            {
                if (string.IsNullOrWhiteSpace(desc)) return false;
                var low = desc.ToLowerInvariant();
                return paymentKeywords.Any(k => low.Contains(k));
            }

            var sum = rows
                .Where(r => r.Date >= start && r.Date <= end)
                .Where(r => !IsPayment(r.Description))
                .Sum(r => r.Amount);

            return sum;
        }

        // Correct AddTransactionsButton event handler (matches XAML Clicked signature)
        private async void AddTransactionsButton_Clicked(object sender, EventArgs e)
        {
            if (!File.Exists(transactionsFile))
            {
                await DisplayAlert("Error", "No new transactions file found.", "OK");
                return;
            }

            var newLines = File.ReadAllLines(transactionsFile).Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
            if (newLines.Count > 0)
                File.AppendAllLines(registerFile, newLines);

            // Preserve only header in transactions file (reset to header)
            var header = File.ReadLines(transactionsFile).FirstOrDefault() ?? "";
            File.WriteAllText(transactionsFile, header + Environment.NewLine);

            LoadRegister();

            await DisplayAlert("Success", "New transactions added.", "OK");
        }

        // Manual transaction entry handler required by XAML — ensures signature matches Clicked event.
        private async void ManualTransactionEntryButton_Clicked(object sender, EventArgs e)
        {
            string dateStr = await DisplayPromptAsync("Manual Entry", "Enter date (MM/dd/yyyy):");
            if (string.IsNullOrWhiteSpace(dateStr) || !DateTime.TryParse(dateStr, out var date))
            {
                await DisplayAlert("Invalid", "Please enter a valid date.", "OK");
                return;
            }

            string description = await DisplayPromptAsync("Manual Entry", "Enter description:");
            if (string.IsNullOrWhiteSpace(description))
            {
                await DisplayAlert("Invalid", "Please enter a description.", "OK");
                return;
            }

            string amountStr = await DisplayPromptAsync("Manual Entry", "Enter amount:");
            if (string.IsNullOrWhiteSpace(amountStr) || !decimal.TryParse(amountStr, out var amount))
            {
                await DisplayAlert("Invalid", "Please enter a valid amount.", "OK");
                return;
            }

            string csvLine = $"{date:MM/dd/yyyy},{description},{amount},USD,,,,,";

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
            if (RegisterCollectionView.SelectedItem is RegistryEntry selectedEntry)
            {
                string newDescription = await DisplayPromptAsync("Edit Description", "Enter new description:", initialValue: selectedEntry.Description);
                if (string.IsNullOrWhiteSpace(newDescription) || newDescription == selectedEntry.Description)
                    return;

                selectedEntry.Description = newDescription;

                var categoryMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(categoryFile))
                {
                    foreach (var parts in File.ReadAllLines(categoryFile).Skip(1).Select(line => line.Split(',')).Where(parts => parts.Length >= 2))
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        if (!string.IsNullOrEmpty(key) && !categoryMap.ContainsKey(key))
                            categoryMap[key] = value;
                    }
                }
                categoryMap.TryGetValue(newDescription, out var newCategory);
                selectedEntry.Category = newCategory ?? "";

                var idx = viewModel.Entries.IndexOf(selectedEntry);
                if (idx >= 0)
                {
                    viewModel.Entries.RemoveAt(idx);
                    viewModel.Entries.Insert(idx, selectedEntry);
                }

                var allLines = File.ReadAllLines(registerFile).ToList();
                int startIdx = 0;
                if (allLines.Count > 0 && allLines[0].ToUpper().Contains("DESCRIPTION"))
                    startIdx = 1;

                for (int i = startIdx; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    if (parts.Length < 9) continue;

                    var date = DateTime.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                    var amount = decimal.TryParse(parts[2].Trim(), out var amt) ? amt : 0;

                    if (date == selectedEntry.Date &&
                        amount == selectedEntry.Amount)
                    {
                        parts[1] = newDescription;
                        allLines[i] = string.Join(",", parts);
                        break;
                    }
                }
                File.WriteAllLines(registerFile, allLines);

                LoadRegister();
            }
            else
            {
                await DisplayAlert("Edit", "Please select a row to edit.", "OK");
            }
        }

        private async void CopyDescriptionButton_Clicked(object sender, EventArgs e)
        {
            var selectedEntry = RegisterCollectionView.SelectedItem;
            if (selectedEntry is RegistryEntry entry)
            {
                await Clipboard.Default.SetTextAsync(entry.Description ?? "");
                await DisplayAlert("Copied", "Description copied to clipboard.", "OK");
            }
            else
            {
                await DisplayAlert("Copy", "Please select a row to copy.", "OK");
            }
        }

        private async void DeleteTransactionButton_Clicked(object sender, EventArgs e)
        {
            var selectedEntry = RegisterCollectionView.SelectedItem;
            if (selectedEntry is RegistryEntry entry)
            {
                bool confirm = await DisplayAlert("Delete", "Are you sure you want to delete this transaction?", "Yes", "No");
                if (!confirm) return;

                viewModel.Entries.Remove(entry);

                string filePath = registerFile;
                var allLines = File.ReadAllLines(filePath).ToList();
                int startIdx = allLines.Count > 0 && allLines[0].ToUpper().Contains("DESCRIPTION") ? 1 : 0;

                for (int i = startIdx; i < allLines.Count; i++)
                {
                    var parts = allLines[i].Split(',');
                    bool match = false;
                    if (DateTime.TryParse(parts[0].Trim(), out var date) &&
                        decimal.TryParse(parts[2].Trim(), out var amt) &&
                        date == entry.Date &&
                        amt == entry.Amount &&
                        (parts.Length > 1 && parts[1].Trim() == entry.Description))
                    {
                        if (parts.Length > 4 && !string.IsNullOrWhiteSpace(entry.CheckNumber))
                        {
                            match = parts[4].Trim() == entry.CheckNumber;
                        }
                        else
                        {
                            match = true;
                        }
                    }

                    if (match)
                    {
                        allLines.RemoveAt(i);
                        break;
                    }
                }
                File.WriteAllLines(filePath, allLines);

                LoadRegister();
            }
            else
            {
                await DisplayAlert("Delete", "Please select a row to delete.", "OK");
            }
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}