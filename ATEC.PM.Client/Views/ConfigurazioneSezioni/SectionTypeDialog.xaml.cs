using System.Windows;
using System.Windows.Controls;

namespace ATEC.PM.Client.Views;

public partial class SectionTypeDialog : Window
{
    public string SectionType =>
        (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "IN_SEDE";

    public SectionTypeDialog()
    {
        InitializeComponent();
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
