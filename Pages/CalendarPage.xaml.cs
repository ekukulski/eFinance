using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using KukiFinance.Services;

namespace KukiFinance.Pages
{
    public partial class CalendarPage : ContentPage
    {
        // ===== Models =====
        public class RegisterEntry
        {
            public DateTime Date { get; set; }
            public string Category { get; set; } = "";
            public decimal Amount { get; set; }
            public decimal Balance { get; set; }
        }

        public class ForecastEntry
        {
            public DateTime Date { get; set; }
            public string Account { get; set; } = "";
            public string Category { get; set; } = "";
            public decimal Amount { get; set; }
        }

        private class ForecastExpenseRow
        {
            public string Account { get; set; } = "";
            public string Frequency { get; set; } = "";
            public int Year { get; set; }
            public string Month { get; set; } = "";
            public int Day { get; set; }
            public string Category { get; set; } = "";
            public decimal Amount { get; set; }
        }

        // ===== Account picker list =====
        private readonly List<string> accounts = new()
        {
            "BMO Check",
            "AMEX",
            "Visa",
            "MasterCard",
            "Cash",
            "BMO MoneyMarket",
            "BMO CD",
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

        // Map account -> *Current.csv
        private readonly Dictionary<string, string> accountFileMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "BMO Check", "BMOCheckCurrent.csv" },
            { "AMEX", "AMEXCurrent.csv" },
            { "Visa", "VisaCurrent.csv" },
            { "MasterCard", "MasterCardCurrent.csv" },
            { "Cash", "CashCurrent.csv" },
            { "BMO MoneyMarket", "BMOMoneyMarketCurrent.csv" },
            { "BMO CD", "BMOCDCurrent.csv" },
            { "Midland", "MidlandCurrent.csv" },

            // matches your real file list
            { "CS Contributory", "CharlesSchwabContributoryCurrent.csv" },
            { "CS Joint Tenant", "CharlesSchwabJointTenantCurrent.csv" },
            { "CS Roth IRA Ed", "CharlesSchwabRothIraEdCurrent.csv" },
            { "CS Roth IRA Patti", "CharlesSchwabRothIraPattiCurrent.csv" },
            { "Pershing NetX", "NetXCurrent.csv" },
            { "Fidelity Health Pro", "HealthProCurrent.csv" },

            { "Select 401K", "Select401KCurrent.csv" },
            { "Gold", "GoldCurrent.csv" },
            { "House", "HouseCurrent.csv" },
            { "Chevrolet Impala", "ChevroletImpalaCurrent.csv" },
            { "Nissan Sentra", "NissanSentraCurrent.csv" }
        };

        public CalendarPage()
        {
            InitializeComponent();

            AccountPicker.ItemsSource = accounts;
            var idx = accounts.IndexOf("BMO Check");
            AccountPicker.SelectedIndex = idx >= 0 ? idx : 0;

            for (int y = DateTime.Today.Year - 1; y <= DateTime.Today.Year + 5; y++)
                YearPicker.Items.Add(y.ToString());
            YearPicker.SelectedItem = DateTime.Today.Year.ToString();

            for (int m = 1; m <= 12; m++)
                MonthPicker.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m));
            MonthPicker.SelectedItem = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(DateTime.Today.Month);

            AccountPicker.SelectedIndexChanged += (_, __) => SafeShowMonth();
            YearPicker.SelectedIndexChanged += (_, __) => SafeShowMonth();
            MonthPicker.SelectedIndexChanged += (_, __) => SafeShowMonth();

            SafeShowMonth();
        }
        
        // Wrap ShowMonth to prevent hard-crash on unexpected exceptions
        private void SafeShowMonth()
        {
            try
            {
                ShowMonth();
            }
            catch (Exception ex)
            {
                // Don’t crash the app; show something visible for debugging
                MonthGrid.Children.Clear();
                MonthGrid.RowDefinitions.Clear();
                MonthGrid.ColumnDefinitions.Clear();

                MonthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                MonthGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });

                MonthGrid.Add(new Label
                {
                    Text = "Calendar error:\n" + ex.Message,
                    TextColor = Colors.Red,
                    FontAttributes = FontAttributes.Bold
                }, 0, 0);

                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        // ===== Buttons =====
        private async void OnChartClicked(object sender, EventArgs e)
        {
            if (AccountPicker.SelectedItem == null || YearPicker.SelectedItem == null)
                return;

            string account = AccountPicker.SelectedItem.ToString();
            int year = int.Parse(YearPicker.SelectedItem.ToString());

            var actualWeeklyBalances = BuildActualWeeklyBalances(account, year);
            var forecastWeeklyBalances = BuildForecastWeeklyBalances(account);

            var chartPage = new ChartPage(account, year, actualWeeklyBalances, forecastWeeklyBalances);
            await Navigation.PushAsync(chartPage);
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        // ===== Main month render =====
        private void ShowMonth()
        {
            string account = AccountPicker.SelectedItem?.ToString() ?? "BMO Check";
            int year = int.TryParse(YearPicker.SelectedItem?.ToString(), out var y) ? y : DateTime.Today.Year;

            // IMPORTANT: MonthPicker.SelectedIndex can be -1 during init/re-measure
            int month = (MonthPicker.SelectedIndex >= 0 ? MonthPicker.SelectedIndex : DateTime.Today.Month - 1) + 1;

            MonthGrid.Children.Clear();
            MonthGrid.RowDefinitions.Clear();
            MonthGrid.ColumnDefinitions.Clear();

            // 7 columns
            for (int c = 0; c < 7; c++)
                MonthGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Header row
            MonthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] dayHeaders = { "Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat" };
            for (int i = 0; i < 7; i++)
            {
                MonthGrid.Add(new Label
                {
                    Text = dayHeaders[i],
                    FontAttributes = FontAttributes.Bold,
                    HorizontalTextAlignment = TextAlignment.Center
                }, i, 0);
            }

            var firstDay = new DateTime(year, month, 1);
            int daysInMonth = DateTime.DaysInMonth(year, month);
            int firstDow = (int)firstDay.DayOfWeek;
            int totalCells = firstDow + daysInMonth;
            int weeks = (int)Math.Ceiling(totalCells / 7.0);

            // ✅ Key stability change: Auto week rows (more stable after navigation)
            for (int r = 0; r < weeks; r++)
                MonthGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Actual entries for month
            var actual = ReadRegisterEntries(account)
                .Where(r => r.Date.Year == year && r.Date.Month == month)
                .ToList();

            var actualByDay = actual
                .GroupBy(r => r.Date.Day)
                .ToDictionary(g => g.Key, g => g.ToList());

            var actualDailyBalances = GetMonthActualDailyBalances(account, year, month);

            // Forecasts
            var baseForecasts = ReadForecastEntriesExpanded(DateTime.Today.AddMonths(-3), DateTime.Today.AddYears(1));

            // ✅ FIX: method exists below now
            var derivedBmoPayments = ComputeCardPaymentsForBmo(baseForecasts, DateTime.Today, DateTime.Today.AddYears(1), true);

            var forecastBalances = CalculateForecastBalances(account, baseForecasts, derivedBmoPayments);

            Dictionary<int, List<ForecastEntry>> forecastByDay;

            if (account.Equals("BMO Check", StringComparison.OrdinalIgnoreCase))
            {
                var display = baseForecasts
                    .Where(f => f.Account.Equals("BMO Check", StringComparison.OrdinalIgnoreCase))
                    .Concat(derivedBmoPayments)
                    .Where(f => f.Date.Year == year && f.Date.Month == month)
                    .ToList();

                forecastByDay = display.GroupBy(f => f.Date.Day).ToDictionary(g => g.Key, g => g.ToList());
            }
            else if (IsCard(account))
            {
                var mirroredPayments = derivedBmoPayments
                    .Select(d =>
                    {
                        var cardName = GetCardNameFromCategory(d.Category);
                        if (string.IsNullOrWhiteSpace(cardName)) return null;
                        if (!cardName.Equals(account, StringComparison.OrdinalIgnoreCase)) return null;

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

                var display = baseForecasts
                    .Where(f => f.Account.Equals(account, StringComparison.OrdinalIgnoreCase))
                    .Concat(mirroredPayments)
                    .Where(f => f.Date.Year == year && f.Date.Month == month)
                    .ToList();

                forecastByDay = display.GroupBy(f => f.Date.Day).ToDictionary(g => g.Key, g => g.ToList());
            }
            else
            {
                var display = baseForecasts
                    .Where(f => f.Account.Equals(account, StringComparison.OrdinalIgnoreCase))
                    .Where(f => f.Date.Year == year && f.Date.Month == month)
                    .ToList();

                forecastByDay = display.GroupBy(f => f.Date.Day).ToDictionary(g => g.Key, g => g.ToList());
            }

            var today = DateTime.Today.Date;

            for (int day = 1; day <= daysInMonth; day++)
            {
                int gridRow = 1 + ((firstDow + (day - 1)) / 7);
                int gridCol = (firstDow + (day - 1)) % 7;

                var date = new DateTime(year, month, day).Date;

                // Day cell: Top = entries, Bottom = balance
                var cellGrid = new Grid
                {
                    Padding = new Thickness(6),
                    RowDefinitions =
                    {
                        new RowDefinition { Height = GridLength.Star },
                        new RowDefinition { Height = GridLength.Auto }
                    },
                    RowSpacing = 2
                };

                var entriesStack = new VerticalStackLayout { Spacing = 1 };
                var footerStack = new VerticalStackLayout { Spacing = 0 };

                cellGrid.Add(entriesStack);
                Grid.SetRow(entriesStack, 0);

                cellGrid.Add(footerStack);
                Grid.SetRow(footerStack, 1);

                entriesStack.Children.Add(new Label
                {
                    Text = day.ToString(),
                    FontAttributes = FontAttributes.Bold,
                    FontSize = 16,
                    TextColor = Colors.Black
                });

                if (actualByDay.TryGetValue(day, out var dayActuals))
                    foreach (var a in dayActuals)
                        entriesStack.Children.Add(MakeLineLabel($"{a.Category} {a.Amount:C2}", a.Amount));

                if (forecastByDay.TryGetValue(day, out var dayForecasts))
                    foreach (var f in dayForecasts)
                        entriesStack.Children.Add(MakeLineLabel($"{f.Category} {f.Amount:C2}", f.Amount));

                if (date <= today)
                {
                    if (actualDailyBalances.TryGetValue(date, out var bal))
                    {
                        if (IsCard(account)) bal = ClampCardBalance(bal);
                        footerStack.Children.Add(MakeBalanceLabel(bal, false));
                    }
                }
                else
                {
                    if (forecastBalances.TryGetValue(date, out var fb))
                    {
                        var bal = fb.balance;
                        if (IsCard(account)) bal = ClampCardBalance(bal);
                        footerStack.Children.Add(MakeBalanceLabel(bal, true));
                    }
                }

                var frame = new Frame
                {
                    BorderColor = Colors.LightGray,
                    Padding = 0,
                    Margin = new Thickness(1),
                    HasShadow = false,
                    Content = cellGrid
                };

                MonthGrid.Add(frame, gridCol, gridRow);
            }

            // Trigger re-measure after rebuild
            Dispatcher.Dispatch(() =>
            {
                this.InvalidateMeasure();
                CalendarHost?.InvalidateMeasure();
                MonthGrid?.InvalidateMeasure();
            });
        }

        // ===== Helpers for colors/labels =====
        private Color AmountColor(decimal amount)
        {
            if (amount < 0m) return Colors.Red;
            if (amount > 0m) return Colors.Green;
            return Colors.Black;
        }

        private Label MakeLineLabel(string text, decimal amount, double fontSize = 12, bool bold = false)
        {
            return new Label
            {
                Text = text,
                FontSize = fontSize,
                FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None,
                TextColor = AmountColor(amount),
                LineBreakMode = LineBreakMode.TailTruncation
            };
        }

        private Label MakeBalanceLabel(decimal balance, bool isForecast)
        {
            return new Label
            {
                Text = isForecast ? $"Forecast Balance: {balance:C2}" : $"Balance: {balance:C2}",
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = balance < 0m ? Colors.Red : Colors.Green,
                LineBreakMode = LineBreakMode.TailTruncation
            };
        }

        // ===== Cards =====
        private bool IsCard(string account) =>
            account.Equals("AMEX", StringComparison.OrdinalIgnoreCase) ||
            account.Equals("Visa", StringComparison.OrdinalIgnoreCase) ||
            account.Equals("MasterCard", StringComparison.OrdinalIgnoreCase);

        private decimal ClampCardBalance(decimal balance) => balance > 0m ? 0m : balance;

        private string GetCardNameFromCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category)) return null;
            if (!category.StartsWith("Card Payment", StringComparison.OrdinalIgnoreCase)) return null;

            var parts = category.Split('-');
            if (parts.Length < 2) return null;
            return parts[1].Trim();
        }

        // ===== Register reading =====
        private List<RegisterEntry> ReadRegisterEntries(string account)
        {
            var result = new List<RegisterEntry>();

            if (!accountFileMap.TryGetValue(account, out var fileName))
                return result;

            string filePath = FilePathHelper.GetKukiFinancePath(fileName);
            if (!File.Exists(filePath))
                return result;

            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2) return result;

            static int FindHeaderIndex(string[] headers, params string[] names)
            {
                for (int i = 0; i < headers.Length; i++)
                {
                    var h = headers[i].Trim().Replace("\uFEFF", "");
                    foreach (var n in names)
                        if (h.Equals(n, StringComparison.OrdinalIgnoreCase))
                            return i;
                }
                return -1;
            }

            var headers = lines[0].Split(',');

            int dateIdx = FindHeaderIndex(headers, "Date", "AsOf", "As Of", "Posting Date", "Transaction Date", "Day");
            int catIdx = FindHeaderIndex(headers, "Category", "Description", "Memo", "Payee", "Details");
            int amtIdx = FindHeaderIndex(headers, "Amount", "Transaction", "Change", "Delta");
            int balIdx = FindHeaderIndex(headers, "Balance", "Ending Balance", "Account Balance",
                                         "Value", "Account Value", "Market Value", "Total Value", "Net Value");

            if (dateIdx < 0 || balIdx < 0)
                return result;

            string[] formats = { "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "M/d/yy", "MM/dd/yy" };

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                int maxIdx = Math.Max(dateIdx, Math.Max(catIdx, Math.Max(amtIdx, balIdx)));
                if (parts.Length <= maxIdx) continue;

                if (!DateTime.TryParseExact(parts[dateIdx].Trim(), formats,
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    if (!DateTime.TryParse(parts[dateIdx].Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out date) &&
                        !DateTime.TryParse(parts[dateIdx].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                        continue;
                }

                string category = catIdx >= 0 ? parts[catIdx].Trim() : "";

                decimal amount = 0m;
                if (amtIdx >= 0)
                    decimal.TryParse(parts[amtIdx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out amount);

                decimal balance = 0m;
                decimal.TryParse(parts[balIdx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out balance);

                result.Add(new RegisterEntry
                {
                    Date = date.Date,
                    Category = category,
                    Amount = amount,
                    Balance = balance
                });
            }

            return result.OrderBy(r => r.Date).ToList();
        }

        private Dictionary<DateTime, decimal> CalculateDailyBalances(string account, DateTime start, DateTime end)
        {
            var rows = ReadRegisterEntries(account);
            var byDate = rows
                .GroupBy(r => r.Date.Date)
                .ToDictionary(g => g.Key, g => g.Last().Balance);

            decimal current = 0m;
            var lastBefore = rows.Where(r => r.Date.Date <= start.Date).OrderBy(r => r.Date).LastOrDefault();
            if (lastBefore != null) current = lastBefore.Balance;

            var result = new Dictionary<DateTime, decimal>();
            for (var d = start.Date; d <= end.Date; d = d.AddDays(1))
            {
                if (byDate.TryGetValue(d, out var b))
                    current = b;

                result[d] = current;
            }
            return result;
        }

        private Dictionary<DateTime, decimal> GetMonthActualDailyBalances(string account, int year, int month)
        {
            var start = new DateTime(year, month, 1).Date;
            var end = new DateTime(year, month, DateTime.DaysInMonth(year, month)).Date;

            var daily = CalculateDailyBalances(account, start, end);

            if (IsCard(account))
            {
                var keys = daily.Keys.ToList();
                foreach (var k in keys)
                    daily[k] = ClampCardBalance(daily[k]);
            }

            return daily;
        }

        // ===== Forecast reading & expansion =====
        private List<ForecastEntry> ReadForecastEntriesExpanded(DateTime from, DateTime to)
        {
            string forecastFile = FilePathHelper.GetKukiFinancePath("ForecastExpenses.csv");
            var expanded = new List<ForecastEntry>();
            if (!File.Exists(forecastFile)) return expanded;

            var lines = File.ReadAllLines(forecastFile);
            if (lines.Length < 2) return expanded;

            foreach (var raw in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var parts = raw.Split(',');
                if (parts.Length < 7) continue;

                var row = new ForecastExpenseRow
                {
                    Account = parts[0].Trim(),
                    Frequency = parts[1].Trim(),
                    Month = parts[3].Trim(),
                    Category = parts[5].Trim()
                };

                int.TryParse(parts[2].Trim(), out var yr);
                row.Year = yr;

                int.TryParse(parts[4].Trim(), out var day);
                row.Day = day <= 0 ? 1 : day;

                if (!decimal.TryParse(parts[6].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var amt))
                    continue;

                row.Amount = amt;

                ExpandForecastRow(row, from, to, expanded);
            }

            return expanded.OrderBy(e => e.Date).ToList();
        }

        private void ExpandForecastRow(ForecastExpenseRow row, DateTime from, DateTime to, List<ForecastEntry> output)
        {
            string freq = (row.Frequency ?? "").Trim();

            if (freq.Equals("Once", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryMonthNumber(row.Month, out var m)) return;
                int dom = Math.Min(row.Day, DateTime.DaysInMonth(row.Year, m));
                var dt = new DateTime(row.Year, m, dom).Date;

                if (dt >= from.Date && dt <= to.Date)
                    output.Add(new ForecastEntry { Date = dt, Account = row.Account, Category = row.Category, Amount = row.Amount });

                return;
            }

            if (freq.Equals("Monthly", StringComparison.OrdinalIgnoreCase))
            {
                if (row.Month.Equals("All", StringComparison.OrdinalIgnoreCase))
                {
                    var cursor = new DateTime(from.Year, from.Month, 1);
                    var endMonth = new DateTime(to.Year, to.Month, 1);

                    while (cursor <= endMonth)
                    {
                        int dom = Math.Min(row.Day, DateTime.DaysInMonth(cursor.Year, cursor.Month));
                        var dt = new DateTime(cursor.Year, cursor.Month, dom).Date;

                        if (cursor.Year >= row.Year && dt >= from.Date && dt <= to.Date)
                            output.Add(new ForecastEntry { Date = dt, Account = row.Account, Category = row.Category, Amount = row.Amount });

                        cursor = cursor.AddMonths(1);
                    }
                }
                else
                {
                    if (!TryMonthNumber(row.Month, out var m)) return;

                    for (int y = from.Year; y <= to.Year; y++)
                    {
                        if (y < row.Year) continue;
                        int dom = Math.Min(row.Day, DateTime.DaysInMonth(y, m));
                        var dt = new DateTime(y, m, dom).Date;

                        if (dt >= from.Date && dt <= to.Date)
                            output.Add(new ForecastEntry { Date = dt, Account = row.Account, Category = row.Category, Amount = row.Amount });
                    }
                }
                return;
            }

            if (freq.Equals("Annual", StringComparison.OrdinalIgnoreCase) ||
                freq.Equals("Yearly", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryMonthNumber(row.Month, out var m)) return;

                for (int y = from.Year; y <= to.Year; y++)
                {
                    if (y < row.Year) continue;
                    int dom = Math.Min(row.Day, DateTime.DaysInMonth(y, m));
                    var dt = new DateTime(y, m, dom).Date;

                    if (dt >= from.Date && dt <= to.Date)
                        output.Add(new ForecastEntry { Date = dt, Account = row.Account, Category = row.Category, Amount = row.Amount });
                }
                return;
            }
        }

        private bool TryMonthNumber(string monthNameOrAll, out int month)
        {
            month = 0;
            if (string.IsNullOrWhiteSpace(monthNameOrAll)) return false;
            if (monthNameOrAll.Equals("All", StringComparison.OrdinalIgnoreCase)) return false;

            if (DateTime.TryParseExact(monthNameOrAll, "MMMM", CultureInfo.CurrentCulture, DateTimeStyles.None, out var dt))
            {
                month = dt.Month;
                return true;
            }

            if (int.TryParse(monthNameOrAll, out var m) && m >= 1 && m <= 12)
            {
                month = m;
                return true;
            }

            return false;
        }

        // ===== Forecast balances =====
        private Dictionary<DateTime, (decimal balance, List<ForecastEntry> forecasts)> CalculateForecastBalances(
            string account,
            List<ForecastEntry> baseForecasts,
            List<ForecastEntry> derivedBmoPayments)
        {
            var startDate = DateTime.Today.Date;
            var endDate = DateTime.Today.AddYears(1).Date;

            var dailyActual = CalculateDailyBalances(account, startDate, startDate);
            decimal running = dailyActual.TryGetValue(startDate, out var b0) ? b0 : 0m;

            var forecasts = new List<ForecastEntry>();
            forecasts.AddRange(baseForecasts);

            if (account.Equals("BMO Check", StringComparison.OrdinalIgnoreCase))
                forecasts.AddRange(derivedBmoPayments);

            if (IsCard(account))
            {
                var mirrored = derivedBmoPayments
                    .Select(d =>
                    {
                        var cn = GetCardNameFromCategory(d.Category);
                        if (string.IsNullOrWhiteSpace(cn)) return null;
                        if (!cn.Equals(account, StringComparison.OrdinalIgnoreCase)) return null;

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

                forecasts.AddRange(mirrored);
            }

            var byDate = forecasts
                .Where(f => f.Account.Equals(account, StringComparison.OrdinalIgnoreCase))
                .GroupBy(f => f.Date.Date)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new Dictionary<DateTime, (decimal balance, List<ForecastEntry> forecasts)>();

            for (var d = startDate; d <= endDate; d = d.AddDays(1))
            {
                if (byDate.TryGetValue(d, out var list))
                {
                    foreach (var f in list)
                        running += f.Amount;

                    if (IsCard(account))
                        running = ClampCardBalance(running);

                    result[d] = (running, list);
                }
                else
                {
                    if (IsCard(account))
                        running = ClampCardBalance(running);

                    result[d] = (running, new List<ForecastEntry>());
                }
            }

            return result;
        }

        // ===== Chart helpers =====
        private List<(DateTime WeekEndDate, decimal Balance)> BuildActualWeeklyBalances(string account, int year)
        {
            var list = new List<(DateTime, decimal)>();

            var yearStart = new DateTime(year, 1, 1);
            var yearEnd = new DateTime(year, 12, 31);

            var daily = CalculateDailyBalances(account, yearStart, yearEnd);

            for (var d = yearStart; d <= yearEnd; d = d.AddDays(1))
            {
                if (d.DayOfWeek != DayOfWeek.Saturday) continue;
                if (d > DateTime.Today) break;

                if (daily.TryGetValue(d.Date, out var bal))
                    list.Add((d.Date, bal));
            }

            return list;
        }

        private List<(DateTime WeekEndDate, decimal Balance)> BuildForecastWeeklyBalances(string account)
        {
            var start = DateTime.Today.Date;
            var end = DateTime.Today.AddYears(1).Date;

            var baseForecasts = ReadForecastEntriesExpanded(DateTime.Today.AddMonths(-3), DateTime.Today.AddYears(1));
            var derived = ComputeCardPaymentsForBmo(baseForecasts, DateTime.Today, DateTime.Today.AddYears(1), true);
            var dailyForecast = CalculateForecastBalances(account, baseForecasts, derived);

            var list = new List<(DateTime, decimal)>();

            var firstSat = start;
            while (firstSat.DayOfWeek != DayOfWeek.Saturday) firstSat = firstSat.AddDays(1);

            for (var d = firstSat; d <= end; d = d.AddDays(7))
            {
                if (dailyForecast.TryGetValue(d.Date, out var fb))
                    list.Add((d.Date, fb.balance));
            }

            return list;
        }

        // =========================================================================================
        // ✅ MISSING METHODS ADDED HERE: ComputeCardPaymentsForBmo + simulation helpers
        // =========================================================================================

        private List<ForecastEntry> ComputeCardPaymentsForBmo(
            List<ForecastEntry> allForecasts,
            DateTime from,
            DateTime to,
            bool useCardForecastBalancesForFuture)
        {
            var results = new List<ForecastEntry>();

            var cards = new[]
            {
                new { Name = "AMEX",       DueDay = 8  },
                new { Name = "Visa",       DueDay = 26 },
                new { Name = "MasterCard", DueDay = 14 }
            };

            // account -> date -> sum(amount)
            var forecastByCardByDate = allForecasts
                .Where(f => !string.IsNullOrWhiteSpace(f.Account))
                .GroupBy(f => f.Account, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => x.Date.Date)
                          .ToDictionary(gg => gg.Key, gg => gg.Sum(x => x.Amount)),
                    StringComparer.OrdinalIgnoreCase);

            // Future simulated payment amounts for due dates whose cutoffs are in the future
            var tuples = cards.Select(c => (c.Name, c.DueDay)).ToList();
            var futurePaymentAmounts = useCardForecastBalancesForFuture
                ? ComputeFutureCardStatementAmounts(allForecasts, tuples, DateTime.Today, to)
                : null;

            DateTime today = DateTime.Today.Date;

            var cursor = new DateTime(from.Year, from.Month, 1);
            var endMonth = new DateTime(to.Year, to.Month, 1);

            while (cursor <= endMonth)
            {
                int year = cursor.Year;
                int month = cursor.Month;

                foreach (var card in cards)
                {
                    int dueDay = Math.Min(card.DueDay, DateTime.DaysInMonth(year, month));
                    var dueDate = new DateTime(year, month, dueDay).Date;

                    if (dueDate < from.Date || dueDate > to.Date) continue;
                    if (dueDate <= today) continue; // don’t generate payments for past/today

                    DateTime billingEnd = GetBillingEndForCard(card.Name, year, month).Date;

                    decimal payAmt = 0m;

                    // CASE 1: cutoff already happened (current-month due date)
                    if (billingEnd <= today)
                    {
                        // Statement due comes from actual cutoff balance
                        var cutoffBal = ClampCardBalance(GetActualBalanceOnOrBefore(card.Name, billingEnd));
                        var stmtDue = Math.Max(0m, -cutoffBal);

                        if (stmtDue > 0m)
                        {
                            decimal todayBal = ClampCardBalance(GetActualBalanceOnOrBefore(card.Name, today));

                            var perDate = forecastByCardByDate.TryGetValue(card.Name, out var m)
                                ? m
                                : new Dictionary<DateTime, decimal>();

                            // balance on due date before payment
                            var balOnDueBeforePay = ProjectCardBalanceToDate(today, dueDate, perDate, todayBal);
                            var needed = Math.Max(0m, -balOnDueBeforePay);

                            payAmt = Math.Min(stmtDue, needed);
                        }
                    }
                    else
                    {
                        // CASE 2: cutoff is in the future (future months) -> use simulator computed pay amount
                        if (futurePaymentAmounts != null
                            && futurePaymentAmounts.TryGetValue(card.Name, out var byDue)
                            && byDue.TryGetValue(dueDate, out var simulatedPay))
                        {
                            payAmt = simulatedPay;
                        }
                    }

                    if (payAmt <= 0m) continue;

                    results.Add(new ForecastEntry
                    {
                        Date = dueDate,
                        Account = "BMO Check",
                        Category = $"Card Payment - {card.Name}",
                        Amount = -payAmt
                    });
                }

                cursor = cursor.AddMonths(1);
            }

            return results.OrderBy(r => r.Date).ToList();
        }

        // Projects card balance from startDate to targetDate (inclusive), applying daily forecast deltas.
        // Cards are clamped to never be positive.
        private decimal ProjectCardBalanceToDate(
            DateTime startDate,
            DateTime targetDate,
            Dictionary<DateTime, decimal> forecastByDate,
            decimal startBalance)
        {
            decimal running = ClampCardBalance(startBalance);

            for (var d = startDate.Date.AddDays(1); d <= targetDate.Date; d = d.AddDays(1))
            {
                if (forecastByDate.TryGetValue(d, out var delta))
                    running += delta;

                running = ClampCardBalance(running);
            }

            return running;
        }

        private Dictionary<string, Dictionary<DateTime, decimal>> ComputeFutureCardStatementAmounts(
            List<ForecastEntry> allForecasts,
            IEnumerable<(string Name, int DueDay)> cards,
            DateTime horizonStart,
            DateTime horizonEnd)
        {
            var today = horizonStart.Date;

            // Build a list of due dates + cutoff dates
            var dueItems = new List<(string card, DateTime dueDate, DateTime cutoffDate)>();
            var cursor = new DateTime(today.Year, today.Month, 1);
            var endMonth = new DateTime(horizonEnd.Year, horizonEnd.Month, 1);

            while (cursor <= endMonth)
            {
                int year = cursor.Year;
                int month = cursor.Month;

                foreach (var (name, dueDayRaw) in cards)
                {
                    int dueDay = Math.Min(dueDayRaw, DateTime.DaysInMonth(year, month));
                    var dueDate = new DateTime(year, month, dueDay).Date;

                    if (dueDate <= today) continue;
                    if (dueDate > horizonEnd.Date) continue;

                    var cutoff = GetBillingEndForCard(name, year, month).Date;
                    dueItems.Add((name, dueDate, cutoff));
                }

                cursor = cursor.AddMonths(1);
            }

            var result = new Dictionary<string, Dictionary<DateTime, decimal>>(StringComparer.OrdinalIgnoreCase);
            if (dueItems.Count == 0) return result;

            var simEnd = dueItems.Max(x => x.dueDate).Date;

            // account -> date -> sum(amount)
            var forecastByCardByDate = allForecasts
                .Where(f => !string.IsNullOrWhiteSpace(f.Account))
                .GroupBy(f => f.Account, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.GroupBy(x => x.Date.Date)
                          .ToDictionary(gg => gg.Key, gg => gg.Sum(x => x.Amount)),
                    StringComparer.OrdinalIgnoreCase);

            // (card, cutoff) -> due dates that share that cutoff
            var cutoffToDueDates = dueItems
                .GroupBy(x => (x.card, x.cutoffDate))
                .ToDictionary(g => g.Key, g => g.Select(x => x.dueDate).Distinct().ToList());

            // (card, due) -> cutoff
            var dueToCutoff = dueItems.ToDictionary(x => (x.card, x.dueDate), x => x.cutoffDate);

            // Running balances start at today's actual balance (clamped)
            var running = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, _) in cards)
            {
                running[name] = ClampCardBalance(GetActualBalanceOnOrBefore(name, today));
                result[name] = new Dictionary<DateTime, decimal>();
            }

            // statement due captured at cutoff: max(0, -balanceAtCutoff)
            var statementDueAtCutoff = new Dictionary<(string card, DateTime cutoff), decimal>();

            // Seed statement dues for cutoffs already in the past (so current-month due dates can be paid)
            foreach (var grp in cutoffToDueDates.Keys)
            {
                var cardName = grp.card;
                var cutoffDate = grp.cutoffDate;

                if (cutoffDate < today)
                {
                    var balAtCutoff = ClampCardBalance(GetActualBalanceOnOrBefore(cardName, cutoffDate));
                    var stmtDue = Math.Max(0m, -balAtCutoff);
                    statementDueAtCutoff[(cardName, cutoffDate)] = stmtDue;
                }
            }

            for (var d = today.AddDays(1); d <= simEnd; d = d.AddDays(1))
            {
                foreach (var (name, _) in cards)
                {
                    // Apply daily forecasts
                    if (forecastByCardByDate.TryGetValue(name, out var byDate) && byDate.TryGetValue(d.Date, out var amt))
                        running[name] += amt;

                    // enforce "never positive"
                    running[name] = ClampCardBalance(running[name]);

                    // Capture statement due at cutoff
                    if (cutoffToDueDates.ContainsKey((name, d.Date)))
                    {
                        var stmtDue = Math.Max(0m, -running[name]);
                        statementDueAtCutoff[(name, d.Date)] = stmtDue;
                    }

                    // On due date: pay min(statementDue, needed)
                    if (dueToCutoff.TryGetValue((name, d.Date), out var cutoff))
                    {
                        statementDueAtCutoff.TryGetValue((name, cutoff), out var stmtDue);

                        var needed = Math.Max(0m, -running[name]);
                        var payAmt = Math.Min(stmtDue, needed);

                        if (payAmt > 0m)
                        {
                            running[name] += payAmt;
                            running[name] = ClampCardBalance(running[name]);
                        }

                        result[name][d.Date] = payAmt;
                    }
                }
            }

            return result;
        }

        private DateTime GetBillingEndForCard(string cardName, int dueYear, int dueMonth)
        {
            if (cardName.Equals("AMEX", StringComparison.OrdinalIgnoreCase))
            {
                var endMonthDate = new DateTime(dueYear, dueMonth, 1).AddMonths(-1);
                int dom = Math.Min(24, DateTime.DaysInMonth(endMonthDate.Year, endMonthDate.Month));
                return new DateTime(endMonthDate.Year, endMonthDate.Month, dom);
            }
            else
            {
                int dom = Math.Min(1, DateTime.DaysInMonth(dueYear, dueMonth));
                return new DateTime(dueYear, dueMonth, dom);
            }
        }

        private decimal GetActualBalanceOnOrBefore(string account, DateTime date)
        {
            var rows = ReadRegisterEntries(account);
            var last = rows
                .Where(r => r.Date.Date <= date.Date)
                .OrderBy(r => r.Date)
                .LastOrDefault();

            return last?.Balance ?? 0m;
        }
    }
}
