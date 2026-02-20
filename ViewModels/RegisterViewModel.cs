using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Data.Repositories;
using eFinance.Pages;
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

    // Selection in UI
    [ObservableProperty] private RegisterRow? selectedRow;
    private RegisterRow? _lastSelected;

    // Search box binds to this
    [ObservableProperty] private string searchText = "";

    // UI list (filtered)
    public ObservableCollection<RegisterRow> Items { get; } = new();

    // Auto-called by CommunityToolkit when SearchText changes
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    // Auto-called by CommunityToolkit when SelectedRow changes
    partial void OnSelectedRowChanged(RegisterRow? value)
    {
        if (_lastSelected != null)
            _lastSelected.IsSelected = false;

        if (value != null)
            value.IsSelected = true;

        _lastSelected = value;
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
                Category = null,
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

        for (int i = 0; i < rowsAsc.Count; i++)
            rowsAsc[i].RowIndex = i;

        _allRows.Clear();
        _allRows.AddRange(rowsAsc);

        CurrentBalance = running;

        // Clear selection when we refresh the list
        SelectedRow = null;

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Items.Clear();

        var raw = (SearchText ?? "").Trim();

        if (string.IsNullOrWhiteSpace(raw))
        {
            foreach (var r in _allRows)
                Items.Add(r);
            return;
        }

        if (TryParseMoneyRobust(raw, out var money))
        {
            var target = Round2(money);
            var foundAny = false;

            foreach (var r in _allRows)
            {
                var amt = Round2(r.Transaction.Amount);

                if (amt == target || Math.Abs(amt) == Math.Abs(target))
                {
                    Items.Add(r);
                    foundAny = true;
                }
            }

            if (foundAny)
                return;
        }

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

        var isParenNeg = s.StartsWith("(") && s.EndsWith(")");
        if (isParenNeg)
            s = s.Substring(1, s.Length - 2);

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
}
public partial class RegisterRow : ObservableObject
{
    public TransactionModel Transaction { get; }
    public DateOnly PostedDate { get; }
    public string Description => Transaction.Description ?? "";
    public string Category => Transaction.Category ?? "";
    public decimal Amount => Transaction.Amount;
    public decimal Balance { get; }

    public int RowIndex { get; set; }   // ← ADD THIS

    [ObservableProperty]
    private bool isSelected;

    public RegisterRow(TransactionModel transaction, decimal balance)
    {
        Transaction = transaction;
        PostedDate = transaction.PostedDate;
        Balance = balance;
    }
}