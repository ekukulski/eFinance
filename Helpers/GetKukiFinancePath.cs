using System;
using System.IO;

namespace KukiFinance.Helpers;

public static class FilePathHelper
{
    /// <summary>
    /// Returns a stable per-user app data path for KukiFinance files and ensures the directory exists.
    /// </summary>
    public static string GetKukiFinancePath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("File name must be provided.", nameof(fileName));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var kukiDir = Path.Combine(appData, "KukiFinance");

        Directory.CreateDirectory(kukiDir);

        return Path.Combine(kukiDir, fileName);
    }
}
