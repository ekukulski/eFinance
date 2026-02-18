using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using eFinance.Data;
using eFinance.Data.Models;
using eFinance.Data.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Maui.Controls;

namespace eFinance.Pages
{
    public partial class TransactionEditPage : ContentPage
    {
        private readonly SqliteDatabase _db;
        private readonly TransactionRepository _transactions;
        private readonly long _accountId;
        private readonly long? _transactionId;

        private readonly TaskCompletionSource<bool> _tcs = new();

        private List<CategoryOption> _categories = new();

        public Task<bool> Result => _tcs.Task;

        public TransactionEditPage(SqliteDatabase db, TransactionRepository transactions, long accountId, long? transactionId)
        {
            InitializeComponent();

            _db = db ?? throw new ArgumentNullException(nameof(db));
            _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
            _accountId = accountId;
            _transactionId = transactionId;

            HeaderLabel.Text = transactionId is null ? "Add Transaction" : "Edit Transaction";
            Title = HeaderLabel.Text;

            // Defaults
            PostedDatePicker.Date = DateTime.Today;
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            try
            {
                await LoadCategoriesAsync();
                await LoadTransactionIfEditAsync();
            }
            catch (Exception ex)
            {
                InfoLabel.Text = "Error: " + ex.Message;
            }
        }

        private sealed class CategoryOption
        {
            public long? Id { get; init; }
            public string Name { get; init; } = "";
            public override string ToString() => Name;
        }

        private async Task LoadCategoriesAsync()
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
SELECT Id, Name
FROM Categories
WHERE IsActive = 1
ORDER BY Name COLLATE NOCASE;
";

            var list = new List<CategoryOption>
            {
                new CategoryOption { Id = null, Name = "(Uncategorized)" }
            };

            using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                list.Add(new CategoryOption
                {
                    Id = r.GetInt64(0),
                    Name = r.GetString(1)
                });
            }

            _categories = list;
            CategoryPicker.ItemsSource = _categories;
            CategoryPicker.SelectedIndex = 0;
        }

        private async Task LoadTransactionIfEditAsync()
        {
            if (_transactionId is null)
            {
                // Add mode
                return;
            }

            var t = await _transactions.GetByIdAsync(_transactionId.Value);
            if (t is null)
            {
                await DisplayAlert("Not found", "This transaction no longer exists.", "OK");
                await CloseAsync(false);
                return;
            }

            if (t.AccountId != _accountId)
            {
                await DisplayAlert("Mismatch", "Selected transaction does not belong to this account.", "OK");
                await CloseAsync(false);
                return;
            }

            PostedDatePicker.Date = t.PostedDate.ToDateTime(TimeOnly.MinValue);
            DescriptionEntry.Text = t.Description;
            AmountEntry.Text = t.Amount.ToString("0.00", CultureInfo.InvariantCulture);

            // Prefer CategoryId match, else try name match, else uncategorized
            var idx = 0;
            if (t.CategoryId is not null)
            {
                idx = _categories.FindIndex(c => c.Id == t.CategoryId);
            }
            else if (!string.IsNullOrWhiteSpace(t.Category))
            {
                idx = _categories.FindIndex(c => string.Equals(c.Name, t.Category, StringComparison.OrdinalIgnoreCase));
            }
            CategoryPicker.SelectedIndex = idx >= 0 ? idx : 0;

            InfoLabel.Text = string.IsNullOrWhiteSpace(t.FitId)
                ? "FitId: (none)"
                : $"FitId: {t.FitId} (will NOT change when you edit)";
        }

        private async void SaveButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                var posted = DateOnly.FromDateTime(PostedDatePicker.Date);

                var desc = (DescriptionEntry.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(desc))
                {
                    await DisplayAlert("Invalid", "Description is required.", "OK");
                    return;
                }

                if (!decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                {
                    // try current culture as fallback
                    if (!decimal.TryParse(AmountEntry.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
                    {
                        await DisplayAlert("Invalid", "Amount must be a number.", "OK");
                        return;
                    }
                }

                var selected = CategoryPicker.SelectedItem as CategoryOption;
                var categoryId = selected?.Id;

                if (_transactionId is null)
                {
                    // Add
                    var t = new Transaction
                    {
                        AccountId = _accountId,
                        PostedDate = posted,
                        Description = desc,
                        Amount = amount,
                        Category = null,
                        CategoryId = categoryId,
                        MatchedRuleId = null,
                        MatchedRulePattern = null,
                        CategorizedUtc = categoryId is null ? null : DateTime.UtcNow,
                        FitId = null,
                        Source = "Manual",
                        CreatedUtc = DateTime.UtcNow
                    };

                    await _transactions.InsertAsync(t);
                    await DisplayAlert("Saved", "Transaction saved.", "OK");
                    await CloseAsync(true);
                    return;
                }

                // Edit
                var existing = await _transactions.GetByIdAsync(_transactionId.Value);
                if (existing is null)
                {
                    await DisplayAlert("Not found", "This transaction no longer exists.", "OK");
                    await CloseAsync(false);
                    return;
                }

                existing.PostedDate = posted;
                existing.Description = desc;
                existing.Amount = amount;

                // Manual override: set CategoryId; clear legacy Category text
                existing.CategoryId = categoryId;
                existing.Category = null;

                // Since user explicitly set category, clear rule audit fields.
                existing.MatchedRuleId = null;
                existing.MatchedRulePattern = null;
                existing.CategorizedUtc = categoryId is null ? null : DateTime.UtcNow;

                await _transactions.UpdateAsync(existing);
                await DisplayAlert("Saved", "Transaction saved.", "OK");
                await CloseAsync(true);
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async void CancelButton_Clicked(object sender, EventArgs e)
        {
            // Give a little feedback so it doesn't feel like a dead click.
            await DisplayAlert("Canceled", "No changes were saved.", "OK");
            await CloseAsync(false);
        }

        private async Task CloseAsync(bool saved)
        {
            if (!_tcs.Task.IsCompleted)
                _tcs.TrySetResult(saved);

            // This page is typically shown inside a *modal* NavigationPage.
            // In that case, Navigation.ModalStack contains the NavigationPage (not this ContentPage),
            // so checking Contains(this) will be false. The reliable approach is: try PopModal first.
            try
            {
                if (Navigation?.ModalStack?.Count > 0)
                {
                    await Navigation.PopModalAsync();
                    return;
                }
            }
            catch
            {
                // ignore and fall through
            }

            try
            {
                await Navigation.PopAsync();
            }
            catch
            {
                // If we can't navigate back, at least complete the TaskCompletionSource.
            }
        }
    }
}
