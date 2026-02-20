using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using eFinance.Data.Repositories;
using eFinance.Pages;
using eFinance.Services;
using System;
using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace eFinance.ViewModels
{
    public sealed partial class MainPageViewModel : ObservableObject
    {
        private readonly AccountRepository _accounts;
        private readonly INavigationService _nav;

        public MainPageViewModel(AccountRepository accounts, INavigationService nav)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _nav = nav ?? throw new ArgumentNullException(nameof(nav));
        }

        [ObservableProperty]
        private string title = "eFinance";

        // ------------------------------------------------------------
        // NAV HELPERS
        // ------------------------------------------------------------
        private async Task OpenRegisterByAccountNameAsync(string accountName)
        {
            var id = await _accounts.GetIdByNameAsync(accountName);
            if (id is null || id <= 0)
            {
                await ShowAlertAsync("eFinance",
                    $"Account '{accountName}' was not found in the database.\n" +
                    $"Ensure it exists in your Accounts table / seeding.");
                return;
            }

            await _nav.GoToAsync($"{nameof(RegisterPage)}?accountId={id.Value}");
        }

        private static Task ShowAlertAsync(string title, string message)
        {
            try
            {
                return Application.Current?.MainPage?.DisplayAlert(title, message, "OK")
                    ?? Task.CompletedTask;
            }
            catch
            {
                return Task.CompletedTask;
            }
        }

        // ------------------------------------------------------------
        // COMMANDS (the ONLY ones MainPage.xaml should bind to)
        // ------------------------------------------------------------

        [RelayCommand]
        private Task OpenAMEXAsync() => OpenRegisterByAccountNameAsync("Amex");
        
        [RelayCommand]
        private async Task OpenAccountsAsync()
        {
            await _nav.GoToAsync(nameof(AccountsPage));
        }
        
        [RelayCommand]
        private Task OpenDuplicateAuditAsync() => _nav.GoToAsync(nameof(DuplicateAuditPage));

        [RelayCommand]
        private Task CategoriesAsync() => _nav.GoToAsync(nameof(CategoriesPage));

        [RelayCommand]
        private void Exit()
        {
#if WINDOWS
            Application.Current?.Quit();
#endif
        }


    }
}
