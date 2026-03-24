using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Shared;

namespace ATEC.PM.Client.Helpers;

/// <summary>
/// Attached property per applicare permessi direttamente sugli elementi XAML (stile VisiWin).
///
/// Uso su bottoni:  auth:Auth.Feature="nav.clienti"
///   → nasconde/disabilita l'elemento in base al livello utente.
///
/// Uso su Expander:  auth:Auth.AutoHide="True"
///   → nasconde l'Expander se tutti i figli diretti sono Collapsed.
/// </summary>
public static class Auth
{
    // ════════════════════════════════════════════════════════════
    // Auth.Feature — permesso su singolo elemento
    // ════════════════════════════════════════════════════════════

    public static readonly DependencyProperty FeatureProperty =
        DependencyProperty.RegisterAttached(
            "Feature",
            typeof(string),
            typeof(Auth),
            new PropertyMetadata(null, OnFeatureChanged));

    public static string GetFeature(DependencyObject obj) => (string)obj.GetValue(FeatureProperty);
    public static void SetFeature(DependencyObject obj, string value) => obj.SetValue(FeatureProperty, value);

    private static void OnFeatureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        string featureKey = e.NewValue as string ?? "";
        if (string.IsNullOrEmpty(featureKey)) return;

        Apply(el, featureKey);

        if (d is FrameworkElement fe)
            fe.Loaded += (_, _) => Apply(el, featureKey);
    }

    private static void Apply(UIElement el, string featureKey)
    {
        if (PermissionEngine.IsDisabledOnly(featureKey))
        {
            el.Visibility = Visibility.Visible;
            el.IsEnabled = false;
            if (el is FrameworkElement fe) fe.Opacity = 0.4;
        }
        else if (PermissionEngine.CanAccess(featureKey))
        {
            el.Visibility = Visibility.Visible;
            el.IsEnabled = true;
            if (el is FrameworkElement fe) fe.Opacity = 1;
        }
        else
        {
            el.Visibility = Visibility.Collapsed;
        }
    }

    // ════════════════════════════════════════════════════════════
    // Auth.AutoHide — nasconde Expander se tutti i figli Collapsed
    // ════════════════════════════════════════════════════════════

    public static readonly DependencyProperty AutoHideProperty =
        DependencyProperty.RegisterAttached(
            "AutoHide",
            typeof(bool),
            typeof(Auth),
            new PropertyMetadata(false, OnAutoHideChanged));

    public static bool GetAutoHide(DependencyObject obj) => (bool)obj.GetValue(AutoHideProperty);
    public static void SetAutoHide(DependencyObject obj, bool value) => obj.SetValue(AutoHideProperty, value);

    private static void OnAutoHideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        if (e.NewValue is true)
            fe.Loaded += (_, _) => CheckAutoHide(fe);
    }

    private static void CheckAutoHide(FrameworkElement fe)
    {
        // Trova il StackPanel contenuto nell'Expander
        Panel? panel = null;
        if (fe is Expander exp && exp.Content is Panel p)
            panel = p;
        else if (fe is Panel directPanel)
            panel = directPanel;

        if (panel == null) return;

        bool anyVisible = panel.Children
            .OfType<UIElement>()
            .Any(child => child.Visibility == Visibility.Visible);

        fe.Visibility = anyVisible ? Visibility.Visible : Visibility.Collapsed;
    }
}
