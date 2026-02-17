using System;
using eFinance.Data;
using eFinance.Data.Repositories;
using eFinance.Importing;
using eFinance.Pages;
using eFinance.Services;
using eFinance.ViewModels;
using LiveChartsCore.SkiaSharpView.Maui;
using Microsoft.Extensions.DependencyInjection;
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

        // -----------------------------
        // SQLite (Microsoft.Data.Sqlite)
        // Register DB early and ensure schema exists
        // -----------------------------
        builder.Services.AddSingleton(sp =>
        {
            var dbPath = SqliteDatabase.DefaultDbPath(appName: "eFinance");
            System.Diagnostics.Debug.WriteLine("USING DB AT: " + dbPath);

            var db = new SqliteDatabase(dbPath);

            // IMPORTANT: ensure schema/migrations exist before anything uses the DB
            db.InitializeAsync().GetAwaiter().GetResult();

            return db;
        });

        // -----------------------------
        // Repositories / Domain services
        // -----------------------------
        builder.Services.AddSingleton<AccountRepository>();
        builder.Services.AddSingleton<TransactionRepository>();
        builder.Services.AddSingleton<CategorizationService>();

        // -----------------------------
        // Services / Infrastructure
        // -----------------------------
        builder.Services.AddSingleton<INavigationService, NavigationService>();
        builder.Services.AddSingleton<IWindowSizingService, WindowSizingService>();
        builder.Services.AddSingleton<ICsvFileService, CsvFileService>();
        builder.Services.AddSingleton<ICloudSyncService, CloudSyncService>();

        // -----------------------------
        // Importing
        // -----------------------------
        builder.Services.AddSingleton<IImporter, AmexImporter>();
        builder.Services.AddSingleton<ImportPipeline>();

        // Choose a folder you want to watch. Example: LocalAppData\eFinance\ImportDrop
        builder.Services.AddSingleton(sp =>
        {
            var pipeline = sp.GetRequiredService<ImportPipeline>();
            var folder = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "eFinance",
                "ImportDrop");

            return new ImportWatcher(folder, pipeline);
        });

        // -----------------------------
        // UI / Navigation
        // -----------------------------
        builder.Services.AddSingleton<AppShell>();

        builder.Services.AddTransient<MainPageViewModel>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<eFinance.Pages.DataSyncPage>();
        builder.Services.AddTransient<AmexRegisterPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Capture the built MauiApp so we can use app.Services later (for window sizing hook)
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
