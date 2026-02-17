using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using eFinance.Helpers;
using Microsoft.Maui.Storage;

namespace eFinance.Services
{
    public sealed class CloudSyncService : ICloudSyncService
    {
        private const string AppFolderName = "eFinance";
        private const string LatestPointerFileName = "LATEST.txt";

        /// <summary>
        /// IMPORTANT: Use the same local folder your pages read from.
        /// ExpensePage loads CSVs via FilePathHelper.GeteFinancePath("MidlandCurrent.csv"), etc.
        /// So we derive the directory from that helper.
        /// </summary>
        private static string LocalDataDir
        {
            get
            {
                // Pick any known file name from your list; we only need the directory.
                var samplePath = FilePathHelper.GeteFinancePath("MidlandCurrent.csv");
                return Path.GetDirectoryName(samplePath) ?? FileSystem.AppDataDirectory;
            }
        }

        private static string LocalBackupsDir => Path.Combine(LocalDataDir, "Backups");

#if WINDOWS
        private static bool TryGetCloudSyncBaseDir(bool createIfMissing, out string baseDir, out string message)
        {
            baseDir = string.Empty;
            message = string.Empty;

            try
            {
                baseDir = CloudSyncPathHelper.GetCloudSynceFinanceDirectory(createIfMissing: createIfMissing);

                if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir))
                {
                    message =
                        "Proton Drive folder not found on this PC.\n\n" +
                        "Please make sure Proton Drive is installed and syncing, then try again.\n\n" +
                        "You can still use the app locally.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                message = "Unable to determine Proton Drive availability: " + ex.Message;
                return false;
            }
        }

        private static string ExportsDir(string baseDir) => Path.Combine(baseDir, "Exports");
        private static string ArchiveDir(string baseDir) => Path.Combine(baseDir, "Archive");

        private static string[] GetLocalCsvFiles()
            => Directory.Exists(LocalDataDir)
                ? Directory.GetFiles(LocalDataDir, "*.csv", SearchOption.TopDirectoryOnly)
                : Array.Empty<string>();

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

        private static (string? zipPath, string? readyPath, string? chosenName) FindLatestExport(string exportsDir)
        {
            // Option A: LATEST.txt pointer
            var latestPath = Path.Combine(exportsDir, LatestPointerFileName);
            if (File.Exists(latestPath))
            {
                var name = File.ReadAllText(latestPath).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    var zip = Path.Combine(exportsDir, name);
                    var ready = Path.Combine(exportsDir, $"{name}.ready");
                    if (File.Exists(zip) && File.Exists(ready))
                        return (zip, ready, name);
                }
            }

            // Option B: newest *.ready
            var readyFiles = Directory.GetFiles(exportsDir, "*.ready", SearchOption.TopDirectoryOnly)
                .OrderByDescending(f => File.GetCreationTimeUtc(f))
                .ToList();

            foreach (var r in readyFiles)
            {
                var name = Path.GetFileNameWithoutExtension(r); // removes .ready
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                var zip = Path.Combine(exportsDir, name);
                if (File.Exists(zip))
                    return (zip, r, name);
            }

            return (null, null, null);
        }
#endif

        public async Task<(bool ok, string message, string? snapshotName)> ExportToCloudAsync()
        {
#if !WINDOWS
            return (false, "Cloud sync export is currently supported on Windows only.", null);
#else
            try
            {
                // ✅ Proton Drive availability guard
                if (!TryGetCloudSyncBaseDir(createIfMissing: true, out var baseDir, out var cloudMsg))
                    return (false, cloudMsg, null);

                var exportsDir = ExportsDir(baseDir);
                var archiveDir = ArchiveDir(baseDir);

                Directory.CreateDirectory(exportsDir);
                Directory.CreateDirectory(archiveDir);
                Directory.CreateDirectory(LocalDataDir);

                var localFiles = GetLocalCsvFiles();
                if (localFiles.Length == 0)
                    return (false, $"No local CSV files found to export in:\n{LocalDataDir}", null);

                var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                var finalName = $"{AppFolderName}DB_{stamp}.zip";
                var tmpName = $"{finalName}.tmp";
                var readyName = $"{finalName}.ready";

                var tmpPath = Path.Combine(exportsDir, tmpName);
                var finalPath = Path.Combine(exportsDir, finalName);
                var readyPath = Path.Combine(exportsDir, readyName);
                var latestPath = Path.Combine(exportsDir, LatestPointerFileName);

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
                    $"Exported {localFiles.Length} file(s) to Proton Drive:\n{finalPath}\n\n" +
                    "If importing on another PC, wait for Proton Drive to finish syncing.";

                return (true, msg, finalName);
            }
            catch (Exception ex)
            {
                return (false, $"Export failed: {ex.Message}", null);
            }
#endif
        }

        public async Task<(bool ok, string message, string? snapshotName)> ImportFromCloudAsync()
        {
#if !WINDOWS
            return (false, "Cloud sync import is currently supported on Windows only.", null);
#else
            try
            {
                // ✅ Proton Drive availability guard
                if (!TryGetCloudSyncBaseDir(createIfMissing: false, out var baseDir, out var cloudMsg))
                    return (false, cloudMsg, null);

                var exportsDir = ExportsDir(baseDir);
                var archiveDir = ArchiveDir(baseDir);

                Directory.CreateDirectory(exportsDir);
                Directory.CreateDirectory(archiveDir);
                Directory.CreateDirectory(LocalDataDir);
                Directory.CreateDirectory(LocalBackupsDir);

                var (zipPath, readyPath, chosenName) = FindLatestExport(exportsDir);
                if (zipPath is null || readyPath is null || chosenName is null)
                    return (false, $"No Proton Drive exports found. Expected files in:\n{exportsDir}", null);

                // Wait-until-safe loop (handles Proton Drive sync delays)
                var stable = await WaitUntilStableAsync(zipPath, readyPath, timeoutSeconds: 60);
                if (!stable)
                    return (false, "The latest export hasn't finished syncing yet. Please wait for Proton Drive to sync and try again.", chosenName);

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
                    return (false, $"Import file contained no CSVs:\n{zipPath}", chosenName);

                foreach (var src in extracted)
                {
                    var dest = Path.Combine(LocalDataDir, Path.GetFileName(src));
                    SafeReplaceFile(src, dest);
                }

                // Archive the imported zip (optional history)
                var archiveDest = Path.Combine(archiveDir, chosenName);
                TryCopyIfMissing(zipPath, archiveDest);

                // Cleanup temp
                Directory.Delete(importTempDir, recursive: true);

                var msg =
                    $"Imported {extracted.Length} file(s) from Proton Drive snapshot:\n{zipPath}\n\n" +
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
            return (false, null, null, "Cloud sync is currently supported on Windows only.");
#else
            try
            {
                // ✅ Proton Drive availability guard
                if (!TryGetCloudSyncBaseDir(createIfMissing: false, out var baseDir, out var cloudMsg))
                    return (false, null, null, cloudMsg);

                var exportsDir = ExportsDir(baseDir);
                Directory.CreateDirectory(exportsDir);

                var (zipPath, readyPath, chosenName) = FindLatestExport(exportsDir);
                if (zipPath is null || readyPath is null || chosenName is null)
                    return (false, null, null, $"No Proton Drive exports found in:\n{exportsDir}");

                if (!File.Exists(zipPath))
                    return (false, chosenName, null, "Latest snapshot is referenced but not present locally yet (Proton Drive still syncing).");

                var writeUtc = File.GetLastWriteTimeUtc(zipPath);
                return (true, chosenName, writeUtc, "OK");
            }
            catch (Exception ex)
            {
                return (false, null, null, $"Failed to read Proton Drive snapshot info: {ex.Message}");
            }
#endif
        }
    }
}