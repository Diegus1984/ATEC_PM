using System.Windows;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class EditCostSectionDialog : Window
{
    public string SectionName => txtName.Text.Trim();
    public int SortOrder => int.TryParse(txtSortOrder.Text, out int n) ? n : 0;
    public bool IsDefaultProject => chkProject.IsChecked == true;
    public bool IsDefaultQuote => chkQuote.IsChecked == true;

    public EditCostSectionDialog(CostSectionTemplateDto section)
    {
        InitializeComponent();
        txtName.Text = section.Name;
        txtSortOrder.Text = section.SortOrder.ToString();
        chkProject.IsChecked = section.IsDefault;
        chkQuote.IsChecked = section.IsDefaultQuote;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SectionName))
            return;
        DialogResult = true;
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
