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
    private bool _loading = true;

    public TimesheetEntryDialog(DateTime weekStart, TimesheetEntryDto? existing = null)
    {
        InitializeComponent();
        _existing = existing;
        dpDate.SelectedDate = existing?.WorkDate ?? DateTime.Today;
        Title = existing == null ? "Registra Ore" : "Modifica Ore";
        Loaded += async (_, _) =>
        {
            await LoadEmployeeDropdown();
            await LoadProjects();
            _loading = false;
        };
    }

    private async Task LoadEmployeeDropdown()
    {
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

            if (employees.Count > 1)
            {
                cmbEmployee.ItemsSource = employees;
                cmbEmployee.SelectedValue = _existing?.EmployeeId ?? App.UserId;
                cmbEmployee.Visibility = Visibility.Visible;
                lblRegistraPer.Visibility = Visibility.Visible;
            }
        }
        catch { }
    }

    private async Task LoadProjects()
    {
        try
        {
            int empId = GetSelectedEmployeeId();
            string json = await ApiClient.GetAsync($"/api/timesheet/projects-for-employee?employeeId={empId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var projects = JsonSerializer.Deserialize<List<TimesheetProjectOption>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            cmbProject.ItemsSource = projects;

            if (_existing != null)
            {
                int existingProjectId = await GetProjectIdForPhase(_existing.ProjectPhaseId);
                cmbProject.SelectedValue = existingProjectId;
            }
            else if (projects.Count > 0)
            {
                cmbProject.SelectedIndex = 0;
            }

            // Carica le fasi per la commessa selezionata
            await LoadPhases();
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadPhases()
    {
        try
        {
            if (cmbProject.SelectedValue is not int projectId) return;

            int empId = GetSelectedEmployeeId();
            string json = await ApiClient.GetAsync($"/api/timesheet/phases-for-employee?employeeId={empId}&projectId={projectId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

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
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
    }

    private async Task<int> GetProjectIdForPhase(int phaseId)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/phases/{phaseId}/project-id");
            JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                return doc.RootElement.GetProperty("data").GetInt32();
        }
        catch { }
        return 0;
    }

    private int GetSelectedEmployeeId()
    {
        if (cmbEmployee.Visibility == Visibility.Visible && cmbEmployee.SelectedValue is int id)
            return id;
        return App.UserId;
    }

    private async void CmbEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || cmbEmployee.SelectedValue == null) return;
        await LoadProjects();
    }

    private async void CmbProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || cmbProject.SelectedValue == null) return;
        await LoadPhases();
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (cmbPhase.SelectedValue == null) { txtError.Text = "Seleziona una fase."; return; }
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