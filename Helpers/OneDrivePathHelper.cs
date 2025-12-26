using System;
using System.IO;
using System.Linq;

namespace KukiFinance.Helpers;

/// <summary>
/// Locates the user's OneDrive folder on Windows and builds a KukiFinance OneDrive directory.
///
/// IMPORTANT:
/// - This is intended for explicit export/import only.
/// - Live app data should remain in FileSystem.AppDataDirectory (LocalState).
///
/// Default OneDrive export/import folder:
///   <OneDriveRoot>\Documents\AppData\KukiFinance
/// </summary>
public static class OneDrivePathHelper
{
    public static string GetOneDriveKukiFinanceDirectory(bool createIfMissing = true)
    {
#if WINDOWS
        var root = TryGetOneDriveRoot()
            ?? throw new InvalidOperationException(
                "OneDrive folder not found on this PC. " +
                "Make sure OneDrive is installed and signed in, then try again.");

        var path = Path.Combine(root, "Documents", "AppData", "KukiFinance");

        if (createIfMissing)
            Directory.CreateDirectory(path);

        return path;
#else
        throw new PlatformNotSupportedException("OneDrive export/import is supported on Windows only.");
#endif
    }

#if WINDOWS
    private static string? TryGetOneDriveRoot()
    {
        // Common environment variables (Consumer / Commercial / Generic)
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("OneDrive"),
            Environment.GetEnvironmentVariable("OneDriveConsumer"),
            Environment.GetEnvironmentVariable("OneDriveCommercial")
        }
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Select(s => s!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var c in candidates)
            if (Directory.Exists(c))
                return c;

        // Fallback: look under the user profile for folders named "OneDrive" or "OneDrive - *"
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile) || !Directory.Exists(userProfile))
            return null;

        try
        {
            var dirs = Directory.GetDirectories(userProfile, "OneDrive*", SearchOption.TopDirectoryOnly)
                .OrderBy(d => d.Length) // prefer "OneDrive" over "OneDrive - Company"
                .ToList();

            foreach (var d in dirs)
                if (Directory.Exists(d))
                    return d;
        }
        catch
        {
            // ignore
        }

        return null;
    }
#endif
}
