using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Data.Models;
using eFinance.Data.Repositories;
using Microsoft.Maui.Controls;

namespace eFinance.ViewModels;

public sealed partial class TransactionEditViewModel : ObservableObject
{
    private readonly TransactionRepository _transactions;
    private readonly CategoryRepository _categories;

    public TransactionEditViewModel(TransactionRepository transactions, CategoryRepository categories)
    {
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _categories = categories ?? throw new ArgumentNullException(nameof(categories));
    }

    // Context passed in by navigation
    [ObservableProperty] private long accountId;

    // Nullable => "Add mode" when null
    [ObservableProperty] private long? transactionId;

    // UI text
    [ObservableProperty] private string title = "Transaction";
    [ObservableProperty] private string header = "";

    // Editable fields
    [ObservableProperty] private DateTime postedDate = DateTime.Today;
    [ObservableProperty] private string description = "";
    [ObservableProperty] private string amountText = "";
    [ObservableProperty] private string memo = "";

    // Proper category selection (SQLite Categories table)
    public ObservableCollection<Category> CategoryItems { get; } = new();
    [ObservableProperty] private Category? selectedCategory;

    private bool _isEdit;

    public async Task InitializeAsync(long accountId, long? transactionId)
    {
        AccountId = accountId;
        TransactionId = transactionId;

        _isEdit = transactionId.HasValue && transactionId.Value > 0;

        Title = _isEdit ? "Edit Transaction" : "Add Transaction";
        Header = _isEdit ? "Edit Transaction" : "Add New Transaction";

        // Always load categories first (for both add and edit)
        await LoadCategoriesAsync();

        if (_isEdit)
        {
            var t = await _transactions.GetByIdAsync(transactionId!.Value);
            if (t is null)
            {
                await ShowAlertAsync("Transaction not found.");
                await GoBackAsync();
                return;
            }

            PostedDate = t.PostedDate.ToDateTime(TimeOnly.MinValue);
            Description = t.Description ?? "";
            AmountText = t.Amount.ToString(CultureInfo.InvariantCulture);
            Memo = t.Memo ?? "";

            // Prefer CategoryId if present
            if (t.CategoryId is not null && t.CategoryId.Value > 0)
            {
                SelectedCategory = CategoryItems.FirstOrDefault(c => c.Id == t.CategoryId.Value);
            }
            else if (!string.IsNullOrWhiteSpace(t.Category))
            {
                // Backward compatibility: legacy text category
                var legacy = t.Category.Trim();
                SelectedCategory = CategoryItems.FirstOrDefault(c =>
                    string.Equals(c.Name, legacy, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                SelectedCategory = null;
            }
        }
        else
        {
            // Defaults for new transaction
            PostedDate = DateTime.Today;
            AmountText = "0.00";
            Memo = "";
            SelectedCategory = null;
            Description = "";
        }
    }

    private async Task LoadCategoriesAsync()
    {
        CategoryItems.Clear();

        // IMPORTANT:
        // Do NOT filter "activeOnly" unless your Categories table actually supports IsActive
        // and you are properly setting it to 1 for existing categories.
        var list = await _categories.GetAllAsync(activeOnly: false);

        foreach (var c in list.OrderBy(x => x.Name))
            CategoryItems.Add(c);

        System.Diagnostics.Debug.WriteLine($"TransactionEditViewModel: loaded {CategoryItems.Count} categories.");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Validate amount
        var raw = (AmountText ?? "")
            .Replace("$", "")
            .Replace(",", "")
            .Trim();

        if (!decimal.TryParse(raw,
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var amount))
        {
            // fallback to current culture
            if (!decimal.TryParse(raw,
                    NumberStyles.Number | NumberStyles.AllowLeadingSign,
                    CultureInfo.CurrentCulture,
                    out amount))
            {
                await ShowAlertAsync("Amount is not valid.");
                return;
            }
        }

        // Validate description
        var desc = (Description ?? "").Trim();
        if (string.IsNullOrWhiteSpace(desc))
        {
            await ShowAlertAsync("Description is required.");
            return;
        }

        var dateOnly = DateOnly.FromDateTime(PostedDate);
        var memoValue = string.IsNullOrWhiteSpace(Memo) ? null : Memo.Trim();
        var categoryId = SelectedCategory?.Id;

        if (_isEdit)
        {
            if (TransactionId is null || TransactionId <= 0)
            {
                await ShowAlertAsync("No transaction selected to edit.");
                return;
            }

            var existing = await _transactions.GetByIdAsync(TransactionId.Value);
            if (existing is null)
            {
                await ShowAlertAsync("Transaction not found.");
                return;
            }

            existing.PostedDate = dateOnly;
            existing.Description = desc;
            existing.Amount = amount;
            existing.Memo = memoValue;
            existing.CategoryId = categoryId;

            // Stop writing legacy text category going forward
            existing.Category = null;

            await _transactions.UpdateAsync(existing);
        }
        else
        {
            var t = new Transaction
            {
                AccountId = AccountId,
                PostedDate = dateOnly,
                Description = desc,
                Amount = amount,
                CategoryId = categoryId,
                Category = null,               // legacy text not used going forward
                Memo = memoValue,
                FitId = null,                  // manual
                Source = "Manual",
                CreatedUtc = DateTime.UtcNow
            };

            await _transactions.InsertAsync(t);
        }

        await GoBackAsync();
    }

    [RelayCommand]
    private Task CancelAsync() => GoBackAsync();

    private static Task ShowAlertAsync(string message)
        => Application.Current?.MainPage?.DisplayAlert("eFinance", message, "OK") ?? Task.CompletedTask;

    private static Task GoBackAsync()
        => Shell.Current?.GoToAsync("..") ?? Task.CompletedTask;
}
