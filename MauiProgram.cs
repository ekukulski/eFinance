using KukiFinance.Services;
using KukiFinance.ViewModels;
using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.DependencyInjection; // <- important for GetService
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace KukiFinance;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseLiveCharts();

        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddTransient<MainPageViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<KukiFinance.Pages.DataSyncPage>();
        builder.Services.AddSingleton<AppShell>();
        builder.Services.AddSingleton<IWindowSizingService, WindowSizingService>();
        builder.Services.AddSingleton<ICsvFileService, CsvFileService>();
        builder.Services.AddSingleton<IOneDriveSyncService, OneDriveSyncService>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // 👇 Capture the built MauiApp so we can use app.Services later
        MauiApp? app = null;

        builder.ConfigureLifecycleEvents(events =>
        {
#if WINDOWS
            events.AddWindows(w =>
            {
                w.OnWindowCreated(window =>
                {
                    var sizer = app?.Services.GetService<IWindowSizingService>();
                    if (sizer is null)
                        return;

                    window.DispatcherQueue.TryEnqueue(() => sizer.ApplyDefaultSizing());
                });
            });
#endif
        });

        app = builder.Build();
        return app;
    }
}