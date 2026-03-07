namespace ATEC.PM.Client.Views;

public partial class CostSectionTemplateDialog : Window
{
    public CostSectionTemplateDialog(List<CostSectionGroupDto> groups)
    {
        InitializeComponent();

        foreach (CostSectionGroupDto g in groups.OrderBy(g => g.SortOrder))
            cmbGroup.Items.Add(new ComboBoxItem { Content = g.Name, Tag = g.Id });

        if (cmbGroup.Items.Count > 0) cmbGroup.SelectedIndex = 0;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
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

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                name,
                sectionType,
                groupId,
                isDefault = chkDefault.IsChecked == true,
                sortOrder,
                isActive = true
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/cost-sections/templates", json);
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
