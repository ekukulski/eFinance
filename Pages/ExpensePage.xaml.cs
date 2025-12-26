using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Maui.Controls;
using CsvHelper;
using CsvHelper.Configuration;
using KukiFinance.Helpers;
using KukiFinance.Models;

namespace KukiFinance.Pages
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

        private List<RegistryEntry> _allExpenses = new();

        public ExpensePage()
        {
            InitializeComponent();
            LoadAllExpenses();
            PopulatePickers();
        }

        private IEnumerable<string> GetCsvFilePaths()
        {
            return CsvFiles.Select(FilePathHelper.GetKukiFinancePath);
        }

        private string GetExcludedCategoriesPath()
        {
            return FilePathHelper.GetKukiFinancePath("ExcludedCategories.csv");
        }

        private HashSet<string> GetExcludedCategoriesFromCsv()
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

                var excludedCategories = GetExcludedCategoriesFromCsv();

                foreach (var filePath in GetCsvFilePaths())
                {
                    if (!File.Exists(filePath))
                        continue;

                    filesLoaded++;

                    using var reader = new StreamReader(filePath);
                    using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                    csv.Context.RegisterClassMap<CurrentRegisterEntryMap>();

                    var records = csv.GetRecords<RegistryEntry>();

                    foreach (var record in records)
                    {
                        if (excludedCategories.Contains(record.Category ?? ""))
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
        private void OnShowExpensesClicked(object sender, EventArgs e)
        {
            // This button is meant to (re)apply the picker filters to the already-loaded expense list
            // and refresh the UI.
            RefreshExpenseSummary();
        }
        private void OnCategorySelected(object sender, EventArgs e)
        {
            RefreshExpenseSummary();
        }

        private void PopulatePickers()
        {
            // ← KEEP your original picker logic here (unchanged)
        }

        private void RefreshExpenseSummary()
        {
            // ← KEEP your original summary logic here (unchanged)
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
