using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.Views;

public partial class EmployeeDialog : Window
{
    private readonly int _employeeId;

    public EmployeeDialog(int employeeId = 0)
    {
        InitializeComponent();
        _employeeId = employeeId;
        Title = employeeId == 0 ? "Nuovo Dipendente" : "Modifica Dipendente";
        dpHireDate.SelectedDate = DateTime.Today;

        if (employeeId > 0)
        {
            Loaded += async (_, _) => await LoadEmployee();
        }
    }

    private async Task LoadEmployee()
    {
        try
        {
            var json = await ApiClient.GetAsync($"/api/employees/{_employeeId}");
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.GetProperty("success").GetBoolean())
            {
                var d = root.GetProperty("data");
                txtBadge.Text = d.GetProperty("badgeNumber").GetString() ?? "";
                txtFirstName.Text = d.GetProperty("firstName").GetString() ?? "";
                txtLastName.Text = d.GetProperty("lastName").GetString() ?? "";
                txtEmail.Text = d.GetProperty("email").GetString() ?? "";
                txtPhone.Text = d.GetProperty("phone").GetString() ?? "";
                txtHourlyCost.Text = d.GetProperty("hourlyCost").GetDecimal().ToString("F2");
                txtWeeklyHours.Text = d.GetProperty("weeklyHours").GetDecimal().ToString("F0");
                txtNotes.Text = d.GetProperty("notes").GetString() ?? "";

                SelectComboItem(cmbType, d.GetProperty("empType").GetString() ?? "INTERNAL");
                SelectComboItem(cmbStatus, d.GetProperty("status").GetString() ?? "ACTIVE");

                if (d.TryGetProperty("hireDate", out var hd) && hd.ValueKind != JsonValueKind.Null)
                    dpHireDate.SelectedDate = hd.GetDateTime();
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore caricamento: {ex.Message}";
        }
    }

    private void SelectComboItem(ComboBox cmb, string value)
    {
        foreach (ComboBoxItem item in cmb.Items)
        {
            if (item.Content?.ToString() == value)
            {
                item.IsSelected = true;
                break;
            }
        }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";

        if (string.IsNullOrWhiteSpace(txtFirstName.Text) || string.IsNullOrWhiteSpace(txtLastName.Text))
        {
            txtError.Text = "Nome e cognome sono obbligatori.";
            return;
        }

        btnSave.IsEnabled = false;
        btnSave.Content = "Salvataggio...";

        try
        {
            decimal.TryParse(txtHourlyCost.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var hourlyCost);
            decimal.TryParse(txtWeeklyHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var weeklyHours);

            var obj = new
            {
                badgeNumber = txtBadge.Text,
                firstName = txtFirstName.Text,
                lastName = txtLastName.Text,
                email = txtEmail.Text,
                phone = txtPhone.Text,
                empType = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "INTERNAL",
                hourlyCost,
                weeklyHours,
                hireDate = dpHireDate.SelectedDate?.ToString("yyyy-MM-dd"),
                status = (cmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "ACTIVE",
                notes = txtNotes.Text
            };

            var jsonBody = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result;
            if (_employeeId == 0)
            {
                result = await ApiClient.PostAsync("/api/employees", jsonBody);
            }
            else
            {
                result = await ApiClient.PutAsync($"/api/employees/{_employeeId}", jsonBody);
            }

            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                DialogResult = true;
                Close();
            }
            else
            {
                txtError.Text = doc.RootElement.GetProperty("message").GetString();
            }
        }
        catch (Exception ex)
        {
            txtError.Text = $"Errore: {ex.Message}";
        }
        finally
        {
            btnSave.IsEnabled = true;
            btnSave.Content = "Salva";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
