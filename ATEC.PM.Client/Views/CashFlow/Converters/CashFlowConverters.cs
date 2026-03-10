using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ATEC.PM.Client.Views.CashFlow;

public class NegativeToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
            return d < 0
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1D26"));
        return new SolidColorBrush(Colors.Black);
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class RowTypeToBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string color && !string.IsNullOrEmpty(color))
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); }
            catch { }
        }
        return Brushes.White;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class CellForegroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d && d < 0)
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1D26"));
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class SeparatorValueConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[1] is bool isSep && isSep)
            return "";
        if (values[0] is decimal d)
            return d.ToString("N0");
        return "0";
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
}

public class IntegerAmountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
            return ((int)d).ToString();
        return "0";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string raw = value?.ToString()?.Replace(".", "").Replace(",", "").Replace("€", "").Replace(" ", "").Trim() ?? "0";
        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            return result;
        return 0m;
    }
}

public class InvertBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class BoolToRedBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}
