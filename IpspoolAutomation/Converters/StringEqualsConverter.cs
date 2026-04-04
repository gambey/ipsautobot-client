using System.Globalization;
using System.Windows.Data;

namespace IpspoolAutomation.Converters;

/// <summary>RadioButton: IsChecked when bound string equals ConverterParameter (string).</summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var a = value?.ToString() ?? "";
        var b = parameter?.ToString() ?? "";
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter != null)
            return parameter.ToString();
        return Binding.DoNothing;
    }
}
