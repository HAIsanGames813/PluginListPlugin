using System;
using System.Globalization;
using System.Windows.Data;

namespace PluginList
{
    /// <summary>
    /// ラジオボタンの IsChecked と string プロパティを双方向バインドするコンバーター。
    /// ConverterParameter に対象文字列を指定する。
    /// </summary>
    public class StringEqualConverter : IValueConverter
    {
        public static readonly StringEqualConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && s == parameter?.ToString();

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is true) return parameter?.ToString() ?? string.Empty;
            return Binding.DoNothing;
        }
    }

    /// <summary>
    /// bool を反転するコンバーター（降順ラジオボタン用）。
    /// </summary>
    public class BoolInvertConverter : IValueConverter
    {
        public static readonly BoolInvertConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;
    }
}