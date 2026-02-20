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
    private readonly INavigationService _nav;
    private readonly IDialogService _dialogs;

    public ObservableCollection<Account> Accounts { get; } = new();

    [ObservableProperty]
    private bool showInactive;

    public string ShowInactiveButtonText => ShowInactive ? "Hide Inactive" : "Show Inactive";

    public AccountsViewModel(AccountRepository accounts, INavigationService nav, IDialogService dialogs)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
    }

    [RelayCommand]
    public async Task LoadAsync()
    {
        Accounts.Clear();

        var list = ShowInactive
            ? await _accounts.GetAllAsync()
            : await _accounts.GetAllActiveAsync();

        foreach (var a in list)
            Accounts.Add(a);

        OnPropertyChanged(nameof(ShowInactiveButtonText));
    }

    [RelayCommand]
    private async Task ToggleInactiveAsync()
    {
        ShowInactive = !ShowInactive;
        OnPropertyChanged(nameof(ShowInactiveButtonText));
        await LoadAsync();
    }

    [RelayCommand]
    private async Task OpenRegisterAsync(long accountId)
    {
        if (accountId <= 0) return;
        await _nav.GoToAsync($"{nameof(RegisterPage)}?accountId={accountId}");
    }

    [RelayCommand]
    private async Task AddAccountAsync()
    {
        var name = await _dialogs.PromptAsync("New Account", "Account name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        // Your IDialogService does NOT have PickAsync, so use PromptAsync
        var type = await _dialogs.PromptAsync(
            "Account Type",
            "Enter type (Checking, Savings, CreditCard):",
            "Checking");

        if (string.IsNullOrWhiteSpace(type))
            type = "Checking";

        type = type.Trim();
        if (type.Equals("credit card", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("creditcard", StringComparison.OrdinalIgnoreCase))
            type = "CreditCard";

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
    private async Task RenameAccountAsync(Account? account)
    {
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
    private async Task ToggleActiveAsync(Account? account)
    {
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
}