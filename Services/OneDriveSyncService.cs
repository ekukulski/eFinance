using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using KukiFinance.Helpers;
using Microsoft.Maui.Storage;

namespace KukiFinance.Services;

public interface IOneDriveSyncService
{
    /// <summary>Exports all local *.csv data files from LocalState (AppDataDirectory) to OneDrive as a single versioned zip snapshot.</summary>
    Task<(bool ok, string message, string? snapshotName)> ExportToOneDriveAsync();

    /// <summary>Imports the latest OneDrive snapshot (zip) into LocalState (AppDataDirectory), backing up current local files first.</summary>
    Task<(bool ok, string message, string? snapshotName)> ImportFromOneDriveAsync();

    /// <summary>Returns the latest snapshot available in OneDrive (based on LATEST.txt or newest .ready).</summary>
    Task<(bool ok, string? snapshotName, DateTime? snapshotWriteUtc, string message)> GetLatestSnapshotInfoAsync();
}

public sealed class OneDriveSyncService : IOneDriveSyncService
{
    private const string AppFolderName = "KukiFinance";
    private const string LatestPointerFileName = "LATEST.txt";

    private static string LocalDataDir => FileSystem.AppDataDirectory;

    private static string[] GetLocalCsvFiles()
        => Directory.Exists(LocalDataDir)
            ? Directory.GetFiles(LocalDataDir, "*.csv", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();

    private static string GetOneDriveBaseDir()
        => OneDrivePathHelper.GetOneDriveKukiFinanceDirectory(createIfMissing: true);

    private static string ExportsDir => Path.Combine(GetOneDriveBaseDir(), "Exports");
    private static string ArchiveDir => Path.Combine(GetOneDriveBaseDir(), "Archive");

    private static string LocalBackupsDir => Path.Combine(LocalDataDir, "Backups");

    public async Task<(bool ok, string message, string? snapshotName)> ExportToOneDriveAsync()
    {
#if !WINDOWS
        return (false, "OneDrive export is currently supported on Windows only.", null);
#else
        try
        {
            Directory.CreateDirectory(ExportsDir);
            Directory.CreateDirectory(ArchiveDir);

            var localFiles = GetLocalCsvFiles();
            if (localFiles.Length == 0)
                return (false, $"No local CSV files found to export in: {LocalDataDir}", null);

            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var finalName = $"{AppFolderName}DB_{stamp}.zip";
            var tmpName = $"{finalName}.tmp";
            var readyName = $"{finalName}.ready";

            var tmpPath = Path.Combine(ExportsDir, tmpName);
            var finalPath = Path.Combine(ExportsDir, finalName);
            var readyPath = Path.Combine(ExportsDir, readyName);
            var latestPath = Path.Combine(ExportsDir, LatestPointerFileName);

            // Cleanup any leftovers from prior failed attempt
            SafeDelete(tmpPath);
            SafeDelete(finalPath);
            SafeDelete(readyPath);

            // Two-phase commit: write tmp zip first
            using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                foreach (var file in localFiles)
                {
                    var entryName = Path.GetFileName(file);
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }

            // Atomic-ish rename tmp -> final
            File.Move(tmpPath, finalPath);

            // Write ready marker LAST (signals import side that zip is complete)
            await File.WriteAllTextAsync(readyPath, DateTime.Now.ToString("O"));

            // Update pointer (optional but very convenient)
            await File.WriteAllTextAsync(latestPath, finalName);

            var msg =
                $"Exported {localFiles.Length} file(s) to OneDrive:\n{finalPath}\n\n" +
                "If importing on another PC, wait for OneDrive to finish syncing (OneDrive icon shows 'Up to date').";

            return (true, msg, finalName);
        }
        catch (Exception ex)
        {
            return (false, $"Export failed: {ex.Message}", null);
        }
#endif
    }

    public async Task<(bool ok, string message, string? snapshotName)> ImportFromOneDriveAsync()
    {
#if !WINDOWS
        return (false, "OneDrive import is currently supported on Windows only.", null);
#else
        try
        {
            Directory.CreateDirectory(ExportsDir);
            Directory.CreateDirectory(ArchiveDir);
            Directory.CreateDirectory(LocalDataDir);
            Directory.CreateDirectory(LocalBackupsDir);

            var (zipPath, readyPath, chosenName) = FindLatestExport();
            if (zipPath is null || readyPath is null || chosenName is null)
                return (false, $"No OneDrive exports found. Expected files in:\n{ExportsDir}", null);

            // Wait-until-safe loop (handles OneDrive sync delays)
            var stable = await WaitUntilStableAsync(zipPath, readyPath, timeoutSeconds: 60);
            if (!stable)
                return (false, "The latest export hasn't finished syncing yet. Please wait for OneDrive to be 'Up to date' and try again.", chosenName);

            // Backup current local CSVs first (zip)
            var localFiles = GetLocalCsvFiles();
            if (localFiles.Length > 0)
            {
                var backupStamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                var backupZip = Path.Combine(LocalBackupsDir, $"LocalBackup_{backupStamp}.zip");
                using (var fs = new FileStream(backupZip, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var file in localFiles)
                        zip.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                }
            }

            // Extract to temp folder, then replace local files
            var importTempDir = Path.Combine(LocalDataDir, "ImportingTmp");
            if (Directory.Exists(importTempDir))
                Directory.Delete(importTempDir, recursive: true);
            Directory.CreateDirectory(importTempDir);

            ZipFile.ExtractToDirectory(zipPath, importTempDir, overwriteFiles: true);

            var extracted = Directory.GetFiles(importTempDir, "*.csv", SearchOption.TopDirectoryOnly);
            if (extracted.Length == 0)
                return (false, $"Import file contained no CSVs: {zipPath}", chosenName);

            foreach (var src in extracted)
            {
                var dest = Path.Combine(LocalDataDir, Path.GetFileName(src));
                SafeReplaceFile(src, dest);
            }

            // Archive the imported zip (optional history)
            var archiveDest = Path.Combine(ArchiveDir, chosenName);
            TryCopyIfMissing(zipPath, archiveDest);

            // Cleanup temp
            Directory.Delete(importTempDir, recursive: true);

            var msg =
                $"Imported {extracted.Length} file(s) from OneDrive snapshot:\n{zipPath}\n\n" +
                $"Local data folder:\n{LocalDataDir}\n\n" +
                "Tip: If you already have a register/summary page open, navigate back and reopen it to reload the files.";

            return (true, msg, chosenName);
        }
        catch (Exception ex)
        {
            return (false, $"Import failed: {ex.Message}", null);
        }
#endif
    }

    public async Task<(bool ok, string? snapshotName, DateTime? snapshotWriteUtc, string message)> GetLatestSnapshotInfoAsync()
    {
#if !WINDOWS
        return (false, null, null, "OneDrive sync is currently supported on Windows only.");
#else
        try
        {
            Directory.CreateDirectory(ExportsDir);

            var (zipPath, readyPath, chosenName) = FindLatestExport();
            if (zipPath is null || readyPath is null || chosenName is null)
                return (false, null, null, $"No OneDrive exports found in:\n{ExportsDir}");

            // If OneDrive is still syncing, we might see the pointer but not have a stable file yet.
            if (!File.Exists(zipPath))
                return (false, chosenName, null, "Latest snapshot is referenced but not present locally yet (OneDrive still syncing).");

            var writeUtc = File.GetLastWriteTimeUtc(zipPath);
            return (true, chosenName, writeUtc, "OK");
        }
        catch (Exception ex)
        {
            return (false, null, null, $"Failed to read OneDrive snapshot info: {ex.Message}");
        }
#endif
    }

#if WINDOWS
    private static (string? zipPath, string? readyPath, string? chosenName) FindLatestExport()
    {
        // Option A: LATEST.txt pointer
        var latestPath = Path.Combine(ExportsDir, LatestPointerFileName);
        if (File.Exists(latestPath))
        {
            var name = File.ReadAllText(latestPath).Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                var zip = Path.Combine(ExportsDir, name);
                var ready = Path.Combine(ExportsDir, $"{name}.ready");
                if (File.Exists(zip) && File.Exists(ready))
                    return (zip, ready, name);
            }
        }

        // Option B: newest *.ready
        var readyFiles = Directory.GetFiles(ExportsDir, "*.ready", SearchOption.TopDirectoryOnly)
            .OrderByDescending(f => File.GetCreationTimeUtc(f))
            .ToList();

        foreach (var r in readyFiles)
        {
            var name = Path.GetFileNameWithoutExtension(r); // removes .ready
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var zip = Path.Combine(ExportsDir, name);
            if (File.Exists(zip))
                return (zip, r, name);
        }

        return (null, null, null);
    }

    private static async Task<bool> WaitUntilStableAsync(string zipPath, string readyPath, int timeoutSeconds)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            if (!File.Exists(readyPath) || !File.Exists(zipPath))
            {
                await Task.Delay(2000);
                continue;
            }

            var size1 = new FileInfo(zipPath).Length;
            await Task.Delay(500);
            var size2 = new FileInfo(zipPath).Length;

            if (size1 > 0 && size1 == size2)
                return true;

            await Task.Delay(1500);
        }

        return false;
    }
#endif

    private static void SafeReplaceFile(string src, string dest)
    {
        var tmp = dest + ".importing.tmp";
        SafeDelete(tmp);

        File.Copy(src, tmp, overwrite: true);

        var old = dest + ".old";
        SafeDelete(old);

        if (File.Exists(dest))
            File.Move(dest, old);

        File.Move(tmp, dest);
        SafeDelete(old); // backups are zipped already
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { /* ignore */ }
    }

    private static void TryCopyIfMissing(string src, string dest)
    {
        try
        {
            if (!File.Exists(dest))
                File.Copy(src, dest);
        }
        catch { /* ignore */ }
    }
}
