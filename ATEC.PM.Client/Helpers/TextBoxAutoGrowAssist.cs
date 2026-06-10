using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ATEC.PM.Client.Helpers;

/// <summary>
/// Aumenta automaticamente l'altezza di un TextBox multilinea mantenendo la larghezza fissa.
/// </summary>
public static class TextBoxAutoGrowAssist
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(TextBoxAutoGrowAssist),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox textBox)
            return;

        if ((bool)e.NewValue)
        {
            textBox.Loaded += OnTextBoxLoaded;
            textBox.TextChanged += OnTextBoxTextChanged;
            textBox.SizeChanged += OnTextBoxSizeChanged;
            if (textBox.IsLoaded)
                Apply(textBox);
        }
        else
        {
            textBox.Loaded -= OnTextBoxLoaded;
            textBox.TextChanged -= OnTextBoxTextChanged;
            textBox.SizeChanged -= OnTextBoxSizeChanged;
        }
    }

    private static void OnTextBoxLoaded(object sender, RoutedEventArgs e) => Apply((TextBox)sender);

    private static void OnTextBoxTextChanged(object sender, TextChangedEventArgs e) => ScheduleAdjust((TextBox)sender);

    private static void OnTextBoxSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged)
            ScheduleAdjust((TextBox)sender);
    }

    private static void Apply(TextBox textBox)
    {
        textBox.ClearValue(FrameworkElement.HeightProperty);
        textBox.VerticalContentAlignment = VerticalAlignment.Top;
        textBox.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        textBox.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;

        textBox.ApplyTemplate();
        if (textBox.Template?.FindName("PART_ContentHost", textBox) is ScrollViewer scrollHost)
        {
            scrollHost.VerticalAlignment = VerticalAlignment.Top;
            scrollHost.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }

        ScheduleAdjust(textBox);
    }

    private static void ScheduleAdjust(TextBox textBox)
    {
        textBox.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => AdjustHeight(textBox));
    }

    private static void AdjustHeight(TextBox textBox)
    {
        if (textBox.ActualWidth <= 0)
            return;

        textBox.ApplyTemplate();
        ScrollViewer? scrollHost = textBox.Template?.FindName("PART_ContentHost", textBox) as ScrollViewer;
        if (scrollHost == null)
            return;

        textBox.UpdateLayout();
        scrollHost.UpdateLayout();

        double minHeight = textBox.MinHeight > 0 ? textBox.MinHeight : 30;
        double padding = textBox.Padding.Top + textBox.Padding.Bottom;
        double border = textBox.BorderThickness.Top + textBox.BorderThickness.Bottom;
        double contentHeight = scrollHost.ExtentHeight;
        double targetHeight = Math.Max(minHeight, contentHeight + padding + border + 2);

        if (double.IsNaN(textBox.Height) || Math.Abs(textBox.Height - targetHeight) > 0.5)
            textBox.Height = targetHeight;
    }
}
