using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Counsel
{
    public class ColorShadeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush && parameter is string factor)
            {
                double factorValue = System.Convert.ToDouble(factor);
                Color originalColor = brush.Color;

                byte r = (byte)(originalColor.R * factorValue);
                byte g = (byte)(originalColor.G * factorValue);
                byte b = (byte)(originalColor.B * factorValue);

                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}