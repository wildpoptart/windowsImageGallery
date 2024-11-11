using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FastImageGallery
{
    public class ScaleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double originalValue && parameter is string multiplierString)
            {
                if (double.TryParse(multiplierString, out double multiplier))
                {
                    return originalValue * multiplier;
                }
            }
            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 