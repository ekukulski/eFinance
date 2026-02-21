using System.Globalization;
using eFinance.Data;
using eFinance.Data.Repositories;
using eFinance.Importing;
using eFinance.Services;
using Microsoft.Data.Sqlite;

namespace eFinance;

public partial class App : Application
{
    private readonly AppShell _shell;
    private readonly ICloudSyncService _sync;
    private readonly SqliteDatabase _db;
    private readonly AccountRepository _accounts;
    private readonly ImportWatcher _importWatcher;

    private const string PrefAutoImport = "eFinance.Auto.ImportOnStartup";
    private const string PrefAutoExport = "eFinance.Auto.ExportOnExit";
    private const string PrefLastImported = "eFinance.Last.ImportedSnapshot";
    private const string PrefLastExported = "eFinance.Last.ExportedSnapshot";

    // One-time seed marker (prevents re-seeding)
    private const string PrefOpeningBalancesSeeded = "eFinance.OpeningBalances.Seeded";

    public App(
        AppShell shell,
        ICloudSyncService sync,
        SqliteDatabase db,
        AccountRepository accounts,
        ImportWatcher importWatcher)
    {
        InitializeComponent();

        _shell = shell ?? throw new ArgumentNullException(nameof(shell));
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _importWatcher = importWatcher ?? throw new ArgumentNullException(nameof(importWatcher));

        // Let Windows/theme decide (don’t force Light)
        UserAppTheme = AppTheme.Unspecified;

        MainPage = _shell;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        // Run startup work only after the UI exists.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // Let Shell render first.
                await Task.Delay(150);

                await InitializeLocalDatabaseAsync();

                var importDrop = EnsureImportDropFolderExists();
                System.Diagnostics.Debug.WriteLine("ImportDrop folder: " + importDrop);

                // Start watching the import drop folder AFTER DB is ready.
                _importWatcher.Start();
                System.Diagnostics.Debug.WriteLine("ImportWatcher started.");

                // OPTIONAL: If CSVs already exist, touch timestamps so Changed event fires.
                // This helps in cases where the file was copied in before the watcher started.
                TryNudgeExistingCsvFiles(importDrop);

                // Optional startup import (guarded & best-effort).
                await TryAutoImportOnStartupAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Startup initialization failed: " + ex);
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

    private static string EnsureImportDropFolderExists()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "eFinance",
            "ImportDrop");

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("EnsureImportDropFolderExists failed: " + ex);
        }

        return folder;
    }

    private static void TryNudgeExistingCsvFiles(string importDropFolder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(importDropFolder) || !Directory.Exists(importDropFolder))
                return;

            // Only look at top-level CSVs (ignore Archive)
            var csvs = Directory.GetFiles(importDropFolder, "*.csv", SearchOption.TopDirectoryOnly);

            if (csvs.Length == 0)
                return;

            System.Diagnostics.Debug.WriteLine($"Found {csvs.Length} existing CSV(s) in ImportDrop. Nudging timestamps...");

            foreach (var path in csvs)
            {
                try
                {
                    // Touch file timestamp to trigger FileSystemWatcher.Changed
                    File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
                }
                catch (Exception exFile)
                {
                    System.Diagnostics.Debug.WriteLine("Nudge failed for " + path + ": " + exFile.Message);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("TryNudgeExistingCsvFiles error: " + ex);
        }
    }

    private async Task InitializeLocalDatabaseAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"USING DB AT: {_db.DatabasePath}");

            // Safe to call even if MauiProgram already initialized the schema
            await _db.InitializeAsync();

            // Seed Accounts if empty
            await _accounts.SeedDefaultsIfEmptyAsync();

            // Seed OpeningBalances from OpeningBalance.csv (best-effort / safe)
            await SeedOpeningBalancesFromCsvIfNeededAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("SQLite init/seed failed: " + ex);
        }
    }

    /// <summary>
    /// Imports OpeningBalance.csv into SQLite OpeningBalances table one-time (or only when table is empty).
    /// Safe to call on every startup.
    /// </summary>
    private async Task SeedOpeningBalancesFromCsvIfNeededAsync()
    {
        try
        {
            if (Preferences.Get(PrefOpeningBalancesSeeded, false))
                return;

            using var conn = _db.OpenConnection();

            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(1) FROM OpeningBalances;";
                var countObj = await check.ExecuteScalarAsync();
                var count = Convert.ToInt64(countObj ?? 0);

                if (count > 0)
                {
                    Preferences.Set(PrefOpeningBalancesSeeded, true);
                    return;
                }
            }

            var csvPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "eFinance",
                "OpeningBalance.csv");

            if (!File.Exists(csvPath))
            {
                System.Diagnostics.Debug.WriteLine($"OpeningBalance.csv not found at: {csvPath}");
                return;
            }

            var lines = File.ReadAllLines(csvPath);
            if (lines.Length <= 1)
                return;

            var nowUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            using var tx = conn.BeginTransaction();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("Date,", StringComparison.OrdinalIgnoreCase)) continue;

                var parts = line.Split(',');
                if (parts.Length < 3) continue;

                var dateText = parts[0].Trim();
                var accountName = parts[1].Trim();
                var balText = parts[2].Trim();

                if (string.IsNullOrWhiteSpace(dateText) ||
                    string.IsNullOrWhiteSpace(accountName) ||
                    string.IsNullOrWhiteSpace(balText))
                    continue;

                if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt))
                    continue;

                balText = balText.Replace("$", "").Replace(",", "");
                if (!decimal.TryParse(balText, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var bal))
                    continue;

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                // Insert rows (simple + safe for first seed when table is empty)
                cmd.CommandText = @"
INSERT INTO OpeningBalances (AccountName, BalanceDate, Balance, CreatedUtc)
VALUES ($name, $date, $bal, $createdUtc);
";
                cmd.Parameters.AddWithValue("$name", accountName);
                cmd.Parameters.AddWithValue("$date", dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("$bal", (double)bal);
                cmd.Parameters.AddWithValue("$createdUtc", nowUtc);

                await cmd.ExecuteNonQueryAsync();
            }

            await tx.CommitAsync();

            Preferences.Set(PrefOpeningBalancesSeeded, true);
            System.Diagnostics.Debug.WriteLine("Opening balances seeded from OpeningBalance.csv.");
        }
        catch (SqliteException sx)
        {
            System.Diagnostics.Debug.WriteLine("SeedOpeningBalancesFromCsvIfNeededAsync SQLite error: " + sx.Message);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("SeedOpeningBalancesFromCsvIfNeededAsync error: " + ex);
        }
    }

    private static bool IsCloudSyncAvailable(out string message)
    {
        try
        {
            var baseDir = CloudSyncPathHelper.GetCloudSynceFinanceDirectory(createIfMissing: false);

            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
            {
                message =
                    "Proton Drive folder not found on this PC.\n\n" +
                    "Please make sure Proton Drive is installed and syncing, then try again.\n\n" +
                    "You can still use the app locally, or use manual import/export once Proton Drive is available.";
                return false;
            }

            message = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            message = "Unable to determine Proton Drive availability: " + ex.Message;
            return false;
        }
    }

    private async Task TryAutoImportOnStartupAsync()
    {
        try
        {
            if (!Preferences.Get(PrefAutoImport, false))
                return;

            if (!IsCloudSyncAvailable(out _))
                return;

            var (ok, latestName, latestUtc, _message) = await _sync.GetLatestSnapshotInfoAsync();
            if (!ok || string.IsNullOrWhiteSpace(latestName))
                return;

            var lastImported = Preferences.Get(PrefLastImported, string.Empty);
            if (string.Equals(lastImported, latestName, StringComparison.OrdinalIgnoreCase))
                return;

            var when = latestUtc.HasValue ? latestUtc.Value.ToLocalTime().ToString("g") : "unknown time";

            var page = Shell.Current;
            if (page is null)
                return;

            var confirm = await page.DisplayAlert(
                "Cloud Sync",
                $"A newer database snapshot is available in Proton Drive:\n\n{latestName}\n({when})\n\nImport it now?\n\nThis will back up your local data first, then overwrite local CSV files.",
                "Import",
                "Not now");

            if (!confirm)
                return;

            var (importOk, importMsg, importedName) = await _sync.ImportFromCloudAsync();
            await page.DisplayAlert(importOk ? "Import" : "Import Failed", importMsg, "OK");

            if (importOk && !string.IsNullOrWhiteSpace(importedName))
                Preferences.Set(PrefLastImported, importedName);

            if (importOk)
                await InitializeLocalDatabaseAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("TryAutoImportOnStartupAsync error: " + ex);
        }
    }

    private async Task TryAutoExportOnExitAsync()
    {
        try
        {
            if (!Preferences.Get(PrefAutoExport, false))
                return;

            if (!IsCloudSyncAvailable(out _))
                return;

            var (ok, _msg, snapshotName) = await _sync.ExportToCloudAsync();
            if (ok && !string.IsNullOrWhiteSpace(snapshotName))
                Preferences.Set(PrefLastExported, snapshotName);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("TryAutoExportOnExitAsync error: " + ex);
        }
    }
}