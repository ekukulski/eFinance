using System;
using System.IO;
using System.Linq;

#if WINDOWS
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace KukiFinance.Helpers;

/// <summary>
/// Centralized data-path logic for KukiFinance "database" files (CSV, etc.).
///
/// Updated Goal:
/// - On Windows: use OneDrive as the primary live datastore so data automatically syncs across PCs.
///   Path: OneDrive\Documents\AppData\KukiFinance
/// - If OneDrive is unavailable, fall back to a shared local folder (ProgramData\KukiFinance) so
///   multiple Windows user accounts on the same PC can use the same local data.
/// - On other platforms: fall back to per-user app data.
/// </summary>
public static class FilePathHelper
{
    private const string AppFolderName = "KukiFinance";

    /// <summary>
    /// Returns the base directory for KukiFinance data files and ensures it exists.
    /// On Windows, prefers OneDrive for automatic sync across PCs.
    /// </summary>
    public static string GetKukiFinanceDirectory()
    {
#if WINDOWS
        // 1) Preferred: OneDrive (automatic sync across PCs)
        //    OneDrive\Documents\AppData\KukiFinance
        try
        {
            var oneDriveDir = OneDrivePathHelper.GetOneDriveKukiFinanceDirectory(createIfMissing: true);

            // One-time migration: if OneDrive folder is empty, copy from local stores.
            TryMigrateLocalDataToOneDrive(oneDriveDir);

            return oneDriveDir;
        }
        catch
        {
            // If OneDrive isn't available (not installed/signed-in), fall back to shared local data.
        }

        // 2) Fallback: shared across all Windows users:
        //    C:\ProgramData\KukiFinance
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var kukiDir = Path.Combine(baseDir, AppFolderName);

        EnsureDirectoryExistsWithUserWriteAccess(kukiDir);
        return kukiDir;
#else
        // Per-user on non-Windows platforms
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var kukiDir = Path.Combine(baseDir, AppFolderName);

        Directory.CreateDirectory(kukiDir);
        return kukiDir;
#endif
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

#if WINDOWS
    /// <summary>
    /// One-time migration into OneDrive if OneDrive folder is empty.
    /// Sources checked (in order):
    /// 1) ProgramData\KukiFinance (shared local)
    /// 2) LocalAppData\KukiFinance (older per-user location)
    /// </summary>
    private static void TryMigrateLocalDataToOneDrive(string oneDriveDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(oneDriveDir) || !Directory.Exists(oneDriveDir))
                return;

            // Only migrate if OneDrive folder has no files yet (prevents overwriting synced data)
            bool oneDriveHasAnyFiles =
                Directory.EnumerateFiles(oneDriveDir, "*", SearchOption.TopDirectoryOnly).Any();

            if (oneDriveHasAnyFiles)
                return;

            var sources = new[]
            {
                // Shared local store (from earlier approach)
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    AppFolderName),

                // Old per-user store (original app behavior)
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    AppFolderName)
            };

            foreach (var src in sources)
            {
                if (!Directory.Exists(src))
                    continue;

                var files = Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly).ToList();
                if (files.Count == 0)
                    continue;

                Directory.CreateDirectory(oneDriveDir);

                foreach (var file in files)
                {
                    var dest = Path.Combine(oneDriveDir, Path.GetFileName(file));
                    File.Copy(file, dest, overwrite: false);
                }

                // migrated from the first valid source; stop.
                break;
            }
        }
        catch
        {
            // ignore migration issues; app still functions using OneDrive folder
        }
    }
#endif

    private static void EnsureDirectoryExistsWithUserWriteAccess(string path)
    {
        Directory.CreateDirectory(path);

#if WINDOWS
        try
        {
            // Make sure all standard users can read/write the shared data directory.
            var di = new DirectoryInfo(path);
            var ds = di.GetAccessControl();

            var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var rule = new FileSystemAccessRule(
                usersSid,
                FileSystemRights.ReadAndExecute |
                FileSystemRights.ListDirectory |
                FileSystemRights.Read |
                FileSystemRights.Write |
                FileSystemRights.Modify |
                FileSystemRights.CreateFiles |
                FileSystemRights.CreateDirectories |
                FileSystemRights.Delete,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            bool modified = false;
            ds.ModifyAccessRule(AccessControlModification.Add, rule, out modified);

            if (modified)
                di.SetAccessControl(ds);
        }
        catch
        {
            // If ACL update fails, directory still exists; the app may be limited by OS policy.
            // Swallow to avoid blocking app startup.
        }
#endif
    }
}
