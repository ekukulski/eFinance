using System;
using Microsoft.Extensions.DependencyInjection;
using eFinance.Pages;
using eFinance.Services;

namespace eFinance;

public partial class AppShell : Shell
{
    private readonly IWindowSizingService _windowSizer;

    public AppShell(IWindowSizingService windowSizer)
    {
        InitializeComponent();
        _windowSizer = windowSizer;

        // Register routes via DI so pages can have constructor injection
        Routing.RegisterRoute(nameof(RegisterPage), new DiRouteFactory(typeof(RegisterPage)));
        Routing.RegisterRoute(nameof(DuplicateAuditPage), new DiRouteFactory(typeof(DuplicateAuditPage)));
        Routing.RegisterRoute(nameof(CategoriesPage), new DiRouteFactory(typeof(CategoriesPage)));
        Routing.RegisterRoute(nameof(TransactionEditPage), new DiRouteFactory(typeof(TransactionEditPage)));
        Routing.RegisterRoute(nameof(DeletedTransactionsPage), typeof(DeletedTransactionsPage));

        Navigated += (_, __) =>
        {
#if WINDOWS
            Dispatcher.Dispatch(() => _windowSizer.ApplyDefaultSizing());
#endif
        };
    }

    private sealed class DiRouteFactory : RouteFactory
    {
        private readonly Type _pageType;

        public DiRouteFactory(Type pageType)
        {
            _pageType = pageType;
        }

        // Some MAUI versions require this parameterless override too.
        public override Element GetOrCreate()
        {
            // Shell should prefer the IServiceProvider overload.
            // If this gets called, it means route creation is bypassing DI.
            throw new InvalidOperationException(
                $"Shell attempted to create {_pageType.Name} without IServiceProvider. " +
                "Use RouteFactory.GetOrCreate(IServiceProvider) for DI page creation.");
        }

        public override Element GetOrCreate(IServiceProvider services)
            => (Element)services.GetRequiredService(_pageType);
    }
}
