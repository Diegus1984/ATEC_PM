using System.Globalization;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.UserControls;

public partial class CostResourceDialog : Window
{
    private readonly int _sectionId;
    private readonly int _projectId;

    public CostResourceDialog(int projectId, int sectionId, string sectionType)
    {
        InitializeComponent();
        _projectId = projectId;
        _sectionId = sectionId;
        if (sectionType == "DA_CLIENTE")
            pnlTrasferta.Visibility = Visibility.Visible;
    }

    private decimal Parse(string text) =>
        decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal v) ? v : 0;

    private int ParseInt(string text) =>
        int.TryParse(text, out int v) ? v : 0;

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtName.Text))
        {
            txtError.Text = "Nome risorsa obbligatorio.";
            return;
        }

        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new
            {
                sectionId = _sectionId,
                resourceName = txtName.Text.Trim(),
                workDays = Parse(txtDays.Text),
                hoursPerDay = Parse(txtHoursDay.Text),
                hourlyCost = Parse(txtCostH.Text),
                numTrips = ParseInt(txtTrips.Text),
                kmPerTrip = Parse(txtKm.Text),
                costPerKm = Parse(txtCostKm.Text),
                dailyFood = Parse(txtFood.Text),
                dailyHotel = Parse(txtHotel.Text),
                allowanceDays = Parse(txtAllowDays.Text),
                dailyAllowance = Parse(txtAllowRate.Text),
                sortOrder = 0
            }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/resources", json);
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
