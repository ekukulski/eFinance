using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CsvHelper;
using KukiFinance.Models;
using System.IO;
using System.Linq;

namespace KukiFinance.ViewModels
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
            FilePathHelper.GetKukiFinancePath("MidlandCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("CharlesSchwabContributoryCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("CharlesSchwabJointTenantCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("CharlesSchwabRothIraEdCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("CharlesSchwabRothIraPattiCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("NetXCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("HealthProCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("Select401KCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("GoldCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("HouseCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("ChevroletImpalaCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("NissanSentraCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("CashCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("BMOCheckCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("BMOMoneyMarketCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("BMOCDCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("AMEXCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("VisaCurrent.csv"),
            FilePathHelper.GetKukiFinancePath("MasterCardCurrent.csv")
        };

        public AverageExpenseViewModel()
        {
            LoadData();
        }

        private static HashSet<string> GetExcludedCategoriesFromCsv()
        {
            var path = FilePathHelper.GetKukiFinancePath("ExcludedCategories.csv");
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
                    string category = record.CATEGORY;
                    string dateStr = record.DATE;
                    string amountStr = record.AMOUNT;
                    string description = record.DESCRIPTION;

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
                        Date = dateStr,
                        Description = description,
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

            /* foreach (var kvp in categoryTotals.OrderBy(kvp => kvp.Key))
            {
                var total = kvp.Value.Sum();
                var frequency = kvp.Value.Count; // Number of times this category was expensed
                var avgPerOccurrence = frequency > 0 ? total / frequency : 0m;
                CategoryAverages.Add(new CategoryAverageExpense
                {
                    Category = kvp.Key,
                    AverageExpense = avgPerOccurrence, // Average per expense occurrence
                    Frequency = frequency
                });
            } */
        }

        public void UpdateSelectedCategoryTotal(string category)
        {
            var matching = AllExpenseRecords
                .Where(r => string.Equals(r.Category?.Trim(), category?.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();

            var total = matching.Sum(r => r.Amount);
            SelectedCategoryTotal = total;
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}