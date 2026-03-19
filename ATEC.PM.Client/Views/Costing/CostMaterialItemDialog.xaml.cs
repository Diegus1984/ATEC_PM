using System.Globalization;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.UserControls;

public partial class CostMaterialItemDialog : Window
{
    private readonly int _projectId;
    private readonly int _sectionId;

    public CostMaterialItemDialog(int projectId, int sectionId)
    {
        InitializeComponent();
        _projectId = projectId;
        _sectionId = sectionId;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtDescription.Text))
        {
            txtError.Text = "Descrizione obbligatoria.";
            return;
        }

        decimal.TryParse(txtQuantity.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal qty);
        decimal.TryParse(txtUnitCost.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal cost);

        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                sectionId = _sectionId,
                description = txtDescription.Text.Trim(),
                quantity = qty > 0 ? qty : 1,
                unitCost = cost,
                sortOrder = 0
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/material-items", json);
            var doc = System.Text.Json.JsonDocument.Parse(result);
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
