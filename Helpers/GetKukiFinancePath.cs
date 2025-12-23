public static class FilePathHelper
{
    public static string GetKukiFinancePath(string fileName)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var kukiDir = Path.Combine(appData, "KukiFinance");
        if (!Directory.Exists(kukiDir))
            Directory.CreateDirectory(kukiDir);
        return Path.Combine(kukiDir, fileName);
    }
}