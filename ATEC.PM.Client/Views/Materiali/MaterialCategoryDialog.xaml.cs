using System.Globalization;

namespace ATEC.PM.Client.Views;

public partial class MaterialCategoryDialog : Window
{
    public MaterialCategoryDialog()
    {
        InitializeComponent();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string name = txtName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            txtError.Text = "Nome obbligatorio.";
            return;
        }

        if (!decimal.TryParse(txtKMaterial.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal kMat))
        {
            txtError.Text = "K Materiale non valido.";
            return;
        }

        if (!decimal.TryParse(txtKCommission.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal kComm))
        {
            txtError.Text = "K Provvigione non valido.";
            return;
        }

        int.TryParse(txtSortOrder.Text, out int sortOrder);

        try
        {
            string json = JsonSerializer.Serialize(new
            {
                name,
                defaultMarkup = kMat,
                defaultCommissionMarkup = kComm,
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
