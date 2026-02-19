using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Data.Models;
using eFinance.Data.Repositories;
using eFinance.Services;

namespace eFinance.ViewModels;

public sealed partial class DeletedTransactionsViewModel : ObservableObject
{
    private readonly TransactionRepository _transactions;
    private readonly INavigationService _nav;

    public ObservableCollection<Transaction> Deleted { get; } = new();

    [ObservableProperty] private long accountId;
    [ObservableProperty] private string title = "Deleted Transactions";
    [ObservableProperty] private bool isBusy;

    public DeletedTransactionsViewModel(TransactionRepository transactions, INavigationService nav)
    {
        _transactions = transactions ?? throw new ArgumentNullException(nameof(transactions));
        _nav = nav ?? throw new ArgumentNullException(nameof(nav));
    }

    public async Task InitializeAsync(long accountId)
    {
        AccountId = accountId;
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        if (AccountId <= 0) return;

        try
        {
            IsBusy = true;

            Deleted.Clear();
            var items = await _transactions.GetDeletedTransactionsAsync(AccountId);
            foreach (var t in items)
                Deleted.Add(t);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync(Transaction? t)
    {
        if (t is null) return;
        if (IsBusy) return;

        try
        {
            IsBusy = true;

            await _transactions.RestoreAsync(t.Id);
            Deleted.Remove(t);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAllAsync()
    {
        if (IsBusy) return;
        if (AccountId <= 0) return;

        try
        {
            IsBusy = true;

            await _transactions.RestoreAllForAccountAsync(AccountId);
            Deleted.Clear();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task BackAsync() => _nav.GoBackAsync();
}
