using System.Windows;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.BudgetVsCosting;

public partial class ImportPhasesDialog : Window
{
    public List<int> SelectedTemplateIds { get; private set; } = new();

    public ImportPhasesDialog(List<PhaseTemplateDto> templates)
    {
        InitializeComponent();
        lbTemplates.ItemsSource = templates;
    }

    private void BtnOk_Click(object sender, RoutedEventArgs e)
    {
        SelectedTemplateIds = lbTemplates.SelectedItems
            .Cast<PhaseTemplateDto>()
            .Select(t => t.Id)
            .ToList();

        if (SelectedTemplateIds.Count == 0)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Seleziona almeno una fase.", "Avviso");
            return;
        }

        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
