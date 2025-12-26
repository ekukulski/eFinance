using System;
using System.IO;
using System.Linq;
using Microsoft.Maui.Storage;

namespace KukiFinance.Helpers;

/// <summary>
/// Centralized data-path logic for KukiFinance "database" files (CSV, etc.).
///
/// Goal (MAUI-correct):
/// - All live data stays in the app's per-user sandbox (FileSystem.AppDataDirectory).
///   On Windows this maps under:
///     C:\Users\<User>\AppData\Local\Packages\<AppId>\LocalState
/// - OneDrive is used ONLY for explicit export/import actions (not as the live datastore).
///
/// Notes:
/// - To preserve existing users' data, a one-time migration runs if the new folder is empty.
///   It copies (non-overwriting) files from older locations:
///     1) OneDrive\Documents\AppData\KukiFinance (if present)
///     2) C:\ProgramData\KukiFinance
///     3) C:\Users\<User>\AppData\Local\KukiFinance
/// </summary>
public static class FilePathHelper
{
    private const string AppFolderName = "KukiFinance";

    /// <summary>
    /// Returns the base directory for KukiFinance data files and ensures it exists.
    /// This is always a per-user, app-sandboxed directory.
    /// </summary>
    public static string GetKukiFinanceDirectory()
    {
        var kukiDir = Path.Combine(FileSystem.AppDataDirectory, AppFolderName);
        Directory.CreateDirectory(kukiDir);

        TryMigrateLegacyDataIntoLocalState(kukiDir);

        return kukiDir;
    }

    /// <summary>
    /// Returns a stable path for a given KukiFinance file name.
    /// </summary>
    public static string GetKukiFinancePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name must be provided.", nameof(fileName));

        return Path.Combine(GetKukiFinanceDirectory(), fileName);
    }

    /// <summary>
    /// If the new LocalState folder has no files yet, copy legacy files into it (non-overwriting).
    /// </summary>
    private static void TryMigrateLegacyDataIntoLocalState(string localStateDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(localStateDir) || !Directory.Exists(localStateDir))
                return;

            bool localStateHasAnyFiles =
                Directory.EnumerateFiles(localStateDir, "*", SearchOption.TopDirectoryOnly).Any();

            // Only migrate into a brand-new/empty store to avoid overwriting user's current data.
            if (localStateHasAnyFiles)
                return;

            var sources = new System.Collections.Generic.List<string>();

#if WINDOWS
            // Legacy OneDrive location used by older versions (if it exists)
            try
            {
                var oneDriveDir = OneDrivePathHelper.GetOneDriveKukiFinanceDirectory(createIfMissing: false);
                if (!string.IsNullOrWhiteSpace(oneDriveDir))
                    sources.Add(oneDriveDir);
            }
            catch
            {
                // ignore if OneDrive isn't available
            }

            // Legacy shared-machine store
            sources.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppFolderName));

            // Legacy per-user store (non-packaged path)
            sources.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName));
#else
            // Non-Windows: prior versions may have used LocalApplicationData\KukiFinance
            sources.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppFolderName));
#endif

            foreach (var src in sources.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(src) || !Directory.Exists(src))
                    continue;

                var files = Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly).ToList();
                if (files.Count == 0)
                    continue;

                foreach (var file in files)
                {
                    var dest = Path.Combine(localStateDir, Path.GetFileName(file));

                    // Don't overwrite: if the user already has a file in LocalState, keep it.
                    if (File.Exists(dest))
                        continue;

                    File.Copy(file, dest, overwrite: false);
                }

                // migrated from the first valid source; stop
                break;
            }
        }
        catch
        {
            // Never block app startup on migration; user can still proceed and import/export manually.
        }
    }
}
