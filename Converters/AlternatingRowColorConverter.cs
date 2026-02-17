using System;
using System.Globalization;
using Microsoft.Maui.Controls;
using System.Collections;

namespace eFinance.Converters
{
    public class AlternatingRowColorConverter : IMultiValueConverter
    {
        public Color EvenColor { get; set; } = Colors.PaleGreen;
        public Color OddColor { get; set; } = Colors.PaleGoldenrod;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 2 && values[0] != null && values[1] is IList items)
            {
                int index = items.IndexOf(values[0]);
                return index % 2 == 0 ? EvenColor : OddColor;
            }
            return EvenColor;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}