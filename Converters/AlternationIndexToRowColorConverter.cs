using System.Globalization;

namespace eFinance.Converters;

public sealed class AlternationIndexToRowColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var idx = value is int i ? i : 0;

        var even = GetThemeColor("RowEvenLight", "RowEvenDark",
            fallbackLight: Color.FromArgb("#FFFFFF"),
            fallbackDark: Color.FromArgb("#1F1F1F"));

        var odd = GetThemeColor("RowOddLight", "RowOddDark",
            fallbackLight: Color.FromArgb("#F7F7F7"),
            fallbackDark: Color.FromArgb("#252525"));

        return (idx % 2 == 0) ? even : odd;
    }

    private static Color GetThemeColor(string lightKey, string darkKey, Color fallbackLight, Color fallbackDark)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var key = isDark == true ? darkKey : lightKey;

        if (Application.Current?.Resources != null &&
            Application.Current.Resources.TryGetValue(key, out var obj) &&
            obj is Color c)
            return c;

        return isDark == true ? fallbackDark : fallbackLight;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}