using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class TimesheetEntryDialog : Window
{
    private readonly TimesheetEntryDto? _existing;

    public TimesheetEntryDialog(DateTime weekStart, TimesheetEntryDto? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        dpDate.SelectedDate = existing?.WorkDate ?? DateTime.Today;
        Title = existing == null ? "Registra Ore" : "Modifica Ore";
        Loaded += async (_, _) =>
        {
            await LoadEmployeeDropdown();
            await LoadPhases();
        };
    }

    private async Task LoadEmployeeDropdown()
    {
        // Solo RESP_REPARTO, PM, ADMIN vedono il dropdown
        if (!App.CurrentUser.IsPm && App.CurrentUser.UserRole != "RESP_REPARTO")
            return;

        try
        {
            string json = await ApiClient.GetAsync("/api/timesheet/registrable-employees");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var employees = JsonSerializer.Deserialize<List<LookupItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            if (employees.Count > 1) // se c'è solo te stesso, non serve il dropdown
            {
                cmbEmployee.ItemsSource = employees;
                cmbEmployee.SelectedValue = _existing?.EmployeeId ?? App.UserId;
                cmbEmployee.Visibility = Visibility.Visible;
                lblRegistraPer.Visibility = Visibility.Visible;
            }
        }
        catch { }
    }

    private async Task LoadPhases()
    {
        try
        {
            int empId = GetSelectedEmployeeId();
            var json = await ApiClient.GetAsync($"/api/timesheet/phases-for-employee?employeeId={empId}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var phases = JsonSerializer.Deserialize<List<TimesheetPhaseOption>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                cmbPhase.ItemsSource = phases;

                if (_existing != null)
                {
                    cmbPhase.SelectedValue = _existing.ProjectPhaseId;
                    txtHours.Text = _existing.Hours.ToString("G", CultureInfo.InvariantCulture);
                    txtNotes.Text = _existing.Notes;
                    foreach (ComboBoxItem item in cmbType.Items)
                    {
                        if (item.Content?.ToString() == _existing.EntryType)
                        {
                            cmbType.SelectedItem = item;
                            break;
                        }
                    }
                }
                else if (phases.Count > 0)
                {
                    cmbPhase.SelectedIndex = 0;
                }
            }
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private int GetSelectedEmployeeId()
    {
        if (cmbEmployee.Visibility == Visibility.Visible && cmbEmployee.SelectedValue is int id)
            return id;
        return App.UserId;
    }

    private async void CmbEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbEmployee.SelectedValue == null) return;
        await LoadPhases();
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
                id = _existing?.Id ?? 0,
                employeeId = GetSelectedEmployeeId(),
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