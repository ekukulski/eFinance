using System;
using System.Threading.Tasks;

namespace eFinance.Services;

public sealed class NavigationService : INavigationService
{
    public Task GoToAsync(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
            return Task.CompletedTask;

        // Shell routing will construct pages via DI if the route is registered
        return Shell.Current.GoToAsync(route);
    }

    public Task GoBackAsync()
    {
        return Shell.Current.GoToAsync("..");
    }
}
