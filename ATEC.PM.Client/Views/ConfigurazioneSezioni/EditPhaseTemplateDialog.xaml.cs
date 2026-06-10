using System.Windows;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class EditPhaseTemplateDialog : Window
{
    public string PhaseName => txtName.Text.Trim();
    public string Category => txtCategory.Text.Trim();

    public EditPhaseTemplateDialog(PhaseTemplateDto phase)
    {
        InitializeComponent();
        Title = $"Modifica fase — {phase.Name}";
        txtTitle.Text = $"Modifica fase — {phase.Name}";
        txtName.Text = phase.Name;
        txtCategory.Text = phase.Category;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PhaseName))
            return;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
