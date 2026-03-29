using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class ProjectDialog : Window
{
    private readonly int _id;
    private List<LookupItem> _customers = new();
    private List<LookupItem> _employees = new();

    public ProjectDialog(int id = 0)
    {
        InitializeComponent();
        _id = id;
        Title = id == 0 ? "Nuova Commessa" : "Modifica Commessa";
        dpStart.SelectedDate = DateTime.Today;
        if (id > 0) chkDefaultPhases.Visibility = Visibility.Collapsed;
        Loaded += async (_, _) => await LoadLookups();
    }

    private async Task LoadLookups()
    {
        try
        {
            var custJson = await ApiClient.GetAsync("/api/lookup/customers");
            var empJson = await ApiClient.GetAsync("/api/lookup/employees");

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

            var custDoc = JsonDocument.Parse(custJson);
            if (custDoc.RootElement.GetProperty("success").GetBoolean())
            {
                _customers = JsonSerializer.Deserialize<List<LookupItem>>(custDoc.RootElement.GetProperty("data").GetRawText(), opts) ?? new();
                cmbCustomer.ItemsSource = _customers;
            }

            var empDoc = JsonDocument.Parse(empJson);
            if (empDoc.RootElement.GetProperty("success").GetBoolean())
            {
                _employees = JsonSerializer.Deserialize<List<LookupItem>>(empDoc.RootElement.GetProperty("data").GetRawText(), opts) ?? new();
                cmbPm.ItemsSource = _employees;
            }

            if (_id > 0) await LoadProject();
            else await LoadNextCode();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadProject()
    {
        await LoadProjectData();
    }

    private async Task LoadNextCode()
    {
        try
        {
            var json = await ApiClient.GetAsync("/api/projects/next-code");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                txtCode.Text = doc.RootElement.GetProperty("data").GetString() ?? "";
        }
        catch { }
    }

    private async Task LoadProjectData()
    {
        var json = await ApiClient.GetAsync($"/api/projects/{_id}");
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("success").GetBoolean())
        {
            var d = doc.RootElement.GetProperty("data");
            txtCode.Text = d.GetProperty("code").GetString() ?? "";
            txtTitle.Text = d.GetProperty("title").GetString() ?? "";
            txtRevenue.Text = d.GetProperty("revenue").GetDecimal().ToString("F0");
            txtBudget.Text = d.GetProperty("budgetTotal").GetDecimal().ToString("F0");
            txtHours.Text = d.GetProperty("budgetHoursTotal").GetDecimal().ToString("F0");
            txtDescription.Text = d.GetProperty("description").GetString() ?? "";
            txtServerPath.Text = d.GetProperty("serverPath").GetString() ?? "";
            txtNotes.Text = d.GetProperty("notes").GetString() ?? "";

            cmbCustomer.SelectedValue = d.GetProperty("customerId").GetInt32();
            cmbPm.SelectedValue = d.GetProperty("pmId").GetInt32();

            SelectComboItem(cmbStatus, d.GetProperty("status").GetString() ?? "DRAFT");
            SelectComboItem(cmbPriority, d.GetProperty("priority").GetString() ?? "MEDIUM");

            if (d.TryGetProperty("startDate", out var sd) && sd.ValueKind != JsonValueKind.Null)
                dpStart.SelectedDate = sd.GetDateTime();
            if (d.TryGetProperty("endDatePlanned", out var ed) && ed.ValueKind != JsonValueKind.Null)
                dpEnd.SelectedDate = ed.GetDateTime();
        }
    }

    private void SelectComboItem(ComboBox cmb, string value)
    {
        foreach (ComboBoxItem item in cmb.Items)
            if (item.Content?.ToString() == value) { item.IsSelected = true; break; }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(txtCode.Text) || string.IsNullOrWhiteSpace(txtTitle.Text))
        { txtError.Text = "Codice e titolo obbligatori."; return; }
        if (cmbCustomer.SelectedValue == null || cmbPm.SelectedValue == null)
        { txtError.Text = "Seleziona cliente e PM."; return; }

        btnSave.IsEnabled = false;
        try
        {
            decimal.TryParse(txtRevenue.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var revenue);
            decimal.TryParse(txtBudget.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var budget);
            decimal.TryParse(txtHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours);

            var obj = new
            {
                code = txtCode.Text,
                title = txtTitle.Text,
                customerId = (int)cmbCustomer.SelectedValue,
                pmId = (int)cmbPm.SelectedValue,
                description = txtDescription.Text,
                startDate = dpStart.SelectedDate?.ToString("yyyy-MM-dd"),
                endDatePlanned = dpEnd.SelectedDate?.ToString("yyyy-MM-dd"),
                budgetTotal = budget,
                budgetHoursTotal = hours,
                revenue,
                status = (cmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "DRAFT",
                priority = (cmbPriority.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "MEDIUM",
                serverPath = txtServerPath.Text,
                notes = txtNotes.Text,
                createDefaultPhases = chkDefaultPhases.IsChecked == true
            };

            var jsonBody = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var result = _id == 0
                ? await ApiClient.PostAsync("/api/projects", jsonBody)
                : await ApiClient.PutAsync($"/api/projects/{_id}", jsonBody);

            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            { DialogResult = true; Close(); }
            else
                txtError.Text = doc.RootElement.GetProperty("message").GetString();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
        finally { btnSave.IsEnabled = true; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
