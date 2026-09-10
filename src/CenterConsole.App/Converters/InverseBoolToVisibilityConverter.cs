using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CenterConsole.App.Converters;

/// <summary>True (e.g. IsElevated) collapses the bound element; false shows it. Used for warning banners.</summary>
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
