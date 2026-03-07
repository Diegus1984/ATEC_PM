using System.Globalization;

namespace ATEC.PM.Client.Views;

public partial class MarkupDialog : Window
{
    public MarkupDialog()
    {
        InitializeComponent();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        string code = txtCode.Text.Trim().ToUpper();
        string description = txtDescription.Text.Trim();

        if (string.IsNullOrEmpty(code))
        {
            txtError.Text = "Codice obbligatorio.";
            return;
        }
        if (string.IsNullOrEmpty(description))
        {
            txtError.Text = "Descrizione obbligatoria.";
            return;
        }
        if (!decimal.TryParse(txtMarkupValue.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal markupValue))
        {
            txtError.Text = "Valore K non valido.";
            return;
        }

        string coeffType = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "MATERIAL";

        decimal? hourlyCost = null;
        if (!string.IsNullOrWhiteSpace(txtHourlyCost.Text))
        {
            if (decimal.TryParse(txtHourlyCost.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal hc))
                hourlyCost = hc;
        }

        int.TryParse(txtSortOrder.Text, out int sortOrder);

        try
        {
            string jsonBody = JsonSerializer.Serialize(new
            {
                code,
                description,
                coefficientType = coeffType,
                markupValue,
                hourlyCost,
                sortOrder,
                isActive = true
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/markup", jsonBody);
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
