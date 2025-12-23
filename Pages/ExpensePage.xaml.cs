using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Maui.Controls;
using KukiFinance.Services;

namespace KukiFinance.Pages
{
    public partial class ExpensePage : ContentPage
    {
        private static readonly string[] CsvFiles = new[]
        {
            "MidlandCurrent.csv",
            "CharlesSchwabContributoryCurrent.csv",
            "CharlesSchwabJointTenantCurrent.csv",
            "CharlesSchwabRothIraEdCurrent.csv",
            "CharlesSchwabRothIraPattiCurrent.csv",
            "NetXCurrent.csv",
            "HealthProCurrent.csv",
            "Select401KCurrent.csv",
            "GoldCurrent.csv",
            "HouseCurrent.csv",
            "ChevroletImpalaCurrent.csv",
            "NissanSentraCurrent.csv",
            "CashCurrent.csv",
            "BMOCheckCurrent.csv",
            "BMOMoneyMarketCurrent.csv",
            "BMOCDCurrent.csv",
            "AMEXCurrent.csv",
            "VisaCurrent.csv",
            "MasterCardCurrent.csv"
        };

        private static string GetKukiFinanceFolder()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "KukiFinance"
            );
        }

        private static IEnumerable<string> GetCsvFilePaths()
        {
            var folder = GetKukiFinanceFolder();
            return CsvFiles.Select(f => Path.Combine(folder, f));
        }

        private static string GetExcludedCategoriesPath()
        {
            return Path.Combine(GetKukiFinanceFolder(), "ExcludedCategories.csv");
        }

        private List<ExpenseEntry> _allExpenses = new();

        public ExpensePage()
        {
            InitializeComponent();
            WindowCenteringService.CenterWindow(515, 1400);
            LoadAllExpenses();
            PopulatePickers();
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            WindowCenteringService.CenterWindow(515, 1400);
        }

        private static HashSet<string> GetExcludedCategoriesFromCsv()
        {
            var path = GetExcludedCategoriesPath();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return set;
            var lines = File.ReadAllLines(path);
            foreach (var line in lines.Skip(1)) // skip header
            {
                var category = line.Trim();
                if (!string.IsNullOrEmpty(category))
                    set.Add(category);
            }
            return set;
        }

        private async void LoadAllExpenses()
        {
            try
            {
                _allExpenses.Clear();
                var excludedCategories = GetExcludedCategoriesFromCsv();
                int filesLoaded = 0;
                foreach (var file in GetCsvFilePaths())
                {
                    if (!File.Exists(file)) continue;
                    filesLoaded++;
                    var lines = File.ReadAllLines(file);
                    if (lines.Length < 2) continue;
                    var headers = lines[0].Split(',');
                    int dateIdx = Array.FindIndex(headers, h => h.Trim().Equals("Date", StringComparison.OrdinalIgnoreCase));
                    int descIdx = Array.FindIndex(headers, h => h.Trim().Equals("Description", StringComparison.OrdinalIgnoreCase));
                    int catIdx = Array.FindIndex(headers, h => h.Trim().Equals("Category", StringComparison.OrdinalIgnoreCase));
                    int amtIdx = Array.FindIndex(headers, h => h.Trim().Equals("Amount", StringComparison.OrdinalIgnoreCase));
                    if (dateIdx < 0 || catIdx < 0 || amtIdx < 0) continue;

                    for (int i = 1; i < lines.Length; i++)
                    {
                        var parts = lines[i].Split(',');
                        if (parts.Length <= Math.Max(Math.Max(dateIdx, catIdx), amtIdx)) continue;
                        if (!DateTime.TryParse(parts[dateIdx], out var date)) continue;
                        if (date.Year < 2023) continue;
                        var category = parts[catIdx].Trim();
                        if (excludedCategories.Contains(category)) continue;
                        if (!decimal.TryParse(parts[amtIdx], NumberStyles.Any, CultureInfo.InvariantCulture, out var amount)) continue;
                        if (amount >= 0) continue; // Only expenses (negative amounts)
                        _allExpenses.Add(new ExpenseEntry
                        {
                            Date = date,
                            Category = category,
                            Description = descIdx >= 0 ? parts[descIdx].Trim() : "",
                            Amount = amount
                        });
                    }
                }
                if (filesLoaded == 0)
                {
                    await DisplayAlert("Warning", "No expense files found. Please check file locations and permissions.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.ToString(), "OK");
            }
        }

        private void PopulatePickers()
        {
            var years = _allExpenses.Select(e => e.Date.Year).Distinct().OrderByDescending(y => y).ToList();
            YearPicker.ItemsSource = years;
            if (years.Count > 0) YearPicker.SelectedIndex = 0;

            QuarterPicker.ItemsSource = new[] { "All", "Q1", "Q2", "Q3", "Q4" };
            QuarterPicker.SelectedIndex = 0;

            var months = Enumerable.Range(1, 12).Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)).ToList();
            months.Insert(0, "All");
            MonthPicker.ItemsSource = months;
            MonthPicker.SelectedIndex = 0;
        }

        private void OnShowExpensesClicked(object sender, EventArgs e)
        {
            if (YearPicker.SelectedItem == null) return;
            int year = (int)YearPicker.SelectedItem;
            string quarter = QuarterPicker.SelectedItem?.ToString() ?? "All";
            string month = MonthPicker.SelectedItem?.ToString() ?? "All";

            var filtered = _allExpenses.Where(e => e.Date.Year == year);

            if (quarter != "All")
            {
                int q = int.Parse(quarter.Substring(1, 1));
                filtered = filtered.Where(e => ((e.Date.Month - 1) / 3 + 1) == q);
            }
            if (month != "All")
            {
                int m = DateTime.ParseExact(month, "MMMM", CultureInfo.CurrentCulture).Month;
                filtered = filtered.Where(e => e.Date.Month == m);
            }

            var grouped = filtered
                .GroupBy(e => e.Category)
                .OrderBy(g => g.Key)
                .Select(g => new ExpenseCategorySummary
                {
                    Category = g.Key,
                    Total = g.Sum(x => x.Amount)
                })
                .ToList();

            ExpenseCollectionView.ItemsSource = grouped;

            // Set the total label
            var total = grouped.Sum(g => g.Total);
            ExpenseTotalLabel.Text = total.ToString("C2");

            // Calculate daily spend
            var filteredList = filtered.ToList();
            if (filteredList.Count > 0)
            {
                var minDate = filteredList.Min(e => e.Date);
                var maxDate = filteredList.Max(e => e.Date);
                int days = (maxDate - minDate).Days + 1;
                if (days > 0)
                {
                    var dailySpend = total / days;
                    DailySpendLabel.Text = dailySpend.ToString("C2");
                }
                else
                {
                    DailySpendLabel.Text = "$0.00";
                }
            }
            else
            {
                DailySpendLabel.Text = "$0.00";
            }
        }

        private async void OnCategorySelected(object sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is ExpenseCategorySummary summary)
            {
                int year = (int)YearPicker.SelectedItem;
                string quarter = QuarterPicker.SelectedItem?.ToString() ?? "All";
                string month = MonthPicker.SelectedItem?.ToString() ?? "All";

                var filtered = _allExpenses.Where(x => x.Category == summary.Category && x.Date.Year == year);

                if (quarter != "All")
                {
                    int q = int.Parse(quarter.Substring(1, 1));
                    filtered = filtered.Where(x => ((x.Date.Month - 1) / 3 + 1) == q);
                }
                if (month != "All")
                {
                    int m = DateTime.ParseExact(month, "MMMM", CultureInfo.CurrentCulture).Month;
                    filtered = filtered.Where(x => x.Date.Month == m);
                }

                var details = filtered
                    .OrderBy(x => x.Date) // <-- Sort by date ascending
                    .ToList();

                var expenseRecords = details.Select(d => new KukiFinance.Models.ExpenseRecord
                {
                    Date = d.Date.ToString("yyyy-MM-dd"),
                    Description = d.Description,
                    Category = d.Category,
                    Amount = d.Amount
                }).ToList();

                await Navigation.PushAsync(new ExpenseRecordPage(summary.Category, expenseRecords));
                ExpenseCollectionView.SelectedItem = null;
            }
        }

        private class ExpenseEntry
        {
            public DateTime Date { get; set; }
            public string Category { get; set; }
            public string Description { get; set; }
            public decimal Amount { get; set; }
        }

        private class ExpenseCategorySummary
        {
            public string Category { get; set; }
            public decimal Total { get; set; }
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}