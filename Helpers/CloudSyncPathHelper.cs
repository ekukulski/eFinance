using System;
using System.IO;

namespace eFinance.Helpers;

/// <summary>
/// Locates the Proton Drive folder on Windows and builds a eFinance cloud sync directory.
///
/// IMPORTANT:
/// - This is intended for explicit export/import only.
/// - Live app data should remain in FileSystem.AppDataDirectory (LocalState).
///
/// Default Proton Drive export/import folder:
///   C:\Users\<USERNAME>\Proton Drive\ekukulski\My files\Data\eFinance
/// </summary>
public static class CloudSyncPathHelper
{
    public static string GetCloudSynceFinanceDirectory(bool createIfMissing = true)
    {
#if WINDOWS
        var root = TryGetProtonDriveRoot()
            ?? throw new InvalidOperationException(
                "Proton Drive folder not found on this PC. " +
                "Make sure Proton Drive is installed and syncing, then try again.");

        var path = Path.Combine(root, "ekukulski", "My files", "Data", "eFinance");

        if (createIfMissing)
            Directory.CreateDirectory(path);

        return path;
#else
        throw new PlatformNotSupportedException("Cloud sync export/import is supported on Windows only.");
#endif
    }

#if WINDOWS
    private static string? TryGetProtonDriveRoot()
    {
        // Proton Drive is typically installed at: C:\Users\<USERNAME>\Proton Drive
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile) || !Directory.Exists(userProfile))
            return null;

        var protonDrivePath = Path.Combine(userProfile, "Proton Drive");
        
        if (Directory.Exists(protonDrivePath))
            return protonDrivePath;

        return null;
    }
#endif
}