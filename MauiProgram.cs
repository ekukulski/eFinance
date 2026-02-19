using eFinance.Data;
using eFinance.Data.Repositories;
using eFinance.Importing;
using eFinance.Pages;
using eFinance.Services;
using eFinance.ViewModels;
using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace eFinance;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseLiveCharts();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // ------------------------------------------------------------
        // Paths / folders
        // ------------------------------------------------------------
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "eFinance");

        var importDropDir = Path.Combine(appDataDir, "ImportDrop");

        Directory.CreateDirectory(appDataDir);
        Directory.CreateDirectory(importDropDir);

        // ------------------------------------------------------------
        // Database
        // ------------------------------------------------------------
        builder.Services.AddSingleton(sp =>
        {
            var dbPath = SqliteDatabase.DefaultDbPath(appName: "eFinance");
            System.Diagnostics.Debug.WriteLine("USING DB AT: " + dbPath);

            var db = new SqliteDatabase(dbPath);
            db.InitializeAsync().GetAwaiter().GetResult();

            return db;
        });

        // ------------------------------------------------------------
        // Repositories
        // ------------------------------------------------------------
        builder.Services.AddSingleton<AccountRepository>();
        builder.Services.AddSingleton<TransactionRepository>();
        builder.Services.AddSingleton<OpeningBalanceRepository>();
        builder.Services.AddSingleton<CategoryRepository>();
        builder.Services.AddSingleton<CategorizationService>();

        // ------------------------------------------------------------
        // Infrastructure services
        // ------------------------------------------------------------
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IWindowSizingService, WindowSizingService>();
        builder.Services.AddSingleton<ICsvFileService, CsvFileService>();
        builder.Services.AddSingleton<ICloudSyncService, CloudSyncService>();
        builder.Services.AddSingleton<IDialogService, DialogService>();

        // ------------------------------------------------------------
        // Importing pipeline
        // ------------------------------------------------------------
        builder.Services.AddSingleton<IImporter, AmexImporter>();
        builder.Services.AddSingleton<ImportPipeline>();

        builder.Services.AddSingleton(sp =>
        {
            var pipeline = sp.GetRequiredService<ImportPipeline>();
            return new ImportWatcher(importDropDir, pipeline);
        });

        // ------------------------------------------------------------
        // ViewModels
        // ------------------------------------------------------------
        builder.Services.AddTransient<MainPageViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddTransient<TransactionEditViewModel>();
        builder.Services.AddTransient<DuplicateAuditViewModel>();
        builder.Services.AddTransient<DeletedTransactionsViewModel>();
        builder.Services.AddTransient<CategoriesViewModel>();
        // ------------------------------------------------------------
        // Pages
        // ------------------------------------------------------------
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<DuplicateAuditPage>();
        builder.Services.AddTransient<CategoriesPage>();
        builder.Services.AddTransient<CategoriesPage>();
        builder.Services.AddTransient<TransactionEditPage>();
        builder.Services.AddTransient<DeletedTransactionsPage>();

        // ------------------------------------------------------------
        // Shell
        // ------------------------------------------------------------
        builder.Services.AddSingleton<AppShell>();

        // ------------------------------------------------------------
        // Window sizing (Windows only)
        // ------------------------------------------------------------
        builder.ConfigureLifecycleEvents(events =>
        {
#if WINDOWS
            events.AddWindows(w =>
            {
                w.OnWindowCreated(window =>
                {
                    try
                    {
                        var services = Application.Current?.Handler?.MauiContext?.Services;
                        var sizer = services?.GetService<IWindowSizingService>();
                        if (sizer is null)
                            return;

                        window.DispatcherQueue.TryEnqueue(() =>
                            sizer.ApplyDefaultSizing());
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("Window sizing hook failed: " + ex);
                    }
                });
            });
#endif
        });

        return builder.Build();
    }
}
