namespace ATEC.PM.Client.Views;

public partial class MaterialCategoryDialog : Window
{
    public MaterialCategoryDialog(List<MarkupCoefficientDto> markups)
    {
        InitializeComponent();

        foreach (MarkupCoefficientDto mk in markups.Where(m => m.CoefficientType == "MATERIAL").OrderBy(m => m.SortOrder))
            cmbMarkup.Items.Add(new ComboBoxItem { Content = $"{mk.Code} — {mk.Description}", Tag = mk.Code });

        if (cmbMarkup.Items.Count > 0) cmbMarkup.SelectedIndex = 0;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string name = txtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            txtError.Text = "Nome obbligatorio.";
            return;
        }

        if (cmbMarkup.SelectedItem is not ComboBoxItem mkItem || mkItem.Tag is not string mkCode || string.IsNullOrEmpty(mkCode))
        {
            txtError.Text = "Seleziona un markup.";
            return;
        }

        int.TryParse(txtSortOrder.Text, out int sortOrder);

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                name,
                markupCode = mkCode,
                sortOrder,
                isActive = true
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/material-categories", json);
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
