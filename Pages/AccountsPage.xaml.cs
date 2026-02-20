using System;
using eFinance.ViewModels;

namespace eFinance.Pages;

public partial class AccountsPage : ContentPage
{
    private readonly AccountsViewModel _vm;

    public AccountsPage(AccountsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm ?? throw new ArgumentNullException(nameof(vm));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            await _vm.LoadAsync();
        }
        catch
        {
            // optional: keep silent to avoid loops
        }
    }
}