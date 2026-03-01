using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class TimesheetEntryDialog : Window
{
    public TimesheetEntryDialog(DateTime weekStart)
    {
        InitializeComponent();
        dpDate.SelectedDate = DateTime.Today;
        Loaded += async (_, _) => await LoadPhases();
    }

    private async Task LoadPhases()
    {
        try
        {
            var json = await ApiClient.GetAsync($"/api/timesheet/phases-for-employee?employeeId={App.UserId}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var phases = JsonSerializer.Deserialize<List<TimesheetPhaseOption>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                cmbPhase.ItemsSource = phases;
                if (phases.Count > 0) cmbPhase.SelectedIndex = 0;
            }
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (cmbPhase.SelectedValue == null) { txtError.Text = "Seleziona una commessa/fase."; return; }
        if (dpDate.SelectedDate == null) { txtError.Text = "Seleziona una data."; return; }
        if (!decimal.TryParse(txtHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var hours) || hours <= 0)
        { txtError.Text = "Ore non valide."; return; }

        btnSave.IsEnabled = false;
        try
        {
            var obj = new
            {
                id = 0,
                employeeId = App.UserId,
                projectPhaseId = (int)cmbPhase.SelectedValue,
                workDate = dpDate.SelectedDate.Value.ToString("yyyy-MM-dd"),
                hours,
                entryType = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "REGULAR",
                notes = txtNotes.Text
            };
            var jsonBody = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            var result = await ApiClient.PostAsync("/api/timesheet", jsonBody);
            var doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            { DialogResult = true; Close(); }
            else txtError.Text = doc.RootElement.GetProperty("message").GetString();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
        finally { btnSave.IsEnabled = true; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
