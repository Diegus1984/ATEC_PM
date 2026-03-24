using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

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
            "ACTIVE" => "Attivo",
            "ON_LEAVE" => "In ferie",
            "SICK" => "Malattia",
            "TERMINATED" => "Terminato",
            "DRAFT" => "Bozza",
            "OPEN" => "Aperta",
            "IN_PROGRESS" => "In corso",
            "COMPLETED" => "Completata",
            "CANCELLED" => "Annullata",
            "NOT_STARTED" => "Non iniziata",
            "INTERNAL" => "Interno",
            "EXTERNAL" => "Esterno",
            "ADMIN" => "Amministratore",
            "PM" => "Project Manager",
            "RESP_REPARTO" => "Resp. Reparto",
            "TECH" => "Tecnico",
            var s => s
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

/// <summary>
/// Formattazione codice in ingresso da codex per gestione codex e ddp meccanica
/// </summary>
public class CodiceFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string s = value?.ToString() ?? "";
        if (s.Length > 3)
            return s[..^3] + "." + s[^3..];
        return s;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converte esadecimale in SolidColorBrush
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                Color color = (Color)ColorConverter.ConvertFromString($"#{hex}");
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.Gray);
            }
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converte esadecimale in SolidColorBrush con alpha (trasparenza)
/// </summary>
public class HexToAlphaBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex)
        {
            try
            {
                hex = hex.TrimStart('#');
                byte alpha = 255; // Default opaco

                if (parameter != null && byte.TryParse(parameter.ToString(), out byte alphaParam))
                {
                    alpha = alphaParam;
                }

                Color color = (Color)ColorConverter.ConvertFromString($"#{hex}");
                color = Color.FromArgb(alpha, color.R, color.G, color.B);
                return new SolidColorBrush(color);
            }
            catch
            {
                return new SolidColorBrush(Colors.LightGray);
            }
        }
        return new SolidColorBrush(Colors.LightGray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converte bool in angolo per rotazione (0° per true, 90° per false)
/// </summary>
public class BoolToAngleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? 90 : 0;
        }
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converte bool in Visibility con logica "Da Cliente"
/// </summary>
public class DaClienteToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isDaCliente)
        {
            return isDaCliente ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converte markup decimale in stringa formattata
/// </summary>
public class MarkupToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal markup)
        {
            return markup.ToString("F3", CultureInfo.InvariantCulture);
        }
        return "0.000";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str && decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
        {
            return result;
        }
        return 0m;
    }
}

/// <summary>
/// Converte percentuale decimale in stringa formattata (es: 0.25 → "25%")
/// </summary>
public class PctToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal pct)
        {
            // Se è in formato decimale (0.25), converti in percentuale (25%)
            if (pct <= 1)
            {
                return (pct * 100).ToString("F1") + "%";
            }
            return pct.ToString("F1") + "%";
        }
        return "0%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string str)
        {
            string clean = str.Replace("%", "").Replace(",", ".").Trim();
            if (decimal.TryParse(clean, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
            {
                // Se il valore è > 1, presumibilmente è già in percentuale (25 → 0.25)
                if (result > 1)
                {
                    return result / 100;
                }
                return result;
            }
        }
        return 0m;
    }
}

/// <summary>
/// Converte tipo item in badge text
/// </summary>
public class ItemTypeToBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value?.ToString() ?? "").ToUpper() switch
        {
            "MATERIAL" => "M",
            "COMMISSION" => "P",
            "SERVICE" => "S",
            _ => "?",
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converte bool in colore (per risorse generiche vs normali)
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isGeneric && isGeneric)
        {
            // Se è una risorsa generica, colore più chiaro
            return new SolidColorBrush(Color.FromRgb(156, 163, 175)); // #9CA3AF
        }

        // Risorsa normale
        return new SolidColorBrush(Color.FromRgb(26, 29, 38)); // #1A1D26
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converte bool in peso del font (per risorse generiche vs normali)
/// </summary>
public class BoolToWeightConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isGeneric && isGeneric)
        {
            return FontWeights.Normal;
        }

        return FontWeights.SemiBold;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converte tipo item in colore badge
/// </summary>
public class ItemTypeToBadgeColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string itemType)
        {
            int alpha = 255;
            bool returnWithAlpha = parameter != null && int.TryParse(parameter.ToString(), out alpha);

            switch (itemType.ToUpper())
            {
                case "MATERIAL":
                    return returnWithAlpha
                        ? new SolidColorBrush(Color.FromArgb((byte)alpha, 79, 110, 247)) // #4F6EF7 con alpha
                        : new SolidColorBrush(Color.FromRgb(79, 110, 247)); // #4F6EF7

                case "COMMISSION":
                    return returnWithAlpha
                        ? new SolidColorBrush(Color.FromArgb((byte)alpha, 124, 58, 237)) // #7C3AED con alpha
                        : new SolidColorBrush(Color.FromRgb(124, 58, 237)); // #7C3AED

                case "SERVICE":
                    return returnWithAlpha
                        ? new SolidColorBrush(Color.FromArgb((byte)alpha, 16, 185, 129)) // #10B981 con alpha
                        : new SolidColorBrush(Color.FromRgb(16, 185, 129)); // #10B981

                default:
                    return returnWithAlpha
                        ? new SolidColorBrush(Color.FromArgb((byte)alpha, 107, 114, 128)) // #6B7280 con alpha
                        : new SolidColorBrush(Color.FromRgb(107, 114, 128)); // #6B7280
            }
        }

        return new SolidColorBrush(Color.FromRgb(107, 114, 128)); // Default #6B7280
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converte valore in euro formattato
/// </summary>
public class EuroCurrencyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is decimal euro)
        {
            return euro.ToString("N2", CultureInfo.CurrentCulture) + " €";
        }
        return "0,00 €";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converter per visibilità basata su feature key (sistema permessi a livelli).
/// ConverterParameter = "nav.clienti" (feature key).
/// Ritorna Visible se l'utente ha il livello sufficiente, Collapsed altrimenti.
/// </summary>
public class AuthFeatureToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string featureKey = parameter?.ToString() ?? "";
        if (string.IsNullOrEmpty(featureKey)) return Visibility.Visible;
        return ATEC.PM.Shared.PermissionEngine.CanAccess(featureKey)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converter per IsEnabled basata su feature key.
/// Per feature con behavior DISABLED: visibile ma non cliccabile.
/// </summary>
public class AuthFeatureToEnabledConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string featureKey = parameter?.ToString() ?? "";
        if (string.IsNullOrEmpty(featureKey)) return true;
        return ATEC.PM.Shared.PermissionEngine.CanAccess(featureKey);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}