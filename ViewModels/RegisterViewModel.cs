using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Data.Repositories;
using eFinance.Pages;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls;
using TransactionModel = eFinance.Data.Models.Transaction;

namespace eFinance.ViewModels;

public sealed partial class RegisterViewModel : ObservableObject
{
    private readonly TransactionRepository _transactions;
    private readonly AccountRepository _accounts;
    private readonly OpeningBalanceRepository _openingBalances;

    private string _accountName = "";

    // Master list (unfiltered). Items is the filtered list shown in UI.
    private readonly List<RegisterRow> _allRows = new();

    public RegisterViewModel(
        TransactionRepository transactions,
        AccountRepository accounts,
        OpeningBalanceRepository openingBalances)
    {
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _openingBalances = openingBalances ?? throw new ArgumentNullException(nameof(openingBalances));
    }

    [ObservableProperty] private long accountId;
    [ObservableProperty] private string title = "Register";
    [ObservableProperty] private decimal currentBalance;

    [ObservableProperty] private RegisterRow? selectedRow;

    // Search box binds to this
    [ObservableProperty] private string searchText = "";

    // UI list (filtered)
    public ObservableCollection<RegisterRow> Items { get; } = new();

    // Auto-called by CommunityToolkit when SearchText changes
    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    public async Task InitializeAsync(long accountId)
    {
        AccountId = accountId;

        var acct = await _accounts.GetByIdAsync(accountId);
        _accountName = acct?.Name ?? $"Account {accountId}";

        Title = $"{_accountName} Register";

        await RefreshCoreAsync();
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshCoreAsync();

    [RelayCommand]
    private Task AddTransactionAsync()
    {
        // Add mode: no transactionId
        return Shell.Current.GoToAsync($"{nameof(TransactionEditPage)}?accountId={AccountId}");
    }

    [RelayCommand]
    private async Task EditTransactionAsync()
    {
        var row = SelectedRow;

        if (row?.Transaction is null)
        {
            await ShowMessageAsync("eFinance", "Select a transaction first.", "OK");
            return;
        }

        // Block synthetic opening balance row (Id == 0)
        if (row.Transaction.Id <= 0)
        {
            await ShowMessageAsync("eFinance", "You cannot edit the Opening Balance row.", "OK");
            return;
        }

        var id = row.Transaction.Id;

        System.Diagnostics.Debug.WriteLine(
            $"EditTransaction: navigating to TransactionEditPage with accountId={AccountId}, transactionId={id}");

        await Shell.Current.GoToAsync(
            $"{nameof(TransactionEditPage)}?accountId={AccountId}&transactionId={id}");
    }

    [RelayCommand]
    private async Task CopyDescriptionAsync()
    {
        var desc = SelectedRow?.Transaction?.Description ?? "";
        if (string.IsNullOrWhiteSpace(desc))
            return;

        await Clipboard.Default.SetTextAsync(desc);
        await ShowMessageAsync("eFinance", "Description copied.", "OK");
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var row = SelectedRow;
        if (row?.Transaction is null)
            return;

        if (row.Transaction.Id <= 0)
        {
            await ShowMessageAsync("eFinance", "You cannot delete the Opening Balance row.", "OK");
            return;
        }

        var confirm = await Application.Current!.MainPage!
            .DisplayAlert("Delete", "Delete the selected transaction?", "Delete", "Cancel");

        if (!confirm)
            return;

        await _transactions.SoftDeleteAsync(row.Transaction.Id);

        SelectedRow = null;
        await RefreshCoreAsync();
    }

    [RelayCommand]
    private Task ReturnAsync() => Shell.Current.GoToAsync("..");

    [RelayCommand]
    private Task OpenTrashAsync()
    {
        if (AccountId <= 0)
            return Task.CompletedTask;

        return Shell.Current.GoToAsync($"{nameof(DeletedTransactionsPage)}?accountId={AccountId}");
    }

    private async Task RefreshCoreAsync()
    {
        if (AccountId <= 0)
            return;

        // Make sure we have account name/title
        if (string.IsNullOrWhiteSpace(_accountName))
        {
            var acct = await _accounts.GetByIdAsync(AccountId);
            _accountName = acct?.Name ?? $"Account {AccountId}";
            Title = $"{_accountName} Register";
        }

        var (obDate, opening) = await _openingBalances.GetOpeningBalanceInfoAsync(_accountName);

        var txns = await _transactions.GetTransactionsAsync(AccountId);

        var orderedAsc = txns
            .OrderBy(t => t.PostedDate)
            .ThenBy(t => t.Id)
            .ToList();

        decimal running = opening;
        var rowsAsc = new List<RegisterRow>(orderedAsc.Count + 1);

        // Synthetic opening balance row
        rowsAsc.Add(new RegisterRow(
            new TransactionModel
            {
                Id = 0,
                PostedDate = obDate,
                Description = "OPENING BALANCE",
                Amount = 0m,
                Category = null,   // legacy display field
                CategoryId = null,
                Memo = null
            },
            opening
        ));

        foreach (var t in orderedAsc)
        {
            running += t.Amount;
            rowsAsc.Add(new RegisterRow(t, running));
        }

        // UI shows newest at top
        rowsAsc.Reverse();

        // Store master list (unfiltered)
        _allRows.Clear();
        _allRows.AddRange(rowsAsc);

        // Update summary
        CurrentBalance = running;

        // Populate Items based on current SearchText
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Items.Clear();

        var raw = (SearchText ?? "").Trim();

        // Empty => show all
        if (string.IsNullOrWhiteSpace(raw))
        {
            foreach (var r in _allRows)
                Items.Add(r);
            return;
        }

        // 1) Money search: supports 39.00, $39.00, (39.00), ($39.00), -39.00
        if (TryParseMoneyRobust(raw, out var money))
        {
            var target = Round2(money);
            var foundAny = false;

            foreach (var r in _allRows)
            {
                var amt = Round2(r.Transaction.Amount);

                // Match exact OR absolute (so 39 matches -39)
                if (amt == target || Math.Abs(amt) == Math.Abs(target))
                {
                    Items.Add(r);
                    foundAny = true;
                }
            }

            if (foundAny)
                return;
            // else fall through to text search so you don't get a blank list
        }

        // 2) Text search
        var qLower = raw.ToLowerInvariant();

        foreach (var r in _allRows)
        {
            var t = r.Transaction;

            var desc = t.Description ?? "";
            var memo = t.Memo ?? "";
            var cat = t.Category ?? "";
            var date = r.PostedDate.ToString();

            if (desc.ToLowerInvariant().Contains(qLower) ||
                memo.ToLowerInvariant().Contains(qLower) ||
                cat.ToLowerInvariant().Contains(qLower) ||
                date.ToLowerInvariant().Contains(qLower))
            {
                Items.Add(r);
            }
        }
    }

    private static decimal Round2(decimal v) =>
        decimal.Round(v, 2, MidpointRounding.AwayFromZero);

    private static bool TryParseMoneyRobust(string input, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var s = input.Trim();

        // Parentheses mean negative: (39.00) or ($39.00)
        var isParenNeg = s.StartsWith("(") && s.EndsWith(")");
        if (isParenNeg)
            s = s.Substring(1, s.Length - 2);

        // Keep only digits, one dot, and a leading minus.
        // (strips $, commas, spaces, etc.)
        var sb = new StringBuilder();
        bool sawDot = false;

        foreach (var ch in s)
        {
            if (char.IsDigit(ch))
                sb.Append(ch);
            else if (ch == '.' && !sawDot)
            {
                sb.Append('.');
                sawDot = true;
            }
            else if (ch == '-' && sb.Length == 0)
            {
                sb.Append('-');
            }
        }

        var cleaned = sb.ToString();
        if (string.IsNullOrWhiteSpace(cleaned) || cleaned == "-" || cleaned == ".")
            return false;

        if (!decimal.TryParse(
                cleaned,
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out var parsed))
            return false;

        value = isParenNeg ? -parsed : parsed;
        return true;
    }

    private static Task ShowMessageAsync(string title, string message, string cancel)
    {
        try
        {
            return Application.Current!.MainPage!.DisplayAlert(title, message, cancel);
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    public sealed class RegisterRow
    {
        public RegisterRow(TransactionModel transaction, decimal balance)
        {
            Transaction = transaction;
            Balance = balance;
        }

        public TransactionModel Transaction { get; }

        public long Id => Transaction.Id;
        public DateOnly PostedDate => Transaction.PostedDate;
        public string Description => Transaction.Description;
        public decimal Amount => Transaction.Amount;

        // Keep for display compatibility; long-term you’ll display Name via JOIN/lookup
        public string? Category => Transaction.Category;

        public decimal Balance { get; }
    }
}
