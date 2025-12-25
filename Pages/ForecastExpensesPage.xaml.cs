using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using KukiFinance.Converters;
using KukiFinance.Helpers;
using KukiFinance.Services;
using Microsoft.Maui.Controls;

namespace KukiFinance.Pages
{
    public partial class ForecastExpensesPage : ContentPage, INotifyPropertyChanged
    {
        private readonly string ForecastFile = FilePathHelper.GetKukiFinancePath("ForecastExpenses.csv");
        private readonly string CategoryFile = FilePathHelper.GetKukiFinancePath("CategoryList.csv");

        public ObservableCollection<ForecastExpenseDisplay> ForecastExpenses { get; set; } = new();

        public event PropertyChangedEventHandler PropertyChanged;

        public ForecastExpensesPage()
        {
            InitializeComponent();
            BindingContext = this;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            LoadForecastExpenses();
        }

        #region Loading / Saving

        private void LoadForecastExpenses()
        {
            ForecastExpenses.Clear();

            if (!File.Exists(ForecastFile))
            {
                // Create empty file with header in new format
                File.WriteAllText(ForecastFile,
                    "Account,Frequency,Year,Month,Day,Category,Amount" + Environment.NewLine);
                return;
            }

            var lines = File.ReadAllLines(ForecastFile);
            if (lines.Length <= 1)
                return;

            foreach (var line in lines.Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var parts = line.Split(',');
                if (parts.Length < 5)
                    continue;

                string account;
                string frequency;
                string yearStr;
                string monthStr;
                string dayStr;
                string category;
                decimal amount;

                if (parts.Length >= 7)
                {
                    // New format: Account,Frequency,Year,Month,Day,Category,Amount
                    account = parts[0].Trim();
                    frequency = parts[1].Trim();
                    yearStr = parts[2].Trim();
                    monthStr = parts[3].Trim();
                    dayStr = parts[4].Trim();
                    category = parts[5].Trim();
                    decimal.TryParse(parts[6].Trim(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out amount);
                }
                else if (parts.Length == 6)
                {
                    // Old format with Account but without explicit Year:
                    // Account,Frequency,Month,Day,Category,Amount
                    account = parts[0].Trim();
                    frequency = parts[1].Trim();
                    yearStr = string.Empty;
                    monthStr = parts[2].Trim();
                    dayStr = parts[3].Trim();
                    category = parts[4].Trim();
                    decimal.TryParse(parts[5].Trim(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out amount);
                }
                else
                {
                    // Very old format without Account / Year:
                    // Frequency,Month,Day,Category,Amount  -> assume BMO Check
                    account = "BMO Check";
                    frequency = parts[0].Trim();
                    yearStr = string.Empty;
                    monthStr = parts[1].Trim();
                    dayStr = parts[2].Trim();
                    category = parts[3].Trim();
                    decimal.TryParse(parts[4].Trim(), NumberStyles.Any,
                        CultureInfo.InvariantCulture, out amount);
                }

                ForecastExpenses.Add(new ForecastExpenseDisplay
                {
                    Account = account,
                    Frequency = frequency,
                    Year = yearStr,
                    Month = monthStr,
                    Day = dayStr,
                    Category = category,
                    Amount = amount,
                    AmountFormatted = AmountFormatConverter.Format(amount),
                    AmountColor = AmountColorConverter.GetColor(amount)
                });
            }
            // NEW: always keep the table sorted by Category
            SortForecastExpenses();
        }

        private void SaveForecastExpenses()
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(ForecastFile);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var lines = ForecastExpenses
                .Select(e =>
                    string.Join(",", new[]
                    {
                        e.Account ?? string.Empty,
                        e.Frequency ?? string.Empty,
                        e.Year ?? string.Empty,
                        e.Month ?? string.Empty,
                        e.Day ?? string.Empty,
                        e.Category ?? string.Empty,
                        e.Amount.ToString(CultureInfo.InvariantCulture)
                    }))
                .ToList();

            lines.Insert(0, "Account,Frequency,Year,Month,Day,Category,Amount");
            File.WriteAllLines(ForecastFile, lines);
        }

        #endregion

        #region Button handlers

        private async void OnAddClicked(object sender, EventArgs e)
        {
            var newExpense = new ForecastExpenseDisplay
            {
                Account = "BMO Check",
                Frequency = "Monthly",
                Year = "All",
                Month = "All",
                Day = "1",
                Category = string.Empty,
                Amount = 0m,
                AmountFormatted = AmountFormatConverter.Format(0m),
                AmountColor = AmountColorConverter.GetColor(0m)
            };

            var page = new EditForecastExpensePage(
                newExpense,
                GetFrequencyOptions(),
                GetYearOptions(),
                GetMonthOptions(),
                GetDayOptions(),
                GetCategoryOptions(),
                () =>
                {
                    ForecastExpenses.Add(newExpense);
                    SortForecastExpenses();
                    SaveForecastExpenses();
                    OnPropertyChanged(nameof(ForecastExpenses));
                });

            await Navigation.PushModalAsync(page);
        }

        private async void OnEditClicked(object sender, EventArgs e)
        {
            if (ForecastExpensesView.SelectedItem is not ForecastExpenseDisplay selected)
            {
                await DisplayAlert("Edit Forecast Expense",
                    "Please select a forecast expense to edit.", "OK");
                return;
            }

            var page = new EditForecastExpensePage(
                selected,
                GetFrequencyOptions(),
                GetYearOptions(),
                GetMonthOptions(),
                GetDayOptions(),
                GetCategoryOptions(),
                () =>
                {
                    SortForecastExpenses();
                    SaveForecastExpenses();
                    OnPropertyChanged(nameof(ForecastExpenses));
                });

            await Navigation.PushModalAsync(page);
        }

        private async void OnDeleteClicked(object sender, EventArgs e)
        {
            if (ForecastExpensesView.SelectedItem is not ForecastExpenseDisplay selected)
            {
                await DisplayAlert("Delete Forecast Expense",
                    "Please select a forecast expense to delete.", "OK");
                return;
            }

            bool confirm = await DisplayAlert("Delete Forecast Expense",
                $"Delete forecast expense \"{selected.Category}\"?",
                "Delete", "Cancel");
            if (!confirm)
                return;

            ForecastExpenses.Remove(selected);
            SortForecastExpenses();
            SaveForecastExpenses();
            OnPropertyChanged(nameof(ForecastExpenses));
        }

        private async void OnReturnClicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }

        #endregion

        #region Helpers

        private string[] GetFrequencyOptions() =>
            new[]
            {
                "Once",
                "Monthly",
                "2 Months",
                "3 Months",
                "4 Months",
                "6 Months",
                "Annual"
            };

        private string[] GetYearOptions()
        {
            int startYear = DateTime.Today.Year;
            // current year + next 10 years, plus "All" at the top
            var years = Enumerable.Range(startYear, 11)
                                  .Select(y => y.ToString(CultureInfo.InvariantCulture))
                                  .ToList();
            years.Insert(0, "All");
            return years.ToArray();
        }

        private string[] GetMonthOptions() =>
            new[]
            {
                "All",
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };

        private string[] GetDayOptions() =>
            Enumerable.Range(1, 31)
                      .Select(d => d.ToString(CultureInfo.InvariantCulture))
                      .ToArray();

        private string[] GetCategoryOptions()
        {
            if (!File.Exists(CategoryFile))
                return Array.Empty<string>();

            return File.ReadAllLines(CategoryFile)
                       .Skip(1)
                       .Where(l => !string.IsNullOrWhiteSpace(l))
                       .Select(l => l.Trim())
                       .OrderBy(l => l)
                       .ToArray();
        }

        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        #endregion

        private void SortForecastExpenses()
        {
            var sorted = ForecastExpenses
                .OrderBy(e => e.Category, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Account, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Frequency, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Month, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(e => e.Day, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            ForecastExpenses.Clear();
            foreach (var item in sorted)
                ForecastExpenses.Add(item);
        }


        #region Nested types

        public class ForecastExpense
        {
            public string Account { get; set; }
            public string Frequency { get; set; }
            public string Year { get; set; }
            public string Month { get; set; }
            public string Day { get; set; }
            public string Category { get; set; }
            public decimal Amount { get; set; }
        }

        public class ForecastExpenseDisplay : ForecastExpense
        {
            public string AmountFormatted { get; set; }
            public Color AmountColor { get; set; }
        }

        #endregion
    }
}
