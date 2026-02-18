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

        
        private async void AddCategoryButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                var name = await DisplayPromptAsync("Add Category", "Enter a new category name:", "Add", "Cancel",
                                                    placeholder: "e.g., Dining", maxLength: 80, keyboard: Keyboard.Text);
                if (name is null)
                    return; // canceled

                name = name.Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    await DisplayAlert("Invalid", "Category name is required.", "OK");
                    return;
                }

                var id = await EnsureCategoryAsync(name);

                // Reload and select the new/existing category
                await LoadCategoriesAsync();
                var idx = _categories.FindIndex(c => c.Id == id);
                CategoryPicker.SelectedIndex = idx >= 0 ? idx : 0;
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async Task<long> EnsureCategoryAsync(string name)
        {
            using var conn = _db.OpenConnection();

            // 1) Try find existing (case-insensitive)
            using (var find = conn.CreateCommand())
            {
                find.CommandText = @"
SELECT Id
FROM Categories
WHERE IsActive = 1 AND Name = $name COLLATE NOCASE
LIMIT 1;";
                find.Parameters.AddWithValue("$name", name);

                var existing = await find.ExecuteScalarAsync();
                if (existing is long id1)
                    return id1;
                if (existing is int idInt)
                    return idInt;
            }

            // 2) Insert new
            using (var ins = conn.CreateCommand())
            {
                ins.CommandText = @"
INSERT INTO Categories (Name, IsActive, CreatedUtc)
VALUES ($name, 1, CURRENT_TIMESTAMP);
SELECT last_insert_rowid();";
                ins.Parameters.AddWithValue("$name", name);

                var newIdObj = await ins.ExecuteScalarAsync();
                if (newIdObj is long id2) return id2;
                if (newIdObj is int id2i) return id2i;
            }

            throw new InvalidOperationException("Unable to create category.");
        }

        private async void CreateRuleButton_Clicked(object sender, EventArgs e)
        {
            try
            {
                var selected = CategoryPicker.SelectedItem as CategoryOption;
                if (selected?.Id is null)
                {
                    await DisplayAlert("Select Category", "Choose a category first (or add a new one), then create a rule.", "OK");
                    return;
                }

                var desc = (DescriptionEntry.Text ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(desc))
                {
                    await DisplayAlert("Invalid", "Description is required to create a rule.", "OK");
                    return;
                }

                // Match type choice (simple first stage)
                var matchType = await DisplayActionSheet("Rule match type", "Cancel", null, "Contains", "StartsWith", "Exact");
                if (matchType is null || matchType == "Cancel")
                    return;

                // Pattern prompt (defaults to full description)
                var pattern = await DisplayPromptAsync("Rule pattern",
                                                      "Enter the text to match against descriptions:",
                                                      "Save Rule",
                                                      "Cancel",
                                                      initialValue: desc,
                                                      maxLength: 200,
                                                      keyboard: Keyboard.Text);

                if (pattern is null)
                    return; // canceled

                pattern = pattern.Trim();
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    await DisplayAlert("Invalid", "Rule pattern cannot be empty.", "OK");
                    return;
                }

                var ruleId = await InsertCategoryRuleAsync(pattern, selected.Id.Value, matchType);

                // Optional: apply to existing matching transactions (safe default: only uncategorized)
                var apply = await DisplayAlert("Apply now?",
                                               "Apply this rule to existing uncategorized transactions for this account?",
                                               "Yes", "No");

                if (apply)
                {
                    var affected = await ApplyRuleToExistingAsync(_accountId, ruleId, pattern, selected.Id.Value, matchType);
                    await DisplayAlert("Rule saved", $"Rule created. Updated {affected} existing transaction(s).", "OK");
                }
                else
                {
                    await DisplayAlert("Rule saved", "Rule created.", "OK");
                }
            }
            catch (Exception ex)
            {
                await DisplayAlert("Error", ex.Message, "OK");
            }
        }

        private async Task<long> InsertCategoryRuleAsync(string pattern, long categoryId, string matchType)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            cmd.CommandText = @"
INSERT INTO CategoryRules (DescriptionPattern, CategoryId, MatchType, Priority, IsEnabled, CreatedUtc)
VALUES ($pattern, $categoryId, $matchType, 100, 1, CURRENT_TIMESTAMP);
SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$pattern", pattern);
            cmd.Parameters.AddWithValue("$categoryId", categoryId);
            cmd.Parameters.AddWithValue("$matchType", matchType);

            var idObj = await cmd.ExecuteScalarAsync();
            if (idObj is long id) return id;
            if (idObj is int id2) return id2;
            throw new InvalidOperationException("Unable to create rule.");
        }

        private async Task<int> ApplyRuleToExistingAsync(long accountId, long ruleId, string pattern, long categoryId, string matchType)
        {
            using var conn = _db.OpenConnection();
            using var cmd = conn.CreateCommand();

            // Safe first stage: only apply to transactions that are currently uncategorized.
            // Respect user's manual overrides.
            cmd.CommandText = matchType switch
            {
                "Exact" => @"
UPDATE Transactions
SET CategoryId = $categoryId,
    Category = NULL,
    MatchedRuleId = $ruleId,
    MatchedRulePattern = $pattern,
    CategorizedUtc = CURRENT_TIMESTAMP
WHERE AccountId = $accountId
  AND CategoryId IS NULL
  AND TRIM(Description) = $pattern;",

                "StartsWith" => @"
UPDATE Transactions
SET CategoryId = $categoryId,
    Category = NULL,
    MatchedRuleId = $ruleId,
    MatchedRulePattern = $pattern,
    CategorizedUtc = CURRENT_TIMESTAMP
WHERE AccountId = $accountId
  AND CategoryId IS NULL
  AND Description LIKE ($pattern || '%') ESCAPE '\';",

                _ => @"
UPDATE Transactions
SET CategoryId = $categoryId,
    Category = NULL,
    MatchedRuleId = $ruleId,
    MatchedRulePattern = $pattern,
    CategorizedUtc = CURRENT_TIMESTAMP
WHERE AccountId = $accountId
  AND CategoryId IS NULL
  AND Description LIKE ('%' || $pattern || '%') ESCAPE '\';"
            };

            cmd.Parameters.AddWithValue("$accountId", accountId);
            cmd.Parameters.AddWithValue("$ruleId", ruleId);
            cmd.Parameters.AddWithValue("$pattern", pattern);
            cmd.Parameters.AddWithValue("$categoryId", categoryId);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows;
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

        

        async void ReturnButton_Clicked(object sender, EventArgs e)
        {
            // Return is a quick navigation back to the register with no prompts/alerts.
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
				if (Navigation is not null)
				{
					await Navigation.PopAsync();
				}
            }
            catch
            {
                // If we can't navigate back, at least complete the TaskCompletionSource.
            }
        }
    }
}
