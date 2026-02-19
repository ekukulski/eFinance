using System.Globalization;
using Microsoft.Maui.Controls;

namespace eFinance.Converters;

public class AmountColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal amount)
            return GetColor(amount);

        return GetThemeColor("MoneyZeroLight", "MoneyZeroDark", Colors.Black, Colors.White);
    }

    public static Color GetColor(decimal amount)
    {
        // Theme-aware: negative / positive / zero
        if (amount < 0)
            return GetThemeColor("MoneyNegativeLight", "MoneyNegativeDark", Colors.Red, Colors.Red);
        if (amount > 0)
            return GetThemeColor("MoneyPositiveLight", "MoneyPositiveDark", Colors.Green, Colors.Green);

        return GetThemeColor("MoneyZeroLight", "MoneyZeroDark", Colors.Black, Colors.White);
    }

    private static Color GetThemeColor(string lightKey, string darkKey, Color fallbackLight, Color fallbackDark)
    {
        try
        {
            var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
            var key = isDark ? darkKey : lightKey;

            if (Application.Current?.Resources != null && Application.Current.Resources.TryGetValue(key, out var obj) && obj is Color c)
                return c;
        }
        catch
        {
            // ignore
        }

        return (Application.Current?.RequestedTheme == AppTheme.Dark) ? fallbackDark : fallbackLight;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) 
        => throw new NotImplementedException();
}