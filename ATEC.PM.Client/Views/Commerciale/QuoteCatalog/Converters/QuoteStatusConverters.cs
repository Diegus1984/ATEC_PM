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
            "draft"       => "#F3F4F6",   // grigio chiaro
            "sent"        => "#DBEAFE",   // azzurro chiaro
            "negotiation" => "#FEF3C7",   // giallo chiaro
            "accepted"    => "#D1FAE5",   // verde chiaro
            "rejected"    => "#FEE2E2",   // rosso chiaro
            "expired"     => "#F3F4F6",   // grigio chiaro
            "converted"   => "#DCFCE7",   // verde lime chiaro
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
            "draft"       => "#6B7280",   // grigio
            "sent"        => "#1D4ED8",   // blu
            "negotiation" => "#D97706",   // arancione
            "accepted"    => "#059669",   // verde
            "rejected"    => "#DC2626",   // rosso
            "expired"     => "#6B7280",   // grigio
            "converted"   => "#16A34A",   // verde scuro
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
            "draft"       => "In preparazione",
            "sent"        => "Preventivo inviato",
            "negotiation" => "In trattativa",
            "accepted"    => "Il cliente ha confermato",
            "rejected"    => "Cliente non interessato",
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
