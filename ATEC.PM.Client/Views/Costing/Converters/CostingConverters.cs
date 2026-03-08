using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ATEC.PM.Client.Views.Costing.Converters;

/// <summary>
/// Stringa colore hex → SolidColorBrush
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string hex = value?.ToString() ?? "#6B7280";
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return new SolidColorBrush(Colors.Gray); }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Stringa colore hex → SolidColorBrush con alpha (per sfondo badge)
/// Parametro = alpha hex, es. "20" → aggiunge 20 come alpha
/// </summary>
public class HexToAlphaBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string hex = value?.ToString() ?? "#6B7280";
        string alpha = parameter?.ToString() ?? "20";
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex + alpha)); }
        catch { return new SolidColorBrush(Colors.LightGray); }
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Decimal → stringa formattata euro. Parametro opzionale: formato (default "N2")
/// </summary>
public class EuroCurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
        {
            string fmt = parameter?.ToString() ?? "N2";
            return $"{d.ToString(fmt, CultureInfo.InvariantCulture)} €";
        }
        return "0.00 €";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Bool IsDetailExpanded → angolo freccia (0 o 90)
/// </summary>
public class BoolToAngleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? 90.0 : 0.0;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// SectionType "DA_CLIENTE" → Visibility.Visible, altrimenti Collapsed
/// </summary>
public class DaClienteToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() == "DA_CLIENTE" ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Decimal markup → stringa "F3" con InvariantCulture (per TextBox K)
/// </summary>
public class MarkupToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal d)
            return d.ToString("F3", CultureInfo.InvariantCulture);
        return "1.450";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (decimal.TryParse(value?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            return result;
        return 1.450m;
    }
}
