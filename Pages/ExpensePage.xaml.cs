using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CsvHelper;
using Microsoft.Maui.Controls;
using eFinance.Helpers;
using eFinance.Models;

namespace eFinance.Pages
{
    public partial class ExpensePage : ContentPage
    {
        private static readonly string[] CsvFiles =
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

        private readonly List<RegistryEntry> _allExpenses = new();

        public ExpensePage()
        {
            InitializeComponent();
            LoadAllExpenses();
            PopulatePickers();
        }

        private static IEnumerable<string> GetCsvFilePaths()
            => CsvFiles.Select(FilePathHelper.GeteFinancePath);

        private static string GetExcludedCategoriesPath()
            => FilePathHelper.GeteFinancePath("ExcludedCategories.csv");

        private static HashSet<string> GetExcludedCategoriesFromCsv()
        {
            var path = GetExcludedCategoriesPath();
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(path))
                return set;

            foreach (var line in File.ReadAllLines(path).Skip(1)) // skip header
            {
                var category = line.Trim();
                if (!string.IsNullOrWhiteSpace(category))
                    set.Add(category);
            }

            return set;
        }

        private async void LoadAllExpenses()
        {
            try
            {
                _allExpenses.Clear();
                int filesLoaded = 0;

                var excluded = GetExcludedCategoriesFromCsv();

                foreach (var filePath in GetCsvFilePaths())
                {
                    if (!File.Exists(filePath))
                        continue;

                    filesLoaded++;

                    using var reader = new StreamReader(filePath);
                    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

                    // Current*.csv format: DATE, DESCRIPTION, CATEGORY, AMOUNT, BALANCE
                    csv.Context.RegisterClassMap<CurrentRegisterEntryMap>();

                    foreach (var record in csv.GetRecords<RegistryEntry>())
                    {
                        if (excluded.Contains(record.Category ?? ""))
                            continue;

                        _allExpenses.Add(record);
                    }
                }

                if (filesLoaded == 0)
                {
                    await DisplayAlert(
                        "Warning",
                        "No expense files found. Please check file locations and permissions.",
                        "OK");
                    return;
                }

                RefreshExpenseSummary();
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", $"Failed to load expenses: {ex.Message}", "OK");
            }
        }

        private void PopulatePickers()
        {
            // Build years from data
            var years = _allExpenses
                .Where(e => e.Date.HasValue)
                .Select(e => e.Date!.Value.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToList();

            YearPicker.Items.Clear();
            YearPicker.Items.Add("All");
            foreach (var y in years)
                YearPicker.Items.Add(y.ToString(CultureInfo.InvariantCulture));
            YearPicker.SelectedIndex = 0;

            // Quarters
            QuarterPicker.Items.Clear();
            QuarterPicker.Items.Add("All");
            QuarterPicker.Items.Add("Q1");
            QuarterPicker.Items.Add("Q2");
            QuarterPicker.Items.Add("Q3");
            QuarterPicker.Items.Add("Q4");
            QuarterPicker.SelectedIndex = 0;

            // Months
            MonthPicker.Items.Clear();
            MonthPicker.Items.Add("All");
            for (int m = 1; m <= 12; m++)
                MonthPicker.Items.Add(CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m));
            MonthPicker.SelectedIndex = 0;
        }

        private void RefreshExpenseSummary()
        {
            IEnumerable<RegistryEntry> query = _allExpenses;

            // Year filter
            var yearText = YearPicker.SelectedItem?.ToString();
            if (!string.IsNullOrWhiteSpace(yearText) && !yearText.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(yearText, out var year))
                    query = query.Where(e => e.Date.HasValue && e.Date.Value.Year == year);
            }

            // Quarter filter
            var quarterText = QuarterPicker.SelectedItem?.ToString();
            if (!string.IsNullOrWhiteSpace(quarterText) && quarterText.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
            {
                int q = quarterText switch
                {
                    "Q1" => 1,
                    "Q2" => 2,
                    "Q3" => 3,
                    "Q4" => 4,
                    _ => 0
                };

                if (q != 0)
                {
                    int startMonth = (q - 1) * 3 + 1; // 1,4,7,10
                    int endMonth = startMonth + 2;

                    query = query.Where(e =>
                        e.Date.HasValue &&
                        e.Date.Value.Month >= startMonth &&
                        e.Date.Value.Month <= endMonth);
                }
            }

            // Month filter (applies on top of year/quarter)
            var monthText = MonthPicker.SelectedItem?.ToString();
            if (!string.IsNullOrWhiteSpace(monthText) && !monthText.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                var monthNumber = Enumerable.Range(1, 12)
                    .FirstOrDefault(m => CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m)
                        .Equals(monthText, StringComparison.OrdinalIgnoreCase));

                if (monthNumber != 0)
                {
                    query = query.Where(e => e.Date.HasValue && e.Date.Value.Month == monthNumber);
                }
            }

            // Summaries by category
            var summaries = query
                .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "(Uncategorized)" : e.Category.Trim())
                .Select(g => new ExpenseCategorySummary
                {
                    Category = g.Key,
                    Total = g.Sum(x => x.Amount ?? 0m)
                })
                .OrderBy(x => x.Category, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Totals
            var expenseTotal = summaries.Sum(s => s.Total);

            // Daily spend: based on distinct transaction dates (simple + stable)
            var days = query
                .Where(e => e.Date.HasValue)
                .Select(e => e.Date!.Value.Date)
                .Distinct()
                .Count();

            var dailySpend = days > 0 ? expenseTotal / days : 0m;

            ExpenseTotalLabel.Text = expenseTotal.ToString("C2");
            DailySpendLabel.Text = dailySpend.ToString("C2");

            ExpenseCollectionView.ItemsSource = summaries;
            ExpenseCollectionView.SelectedItem = null;
        }

        // XAML: Button Clicked="OnShowExpensesClicked"
        private void OnShowExpensesClicked(object sender, EventArgs e)
        {
            RefreshExpenseSummary();
        }

        // XAML: SelectionChanged="OnCategorySelected"
        private void OnCategorySelected(object sender, SelectionChangedEventArgs e)
        {
            // You can add “drill-down” later if you want.
            // For now, just clear selection so clicking again works.
            ExpenseCollectionView.SelectedItem = null;
        }

        private class ExpenseCategorySummary
        {
            public string Category { get; set; } = "";
            public decimal Total { get; set; }
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
