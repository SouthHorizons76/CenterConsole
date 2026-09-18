using System.Globalization;
using System.Windows.Data;

namespace CenterConsole.App.Converters;

/// <summary>True (live) is fully opaque; false (a remembered but not-yet-active volume target) is
/// dimmed, signalling "still selected, just no audio session right now" instead of just disappearing.</summary>
public sealed class BoolToInactiveOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? 1.0 : 0.55;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
