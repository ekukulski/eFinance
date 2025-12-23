using System.Globalization;
using Microsoft.Maui.Controls;

namespace KukiFinance.Converters;

public class AmountFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal amount)
            return amount < 0 ? $"(${Math.Abs(amount):N2})" : $"${amount:N2}";
        return value;
    }

    public static string Format(decimal amount)
    {
        // Example: show negative as ($123.45), positive as $123.45
        if (amount < 0) return $"(${Math.Abs(amount):N2})";
        return $"${amount:N2}";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}