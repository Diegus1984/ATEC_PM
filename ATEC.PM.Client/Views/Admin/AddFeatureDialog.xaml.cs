using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ATEC.PM.Client.Views.Admin;

public partial class AddFeatureDialog : Window
{
    public string FeatureKey { get; private set; } = "";
    public string FeatureDisplayName { get; private set; } = "";
    public string FeatureCategory { get; private set; } = "navigation";
    public int FeatureMinLevel { get; private set; }

    public AddFeatureDialog(List<LevelOption> levelOptions)
    {
        InitializeComponent();
        cboLevel.ItemsSource = levelOptions;
        if (levelOptions.Count > 0)
            cboLevel.SelectedIndex = 0;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtKey.Text) || string.IsNullOrWhiteSpace(txtName.Text))
        {
            MessageBox.Show("Chiave e Nome sono obbligatori.", "Attenzione", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        FeatureKey = txtKey.Text.Trim();
        FeatureDisplayName = txtName.Text.Trim();
        FeatureCategory = (cboCategory.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "navigation";
        FeatureMinLevel = (cboLevel.SelectedItem as LevelOption)?.Value ?? 0;

        DialogResult = true;
    }
}
