using System.Globalization;
using System.Windows.Data;

namespace IpspoolAutomation.Converters;

/// <summary>多路绑定：当前页名称与控件 Tag 字符串相等时返回 true（用于侧栏导航选中态）。</summary>
public sealed class NavPageSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2)
            return false;
        var a = values[0]?.ToString() ?? "";
        var b = values[1]?.ToString() ?? "";
        return string.Equals(a, b, StringComparison.Ordinal);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
