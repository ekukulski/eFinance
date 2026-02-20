using System;
using System.Threading.Tasks;
using eFinance.Importing;
using eFinance.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace eFinance.Pages;

[QueryProperty(nameof(AccountId), "accountId")]
public partial class RegisterPage : ContentPage
{
    private RegisterViewModel? _vm;
    private long _accountId;

    public RegisterPage()
    {
        InitializeComponent();

        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;

            _vm = services?.GetRequiredService<RegisterViewModel>();
            if (_vm is null)
            {
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
            {
                _accountId = id;

                // ✅ set current import target here (no field needed)
                try
                {
                    var services = Application.Current?.Handler?.MauiContext?.Services;
                    var target = services?.GetService<IImportTargetContext>();
                    if (target is not null)
                        target.CurrentAccountId = _accountId;
                }
                catch
                {
                    // ignore; importing just won't be enabled
                }

                _ = InitializeSafeAsync(_accountId);
            }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Optional: clear import target if this page set it
        try
        {
            var services = Application.Current?.Handler?.MauiContext?.Services;
            var target = services?.GetService<IImportTargetContext>();
            if (target is not null && target.CurrentAccountId == _accountId)
                target.CurrentAccountId = null;
        }
        catch
        {
            // ignore
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
            entry.Unfocus();
    }

    private async Task ShowAndGoBackAsync(string message)
    {
        await DisplayAlert("eFinance", message, "OK");
        await Shell.Current.GoToAsync("..");
    }
}