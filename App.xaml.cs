using System;
using System.IO;
using System.Threading.Tasks;
using KukiFinance.Helpers;
using KukiFinance.Services;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;

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

    // Run startup sync only after the window/UI exists.
    protected override Window CreateWindow(IActivationState activationState)
    {
        var window = base.CreateWindow(activationState);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // Let Shell render first.
                await Task.Delay(300);

                await TryAutoImportOnStartupAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Startup auto-import failed: " + ex);
            }
        });

        return window;
    }

    protected override void OnSleep()
    {
        base.OnSleep();

        // Best-effort export when app is backgrounded/closing.
        _ = TryAutoExportOnExitAsync();
    }

    private static bool IsOneDriveAvailable(out string message)
    {
        try
        {
            // Don't create directories just to check availability.
            var baseDir = OneDrivePathHelper.GetOneDriveKukiFinanceDirectory(createIfMissing: false);

            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
            {
                message =
                    "OneDrive folder not found on this PC.\n\n" +
                    "Please sign into OneDrive (or ensure it is installed and syncing), then try again.\n\n" +
                    "You can still use the app locally, or use manual import/export once OneDrive is available.";
                return false;
            }

            message = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            message = "Unable to determine OneDrive availability: " + ex.Message;
            return false;
        }
    }

    private async Task TryAutoImportOnStartupAsync()
    {
        try
        {
            // Safer default: OFF unless user explicitly enables.
            if (!Preferences.Get(PrefAutoImport, false))
                return;

            // ✅ OneDrive availability guard (prevents crashes / confusing behavior on a new PC)
            if (!IsOneDriveAvailable(out _))
                return;

            var (ok, latestName, latestUtc, _message) = await _sync.GetLatestSnapshotInfoAsync();
            if (!ok || string.IsNullOrWhiteSpace(latestName))
                return;

            var lastImported = Preferences.Get(PrefLastImported, string.Empty);

            // If we already imported this snapshot, don't prompt again.
            if (string.Equals(lastImported, latestName, StringComparison.OrdinalIgnoreCase))
                return;

            var when = latestUtc.HasValue ? latestUtc.Value.ToLocalTime().ToString("g") : "unknown time";

            // Prefer Shell.Current for UI prompts (more reliable than Current.MainPage).
            var page = Shell.Current;
            if (page is null)
                return;

            var confirm = await page.DisplayAlert(
                "OneDrive Sync",
                $"A newer database snapshot is available in OneDrive:\n\n{latestName}\n({when})\n\nImport it now?\n\nThis will back up your local data first, then overwrite local CSV files.",
                "Import",
                "Not now");

            if (!confirm)
                return;

            var (importOk, importMsg, importedName) = await _sync.ImportFromOneDriveAsync();
            await page.DisplayAlert(importOk ? "Import" : "Import Failed", importMsg, "OK");

            if (importOk && !string.IsNullOrWhiteSpace(importedName))
                Preferences.Set(PrefLastImported, importedName);
        }
        catch (Exception ex)
        {
            // Don't crash startup for sync issues.
            System.Diagnostics.Debug.WriteLine("TryAutoImportOnStartupAsync error: " + ex);
        }
    }

    private async Task TryAutoExportOnExitAsync()
    {
        try
        {
            // Safer default: OFF unless user explicitly enables.
            if (!Preferences.Get(PrefAutoExport, false))
                return;

            // ✅ OneDrive availability guard (prevents exceptions when OneDrive isn't set up)
            if (!IsOneDriveAvailable(out _))
                return;

            var (ok, _msg, snapshotName) = await _sync.ExportToOneDriveAsync();
            if (ok && !string.IsNullOrWhiteSpace(snapshotName))
                Preferences.Set(PrefLastExported, snapshotName);
        }
        catch (Exception ex)
        {
            // Best-effort only.
            System.Diagnostics.Debug.WriteLine("TryAutoExportOnExitAsync error: " + ex);
        }
    }
}
