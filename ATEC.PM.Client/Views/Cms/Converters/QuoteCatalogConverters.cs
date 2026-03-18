using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ATEC.PM.Client.Converters;

public class ItemTypeToBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string type = value?.ToString() ?? "product";
        return type == "content"
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ItemTypeToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string type = value?.ToString() ?? "product";
        return type == "content" ? "Cont." : "Prod.";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
