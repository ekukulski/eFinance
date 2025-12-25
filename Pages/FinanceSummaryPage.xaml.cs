using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Maui.Controls;
using KukiFinance.Services;
using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Maui;
using System.Collections.ObjectModel;

namespace KukiFinance.Pages
{
    public partial class FinanceSummaryPage : ContentPage
    {
        // Account definitions (Name, FilePath)
        private readonly (string Name, string File)[] AssetAccounts = new[]
        {
            ("Midland", FilePathHelper.GetKukiFinancePath("MidlandCurrent.csv")),
            ("Charles Schwab Contributory", FilePathHelper.GetKukiFinancePath("CharlesSchwabContributoryCurrent.csv")),
            ("Charles Schwab Joint Tenant", FilePathHelper.GetKukiFinancePath("CharlesSchwabJointTenantCurrent.csv")),
            ("Charles Schwab Roth IRA Ed", FilePathHelper.GetKukiFinancePath("CharlesSchwabRothIraEdCurrent.csv")),
            ("Charles Schwab Roth IRA Patti", FilePathHelper.GetKukiFinancePath("CharlesSchwabRothIraPattiCurrent.csv")),
            ("NetX", FilePathHelper.GetKukiFinancePath("NetXCurrent.csv")),
            ("HealthPro", FilePathHelper.GetKukiFinancePath("HealthProCurrent.csv")),
            ("Select 401(K)", FilePathHelper.GetKukiFinancePath("Select401KCurrent.csv")),
            ("Gold", FilePathHelper.GetKukiFinancePath("GoldCurrent.csv")),
            ("House", FilePathHelper.GetKukiFinancePath("HouseCurrent.csv")),
            ("Chevrolet Impala", FilePathHelper.GetKukiFinancePath("ChevroletImpalaCurrent.csv")),
            ("Nissan Sentra", FilePathHelper.GetKukiFinancePath("NissanSentraCurrent.csv")),
        };

        private readonly (string Name, string File)[] CashAccounts = new[]
        {
            ("Cash", FilePathHelper.GetKukiFinancePath("CashCurrent.csv")),
            ("BMO Check", FilePathHelper.GetKukiFinancePath("BMOCheckCurrent.csv")),
            ("BMO Money Market", FilePathHelper.GetKukiFinancePath("BMOMoneyMarketCurrent.csv")),
            ("BMO CD", FilePathHelper.GetKukiFinancePath("BMOCDCurrent.csv")),
        };

        private readonly (string Name, string File)[] LiabilityAccounts = new[]
        {
            ("AMEX", FilePathHelper.GetKukiFinancePath("AMEXCurrent.csv")),
            ("Visa", FilePathHelper.GetKukiFinancePath("VisaCurrent.csv")),
            ("MasterCard", FilePathHelper.GetKukiFinancePath("MasterCardCurrent.csv")),
        };

        public FinanceSummaryPage()
        {
            InitializeComponent();
            try
            {
                BuildSummaryTable();
                PopulatePickers();
            }
            catch (Exception ex)
            {
                Content = new Label
                {
                    Text = "Error: " + ex.ToString(),
                    TextColor = Colors.Red,
                    FontSize = 16
                };
            }
        }

        private void PopulatePickers()
        {
            var years = GetAvailableYears();
            YearPicker.ItemsSource = years;
            if (years.Count > 0) YearPicker.SelectedIndex = 0;

            QuarterPicker.ItemsSource = new[] { "All", "Q1", "Q2", "Q3", "Q4" };
            QuarterPicker.SelectedIndex = 0;

            var months = Enumerable.Range(1, 12)
                .Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m))
                .ToList();
            months.Insert(0, "All");
            MonthPicker.ItemsSource = months;
            MonthPicker.SelectedIndex = 0;
        }

        private List<int> GetAvailableYears()
        {
            var years = new HashSet<int>();
            foreach (var account in AssetAccounts.Concat(CashAccounts).Concat(LiabilityAccounts))
            {
                if (!File.Exists(account.File)) continue;
                var lines = File.ReadAllLines(account.File);
                if (lines.Length < 2) continue;
                var headers = lines[0].Split(',');
                int dateIdx = Array.FindIndex(headers, h => h.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));
                if (dateIdx < 0) continue;
                foreach (var line in lines.Skip(1))
                {
                    var cols = line.Split(',');
                    if (cols.Length <= dateIdx) continue;
                    if (DateTime.TryParse(cols[dateIdx], out var date))
                        years.Add(date.Year);
                }
            }
            return years.OrderByDescending(y => y).ToList();
        }

        private void BuildSummaryTable()
        {
            SummaryGrid.Children.Clear();

            // Add header row (no background color for header)
            SummaryGrid.Add(new Label { Text = "Asset Name", FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center }, 0, 0);
            SummaryGrid.Add(new Label { Text = "Asset Value", FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center }, 1, 0);
            SummaryGrid.Add(new Label { Text = "Cash Name", FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center }, 2, 0);
            SummaryGrid.Add(new Label { Text = "Cash Value", FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center }, 3, 0);
            SummaryGrid.Add(new Label { Text = "Liability Name", FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center }, 4, 0);
            SummaryGrid.Add(new Label { Text = "Liability Value", FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center }, 5, 0);

            int maxRows = Math.Max(AssetAccounts.Length, Math.Max(CashAccounts.Length, LiabilityAccounts.Length));
            var dataRows = new List<(string assetName, decimal? assetValue, string cashName, decimal? cashValue, string liabilityName, decimal? liabilityValue)>();

            decimal assetTotal = 0, cashTotal = 0, liabilityTotal = 0;

            for (int i = 0; i < maxRows; i++)
            {
                string assetName = null, cashName = null, liabilityName = null;
                decimal? assetValue = null, cashValue = null, liabilityValue = null;

                if (i < AssetAccounts.Length)
                {
                    var (name, file) = AssetAccounts[i];
                    var value = GetCurrentBalanceFromCsv(file);
                    assetName = name;
                    assetValue = value;
                    assetTotal += value;
                }
                if (i < CashAccounts.Length)
                {
                    var (name, file) = CashAccounts[i];
                    var value = GetCurrentBalanceFromCsv(file);
                    cashName = name;
                    cashValue = value;
                    cashTotal += value;
                }
                if (i < LiabilityAccounts.Length)
                {
                    var (name, file) = LiabilityAccounts[i];
                    var value = GetCurrentBalanceFromCsv(file);
                    liabilityName = name;
                    liabilityValue = value;
                    liabilityTotal += value;
                }

                dataRows.Add((assetName, assetValue, cashName, cashValue, liabilityName, liabilityValue));
            }

            // Add data rows with background color only where a value exists
            int rowIndex = 1;
            foreach (var row in dataRows)
            {
                // Only color cells where a value exists, otherwise transparent
                SummaryGrid.Add(new Label
                {
                    Text = row.assetName ?? "",
                    HorizontalOptions = LayoutOptions.Center,
                    BackgroundColor = row.assetName != null ? GetRowColor(rowIndex) : Colors.Transparent
                }, 0, rowIndex);

                SummaryGrid.Add(CreateAmountLabel(row.assetValue, row.assetName, GetRowColor(rowIndex)), 1, rowIndex);

                SummaryGrid.Add(new Label
                {
                    Text = row.cashName ?? "",
                    HorizontalOptions = LayoutOptions.Center,
                    BackgroundColor = row.cashName != null ? GetRowColor(rowIndex) : Colors.Transparent
                }, 2, rowIndex);

                SummaryGrid.Add(CreateAmountLabel(row.cashValue, row.cashName, GetRowColor(rowIndex)), 3, rowIndex);

                SummaryGrid.Add(new Label
                {
                    Text = row.liabilityName ?? "",
                    HorizontalOptions = LayoutOptions.Center,
                    BackgroundColor = row.liabilityName != null ? GetRowColor(rowIndex) : Colors.Transparent
                }, 4, rowIndex);

                SummaryGrid.Add(CreateAmountLabel(row.liabilityValue, row.liabilityName, GetRowColor(rowIndex)), 5, rowIndex);

                rowIndex++;
            }

            // 1. Blank row between last data row and Sub-Totals
            SummaryGrid.Add(new Label { Text = "", HeightRequest = 15, BackgroundColor = Colors.Transparent }, 0, rowIndex);
            SummaryGrid.SetColumnSpan(SummaryGrid.Children.Last(), 6);
            rowIndex++;

            // Prepare summary rows as a list for consistent alternation
            var summaryRows = new List<(string label, decimal? asset, decimal? cash, decimal? liability)>
            {
                (
                    "Sub-Totals",
                    assetTotal,
                    cashTotal,
                    liabilityTotal
                ),
                (
                    "Equity",
                    assetTotal + cashTotal,
                    null,
                    null
                ),
                (
                    "Liabilities",
                    liabilityTotal,
                    null,
                    null
                ),
                (
                    "Net Worth",
                    (assetTotal + cashTotal) + liabilityTotal,
                    null,
                    null
                )
            };

            // Sub-Totals row
            {
                Color rowColor = GetRowColor(rowIndex);
                var summary = summaryRows[0];
                SummaryGrid.Add(new Label { Text = summary.label, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, BackgroundColor = rowColor }, 0, rowIndex);
                SummaryGrid.Add(CreateAmountLabel(summary.asset, summary.label, rowColor, true), 1, rowIndex);
                SummaryGrid.Add(new Label { Text = "", BackgroundColor = Colors.Transparent }, 2, rowIndex);
                SummaryGrid.Add(CreateAmountLabel(summary.cash, summary.label, rowColor, true), 3, rowIndex);
                SummaryGrid.Add(new Label { Text = "", BackgroundColor = Colors.Transparent }, 4, rowIndex);
                SummaryGrid.Add(CreateAmountLabel(summary.liability, summary.label, rowColor, true), 5, rowIndex);
                rowIndex++;
            }

            // 2. Blank row between Sub-Totals and Equity
            SummaryGrid.Add(new Label { Text = "", HeightRequest = 15, BackgroundColor = Colors.Transparent }, 0, rowIndex);
            SummaryGrid.SetColumnSpan(SummaryGrid.Children.Last(), 6);
            rowIndex++;

            // Equity row
            {
                Color rowColor = GetRowColor(rowIndex);
                var summary = summaryRows[1];
                SummaryGrid.Add(new Label { Text = summary.label, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, BackgroundColor = rowColor }, 0, rowIndex);
                SummaryGrid.Add(CreateAmountLabel(summary.asset, summary.label, rowColor, true), 1, rowIndex);
                for (int col = 2; col < 6; col++)
                    SummaryGrid.Add(new Label { Text = "", BackgroundColor = Colors.Transparent }, col, rowIndex);
                rowIndex++;
            }

            // Liabilities row
            {
                Color rowColor = GetRowColor(rowIndex);
                var summary = summaryRows[2];
                SummaryGrid.Add(new Label { Text = summary.label, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, BackgroundColor = rowColor }, 0, rowIndex);
                SummaryGrid.Add(CreateAmountLabel(summary.asset, summary.label, rowColor, true), 1, rowIndex);
                for (int col = 2; col < 6; col++)
                    SummaryGrid.Add(new Label { Text = "", BackgroundColor = Colors.Transparent }, col, rowIndex);
                rowIndex++;
            }

            // 3. Blank row between Liabilities and Net Worth
            SummaryGrid.Add(new Label { Text = "", HeightRequest = 15, BackgroundColor = Colors.Transparent }, 0, rowIndex);
            SummaryGrid.SetColumnSpan(SummaryGrid.Children.Last(), 6);
            rowIndex++;

            // Net Worth row
            {
                Color rowColor = GetRowColor(rowIndex);
                var summary = summaryRows[3];
                SummaryGrid.Add(new Label { Text = summary.label, FontAttributes = FontAttributes.Bold, HorizontalOptions = LayoutOptions.Center, BackgroundColor = rowColor }, 0, rowIndex);
                SummaryGrid.Add(CreateAmountLabel(summary.asset, summary.label, rowColor, true), 1, rowIndex);
                for (int col = 2; col < 6; col++)
                    SummaryGrid.Add(new Label { Text = "", BackgroundColor = Colors.Transparent }, col, rowIndex);
                rowIndex++;
            }
        }

        // Helper to get alternating row color
        private Color GetRowColor(int rowIndex)
        {
            return (rowIndex % 2 == 0) ? Color.FromArgb("#CCFFCC") : Color.FromArgb("#FFFFCC");
        }

        // Helper to create a value label with color and formatting logic
        private Label CreateAmountLabel(decimal? value, string accountName, Color rowColor, bool isBold = false)
        {
            // Only show $0.00 if the account exists (name is not null)
            if (accountName == null)
                return new Label { Text = "", BackgroundColor = Colors.Transparent };

            // Format and color as per AmountFormatConverter and AmountColorConverter
            string formatted = value.HasValue
                ? (value.Value < 0 ? $"(${Math.Abs(value.Value):N2})" : $"${value.Value:N2}")
                : "$0.00";
            Color textColor = (value.HasValue && value.Value < 0) ? Colors.Red : Colors.Black;

            return new Label
            {
                Text = formatted,
                TextColor = textColor,
                FontAttributes = isBold ? FontAttributes.Bold : FontAttributes.None,
                HorizontalOptions = LayoutOptions.Center,
                BackgroundColor = rowColor
            };
        }

        // Reads the last balance from a CSV file (last column or "Balance" column)
        private decimal GetCurrentBalanceFromCsv(string filePath)
        {
            if (!File.Exists(filePath))
                return 0m;

            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                return 0m; // No data

            var lastLine = lines.Last();
            var columns = lastLine.Split(',');

            // Try last column
            if (decimal.TryParse(columns.Last().Trim(), out var balance))
                return balance;

            // Try "Balance" column by header
            var headers = lines[0].Split(',');
            int balanceIndex = Array.FindIndex(headers, h => h.Trim().Equals("Balance", StringComparison.OrdinalIgnoreCase));
            if (balanceIndex >= 0 && balanceIndex < columns.Length)
            {
                if (decimal.TryParse(columns[balanceIndex].Trim(), out balance))
                    return balance;
            }

            return 0m;
        }

        // Chart button event handler
        private void ChartButton_Clicked(object sender, EventArgs e)
        {
            if (YearPicker.SelectedItem == null) return;
            var yearSelected = YearPicker.SelectedItem;
            string quarter = QuarterPicker.SelectedItem?.ToString() ?? "All";
            string month = MonthPicker.SelectedItem?.ToString() ?? "All";

            List<DateTime> saturdays;
            if (yearSelected is string && (string)yearSelected == "All")
            {
                // Get all saturdays for all years in your data
                var years = GetAvailableYears();
                saturdays = new List<DateTime>();
                foreach (var year in years)
                    saturdays.AddRange(GetSaturdays(year, "All", "All"));
                saturdays = saturdays.OrderBy(d => d).ToList();
            }
            else
            {
                int year = (int)yearSelected;
                saturdays = GetSaturdays(year, quarter, month);
            }

            var netWorthValues = new List<double>();
            var labels = new List<string>();

            foreach (var saturday in saturdays)
            {
                decimal assetTotal = 0, cashTotal = 0, liabilityTotal = 0;

                foreach (var (name, file) in AssetAccounts)
                    assetTotal += GetBalanceAsOf(file, saturday);
                foreach (var (name, file) in CashAccounts)
                    cashTotal += GetBalanceAsOf(file, saturday);
                foreach (var (name, file) in LiabilityAccounts)
                    liabilityTotal += GetBalanceAsOf(file, saturday);

                var netWorth = assetTotal + cashTotal + liabilityTotal;
                netWorthValues.Add((double)netWorth);
                labels.Add(saturday.ToString("MM/dd/yyyy"));
            }

            NetWorthChart.Series = new ISeries[]
            {
        new LineSeries<double>
        {
            Values = netWorthValues,
            Name = "Net Worth"
        }
            };

            NetWorthChart.XAxes = new Axis[]
            {
        new Axis
        {
            Labels = labels,
            Name = "Week Ending",
            LabelsRotation = 45
        }
            };

            NetWorthChart.YAxes = new Axis[]
            {
        new Axis
        {
            Name = "Net Worth"
        }
            };

            NetWorthChart.IsVisible = true;
        }

        // Helper: Get all Saturdays in the selected period
        private List<DateTime> GetSaturdays(int year, string quarter, string month)
        {
            DateTime start = new DateTime(year, 1, 1);
            DateTime end = new DateTime(year, 12, 31);

            if (quarter != "All")
            {
                int q = int.Parse(quarter.Substring(1, 1));
                start = new DateTime(year, (q - 1) * 3 + 1, 1);
                end = start.AddMonths(3).AddDays(-1);
            }
            if (month != "All")
            {
                int m = DateTime.ParseExact(month, "MMMM", CultureInfo.CurrentCulture).Month;
                start = new DateTime(year, m, 1);
                end = start.AddMonths(1).AddDays(-1);
            }

            // If the period includes today and end is after today, set end to last Saturday before or on today
            var today = DateTime.Today;
            if (end > today)
            {
                // Find last Saturday before or on today
                int daysBack = today.DayOfWeek == DayOfWeek.Saturday ? 0 : ((int)today.DayOfWeek + 1);
                end = today.AddDays(-daysBack);
            }

            // Find first Saturday on/after start
            int daysToSaturday = ((int)DayOfWeek.Saturday - (int)start.DayOfWeek + 7) % 7;
            DateTime firstSaturday = start.AddDays(daysToSaturday);

            var saturdays = new List<DateTime>();
            for (var dt = firstSaturday; dt <= end; dt = dt.AddDays(7))
                saturdays.Add(dt);

            return saturdays;
        }

        private decimal GetFilteredBalance(string filePath, int year, string quarter, string month)
        {
            if (!File.Exists(filePath)) return 0m;
            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2) return 0m;
            var headers = lines[0].Split(',');
            int dateIdx = Array.FindIndex(headers, h => h.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));
            int balanceIdx = Array.FindIndex(headers, h => h.Trim().Equals("Balance", StringComparison.OrdinalIgnoreCase));
            if (dateIdx < 0 || balanceIdx < 0) return 0m;

            decimal lastBalance = 0m;
            foreach (var line in lines.Skip(1))
            {
                var cols = line.Split(',');
                if (cols.Length <= Math.Max(dateIdx, balanceIdx)) continue;
                if (!DateTime.TryParse(cols[dateIdx], out var date)) continue;
                if (date.Year != year) continue;

                if (quarter != "All")
                {
                    int q = int.Parse(quarter.Substring(1, 1));
                    if (((date.Month - 1) / 3 + 1) != q) continue;
                }
                if (month != "All")
                {
                    int m = DateTime.ParseExact(month, "MMMM", CultureInfo.CurrentCulture).Month;
                    if (date.Month != m) continue;
                }
                if (decimal.TryParse(cols[balanceIdx], out var bal))
                    lastBalance = bal;
            }
            return lastBalance;
        }
        private decimal GetBalanceAsOf(string filePath, DateTime asOfDate)
        {
            if (!File.Exists(filePath))
                return 0m;

            var lines = File.ReadAllLines(filePath);
            if (lines.Length < 2)
                return 0m;

            var headers = lines[0].Split(',');
            int dateIdx = Array.FindIndex(headers, h => h.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));
            int balanceIdx = Array.FindIndex(headers, h => h.Trim().Equals("Balance", StringComparison.OrdinalIgnoreCase));
            if (dateIdx < 0 || balanceIdx < 0)
                return 0m;

            decimal lastBalance = 0m;
            foreach (var line in lines.Skip(1))
            {
                var cols = line.Split(',');
                if (cols.Length <= Math.Max(dateIdx, balanceIdx))
                    continue;
                if (DateTime.TryParse(cols[dateIdx], out var date) && date <= asOfDate)
                {
                    if (decimal.TryParse(cols[balanceIdx], out var bal))
                        lastBalance = bal;
                }
            }
            return lastBalance;
        }
        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}