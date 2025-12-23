using Microsoft.Maui.Controls;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using KukiFinance.Converters;
using KukiFinance.Services;

namespace KukiFinance.Pages
{
    public partial class CalendarPage : ContentPage
    {
        public class CalendarExpense
        {
            public DateTime Date { get; set; }
            public string Category { get; set; }
            public decimal Amount { get; set; }
        }

        private class Entry
        {
            public DateTime Date { get; set; }
            public string Category { get; set; }
            public decimal Amount { get; set; }
            public decimal Balance { get; set; }
        }

        private class ForecastEntry
        {
            public DateTime Date { get; set; }
            public string Category { get; set; }
            public decimal Amount { get; set; }
            public string Account { get; set; }
        }

        public CalendarPage()
        {
            InitializeComponent();
            WindowCenteringService.CenterWindow(1650, 1285);

            AccountPicker.ItemsSource = new List<string>
            {
                "Cash",
                "BMO Check",
                "BMO MoneyMarket",
                "BMO CD",
                "AMEX",
                "Visa",
                "MasterCard",
                "Midland",
                "CS Contributory",
                "CS Joint Tenant",
                "CS Roth IRA Ed",
                "CS Roth IRA Patti",
                "Pershing NetX",
                "Fidelity Health Pro",
                "Select 401K",
                "Gold",
                "House",
                "Chevrolet Impala",
                "Nissan Sentra"
            };
            AccountPicker.SelectedIndex = 1; // Default to BMO Check

            var years = new List<int> { 2023, 2024, 2025, 2026, 2027, 2028, 2029, 2030, 2031, 2032, 2033, 2034, 2035, 2036, 2037, 2038, 2039, 2040, 2041, 2042, 2043, 2044, 2045, 2046, 2047, 2048, 2049, 2050, 2051, 2052, 2053, 2054, 2055, 2056, 2057, 2058 };
            YearPicker.ItemsSource = years;
            YearPicker.SelectedIndex = years.IndexOf(DateTime.Today.Year);

            var months = Enumerable.Range(1, 12)
                .Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();
            MonthPicker.ItemsSource = months;
            MonthPicker.SelectedIndex = DateTime.Today.Month - 1;

            // Show the calendar for the current month on page load
            ShowMonth(AccountPicker.ItemsSource[AccountPicker.SelectedIndex].ToString(), DateTime.Today.Year, DateTime.Today.Month);
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            WindowCenteringService.CenterWindow(1650, 1285);
        }

        private void OnShowMonthClicked(object sender, EventArgs e)
        {
            if (AccountPicker.SelectedItem == null ||
                YearPicker.SelectedItem == null ||
                MonthPicker.SelectedItem == null) return;

            int year = (int)YearPicker.SelectedItem;
            int month = DateTime.ParseExact(MonthPicker.SelectedItem.ToString(), "MMMM", CultureInfo.CurrentCulture).Month;
            string account = AccountPicker.SelectedItem.ToString();
            ShowMonth(account, year, month);
        }

        private void ShowMonth(string account, int year, int month)
        {
            MonthGrid.Children.Clear();
            MonthGrid.RowDefinitions.Clear();
            MonthGrid.ColumnDefinitions.Clear();

            for (int c = 0; c < 7; c++)
                MonthGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var firstDay = new DateTime(year, month, 1);
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int firstDayOfWeek = (int)firstDay.DayOfWeek;
            int totalCells = firstDayOfWeek + daysInMonth;
            int rows = (int)Math.Ceiling(totalCells / 7.0);

            for (int r = 0; r < rows; r++)
                MonthGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Actual balances and entries
            var dayBalances = CalculateDailyBalances(account, year, month);
            var actualEntries = ReadEntries(account, year, month)
                .Where(e => e.Date <= DateTime.Today)
                .OrderBy(e => e.Date)
                .ToList();
            var entriesByDay = actualEntries
                .GroupBy(e => e.Date.Day)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Forecast handling
            Dictionary<DateTime, (decimal balance, List<ForecastEntry> forecasts)> forecastBalances = null;
            List<ForecastEntry> forecastEntries = null;
            Dictionary<int, List<ForecastEntry>> forecastEntriesByDay = null;

            if (account == "BMO Check")
            {
                // For BMO: show forecasted balances and forecast entries including derived card payments
                forecastBalances = CalculateForecastBalances(account);

                // Read forecasts and include derived BMO payments so they appear as entries on the calendar.
                // ComputeCardPaymentsForBmo returns the BMO-side payment entries (negative values).
                var baseForecasts = ReadForecastEntries();
                var derived = ComputeCardPaymentsForBmo(baseForecasts, DateTime.Today, DateTime.Today.AddYears(1), true);
                forecastEntries = baseForecasts.Concat(derived).OrderBy(f => f.Date).ToList();

                forecastEntriesByDay = forecastEntries
                    .Where(f => f.Account.Equals("BMO Check", StringComparison.OrdinalIgnoreCase) && f.Date.Year == year && f.Date.Month == month && f.Date > DateTime.Today)
                    .GroupBy(f => f.Date.Day)
                    .ToDictionary(g => g.Key, g => g.ToList());
            }
            else if (new[] { "AMEX", "Visa", "MasterCard" }.Contains(account))
            {
                // For card accounts: show forecasted charges assigned to the selected card (future),
                // include the derived card-side payment entries (positive amounts) corresponding to BMO payments,
                // and compute forecasted card balances.
                var baseForecasts = ReadForecastEntries();

                // Get the BMO-side derived payment entries (these are negative amounts in BMO Check)
                var derivedBmoPayments = ComputeCardPaymentsForBmo(baseForecasts, DateTime.Today, DateTime.Today.AddYears(1), true);


                // Mirror BMO derived payments into card accounts by inverting sign for display only.
                var derivedCardSide = derivedBmoPayments
                    .Select(d =>
                    {
                        var cardName = GetCardNameFromCategory(d.Category);
                        if (string.IsNullOrEmpty(cardName)) return null;
                        return new ForecastEntry
                        {
                            Date = d.Date,
                            Account = cardName,
                            Category = d.Category,
                            Amount = -d.Amount // invert sign to make it positive for the card account
                        };
                    })
                    .Where(x => x != null)
                    .ToList();

                // Combine base forecasts with derived card-side entries so calendar shows them.
                // NOTE: derivedCardSide is for display only; it is not injected into CalculateForecastBalances
                // to avoid double-counting/propagation.
                forecastEntries = baseForecasts
                    .Concat(derivedCardSide)
                    .OrderBy(f => f.Date)
                    .ToList();

                forecastEntriesByDay = forecastEntries
                    .Where(f => f.Account.Equals(account, StringComparison.OrdinalIgnoreCase) && f.Date.Year == year && f.Date.Month == month && f.Date > DateTime.Today)
                    .GroupBy(f => f.Date.Day)
                    .ToDictionary(g => g.Key, g => g.ToList());

                // Compute forecast balances for the selected card (uses card register + forecasts).
                // CalculateForecastBalances does NOT inject the mirrored card entries — card balances are built
                // from the card's own forecasts and register data (this prevents duplicated / propagated amounts).
                forecastBalances = CalculateForecastBalances(account);
            }
            else
            {
                forecastBalances = new Dictionary<DateTime, (decimal balance, List<ForecastEntry>)>();
                forecastEntriesByDay = new Dictionary<int, List<ForecastEntry>>();
            }

            int dayCounter = 1;
            for (int cell = 0; cell < totalCells; cell++)
            {
                int row = cell / 7;
                int col = cell % 7;
                if (cell < firstDayOfWeek)
                {
                    MonthGrid.Add(new Label { Text = "" }, col, row);
                }
                else if (dayCounter <= daysInMonth)
                {
                    var date = new DateTime(year, month, dayCounter);
                    var dayStack = new VerticalStackLayout
                    {
                        Padding = 4,
                        BackgroundColor = Colors.WhiteSmoke
                    };

                    dayStack.Children.Add(new Label { Text = dayCounter.ToString(), FontAttributes = FontAttributes.Bold });

                    if (date <= DateTime.Today)
                    {
                        // Show actual entries
                        var dayEntries = entriesByDay.ContainsKey(dayCounter) ? entriesByDay[dayCounter] : new List<Entry>();
                        foreach (var entry in dayEntries)
                        {
                            var catLabel = new Label { Text = entry.Category, FontSize = 12 };
                            var amtLabel = new Label
                            {
                                Text = AmountFormatConverter.Format(entry.Amount),
                                TextColor = AmountColorConverter.GetColor(entry.Amount),
                                FontSize = 12
                            };
                            var rowStack = new HorizontalStackLayout { Spacing = 4 };
                            rowStack.Children.Add(catLabel);
                            rowStack.Children.Add(amtLabel);
                            dayStack.Children.Add(rowStack);
                        }
                        dayStack.Children.Add(new BoxView
                        {
                            HeightRequest = 8,
                            Color = Colors.Transparent
                        });
                        decimal balance = dayBalances.ContainsKey(date) ? dayBalances[date] : 0;
                        dayStack.Children.Add(new Label
                        {
                            Text = $"Balance: {AmountFormatConverter.Format(balance)}",
                            FontSize = 12,
                            FontAttributes = FontAttributes.Bold,
                            TextColor = AmountColorConverter.GetColor(balance)
                        });
                    }
                    else
                    {
                        // Show forecasted entries and balances for BMO; for cards show forecasted charges and forecasted card balances
                        if (forecastEntriesByDay != null && forecastEntriesByDay.ContainsKey(dayCounter))
                        {
                            foreach (var entry in forecastEntriesByDay[dayCounter])
                            {
                                var catLabel = new Label { Text = entry.Category, FontSize = 12 };
                                var amtLabel = new Label
                                {
                                    Text = AmountFormatConverter.Format(entry.Amount),
                                    TextColor = AmountColorConverter.GetColor(entry.Amount),
                                    FontSize = 12
                                };
                                var rowStack = new HorizontalStackLayout { Spacing = 4 };
                                rowStack.Children.Add(catLabel);
                                rowStack.Children.Add(amtLabel);
                                dayStack.Children.Add(rowStack);
                            }
                        }

                        dayStack.Children.Add(new BoxView
                        {
                            HeightRequest = 8,
                            Color = Colors.Transparent
                        });

                        // Show forecast balance when available (BMO and card accounts)
                        if (forecastBalances != null && forecastBalances.ContainsKey(date))
                        {
                            decimal balance = forecastBalances[date].balance;
                            dayStack.Children.Add(new Label
                            {
                                Text = $"Forecast Balance: {AmountFormatConverter.Format(balance)}",
                                FontSize = 12,
                                FontAttributes = FontAttributes.Bold,
                                TextColor = AmountColorConverter.GetColor(balance)
                            });
                        }
                    }

                    MonthGrid.Add(dayStack, col, row);
                    dayCounter++;
                }
            }
        }

        private List<Entry> ReadEntries(string account, int year, int month)
        {
            string basePath = FilePathHelper.GetKukiFinancePath("");
            string fileName = account switch
            {
                "Cash" => "CashCurrent.csv",
                "BMO Check" => "BMOCheckCurrent.csv",
                "BMO MoneyMarket" => "BMOMoneyMarketCurrent.csv",
                "BMO CD" => "BMOCDCurrent.csv",
                "AMEX" => "AMEXCurrent.csv",
                "Visa" => "VisaCurrent.csv",
                "MasterCard" => "MasterCardCurrent.csv",
                "Midland" => "MidlandCurrent.csv",
                "CS Contributory" => "CharlesSchwabContributoryCurrent.csv",
                "CS Joint Tenant" => "CharlesSchwabJointTenantCurrent.csv",
                "CS Roth IRA Ed" => "CharlesSchwabRothIraEdCurrent.csv",
                "CS Roth IRA Patti" => "CharlesSchwabRothIraPattiCurrent.csv",
                "Pershing NetX" => "NetXCurrent.csv",
                "Fidelity Health Pro" => "HealthProCurrent.csv",
                "Select 401K" => "Select401KCurrent.csv",
                "Gold" => "GoldCurrent.csv",
                "House" => "HouseCurrent.csv",
                "Chevrolet Impala" => "ChevroletImpalaCurrent.csv",
                "Nissan Sentra" => "NissanSentraCurrent.csv",
                _ => "BMOCheckCurrent.csv"
            };
            string csvFile = Path.Combine(basePath, fileName);

            var result = new List<Entry>();
            if (!File.Exists(csvFile)) return result;
            var lines = File.ReadAllLines(csvFile);
            if (lines.Length < 2) return result;
            var headers = lines[0].Split(',');
            int dateIdx = Array.FindIndex(headers, h => h.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));
            int catIdx = Array.FindIndex(headers, h => h.Trim().Equals("Category", StringComparison.OrdinalIgnoreCase));
            int amtIdx = Array.FindIndex(headers, h => h.Trim().Equals("Amount", StringComparison.OrdinalIgnoreCase));
            int balIdx = Array.FindIndex(headers, h => h.Trim().Equals("Balance", StringComparison.OrdinalIgnoreCase));
            if (dateIdx < 0 || catIdx < 0 || amtIdx < 0 || balIdx < 0) return result;

            string[] formats = { "yyyy-MM-dd", "MM/dd/yyyy", "M/dyyyy" };
            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length <= Math.Max(Math.Max(dateIdx, catIdx), Math.Max(amtIdx, balIdx))) continue;
                if (!DateTime.TryParseExact(parts[dateIdx], formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (date.Year != year || date.Month != month) continue;
                var category = parts[catIdx].Trim();
                if (!decimal.TryParse(parts[amtIdx], out var amount)) continue;
                if (!decimal.TryParse(parts[balIdx], out var balance)) continue;
                result.Add(new Entry { Date = date, Category = category, Amount = amount, Balance = balance });
            }
            return result;
        }

        private Dictionary<DateTime, decimal> CalculateDailyBalances(string account, int year, int month)
        {
            string basePath = FilePathHelper.GetKukiFinancePath("");
            string fileName = account switch
            {
                "Cash" => "CashCurrent.csv",
                "BMO Check" => "BMOCheckCurrent.csv",
                "BMO MoneyMarket" => "BMOMoneyMarketCurrent.csv",
                "BMO CD" => "BMOCDCurrent.csv",
                "AMEX" => "AMEXCurrent.csv",
                "Visa" => "VisaCurrent.csv",
                "MasterCard" => "MasterCardCurrent.csv",
                "Midland" => "MidlandCurrent.csv",
                "CS Contributory" => "CharlesSchwabContributoryCurrent.csv",
                "CS Joint Tenant" => "CharlesSchwabJointTenantCurrent.csv",
                "CS Roth IRA Ed" => "CharlesSchwabRothIraEdCurrent.csv",
                "CS Roth IRA Patti" => "CharlesSchwabRothIraPattiCurrent.csv",
                "Pershing NetX" => "NetXCurrent.csv",
                "Fidelity Health Pro" => "HealthProCurrent.csv",
                "Select 401K" => "Select401KCurrent.csv",
                "Gold" => "GoldCurrent.csv",
                "House" => "HouseCurrent.csv",
                "Chevrolet Impala" => "ChevroletImpalaCurrent.csv",
                "Nissan Sentra" => "NissanSentraCurrent.csv",
                _ => "BMOCheckCurrent.csv"
            };
            string csvFile = Path.Combine(basePath, fileName);

            if (!File.Exists(csvFile)) return new Dictionary<DateTime, decimal>();
            var lines = File.ReadAllLines(csvFile);
            if (lines.Length < 2) return new Dictionary<DateTime, decimal>();
            var headers = lines[0].Split(',');
            int dateIdx = Array.FindIndex(headers, h => h.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));
            int balIdx = Array.FindIndex(headers, h => h.Trim().Equals("Balance", StringComparison.OrdinalIgnoreCase));
            string[] formats = { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy" };

            var balances = new Dictionary<DateTime, decimal>();
            decimal lastBalance = 0;
            DateTime? lastDate = null;

            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length <= Math.Max(dateIdx, balIdx)) continue;
                if (!DateTime.TryParseExact(parts[dateIdx], formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
                if (!decimal.TryParse(parts[balIdx], out var balance)) continue;
                if (date > DateTime.Today) break;

                if (lastDate != null)
                {
                    var nextDate = lastDate.Value.AddDays(1);
                    while (nextDate < date)
                    {
                        balances[nextDate] = lastBalance;
                        nextDate = nextDate.AddDays(1);
                    }
                }
                balances[date] = balance;
                lastBalance = balance;
                lastDate = date;
            }

            if (lastDate != null)
            {
                var nextDate = lastDate.Value.AddDays(1);
                while (nextDate <= DateTime.Today)
                {
                    balances[nextDate] = lastBalance;
                    nextDate = nextDate.AddDays(1);
                }
            }

            var result = new Dictionary<DateTime, decimal>();
            for (int d = 1; d <= DateTime.DaysInMonth(year, month); d++)
            {
                var date = new DateTime(year, month, d);
                if (balances.ContainsKey(date))
                    result[date] = balances[date];
                else if (d > 1 && result.ContainsKey(new DateTime(year, month, d - 1)))
                    result[date] = result[new DateTime(year, month, d - 1)];
                else
                    result[date] = lastBalance;
            }
            return result;
        }

        // --- updated ReadForecastEntries method ---
        private List<ForecastEntry> ReadForecastEntries()
        {
            string forecastFile = FilePathHelper.GetKukiFinancePath("ForecastExpenses.csv");
            var result = new List<ForecastEntry>();
            if (!File.Exists(forecastFile)) return result;

            var lines = File.ReadAllLines(forecastFile);
            if (lines.Length < 2) return result;

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 5)
                    continue;

                // Backwards-compatible parsing:
                // New format:  Account,Frequency,Year,Month,Day,Category,Amount
                // Old format1: Account,Frequency,Month,Day,Category,Amount  (no Year)
                // Old format2: Frequency,Month,Day,Category,Amount          (no Account/Year)
                string account;
                string frequency;
                string yearStr;
                string monthStr;
                int day = 1;
                string category;
                decimal amount;

                if (parts.Length >= 7)
                {
                    // New format: Account,Frequency,Year,Month,Day,Category,Amount
                    account = parts[0].Trim();
                    frequency = parts[1].Trim();
                    yearStr = parts[2].Trim();
                    monthStr = parts[3].Trim();
                    int.TryParse(parts[4].Trim(), out day);
                    category = parts[5].Trim();
                    if (!decimal.TryParse(parts[6].Trim(), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out amount))
                        continue;
                }
                else if (parts.Length == 6)
                {
                    // Old format with Account but without explicit Year:
                    // Account,Frequency,Month,Day,Category,Amount
                    account = parts[0].Trim();
                    frequency = parts[1].Trim();
                    yearStr = string.Empty;
                    monthStr = parts[2].Trim();
                    int.TryParse(parts[3].Trim(), out day);
                    category = parts[4].Trim();
                    if (!decimal.TryParse(parts[5].Trim(), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out amount))
                        continue;
                }
                else
                {
                    // Very old format without Account / Year:
                    // Frequency,Month,Day,Category,Amount  -> assume BMO Check
                    account = "BMO Check";
                    frequency = parts[0].Trim();
                    yearStr = string.Empty;
                    monthStr = parts[1].Trim();
                    int.TryParse(parts[2].Trim(), out day);
                    category = parts[3].Trim();
                    if (!decimal.TryParse(parts[4].Trim(), NumberStyles.Any,
                            CultureInfo.InvariantCulture, out amount))
                        continue;
                }

                // NOTE: yearStr is currently informational only; we treat entries the same
                // as before (repeating according to Frequency) so existing behavior is preserved.

                // Expand occurrences starting a few months back so billing windows that start
                // prior to today are included when summing card billing cycles.
                DateTime today = DateTime.Today;
                DateTime startDate = today.AddMonths(-3); // cover billing windows that start up to 2 months earlier
                DateTime endDate = today.AddYears(1);
                var dates = new List<DateTime>();

                if (frequency.Equals("Once", StringComparison.OrdinalIgnoreCase))
                {
                    int monthNum = DateTime.TryParseExact(
                                       monthStr,
                                       "MMMM",
                                       CultureInfo.CurrentCulture,
                                       DateTimeStyles.None,
                                       out var mdt)
                                   ? mdt.Month
                                   : today.Month;

                    // If a specific year is stored, use it; otherwise use current year (old behavior)
                    int year = int.TryParse(yearStr, out var yParsed) ? yParsed : today.Year;

                    int dayOfMonth = Math.Min(day, DateTime.DaysInMonth(year, monthNum));
                    var forecastDate = new DateTime(year, monthNum, dayOfMonth);
                    if (forecastDate >= startDate && forecastDate <= endDate)
                        dates.Add(forecastDate);
                }
                else if (frequency.Equals("Monthly", StringComparison.OrdinalIgnoreCase))
                {
                    // Same behavior as before: monthly from startDate through endDate
                    for (var dt = new DateTime(startDate.Year, startDate.Month, 1);
                         dt <= endDate;
                         dt = dt.AddMonths(1))
                    {
                        int m = dt.Month;
                        int y = dt.Year;
                        int dayOfMonth = Math.Min(day, DateTime.DaysInMonth(y, m));
                        var forecastDate = new DateTime(y, m, dayOfMonth);
                        if (forecastDate >= startDate && forecastDate <= endDate)
                            dates.Add(forecastDate);
                    }
                }
                else if (frequency.Equals("Annual", StringComparison.OrdinalIgnoreCase))
                {
                    int monthNum = DateTime.TryParseExact(
                                       monthStr,
                                       "MMMM",
                                       CultureInfo.CurrentCulture,
                                       DateTimeStyles.None,
                                       out var mdt)
                                   ? mdt.Month
                                   : today.Month;

                    // If a specific year is stored, only that year; otherwise all years in range
                    int loopStartYear = int.TryParse(yearStr, out var yParsed)
                                        ? yParsed
                                        : startDate.Year;
                    int loopEndYear = int.TryParse(yearStr, out yParsed)
                                        ? yParsed
                                        : endDate.Year;

                    for (int y = loopStartYear; y <= loopEndYear; y++)
                    {
                        int dayOfMonth = Math.Min(day, DateTime.DaysInMonth(y, monthNum));
                        var forecastDate = new DateTime(y, monthNum, dayOfMonth);
                        if (forecastDate >= startDate && forecastDate <= endDate)
                            dates.Add(forecastDate);
                    }
                }
                else if (frequency.EndsWith("Months", StringComparison.OrdinalIgnoreCase))
                {
                    int freqMonths = int.TryParse(frequency.Split(' ')[0], out var n) ? n : 1;
                    int startMonth = DateTime.TryParseExact(
                                         monthStr,
                                         "MMMM",
                                         CultureInfo.CurrentCulture,
                                         DateTimeStyles.None,
                                         out var smdt)
                                     ? smdt.Month
                                     : startDate.Month;

                    var firstDate = new DateTime(
                        startDate.Year,
                        startMonth,
                        Math.Min(day, DateTime.DaysInMonth(startDate.Year, startMonth)));

                    if (firstDate < startDate)
                        firstDate = firstDate.AddMonths(freqMonths);

                    for (var dt = firstDate; dt <= endDate; dt = dt.AddMonths(freqMonths))
                    {
                        int dayOfMonth = Math.Min(day, DateTime.DaysInMonth(dt.Year, dt.Month));
                        var forecastDate = new DateTime(dt.Year, dt.Month, dayOfMonth);
                        if (forecastDate >= startDate && forecastDate <= endDate)
                            dates.Add(forecastDate);
                    }
                }

                foreach (var d in dates)
                {
                    result.Add(new ForecastEntry
                    {
                        Date = d,
                        Category = category,
                        Amount = amount,
                        Account = account
                    });
                }
            }

            return result.OrderBy(e => e.Date).ToList();
        }


        private Dictionary<DateTime, (decimal balance, List<ForecastEntry> forecasts)> CalculateForecastBalances(string account)
        {
            // Start from today's calculated balance for the account
            var balances = CalculateDailyBalances(account, DateTime.Today.Year, DateTime.Today.Month);
            decimal currentBalance = balances.ContainsKey(DateTime.Today) ? balances[DateTime.Today] : balances.Values.LastOrDefault();

            // 1) Read base forecasts from file (baseForecasts)
            var baseForecasts = ReadForecastEntries();

            // 2) Derive BMO-side payment entries from base forecasts (these are negative amounts in BMO Check)
            var derivedBmoPayments = ComputeCardPaymentsForBmo(baseForecasts, DateTime.Today, DateTime.Today.AddYears(1), false);


            // 3) Build the working forecasts set used for balance calculations:
            //    base forecasts + derived BMO payments (BMO withdrawals)
            var forecasts = baseForecasts.Concat(derivedBmoPayments).ToList();

            // 4) If computing forecast balances for a credit card account, mirror the BMO-derived payment
            //    into a positive card-side forecast entry and add it to this account's forecasts.
            //    This ensures the card's running forecast is reduced on the due date.
            if (new[] { "AMEX", "Visa", "MasterCard" }.Contains(account))
            {
                var mirroredForThisCard = derivedBmoPayments
                    .Select(d =>
                    {
                        var cardName = GetCardNameFromCategory(d.Category);
                        if (string.IsNullOrEmpty(cardName)) return null;
                        if (!cardName.Equals(account, StringComparison.OrdinalIgnoreCase)) return null;

                        // d.Amount is negative (BMO withdrawal). Mirror as positive on the card.
                        return new ForecastEntry
                        {
                            Date = d.Date,
                            Account = account,
                            Category = d.Category,
                            Amount = -d.Amount
                        };
                    })
                    .Where(x => x != null)
                    .ToList();

                if (mirroredForThisCard.Any())
                    forecasts.AddRange(mirroredForThisCard);
            }

            // Build forecastByDate for the selected account only
            var forecastByDate = forecasts
                .Where(f => f.Account.Equals(account, StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => f.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new Dictionary<DateTime, (decimal balance, List<ForecastEntry>)>();
            decimal runningBalance = currentBalance;
            DateTime todayLocal = DateTime.Today;
            DateTime endDate = todayLocal.AddYears(1);

            for (var date = todayLocal.AddDays(1); date <= endDate; date = date.AddDays(1))
            {
                if (forecastByDate.ContainsKey(date))
                {
                    foreach (var entry in forecastByDate[date])
                        runningBalance += entry.Amount;
                }
                result[date] = (runningBalance, forecastByDate.ContainsKey(date) ? forecastByDate[date] : new List<ForecastEntry>());
            }

            return result;
        }

        // useCardForecastBalancesForFuture:
        //  - true  => future-cycle payments are based on the card's Forecast Balance
        //             on the billing cutoff date (what you see on the card calendar)
        //  - false => future-cycle payments are based on the sum of forecast rows
        private List<ForecastEntry> ComputeCardPaymentsForBmo(
    List<ForecastEntry> allForecasts,
    DateTime from,
    DateTime to,
    bool useCardForecastBalancesForFuture)
        {
            var results = new List<ForecastEntry>();

            // Card metadata: due day in the month of payment
            var cards = new[]
            {
        new { Name = "AMEX",       DueDay = 8  },
        new { Name = "Visa",       DueDay = 26 },
        new { Name = "MasterCard", DueDay = 14 }
    };

            DateTime today = DateTime.Today;

            // We work month-by-month over the requested range
            var cursor = new DateTime(from.Year, from.Month, 1);
            var endMonth = new DateTime(to.Year, to.Month, 1);

            while (cursor <= endMonth)
            {
                int year = cursor.Year;
                int month = cursor.Month;

                foreach (var card in cards)
                {
                    int dueDay = Math.Min(card.DueDay, DateTime.DaysInMonth(year, month));
                    var dueDate = new DateTime(year, month, dueDay);

                    // Only generate payments inside the [from, to] horizon
                    if (dueDate < from || dueDate > to)
                        continue;

                    // Determine billing window for this card / due month
                    DateTime billingStart = GetBillingStartForCard(card.Name, year, month);
                    DateTime billingEnd = GetBillingEndForCard(card.Name, year, month);

                    decimal amountDue = 0m;

                    // --- CURRENT MONTH: derive amount due from card register (plus any forecast charges in the same window) ---
                    if (dueDate.Year == today.Year && dueDate.Month == today.Month)
                    {
                        var cardRows = ReadCardRegister(card.Name);

                        // Try using the last Balance on or before the cutoff
                        decimal? balanceOnCutoff = GetLastBalanceOnOrBefore(cardRows, billingEnd);
                        if (balanceOnCutoff.HasValue)
                        {
                            amountDue = Math.Abs(balanceOnCutoff.Value);
                        }
                        else
                        {
                            // No balance column/row – fall back to summing transactions and forecasts
                            decimal txSum = SumTransactionsBetweenExcludingPayments(cardRows, billingStart, billingEnd);

                            decimal forecastSum = allForecasts
                                .Where(f => f.Account.Equals(card.Name, StringComparison.OrdinalIgnoreCase)
                                            && f.Date >= billingStart
                                            && f.Date <= billingEnd)
                                .Sum(f => f.Amount);

                            amountDue = Math.Abs(txSum + forecastSum);
                        }
                    }
                    // --- FUTURE MONTHS ---
                    else if (dueDate > today)
                    {
                        if (useCardForecastBalancesForFuture)
                        {
                            // Use the card's Forecast Balance on the billing cutoff date
                            // (the exact number shown on the Calendar page for that card).
                            var cardForecastBalances = CalculateForecastBalances(card.Name);

                            if (cardForecastBalances.TryGetValue(billingEnd, out var fb))
                            {
                                amountDue = Math.Abs(fb.balance);
                            }
                            else
                            {
                                // Fallback: sum forecast rows in the billing window, just in case
                                decimal forecastSum = allForecasts
                                    .Where(f => f.Account.Equals(card.Name, StringComparison.OrdinalIgnoreCase)
                                                && f.Date >= billingStart
                                                && f.Date <= billingEnd)
                                    .Sum(f => f.Amount);

                                amountDue = Math.Abs(forecastSum);
                            }
                        }
                        else
                        {
                            // Used only inside CalculateForecastBalances to avoid recursion:
                            // derive an approximate amount from forecast rows in this billing window.
                            decimal forecastSum = allForecasts
                                .Where(f => f.Account.Equals(card.Name, StringComparison.OrdinalIgnoreCase)
                                            && f.Date >= billingStart
                                            && f.Date <= billingEnd)
                                .Sum(f => f.Amount);

                            amountDue = Math.Abs(forecastSum);
                        }
                    }
                    else
                    {
                        // Past due dates: do not generate anything. Past real payments live in the BMO register CSV.
                        continue;
                    }

                    if (amountDue <= 0m)
                        continue;

                    // BMO sees payments as withdrawals (negative in BMO Check)
                    results.Add(new ForecastEntry
                    {
                        Date = dueDate,
                        Account = "BMO Check",
                        Category = $"Card Payment - {card.Name}",
                        Amount = -amountDue
                    });
                }

                cursor = cursor.AddMonths(1);
            }

            return results.OrderBy(r => r.Date).ToList();
        }



        // Helper to compute billing start for a card, relative to the due month
        // AMEX:      start = 25th of month (M-2)
        // Visa/MC:   start = 2nd of month (M-1)
        private DateTime GetBillingStartForCard(string cardName, int dueYear, int dueMonth)
        {
            if (cardName.Equals("AMEX", StringComparison.OrdinalIgnoreCase))
            {
                var startMonthDate = new DateTime(dueYear, dueMonth, 1).AddMonths(-2);
                int startDay = Math.Min(25, DateTime.DaysInMonth(startMonthDate.Year, startMonthDate.Month));
                return new DateTime(startMonthDate.Year, startMonthDate.Month, startDay);
            }
            else // Visa & MasterCard
            {
                var startMonthDate = new DateTime(dueYear, dueMonth, 1).AddMonths(-1);
                int startDay = Math.Min(2, DateTime.DaysInMonth(startMonthDate.Year, startMonthDate.Month));
                return new DateTime(startMonthDate.Year, startMonthDate.Month, startDay);
            }
        }

        // Represents a row in a credit-card CSV register
        private record CardRow(DateTime Date, decimal Amount, decimal? Balance, string Description);

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

            var result = new List<CardRow>();
            string[] formats = { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy" };

            foreach (var line in lines.Skip(1))
            {
                var parts = line.Split(',');
                if (parts.Length <= Math.Max(dateIdx, amtIdx)) continue;

                if (!DateTime.TryParseExact(parts[dateIdx].Trim(), formats,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
                    && !DateTime.TryParse(parts[dateIdx].Trim(), out dt))
                {
                    continue;
                }

                if (!decimal.TryParse(parts[amtIdx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                    continue;

                decimal? bal = null;
                if (balIdx >= 0 && parts.Length > balIdx &&
                    decimal.TryParse(parts[balIdx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
                {
                    bal = b;
                }

                string desc = descIdx >= 0 && parts.Length > descIdx
                    ? parts[descIdx].Trim()
                    : string.Empty;

                result.Add(new CardRow(dt, amt, bal, desc));
            }

            return result.OrderBy(r => r.Date).ToList();
        }

        // Last known Balance value on or before a given date
        private decimal? GetLastBalanceOnOrBefore(List<CardRow> rows, DateTime date)
        {
            var withBalance = rows
                .Where(r => r.Balance.HasValue && r.Date <= date)
                .OrderBy(r => r.Date)
                .ToList();

            if (!withBalance.Any()) return null;
            return withBalance.Last().Balance;
        }

        // Sum card register transactions between start and end, excluding rows that are "payments"
        // (we don’t want to treat existing payments as part of the amount due)
        private decimal SumTransactionsBetweenExcludingPayments(List<CardRow> rows, DateTime start, DateTime end)
        {
            return rows
                .Where(r => r.Date >= start && r.Date <= end && !IsPayment(r.Description))
                .Sum(r => r.Amount);
        }

        private bool IsPayment(string description)
        {
            if (string.IsNullOrWhiteSpace(description)) return false;

            return description.Contains("Payment", StringComparison.OrdinalIgnoreCase) ||
                   description.Contains("Pmt", StringComparison.OrdinalIgnoreCase) ||
                   description.Contains("Card Payment", StringComparison.OrdinalIgnoreCase);
        }

        // Extract card name from category "Card Payment - {CardName}"
        private string GetCardNameFromCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            const string prefix = "Card Payment - ";
            if (category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return category.Substring(prefix.Length).Trim();
            return null;
        }


        // Helper to compute billing end (cutoff) for a card, relative to the due month
        // AMEX:      end = 24th of previous month (M-1)
        // Visa/MC:   end = 1st of due month (M)
        private DateTime GetBillingEndForCard(string cardName, int dueYear, int dueMonth)
        {
            if (cardName.Equals("AMEX", StringComparison.OrdinalIgnoreCase))
            {
                var endMonthDate = new DateTime(dueYear, dueMonth, 1).AddMonths(-1);
                int endDay = Math.Min(24, DateTime.DaysInMonth(endMonthDate.Year, endMonthDate.Month));
                return new DateTime(endMonthDate.Year, endMonthDate.Month, endDay);
            }
            else // Visa & MasterCard
            {
                var endMonthDate = new DateTime(dueYear, dueMonth, 1);
                int endDay = Math.Min(1, DateTime.DaysInMonth(endMonthDate.Year, endMonthDate.Month));
                return new DateTime(endMonthDate.Year, endMonthDate.Month, endDay);
            }
        }

        // Forecast the card account balance on a given date by applying forecast entries for that card only.
        // This avoids circular dependency on CalculateForecastBalances when computing card-side derived amounts.
        private decimal GetCardForecastBalanceOnCutoff(string cardName, DateTime cutoffDate)
        {
            // Prefer last known register balance on or before today as the starting point.
            var cardRows = ReadCardRegister(cardName);
            var withBalanceOnOrBeforeToday = cardRows
                .Where(r => r.Balance.HasValue && r.Date <= DateTime.Today)
                .OrderBy(r => r.Date)
                .ToList();

            decimal running;
            DateTime cursor;

            if (withBalanceOnOrBeforeToday.Any())
            {
                // Start from the last real register balance and apply forecasts after that date.
                var last = withBalanceOnOrBeforeToday.Last();
                running = last.Balance.Value;
                cursor = last.Date.AddDays(1);
            }
            else
            {
                // No register balance available — use today's calculated balance as a reasonable starting point.
                var today = DateTime.Today;
                var todayBalances = CalculateDailyBalances(cardName, today.Year, today.Month);
                running = todayBalances.ContainsKey(today) ? todayBalances[today] : todayBalances.Values.LastOrDefault();
                cursor = today.AddDays(1);
            }

            // Apply only base forecast entries that target the card (no derived BMO or mirrored card entries).
            // This prevents circular dependency and avoids double-counting.
            var cardForecasts = ReadForecastEntries()
                .Where(f => f.Account.Equals(cardName, StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => f.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            // Apply forecasts from cursor up to and including cutoffDate
            if (cursor <= cutoffDate)
            {
                for (var dt = cursor; dt <= cutoffDate; dt = dt.AddDays(1))
                {
                    if (cardForecasts.TryGetValue(dt, out var list))
                    {
                        foreach (var f in list)
                            running += f.Amount;
                    }
                }
            }

            return running;
        }

        private async void OnChartClicked(object sender, EventArgs e)
        {
            if (AccountPicker.SelectedItem == null || YearPicker.SelectedItem == null)
                return;

            string account = AccountPicker.SelectedItem.ToString();
            int year = (int)YearPicker.SelectedItem;

            var actualWeeklyBalances = new List<(DateTime WeekEndDate, decimal Balance)>();
            for (int month = 1; month <= 12; month++)
            {
                var balances = CalculateDailyBalances(account, year, month);
                var saturdays = Enumerable.Range(1, DateTime.DaysInMonth(year, month))
                    .Select(day => new DateTime(year, month, day))
                    .Where(date => date.DayOfWeek == DayOfWeek.Saturday && date <= DateTime.Today);

                foreach (var saturday in saturdays)
                {
                    decimal balance = balances.ContainsKey(saturday) ? balances[saturday] : balances.Values.LastOrDefault();
                    actualWeeklyBalances.Add((saturday, balance));
                }
            }

            List<(DateTime WeekEndDate, decimal Balance)> forecastWeeklyBalances = new();

            if (account == "BMO Check")
            {
                DateTime firstForecastSaturday = DateTime.Today.AddDays(1);
                while (firstForecastSaturday.DayOfWeek != DayOfWeek.Saturday)
                    firstForecastSaturday = firstForecastSaturday.AddDays(1);

                var forecastBalances = CalculateForecastBalances(account);
                var forecastSaturdays = new List<DateTime>();
                for (var dt = firstForecastSaturday; dt <= DateTime.Today.AddYears(1); dt = dt.AddDays(7))
                    forecastSaturdays.Add(dt);

                forecastWeeklyBalances = forecastSaturdays
                    .Select(date => (date, forecastBalances.ContainsKey(date) ? forecastBalances[date].balance : 0m))
                    .ToList();
            }
            else
            {
                // Only show actuals up to the last Saturday ≤ today
                if (actualWeeklyBalances.Count > 0)
                {
                    var lastActualSaturday = actualWeeklyBalances
                        .Where(b => b.WeekEndDate <= DateTime.Today)
                        .Select(b => b.WeekEndDate)
                        .DefaultIfEmpty()
                        .Max();

                    actualWeeklyBalances = actualWeeklyBalances
                        .Where(b => b.WeekEndDate <= lastActualSaturday)
                        .ToList();
                }
                // forecastWeeklyBalances remains empty
            }

            var chartPage = new ChartPage(account, year, actualWeeklyBalances, forecastWeeklyBalances);
            await Navigation.PushModalAsync(chartPage);
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}