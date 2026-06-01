using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TumblThree.Presentation.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = ConverterHelper.IsParameterSet("invert", parameter);
            bool includeEmpty = ConverterHelper.IsParameterSet("includeEmpty", parameter);
            bool invertIncludeEmpty = ConverterHelper.IsParameterSet("invertIncludeEmpty", parameter);
            if (invertIncludeEmpty) { invert = includeEmpty = true; }

            return invert
                ? value == null || (includeEmpty && value is string s1 && s1.Length == 0) ? Visibility.Visible : Visibility.Collapsed
                : value == null || (includeEmpty && value is string s2 && s2.Length == 0) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
