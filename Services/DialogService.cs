using System.Threading.Tasks;
using Microsoft.Maui.Controls;

namespace eFinance.Services;

public sealed class DialogService : IDialogService
{
    private static Page? CurrentPage
        => Shell.Current?.CurrentPage ?? Application.Current?.MainPage;

    public Task<string?> PromptAsync(string title, string message, string? initialValue = null)
    {
        var page = CurrentPage;
        if (page is null) return Task.FromResult<string?>(null);

        return page.DisplayPromptAsync(title, message, initialValue: initialValue);
    }

    public Task AlertAsync(string title, string message, string cancel = "OK")
    {
        var page = CurrentPage;
        if (page is null) return Task.CompletedTask;

        return page.DisplayAlert(title, message, cancel);
    }

    public Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel")
    {
        var page = CurrentPage;
        if (page is null) return Task.FromResult(false);

        return page.DisplayAlert(title, message, accept, cancel);
    }
}
