using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ATEC.PM.Client.Converters;

/// <summary>
/// Converte uno status stringa in etichetta leggibile.
/// </summary>
public class StatusToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value?.ToString() ?? "") switch
        {
            "ACTIVE"       => "Attivo",
            "ON_LEAVE"     => "In ferie",
            "SICK"         => "Malattia",
            "TERMINATED"   => "Terminato",
            "DRAFT"        => "Bozza",
            "OPEN"         => "Aperta",
            "IN_PROGRESS"  => "In corso",
            "COMPLETED"    => "Completata",
            "CANCELLED"    => "Annullata",
            "NOT_STARTED"  => "Non iniziata",
            "INTERNAL"     => "Interno",
            "EXTERNAL"     => "Esterno",
            "ADMIN"        => "Amministratore",
            "PM"           => "Project Manager",
            "RESP_REPARTO" => "Resp. Reparto",
            "TECH"         => "Tecnico",
            var s          => s
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Bool → Visibility. Parametro "invert" per invertire.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool val = value is bool b && b;
        if (parameter?.ToString() == "invert") val = !val;
        return val ? Visibility.Visible : Visibility.Collapsed;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>
/// Bool → Opacity (1.0 / 0.4).
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b && b ? 1.0 : 0.4;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Stringa vuota/null → Collapsed, con valore → Visible.
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
