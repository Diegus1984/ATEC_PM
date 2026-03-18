using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ATEC.PM.Client.Converters;

public class QuoteStatusToBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string status = value?.ToString() ?? "";
        string color = status switch
        {
            "draft"       => "#F3F4F6",
            "sent"        => "#DBEAFE",
            "negotiation" => "#FEF3C7",
            "accepted"    => "#D1FAE5",
            "rejected"    => "#FEE2E2",
            "expired"     => "#F3F4F6",
            "converted"   => "#D1FAE5",
            _ => "#F3F4F6"
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class QuoteStatusToFgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string status = value?.ToString() ?? "";
        string color = status switch
        {
            "draft"       => "#374151",
            "sent"        => "#1D4ED8",
            "negotiation" => "#D97706",
            "accepted"    => "#059669",
            "rejected"    => "#DC2626",
            "expired"     => "#6B7280",
            "converted"   => "#059669",
            _ => "#374151"
        };
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class QuoteStatusToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value?.ToString() ?? "") switch
        {
            "draft"       => "Bozza",
            "sent"        => "Inviato",
            "negotiation" => "In trattativa",
            "accepted"    => "Accettato",
            "rejected"    => "Rifiutato",
            "expired"     => "Scaduto",
            "converted"   => "Convertito",
            _ => value?.ToString() ?? ""
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

public class QuoteItemTypeToBgConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string type = value?.ToString() ?? "product";
        return type == "content"
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2563EB"));
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
