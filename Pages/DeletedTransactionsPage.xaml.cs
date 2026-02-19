using System;
using eFinance.ViewModels;

namespace eFinance.Pages;

[QueryProperty(nameof(AccountId), "accountId")]
public partial class DeletedTransactionsPage : ContentPage
{
    private readonly DeletedTransactionsViewModel _vm;
    private long _accountId;

    public DeletedTransactionsPage(DeletedTransactionsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    public string AccountId
    {
        set
        {
            if (long.TryParse(value, out var id) && id > 0)
                _accountId = id;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_accountId > 0)
            await _vm.InitializeAsync(_accountId);
    }
}
