using System.Collections.ObjectModel;

namespace eFinance.Pages
{
    public partial class OpeningBalancesPage : ContentPage
    {
        private static readonly string CsvPath = FilePathHelper.GeteFinancePath("OpeningBalances.csv");
        public ObservableCollection<OpeningBalanceItem> Balances { get; set; } = new();

        public OpeningBalancesPage()
        {
            InitializeComponent();
            
            LoadBalances();
            BalancesCollectionView.ItemsSource = Balances;
        }

        private void LoadBalances()
        {
            Balances.Clear();
            if (File.Exists(CsvPath))
            {
                foreach (var line in File.ReadAllLines(CsvPath).Skip(1))
                {
                    var parts = line.Split(',');
                    if (parts.Length >= 3 &&
                        DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var date) &&
                        !string.IsNullOrWhiteSpace(parts[1]) &&
                        decimal.TryParse(parts[2], out var bal))
                    {
                        Balances.Add(new OpeningBalanceItem { Date = date, Account = parts[1].Trim(), Balance = bal });
                    }
                }
            }
            else
            {
                // If file doesn't exist, create from OpeningBalances.cs (pseudo-code, adapt as needed)
                foreach (var prop in typeof(eFinance.Constants.OpeningBalances).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (prop.FieldType == typeof(decimal))
                    {
                        var value = prop.GetValue(null);
                        if (value != null)
                        {
                            Balances.Add(new OpeningBalanceItem { Account = prop.Name, Balance = (decimal)value });
                        }
                    }
                }
                SaveBalances();
            }
        }

        private void SaveBalances()
        {
            var lines = new[] { "Date,Account,Balance" }
                .Concat(Balances.Select(b => $"{b.Date:yyyy-MM-dd},{b.Account},{b.Balance}"));
            File.WriteAllLines(CsvPath, lines);
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            if (BalancesCollectionView.SelectedItem is OpeningBalanceItem selected)
            {
                string dateResult = await DisplayPromptAsync("Edit Date", $"Enter new date for {selected.Account} (yyyy-MM-dd):", initialValue: selected.Date.ToString("yyyy-MM-dd"));
                if (!DateTime.TryParseExact(dateResult, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var newDate))
                {
                    await DisplayAlert("Invalid", "Please enter a valid date in yyyy-MM-dd format.", "OK");
                    return;
                }

                string balanceResult = await DisplayPromptAsync("Edit Balance", $"Enter new balance for {selected.Account}:", initialValue: selected.Balance.ToString());
                if (!decimal.TryParse(balanceResult, out var newBalance))
                {
                    await DisplayAlert("Invalid", "Please enter a valid balance.", "OK");
                    return;
                }

                selected.Date = newDate;
                selected.Balance = newBalance;
                SaveBalances();
                LoadBalances();
            }
            else
            {
                await DisplayAlert("Select Row", "Please select an account to edit.", "OK");
            }
        }
        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Navigation.PopAsync();
        }
    }

    public class OpeningBalanceItem
    {
        public DateTime Date { get; set; }
        public required string Account { get; set; }
        public decimal Balance { get; set; }
    }
}