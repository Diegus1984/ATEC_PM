using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ATEC.PM.Client.Views.CashFlow;

/// <summary>Decimal negativo → rosso, positivo → verde scuro</summary>
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

/// <summary>Stringa colore hex → SolidColorBrush (per sfondo celle)</summary>
public class HexToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string hex = value?.ToString() ?? "";
        if (string.IsNullOrEmpty(hex)) return Brushes.White;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
        catch { return Brushes.White; }
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Bool warning % → rosso se true</summary>
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

/// <summary>CfRowType → sfondo riga (giallo calcolato, verde editabile, bianco default)</summary>
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

/// <summary>CfRowType == Separator → altezza ridotta</summary>
public class SeparatorHeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CfRowType rt && rt == CfRowType.Separator) return 8.0;
        return 28.0;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotImplementedException();
}

/// <summary>Riga DIFFERENZA con valore negativo → sfondo rosso chiaro</summary>
public class CumulativeRowBgConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is CfRowType rt && rt == CfRowType.Cumulative && values[1] is decimal d && d < 0)
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FEE2E2"));
        return Brushes.Transparent;
    }
    public object[] ConvertBack(object value, Type[] t, object p, CultureInfo c) => throw new NotImplementedException();
}


/// <summary>Valore cella → foreground. Negativo = rosso, altrimenti nero</summary>
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
/// <summary>Nasconde il valore 0 per righe separatore</summary>
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

/// <summary>Decimal → stringa senza decimali, solo numero intero</summary>
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