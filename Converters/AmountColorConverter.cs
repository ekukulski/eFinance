using System.Globalization;
using Microsoft.Maui.Controls;

namespace eFinance.Converters;

public class AmountColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is decimal amount)
            return amount < 0 ? Colors.Red : Colors.Black;
        return Colors.Black;
    }

    public static Color GetColor(decimal amount)
    {
        // Example logic: negative = red, positive = green, zero = black
        if (amount < 0) return Colors.Red;
        if (amount > 0) return Colors.Green;
        return Colors.Black;
    }
    
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) 
        => throw new NotImplementedException();
}