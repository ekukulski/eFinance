using System;
using System.Threading.Tasks;
using KukiFinance.Services;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Dispatching;


namespace KukiFinance;

public partial class App : Application
{
    private readonly IOneDriveSyncService _sync;

    private const string PrefAutoImport = "KukiFinance.AutoImportOnStartup";
    private const string PrefAutoExport = "KukiFinance.AutoExportOnExit";
    private const string PrefLastImported = "KukiFinance.LastImportedSnapshot";
    private const string PrefLastExported = "KukiFinance.LastExportedSnapshot";

    public App(AppShell shell, IOneDriveSyncService sync)
    {
        InitializeComponent();
        MainPage = shell;
        _sync = sync;
    }

    protected override void OnStart()
    {
        base.OnStart();

        // Fire-and-forget so we don't block UI startup.
        _ = TryAutoImportOnStartupAsync();
    }

    protected override void OnSleep()
    {
        base.OnSleep();

        // Best-effort export when app is backgrounded/closing.
        _ = TryAutoExportOnExitAsync();
    }

    private async Task TryAutoImportOnStartupAsync()
    {
        try
        {
            if (!Preferences.Get(PrefAutoImport, true))
                return;

            var (ok, latestName, latestUtc, message) = await _sync.GetLatestSnapshotInfoAsync();
            if (!ok || string.IsNullOrWhiteSpace(latestName))
                return;

            var lastImported = Preferences.Get(PrefLastImported, string.Empty);

            // If we haven't imported this snapshot yet, offer to import it now.
            if (string.Equals(lastImported, latestName, StringComparison.OrdinalIgnoreCase))
                return;

            var when = latestUtc.HasValue ? latestUtc.Value.ToLocalTime().ToString("g") : "unknown time";
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var confirm = await Current!.MainPage!.DisplayAlert(
                    "OneDrive Sync",
                    $"A newer database snapshot is available in OneDrive:\n\n{latestName}\n({when})\n\nImport it now?\n\nThis will back up your local data first, then overwrite local CSV files.",
                    "Import",
                    "Not now");

                if (!confirm)
                    return;

                var (importOk, importMsg, importedName) = await _sync.ImportFromOneDriveAsync();
                await Current!.MainPage!.DisplayAlert(importOk ? "Import" : "Import Failed", importMsg, "OK");

                if (importOk && !string.IsNullOrWhiteSpace(importedName))
                    Preferences.Set(PrefLastImported, importedName);
            });
        }
        catch
        {
            // Don't crash startup for sync issues.
        }
    }

    private async Task TryAutoExportOnExitAsync()
    {
        try
        {
            if (!Preferences.Get(PrefAutoExport, true))
                return;

            var (ok, _msg, snapshotName) = await _sync.ExportToOneDriveAsync();
            if (ok && !string.IsNullOrWhiteSpace(snapshotName))
                Preferences.Set(PrefLastExported, snapshotName);
        }
        catch
        {
            // Best-effort only.
        }
    }
}
