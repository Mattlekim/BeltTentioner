using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BeltTentionerWPF.Converters
{
    public class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b)
                ? new SolidColorBrush(Color.FromRgb(80, 200, 80))
                : new SolidColorBrush(Color.FromRgb(200, 80, 80));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => DependencyProperty.UnsetValue;
    }

    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => DependencyProperty.UnsetValue;
    }

    public class ActiveButtonBorderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b)
                ? new SolidColorBrush(Colors.White)
                : new SolidColorBrush(Color.FromRgb(70, 70, 100));

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => DependencyProperty.UnsetValue;
    }

    public class FloatFormatConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is float f ? f.ToString("F1") : value?.ToString() ?? "";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && float.TryParse(s, out float f)) return f;
            return DependencyProperty.UnsetValue;
        }
    }
}
