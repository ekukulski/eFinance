using System.Collections.ObjectModel;

namespace eFinance.ViewModels;

public sealed class AccountGroup : ObservableCollection<AccountListItem>
{
    public string Title { get; }
    public decimal Total { get; }

    public string TotalText => Total.ToString("C");

    public AccountGroup(string title, IEnumerable<AccountListItem> items)
        : base(items)
    {
        Title = title;

        decimal total = 0m;
        foreach (var i in this)
            total += i.Balance;

        Total = total;
    }
}