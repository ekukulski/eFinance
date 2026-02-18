using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using eFinance.Constants;
using eFinance.Data;
using eFinance.Data.Models;
using eFinance.Data.Repositories;
using eFinance.Helpers;
using eFinance.Models;
using eFinance.Services;
using Microsoft.Maui.Controls;

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

                // For running balance, we must compute chronologically (oldest -> newest)
                foreach (var t in dbTx.OrderBy(x => x.PostedDate).ThenBy(x => x.Id))
                {
                    entries.Add(new RegistryEntry
                    {
                        TransactionId = t.Id,
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

                // Display newest -> oldest (opening balance naturally ends up at the bottom)
                var displayEntries = entries
                    .OrderByDescending(e => e.Date)
                    .ThenByDescending(e => e.TransactionId ?? long.MinValue)
                    .ToList();

                // Load into VM
                viewModel.Entries.Clear();
                foreach (var e in displayEntries)
                    viewModel.Entries.Add(e);

                // Current balance should be the running balance after applying all transactions
                viewModel.CurrentBalance = running;
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
        }

        private void RegisterCollectionView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No selection logic needed
        }

        private async void ManualTransactionEntryButton_Clicked(object sender, EventArgs e)
        {
            await _accounts.SeedDefaultsIfEmptyAsync();
            var accountId = await _accounts.GetIdByNameAsync(AccountNameInDb);
            if (accountId is null)
            {
                await DisplayAlert("AMEX", $"Account '{AccountNameInDb}' not found in database.", "OK");
                return;
            }

            var page = new TransactionEditPage(_db, _transactions, accountId.Value, transactionId: null);
            await Navigation.PushModalAsync(new NavigationPage(page));
            var saved = await page.Result;

            if (saved)
                await LoadFromDbAsync();
        }

        private async void EditButton_Clicked(object sender, EventArgs e)
        {
            if (RegisterCollectionView.SelectedItem is not RegistryEntry entry)
            {
                await DisplayAlert("Edit", "Please select a transaction row to edit.", "OK");
                return;
            }

            if (entry.TransactionId is null)
            {
                await DisplayAlert("Edit", "The opening balance row cannot be edited here.", "OK");
                return;
            }

            await _accounts.SeedDefaultsIfEmptyAsync();
            var accountId = await _accounts.GetIdByNameAsync(AccountNameInDb);
            if (accountId is null)
            {
                await DisplayAlert("AMEX", $"Account '{AccountNameInDb}' not found in database.", "OK");
                return;
            }

            var page = new TransactionEditPage(_db, _transactions, accountId.Value, entry.TransactionId.Value);
            await Navigation.PushModalAsync(new NavigationPage(page));
            var saved = await page.Result;

            if (saved)
                await LoadFromDbAsync();
        }

        private async void DeleteTransactionButton_Clicked(object sender, EventArgs e)
        {
            if (RegisterCollectionView.SelectedItem is not RegistryEntry entry)
            {
                await DisplayAlert("Delete", "Please select a transaction row to delete.", "OK");
                return;
            }

            if (entry.TransactionId is null)
            {
                await DisplayAlert("Delete", "The opening balance row cannot be deleted here.", "OK");
                return;
            }

            var ok = await DisplayAlert(
                "Confirm Delete",
                $"Delete this transaction?\n\n{entry.Date:yyyy-MM-dd}\n{entry.Description}\n{entry.Amount}",
                "Delete",
                "Cancel");

            if (!ok)
                return;

            // Preserve approximate scroll position (keep the user near where they were).
            var priorIndex = viewModel.FilteredEntries.IndexOf(entry);

            var deleted = await _transactions.DeleteByIdAsync(entry.TransactionId.Value);
            if (!deleted)
            {
                await DisplayAlert("Delete", "Nothing was deleted (it may have already been removed).", "OK");
                await LoadFromDbAsync();
                return;
            }

            await LoadFromDbAsync();

            // Scroll back near the previous index (bounded).
            try
            {
                var targetIndex = priorIndex;
                if (targetIndex < 0) targetIndex = 0;
                if (targetIndex >= viewModel.FilteredEntries.Count)
                    targetIndex = Math.Max(0, viewModel.FilteredEntries.Count - 1);

                if (viewModel.FilteredEntries.Count > 0)
                {
                    Dispatcher.Dispatch(() =>
                    {
                        RegisterCollectionView.ScrollTo(targetIndex, position: ScrollToPosition.Center, animate: false);
                    });
                }
            }
            catch
            {
                // non-fatal
            }
        }

        private async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            await Shell.Current.GoToAsync("//MainPage");
        }
    }
}