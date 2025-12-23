using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Maui.Controls;
using KukiFinance.Converters;

namespace KukiFinance.Pages
{
    public partial class EditForecastExpensePage : ContentPage
    {
        private ForecastExpensesPage.ForecastExpenseDisplay _expense;
        private int _currentFieldIndex = 0;
        private readonly List<View> _fields;
        private readonly Action _onSaveCallback;

        public EditForecastExpensePage(
            ForecastExpensesPage.ForecastExpenseDisplay expense,
            string[] frequencies,
            string[] years,
            string[] months,
            string[] days,
            string[] categories,
            Action onSaveCallback)
        {
            InitializeComponent();
            _expense = expense;
            _onSaveCallback = onSaveCallback;

            // Accounts available in the app
            var accounts = new[] { "BMO Check", "AMEX", "Visa", "MasterCard" };
            AccountPicker.ItemsSource = accounts;
            AccountPicker.SelectedItem =
                string.IsNullOrWhiteSpace(expense.Account) ? accounts[0] : expense.Account;

            FrequencyPicker.ItemsSource = frequencies;
            FrequencyPicker.SelectedItem =
                string.IsNullOrWhiteSpace(expense.Frequency) ? frequencies[0] : expense.Frequency;

            YearPicker.ItemsSource = years;
            YearPicker.SelectedItem =
                string.IsNullOrWhiteSpace(expense.Year) ? years[0] : expense.Year;

            MonthPicker.ItemsSource = months;
            MonthPicker.SelectedItem =
                string.IsNullOrWhiteSpace(expense.Month) ? months[0] : expense.Month;

            DayPicker.ItemsSource = days;
            DayPicker.SelectedItem =
                string.IsNullOrWhiteSpace(expense.Day) ? days[0] : expense.Day;

            CategoryPicker.ItemsSource = categories;
            CategoryPicker.SelectedItem = expense.Category;

            AmountEntry.Text = expense.Amount.ToString(CultureInfo.InvariantCulture);

            // Order of fields cycled by the "Next" button
            _fields = new List<View>
            {
                AccountPicker,
                FrequencyPicker,
                YearPicker,
                MonthPicker,
                DayPicker,
                CategoryPicker,
                AmountEntry
            };
            FocusField(_currentFieldIndex);
        }

        private void FocusField(int index)
        {
            foreach (var field in _fields)
                field.IsEnabled = false;

            _fields[index].IsEnabled = true;
            _fields[index].Focus();
        }

        private void OnNextClicked(object sender, EventArgs e)
        {
            _currentFieldIndex = (_currentFieldIndex + 1) % _fields.Count;
            FocusField(_currentFieldIndex);
        }

        private void AccountPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (AccountPicker.SelectedItem is string acct)
                _expense.Account = acct;
        }

        private void FrequencyPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (FrequencyPicker.SelectedItem is string freq)
                _expense.Frequency = freq;
        }

        private void YearPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (YearPicker.SelectedItem is string year)
                _expense.Year = year;
        }

        private void MonthPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (MonthPicker.SelectedItem is string month)
                _expense.Month = month;
        }

        private void DayPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (DayPicker.SelectedItem is string day)
                _expense.Day = day;
        }

        private void CategoryPicker_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (CategoryPicker.SelectedItem is string cat)
                _expense.Category = cat;
        }

        private async void OnSaveClicked(object sender, EventArgs e)
        {
            _expense.Account = AccountPicker.SelectedItem?.ToString() ?? _expense.Account;
            _expense.Frequency = FrequencyPicker.SelectedItem?.ToString() ?? _expense.Frequency;
            _expense.Year = YearPicker.SelectedItem?.ToString() ?? _expense.Year;
            _expense.Month = MonthPicker.SelectedItem?.ToString() ?? _expense.Month;
            _expense.Day = DayPicker.SelectedItem?.ToString() ?? _expense.Day;
            _expense.Category = CategoryPicker.SelectedItem?.ToString() ?? _expense.Category;

            if (decimal.TryParse(AmountEntry.Text, NumberStyles.Any,
                    CultureInfo.InvariantCulture, out var amt))
            {
                _expense.Amount = amt;
            }

            _expense.AmountFormatted = AmountFormatConverter.Format(_expense.Amount);
            _expense.AmountColor = AmountColorConverter.GetColor(_expense.Amount);

            _onSaveCallback?.Invoke(); // Save and refresh table

            await Navigation.PopModalAsync();
        }

        private async void OnCancelClicked(object sender, EventArgs e)
        {
            await Navigation.PopModalAsync();
        }
    }
}
