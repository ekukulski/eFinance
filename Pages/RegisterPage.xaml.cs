using eFinance.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace eFinance.Pages;

[QueryProperty(nameof(AccountId), "accountId")]
public partial class RegisterPage : ContentPage
{
    private RegisterViewModel? _vm;

    public RegisterPage()
    {
        InitializeComponent();

        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;

            _vm = services?.GetRequiredService<RegisterViewModel>();
            if (_vm is null)
            {
                // Don’t crash the whole app; show a helpful message and go back
                _ = ShowAndGoBackAsync(
                    "RegisterViewModel is not available (DI). " +
                    "Add: builder.Services.AddTransient<RegisterViewModel>();");
                return;
            }

            BindingContext = _vm;
        }
        catch (Exception ex)
        {
            _ = ShowAndGoBackAsync("Failed to create RegisterPage: " + ex.Message);
        }
    }

    // Keep this constructor if you ever navigate to RegisterPage via DI directly
    public RegisterPage(RegisterViewModel vm) : this()
    {
        _vm = vm;
        BindingContext = _vm;
    }

    public string AccountId
    {
        set
        {
            if (_vm is null)
                return;

            if (long.TryParse(value, out var id) && id > 0)
                _ = InitializeSafeAsync(id);
        }
    }

    private async Task InitializeSafeAsync(long accountId)
    {
        try
        {
            await _vm!.InitializeAsync(accountId);
        }
        catch (Exception ex)
        {
            await DisplayAlert("eFinance", "Register init failed: " + ex.Message, "OK");
        }
    }
    private void SearchEntry_Completed(object sender, EventArgs e)
    {
        if (sender is Entry entry)
            entry.Unfocus(); // dismiss keyboard
    }

    private async Task ShowAndGoBackAsync(string message)
    {
        await DisplayAlert("eFinance", message, "OK");
        await Shell.Current.GoToAsync("..");
    }
}
