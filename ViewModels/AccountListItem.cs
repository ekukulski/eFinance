using eFinance.Data.Models;

namespace eFinance.ViewModels;

public sealed class AccountListItem
{
    public Account Account { get; }
    public decimal Balance { get; }

    public long Id => Account.Id;
    public string Name => Account.Name;
    public string AccountType => Account.AccountType;
    public bool IsActive => Account.IsActive;

    public string BalanceText => Balance.ToString("C");

    public AccountListItem(Account account, decimal balance)
    {
        Account = account ?? throw new ArgumentNullException(nameof(account));
        Balance = balance;
    }
}