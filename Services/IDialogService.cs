using System.Threading.Tasks;

namespace eFinance.Services;

public interface IDialogService
{
    Task<string?> PromptAsync(string title, string message, string? initialValue = null);
    Task AlertAsync(string title, string message, string cancel = "OK");
    Task<bool> ConfirmAsync(string title, string message, string accept = "OK", string cancel = "Cancel");
}
