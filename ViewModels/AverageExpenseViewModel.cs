using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CsvHelper;
using System.IO;
using System.Linq;
using eFinance.Models;

namespace eFinance.ViewModels
{
    public class AverageExpenseViewModel : INotifyPropertyChanged
    {
        public ObservableCollection<CategoryAverageExpense> CategoryAverages { get; set; } = new();
        public List<ExpenseRecord> AllExpenseRecords { get; set; } = new();

        private decimal _selectedCategoryTotal;
        public decimal SelectedCategoryTotal
        {
            get => _selectedCategoryTotal;
            set
            {
                if (_selectedCategoryTotal != value)
                {
                    _selectedCategoryTotal = value;
                    OnPropertyChanged(nameof(SelectedCategoryTotal));
                }
            }
        }

        private static readonly string[] CsvFiles = new[]
        {
            FilePathHelper.GeteFinancePath("MidlandCurrent.csv"),
            FilePathHelper.GeteFinancePath("CharlesSchwabContributoryCurrent.csv"),
            FilePathHelper.GeteFinancePath("CharlesSchwabJointTenantCurrent.csv"),
            FilePathHelper.GeteFinancePath("CharlesSchwabRothIraEdCurrent.csv"),
            FilePathHelper.GeteFinancePath("CharlesSchwabRothIraPattiCurrent.csv"),
            FilePathHelper.GeteFinancePath("NetXCurrent.csv"),
            FilePathHelper.GeteFinancePath("HealthProCurrent.csv"),
            FilePathHelper.GeteFinancePath("Select401KCurrent.csv"),
            FilePathHelper.GeteFinancePath("GoldCurrent.csv"),
            FilePathHelper.GeteFinancePath("HouseCurrent.csv"),
            FilePathHelper.GeteFinancePath("ChevroletImpalaCurrent.csv"),
            FilePathHelper.GeteFinancePath("NissanSentraCurrent.csv"),
            FilePathHelper.GeteFinancePath("CashCurrent.csv"),
            FilePathHelper.GeteFinancePath("BMOCheckCurrent.csv"),
            FilePathHelper.GeteFinancePath("BMOMoneyMarketCurrent.csv"),
            FilePathHelper.GeteFinancePath("BMOCDCurrent.csv"),
            FilePathHelper.GeteFinancePath("AMEXCurrent.csv"),
            FilePathHelper.GeteFinancePath("VisaCurrent.csv"),
            FilePathHelper.GeteFinancePath("MasterCardCurrent.csv")
        };

        public AverageExpenseViewModel()
        {
            LoadData();
        }

        private static HashSet<string> GetExcludedCategoriesFromCsv()
        {
            var path = FilePathHelper.GeteFinancePath("ExcludedCategories.csv");
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

        private void LoadData()
        {
            var oneYearAgo = DateTime.Today.AddYears(-1);
            var categoryTotals = new Dictionary<string, List<decimal>>();
            AllExpenseRecords.Clear();

            var excludedCategories = GetExcludedCategoriesFromCsv();

            foreach (var file in CsvFiles)
            {
                if (!File.Exists(file)) continue;

                using var reader = new StreamReader(file);
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

                var records = csv.GetRecords<dynamic>();
                foreach (var record in records)
                {
                    string? category = record.CATEGORY;
                    string? dateStr = record.DATE;
                    string? amountStr = record.AMOUNT;
                    string? description = record.DESCRIPTION;

                    if (string.IsNullOrWhiteSpace(category) || excludedCategories.Contains(category))
                        continue;

                    if (!DateTime.TryParse(dateStr, out var date) || date < oneYearAgo)
                        continue;

                    if (!decimal.TryParse(amountStr, out var amount) || amount >= 0)
                        continue; // Only expenses (negative amounts)

                    if (!categoryTotals.ContainsKey(category))
                        categoryTotals[category] = new List<decimal>();

                    categoryTotals[category].Add(Math.Abs(amount));

                    AllExpenseRecords.Add(new ExpenseRecord
                    {
                        Date = dateStr ?? string.Empty,
                        Description = description ?? string.Empty,
                        Category = category,
                        Amount = Math.Abs(amount)
                    });
                }
            }

            foreach (var kvp in categoryTotals.OrderBy(kvp => kvp.Key))
            {
                var total = kvp.Value.Sum();
                var avgPerMonth = total / 12m; // average per month over 12 months
                var frequency = kvp.Value.Count;
                CategoryAverages.Add(new CategoryAverageExpense
                {
                    Category = kvp.Key,
                    AverageExpense = avgPerMonth,
                    Frequency = frequency
                });
            }
        }

        public void UpdateSelectedCategoryTotal(string? category)
        {
            var matching = AllExpenseRecords
                .Where(r => string.Equals(r.Category?.Trim(), category?.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            var total = matching.Sum(r => r.Amount);
            SelectedCategoryTotal = total;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string? propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}