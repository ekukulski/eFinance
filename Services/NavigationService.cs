namespace KukiFinance.Services;

public interface INavigationService
{
    Task GoToAsync(string route);
    Task BackAsync();
}

public sealed class NavigationService : INavigationService
{
    public Task GoToAsync(string route) => Shell.Current.GoToAsync(route);
    public Task BackAsync() => Shell.Current.GoToAsync("..");
}
