using eFinance.ViewModels;

namespace eFinance.Pages;

[QueryProperty(nameof(AccountId), "accountId")]
public partial class RegisterPage : ContentPage
{
    private readonly RegisterViewModel _vm;

    public RegisterPage(RegisterViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    public string AccountId
    {
        set
        {
            if (long.TryParse(value, out var id) && id > 0)
                _ = _vm.InitializeAsync(id);
        }
    }
}
