using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using eFinance.Data;
using eFinance.Data.Repositories;
using eFinance.Importing;
using eFinance.Services;
using Microsoft.Data.Sqlite;
using eFinance.Helpers;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;

namespace eFinance;

public partial class App : Application
{
    private readonly ICloudSyncService _sync;
    private readonly SqliteDatabase _db;
    private readonly AccountRepository _accounts;
    private readonly ImportWatcher _importWatcher;

    private const string PrefAutoImport = "eFinance.Auto.ImportOnStartup";
    private const string PrefAutoExport = "eFinance.Auto.ExportOnExit";
    private const string PrefLastImported = "eFinance.Last.ImportedSnapshot";
    private const string PrefLastExported = "eFinance.Last.ExportedSnapshot";

    // ✅ One-time seed marker (prevents re-seeding)
    private const string PrefOpeningBalancesSeeded = "eFinance.OpeningBalances.Seeded";

    public App(
        AppShell shell,
        ICloudSyncService sync,
        SqliteDatabase db,
        AccountRepository accounts,
        ImportWatcher importWatcher)
    {
        InitializeComponent();

        // Force the app to always use Light mode
        UserAppTheme = AppTheme.Light;

        MainPage = shell;

        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _importWatcher = importWatcher ?? throw new ArgumentNullException(nameof(importWatcher));
    }

    // Run startup work only after the window/UI exists.
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = base.CreateWindow(activationState);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                // Let Shell render first.
                await Task.Delay(300);

                // 1) Ensure local SQLite DB exists (non-disruptive, fast).
                await InitializeLocalDatabaseAsync();

                // ✅ Start watching the import drop folder AFTER DB is ready.
                _importWatcher.Start();

                // 2) Then do optional startup import (still guarded & best-effort).
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

        // Optional (only if your ImportWatcher supports it):
        // _importWatcher.Stop();
    }

    private async Task InitializeLocalDatabaseAsync()
    {
        try
        {
            // ✅ Make it obvious in output where the DB is
            System.Diagnostics.Debug.WriteLine($"USING DB AT: {_db.DatabasePath}");

            // Create/upgrade schema
            await _db.InitializeAsync();

            // Seed Accounts if empty
            await _accounts.SeedDefaultsIfEmptyAsync();

            // ✅ Seed OpeningBalances from OpeningBalance.csv ONLY if needed
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
            // If you've already seeded once, skip.
            if (Preferences.Get(PrefOpeningBalancesSeeded, false))
                return;

            using var conn = _db.OpenConnection();

            // If table is missing (schema not updated yet), this will throw.
            // That’s OK: it tells you InitializeAsync() doesn’t include OpeningBalances yet.
            using (var check = conn.CreateCommand())
            {
                check.CommandText = "SELECT COUNT(1) FROM OpeningBalances;";
                var count = (long)(await check.ExecuteScalarAsync() ?? 0L);
                if (count > 0)
                {
                    Preferences.Set(PrefOpeningBalancesSeeded, true);
                    return;
                }
            }

            // Where is OpeningBalance.csv?
            // Prefer: the same app data folder you already use for other files.
            // If you have a helper, use it; otherwise use LocalAppData\eFinance\OpeningBalance.csv.
            var csvPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "eFinance",
                "OpeningBalance.csv");

            if (!File.Exists(csvPath))
            {
                // Nothing to seed, don't set the flag (you may add it later).
                System.Diagnostics.Debug.WriteLine($"OpeningBalance.csv not found at: {csvPath}");
                return;
            }

            var lines = File.ReadAllLines(csvPath);
            if (lines.Length <= 1)
                return;

            // Parse CSV: Date,Account,Balance
            // Example: 2023-07-23,Amex,-4300.19
            var nowUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);

            using var tx = conn.BeginTransaction();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("Date,", StringComparison.OrdinalIgnoreCase)) continue;

                // Simple split (your file is simple: no quoted commas)
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

                // Allow commas/currency if someone edited it
                balText = balText.Replace("$", "").Replace(",", "");
                if (!decimal.TryParse(balText, NumberStyles.Number | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var bal))
                    continue;

                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;

                // SQLite UPSERT (requires UNIQUE(AccountName) index)
                cmd.CommandText = @"
INSERT INTO OpeningBalances (AccountName, BalanceDate, Balance, CreatedUtc)
VALUES ($name, $date, $bal, $createdUtc)
ON CONFLICT(AccountName) DO UPDATE SET
    BalanceDate = excluded.BalanceDate,
    Balance = excluded.Balance,
    CreatedUtc = excluded.CreatedUtc;
";
                cmd.Parameters.AddWithValue("$name", accountName);
                cmd.Parameters.AddWithValue("$date", dt.ToString("yyyy-MM-dd"));
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
            // Most common reason: table doesn't exist yet because SqliteDatabase.InitializeAsync()
            // hasn't been updated with OpeningBalances DDL.
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
            // Don't create directories just to check availability.
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
