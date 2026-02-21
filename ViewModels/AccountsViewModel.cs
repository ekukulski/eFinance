using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Data.Models;
using eFinance.Data.Repositories;
using eFinance.Pages;
using eFinance.Services;

namespace eFinance.ViewModels;

public sealed partial class AccountsViewModel : ObservableObject
{
    private readonly AccountRepository _accounts;
    private readonly TransactionRepository _transactions;
    private readonly OpeningBalanceRepository _openingBalances;
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;

    public ObservableCollection<AccountGroup> GroupedAccounts { get; } = new();

    [ObservableProperty]
    private bool showInactive;

    public string ShowInactiveButtonText => ShowInactive ? "Hide Inactive" : "Show Inactive";

    public AccountsViewModel(
        AccountRepository accounts,
        TransactionRepository transactions,
        OpeningBalanceRepository openingBalances,
        INavigationService nav,
        IDialogService dialogs)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _openingBalances = openingBalances ?? throw new ArgumentNullException(nameof(openingBalances));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    // ------------------------------------------------------------
    // Load + Group + Balance
    // ------------------------------------------------------------
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            var accounts = ShowInactive
                ? await _accounts.GetAllAsync()
                : await _accounts.GetAllActiveAsync();

            var ids = accounts.Select(a => a.Id).Where(id => id > 0).Distinct().ToArray();

            Dictionary<long, decimal> txnSums = new();
            Dictionary<long, decimal> openings = new();

            try
            {
                txnSums = await _transactions.GetSumByAccountIdsAsync(ids);
            }
            catch
            {
                txnSums = ids.ToDictionary(id => id, _ => 0m);
            }

            try
            {
                openings = await _openingBalances.GetOpeningByAccountIdsAsync(ids);
            }
            catch
            {
                openings = ids.ToDictionary(id => id, _ => 0m);
            }

            AccountListItem ToItem(Account a)
            {
                openings.TryGetValue(a.Id, out var open);
                txnSums.TryGetValue(a.Id, out var sum);
                return new AccountListItem(a, open + sum);
            }

            var items = accounts.Select(ToItem).ToList();

            var liabilities = items.Where(i => i.AccountType == "CreditCard");
            var cash = items.Where(i =>
                i.AccountType == "Checking" ||
                i.AccountType == "Savings" ||
                i.AccountType == "CD");

            var assets = items.Where(i => i.AccountType == "Investment");
            var other = items.Except(liabilities).Except(cash).Except(assets);

            GroupedAccounts.Clear();

            if (liabilities.Any()) GroupedAccounts.Add(new AccountGroup("LIABILITIES", liabilities));
            if (cash.Any()) GroupedAccounts.Add(new AccountGroup("CASH", cash));
            if (assets.Any()) GroupedAccounts.Add(new AccountGroup("ASSETS", assets));
            if (other.Any()) GroupedAccounts.Add(new AccountGroup("OTHER", other));

            OnPropertyChanged(nameof(ShowInactiveButtonText));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AccountsViewModel error: {ex}");
        }
    }

    [RelayCommand]
    private async Task ToggleInactiveAsync()
    {
        ShowInactive = !ShowInactive;
        OnPropertyChanged(nameof(ShowInactiveButtonText));
        await LoadAsync();
    }

    // ------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------
    [RelayCommand]
    private async Task OpenRegisterAsync(long accountId)
    {
        if (accountId <= 0) return;
        await _nav.GoToAsync($"{nameof(RegisterPage)}?accountId={accountId}");
    }

    // ------------------------------------------------------------
    // Account CRUD
    // ------------------------------------------------------------
    [RelayCommand]
    private async Task AddAccountAsync()
    {
        var name = await _dialogs.PromptAsync("New Account", "Account name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var type = await _dialogs.PromptAsync(
            "Account Type",
            "Enter type (Checking, Savings, CD, CreditCard, Investment):",
            "Checking");

        if (string.IsNullOrWhiteSpace(type))
            type = "Checking";

        type = NormalizeAccountType(type);

        var account = new Account
        {
            Name = name.Trim(),
            AccountType = type,
            CreatedUtc = DateTime.UtcNow,
            IsActive = true
        };

        await _accounts.AddAsync(account);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task RenameAccountAsync(AccountListItem? item)
    {
        var account = item?.Account;
        if (account is null || account.Id <= 0) return;

        var newName = await _dialogs.PromptAsync("Rename Account", "New account name:", account.Name);
        if (string.IsNullOrWhiteSpace(newName)) return;

        newName = newName.Trim();
        if (string.Equals(newName, account.Name, StringComparison.OrdinalIgnoreCase))
            return;

        await _accounts.UpdateNameAsync(account.Id, newName);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ToggleActiveAsync(AccountListItem? item)
    {
        var account = item?.Account;
        if (account is null || account.Id <= 0) return;

        if (account.IsActive)
        {
            var ok = await _dialogs.ConfirmAsync(
                "Deactivate account?",
                "This hides the account but keeps all its transactions. You can reactivate later.");
            if (!ok) return;

            await _accounts.DeactivateByIdAsync(account.Id);
        }
        else
        {
            var ok = await _dialogs.ConfirmAsync(
                "Reactivate account?",
                "This will show the account again.");
            if (!ok) return;

            await _accounts.ReactivateByIdAsync(account.Id);
        }

        await LoadAsync();
    }

    [RelayCommand]
    private Task BackAsync() => _nav.GoBackAsync();

    // ------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------
    private static string NormalizeAccountType(string raw)
    {
        var t = raw.Trim();

        // Normalize common variants
        if (t.Equals("credit card", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("creditcard", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("cc", StringComparison.OrdinalIgnoreCase))
            return "CreditCard";

        if (t.Equals("checking", StringComparison.OrdinalIgnoreCase))
            return "Checking";

        if (t.Equals("savings", StringComparison.OrdinalIgnoreCase))
            return "Savings";

        if (t.Equals("cd", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("certificate of deposit", StringComparison.OrdinalIgnoreCase))
            return "CD";

        if (t.Equals("investment", StringComparison.OrdinalIgnoreCase) ||
            t.Equals("brokerage", StringComparison.OrdinalIgnoreCase))
            return "Investment";

        // Keep unknown types, but trimmed
        return t;
    }
}