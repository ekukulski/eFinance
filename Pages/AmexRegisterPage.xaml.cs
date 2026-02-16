using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using eFinance.Data;
using eFinance.Data.Models;
using eFinance.Data.Repositories;
using eFinance.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Controls;
using eFinance.Models;
using eFinance.Services;
using Microsoft.Maui.Controls;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Globalization;
using CsvHelper;
using eFinance.Constants;
using eFinance.Helpers;
>>>>>>> Update

namespace eFinance.Pages
{
    public partial class AmexRegisterPage : ContentPage
    {
        // Transactions table uses Accounts.Name = "AMEX"
        private const string AccountNameInDb = "AMEX";

        // OpeningBalances table uses AccountName like your CSV: "Amex"
        private const string OpeningBalanceAccountName = "Amex";

        private readonly SqliteDatabase _db;
        private readonly AccountRepository _accounts;
        private readonly TransactionRepository _transactions;

        // File paths and opening balance
        private readonly string registerFile = FilePathHelper.GeteFinancePath("AMEX.csv");
        private readonly string currentFile = FilePathHelper.GeteFinancePath("AMEXCurrent.csv");
        private readonly string transactionsFile = FilePathHelper.GeteFinancePath("transactionsAMEX.csv");
        private readonly string categoryFile = FilePathHelper.GeteFinancePath("Category.csv");
        private readonly decimal openingBalance = OpeningBalances.Get("Amex");
        private readonly DateTime? openingBalanceDate = OpeningBalances.GetDate("Amex");
	Update

        private readonly RegisterViewModel viewModel = new();

        public AmexRegisterPage(SqliteDatabase db, AccountRepository accounts, TransactionRepository transactions)
        {
            InitializeComponent();

            _db = db ?? throw new ArgumentNullException(nameof(db));
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));

            BindingContext = viewModel;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();
            await LoadFromDbAsync();
        }

        private async Task LoadFromDbAsync()
        {
            try
            {
                // Ensure schema + seed accounts (safe to call repeatedly)
                await _db.InitializeAsync();
                await _accounts.SeedDefaultsIfEmptyAsync();

                var accountId = await _accounts.GetIdByNameAsync(AccountNameInDb);
                if (accountId is null)
                {
                    viewModel.Entries.Clear();
                    viewModel.CurrentBalance = 0m;
                    viewModel.FilterEntries();

                    await DisplayAlert("AMEX", $"Account '{AccountNameInDb}' not found in database.", "OK");
                    return;
                }

                // ✅ Get opening balance from SQLite OpeningBalances table
                var (openingDate, openingBalance) = await GetOpeningBalanceAsync(OpeningBalanceAccountName);

                // Pull all AMEX transactions
                var dbTx = await _transactions.GetByAccountAsync(accountId.Value);

                var entries = new List<RegistryEntry>(capacity: dbTx.Count + 1);

                // Opening row (from DB)
                entries.Add(new RegistryEntry
                {
                    Date = openingDate.ToDateTime(TimeOnly.MinValue),
                    Description = "OPENING BALANCE",
                    Category = "Equity",
                    Amount = openingBalance,
                    Balance = openingBalance
                });

                // For running balance, use chronological order
                foreach (var t in dbTx.OrderBy(x => x.PostedDate).ThenBy(x => x.Id))
                {
                    entries.Add(new RegistryEntry
                    {
                        Date = t.PostedDate.ToDateTime(TimeOnly.MinValue),
                        Description = t.Description,
                        Category = t.Category ?? "",
                        Amount = t.Amount,
                        Balance = 0m
                    });
                }

                // Running balance
                decimal running = openingBalance;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (i == 0)
                    {
                        entries[i].Balance = openingBalance;
                        continue;
                    }

                    running += entries[i].Amount ?? 0m;
                    entries[i].Balance = running;
                }

                // Load into VM
                viewModel.Entries.Clear();
                foreach (var e in entries)
                    viewModel.Entries.Add(e);

                viewModel.CurrentBalance = viewModel.Entries.LastOrDefault()?.Balance ?? 0m;
                viewModel.FilterEntries();

                Debug.WriteLine($"AMEX register loaded: {dbTx.Count} tx. Opening={openingBalance} on {openingDate:yyyy-MM-dd}. Balance={viewModel.CurrentBalance}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AMEX LoadFromDbAsync FAILED: {ex}");
                await DisplayAlert("Error", $"Failed to load AMEX register: {ex.Message}", "OK");
            }
        }

        /// <summary>
        /// Reads opening balance for a logical account name (e.g. 'Amex') from OpeningBalances table.
        /// If not found, returns (today, 0).
        /// </summary>
        private async Task<(DateOnly Date, decimal Balance)> GetOpeningBalanceAsync(string openingAccountName)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT BalanceDate, Balance
FROM OpeningBalances
WHERE LOWER(AccountName) = LOWER($name)
LIMIT 1;
";
            cmd.Parameters.AddWithValue("$name", openingAccountName.Trim());

            using var r = await cmd.ExecuteReaderAsync();
            if (await r.ReadAsync())
            {
                var date = DateOnly.Parse(r.GetString(0));
                var bal = (decimal)r.GetDouble(1);
                return (date, bal);
            }

            // Not found -> fallback
            return (DateOnly.FromDateTime(DateTime.Today), 0m);
        }

        // ------------------------------
        // BUTTON HANDLERS
        // ------------------------------

        private async void AddTransactionsButton_Clicked(object sender, EventArgs e)
        {
            // Refresh (imports happen via ImportWatcher)
            await LoadFromDbAsync();
            await DisplayAlert("AMEX", "Refreshed from database.", "OK");
        }

        private void RegisterCollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No selection logic needed
        }

        private async void ManualTransactionEntryButton_Clicked(object sender, EventArgs e)
        {
            string dateStr = await DisplayPromptAsync("Manual Entry", "Enter date (MM/dd/yyyy):");
            if (string.IsNullOrWhiteSpace(dateStr) || !DateTime.TryParse(dateStr, out var date))
            {
                await DisplayAlert("Invalid", "Please enter a valid date.", "OK");
                return;
            }

            string description = await DisplayPromptAsync("Manual Entry", "Enter description:");
            if (string.IsNullOrWhiteSpace(description))
            {
                await DisplayAlert("Invalid", "Please enter a description.", "OK");
                return;
            }

            string amountStr = await DisplayPromptAsync("Manual Entry", "Enter amount:");
            if (string.IsNullOrWhiteSpace(amountStr) || !decimal.TryParse(amountStr, out var amount))
            {
                await DisplayAlert("Invalid", "Please enter a valid amount.", "OK");
                return;
            }

            await InsertManualAsync(date, description, amount);
            await LoadFromDbAsync();

            await DisplayAlert("Success", "Manual transaction added.", "OK");
        }

        private async Task InsertManualAsync(DateTime date, string description, decimal amount)
        {
            await _accounts.SeedDefaultsIfEmptyAsync();
            var accountId = await _accounts.GetIdByNameAsync(AccountNameInDb);
            if (accountId is null)
                throw new InvalidOperationException($"Account '{AccountNameInDb}' not found.");

            var t = new Transaction
            {
                AccountId = accountId.Value,
                PostedDate = DateOnly.FromDateTime(date),
                Description = description,
                Amount = amount,
                Category = null,
                FitId = null,
                Source = "Manual",
                CreatedUtc = DateTime.UtcNow
            };

            await _transactions.InsertAsync(t);
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            await DisplayAlert("Edit", "Edit is not wired to SQLite yet. Next step: add TransactionId to RegistryEntry and update by Id.", "OK");
        }

        private async void CopyDescriptionButton_Clicked(object sender, EventArgs e)
        {
            if (RegisterCollectionView.SelectedItem is RegistryEntry entry)
            {
                await Clipboard.Default.SetTextAsync(entry.Description ?? "");
                await DisplayAlert("Copied", "Description copied to clipboard.", "OK");
            }
            else
            {
                await DisplayAlert("Copy", "Please select a row to copy.", "OK");
            }
        }

        private async void DeleteTransactionButton_Clicked(object sender, EventArgs e)
        {
            await DisplayAlert("Delete", "Delete is not wired to SQLite yet. Next step: add TransactionId to RegistryEntry and delete by Id.", "OK");
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}
