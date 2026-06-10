using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ATEC.PM.Client.Helpers;

/// <summary>
/// Consente di usare il DatePicker solo tramite calendario popup, senza digitazione manuale.
/// </summary>
public static class DatePickerAssist
{
    public static readonly DependencyProperty PickerOnlyProperty =
        DependencyProperty.RegisterAttached(
            "PickerOnly",
            typeof(bool),
            typeof(DatePickerAssist),
            new PropertyMetadata(false, OnPickerOnlyChanged));

    public static bool GetPickerOnly(DependencyObject obj) => (bool)obj.GetValue(PickerOnlyProperty);

    public static void SetPickerOnly(DependencyObject obj, bool value) => obj.SetValue(PickerOnlyProperty, value);

    private static void OnPickerOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DatePicker dp)
            return;

        if ((bool)e.NewValue)
        {
            dp.Loaded += OnDatePickerLoaded;
            if (dp.IsLoaded)
                ApplyPickerOnly(dp);
        }
        else
        {
            dp.Loaded -= OnDatePickerLoaded;
        }
    }

    private static void OnDatePickerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is DatePicker dp)
            ApplyPickerOnly(dp);
    }

    private static void ApplyPickerOnly(DatePicker dp)
    {
        dp.ApplyTemplate();
        if (dp.Template.FindName("PART_TextBox", dp) is not DatePickerTextBox textBox)
            return;

        textBox.IsReadOnly = true;
        textBox.Focusable = false;
        textBox.IsHitTestVisible = false;
        textBox.PreviewKeyDown += (_, args) => args.Handled = true;
        textBox.PreviewTextInput += (_, args) => args.Handled = true;
    }
}
