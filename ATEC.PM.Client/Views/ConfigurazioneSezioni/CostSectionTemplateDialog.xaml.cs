using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CostSectionTemplateDialog : Window
{
    public CostSectionTemplateDialog(List<CostSectionGroupDto> groups, List<DepartmentDto> departments)
    {
        InitializeComponent();

        foreach (CostSectionGroupDto g in groups.OrderBy(g => g.SortOrder))
            cmbGroup.Items.Add(new ComboBoxItem { Content = g.Name, Tag = g.Id });
        if (cmbGroup.Items.Count > 0) cmbGroup.SelectedIndex = 0;

        foreach (DepartmentDto dept in departments.Where(d => d.IsActive).OrderBy(d => d.SortOrder))
        {
            CheckBox chk = new()
            {
                Content = $"{dept.Code} — {dept.Name}",
                FontSize = 12,
                Margin = new Thickness(0, 3, 16, 3),
                Tag = dept.Id,
                Foreground = (System.Windows.Media.Brush)FindResource("ShadcnForegroundBrush")
            };
            wpDepartments.Children.Add(chk);
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";
        string name = txtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            txtError.Text = "Nome obbligatorio.";
            return;
        }

        if (cmbGroup.SelectedItem is not ComboBoxItem groupItem || groupItem.Tag is not int groupId)
        {
            txtError.Text = "Seleziona un gruppo.";
            return;
        }

        string sectionType = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "IN_SEDE";
        int.TryParse(txtSortOrder.Text, out int sortOrder);

        List<int> deptIds = new();
        foreach (object child in wpDepartments.Children)
        {
            if (child is CheckBox chk && chk.IsChecked == true && chk.Tag is int dId)
                deptIds.Add(dId);
        }

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                name,
                sectionType,
                groupId,
                isDefault = chkDefaultProject.IsChecked == true,
                isDefaultQuote = chkDefaultQuote.IsChecked == true,
                sortOrder,
                isActive = true,
                departmentIds = deptIds
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/cost-sections/templates", json);
            if (ApiClient.IsApiSuccess(result, out string msg))
            {
                DialogResult = true;
                Close();
            }
            else
                txtError.Text = msg;
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
