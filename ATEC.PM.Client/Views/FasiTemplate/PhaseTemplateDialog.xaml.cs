namespace ATEC.PM.Client.Views;

public partial class PhaseTemplateDialog : Window
{
    public PhaseTemplateDialog(List<DepartmentDto> departments, List<CostSectionTemplateDto> costSections)
    {
        InitializeComponent();

        // Popola combo reparti
        cmbDepartment.Items.Add(new ComboBoxItem { Content = "— Nessuno (trasversale) —", Tag = (int?)null });
        foreach (DepartmentDto d in departments)
            cmbDepartment.Items.Add(new ComboBoxItem { Content = $"{d.Code} — {d.Name}", Tag = (int?)d.Id });
        cmbDepartment.SelectedIndex = 0;

        // Popola combo sezioni costo
        cmbCostSection.Items.Add(new ComboBoxItem { Content = "— Nessuna —", Tag = (int?)null });
        foreach (var cs in costSections.OrderBy(c => c.SortOrder))
        {
            string groupLabel = string.IsNullOrEmpty(cs.GroupName) ? "" : $"[{cs.GroupName}] ";
            cmbCostSection.Items.Add(new ComboBoxItem { Content = $"{groupLabel}{cs.Name}", Tag = (int?)cs.Id });
        }
        cmbCostSection.SelectedIndex = 0;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string name = txtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            txtError.Text = "Il nome è obbligatorio.";
            return;
        }

        int? deptId = (cmbDepartment.SelectedItem as ComboBoxItem)?.Tag as int?;
        int? costSectionId = (cmbCostSection.SelectedItem as ComboBoxItem)?.Tag as int?;
        int.TryParse(txtSortOrder.Text, out int sortOrder);

        string category = txtCategory.Text.Trim();
        if (string.IsNullOrEmpty(category))
        {
            if (deptId == null)
                category = "TRASV";
            else
            {
                string deptContent = (cmbDepartment.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                category = deptContent.Contains('—') ? deptContent.Split('—')[0].Trim() : "TRASV";
            }
        }

        try
        {
            string jsonBody = JsonSerializer.Serialize(new
            {
                name,
                category,
                departmentId = deptId,
                costSectionTemplateId = costSectionId,
                sortOrder,
                isDefault = chkDefault.IsChecked == true
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/phases/templates", jsonBody);
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                DialogResult = true;
                Close();
            }
            else
                txtError.Text = doc.RootElement.GetProperty("message").GetString();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
