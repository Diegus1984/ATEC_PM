using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class TimesheetEntryDialog : Window
{
    private const decimal MaxHoursPerEntry = 24m;
    private const decimal MaxHoursPerDay = 24m;

    private static readonly CultureInfo It = new("it-IT");
    private readonly TimesheetEntryDto? _existing;
    private readonly int? _presetEmployeeId;
    private decimal _otherHoursOnDay;
    private bool _loading = true;

    private void UpdateHeader()
    {
        if (dpDate.SelectedDate is DateTime d)
        {
            string s = d.ToString("dddd d MMMM yyyy", It);
            hdrDate.Text = char.ToUpper(s[0]) + s[1..];
        }
        else hdrDate.Text = _existing == null ? "Registra ore" : "Modifica ore";
    }

    private void ApplyDateConstraints()
    {
        dpDate.DisplayDateEnd = DateTime.Today;
        try
        {
            if (dpDate.BlackoutDates.Count == 0)
                dpDate.BlackoutDates.Add(new CalendarDateRange(DateTime.Today.AddDays(1), DateTime.MaxValue));
        }
        catch { /* blackout già presente */ }
    }

    private void SetModeTexts()
    {
        bool editing = _existing != null;
        Title = editing ? "Modifica Ore" : "Registra Ore";
        btnSave.Content = editing ? "Salva modifiche" : "Salva";
        btnDelete.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        hdrSubtitle.Text = editing
            ? "Correggi una registrazione di oggi o di un giorno precedente. Il totale del giorno non può superare 24 ore."
            : "Nuova registrazione per oggi o un giorno passato. Il totale del giorno non può superare 24 ore.";
    }

    private async Task RefreshDayTotalAsync()
    {
        if (dpDate.SelectedDate is not DateTime d)
            return;

        int empId = GetSelectedEmployeeId();
        int excludeId = _existing?.Id ?? 0;
        decimal? total = await ApiClient.GetDataAsync<decimal>(
            $"/api/timesheet/day-total?employeeId={empId}&date={d:yyyy-MM-dd}&excludeId={excludeId}");
        _otherHoursOnDay = total ?? 0;
        UpdateDayHint();
    }

    private void UpdateDayHint()
    {
        decimal available = MaxHoursPerDay - _otherHoursOnDay;
        if (available < 0) available = 0;
        txtDayHint.Text =
            $"Massimo {MaxHoursPerDay:N0}h per giorno · altre registrazioni: {_otherHoursOnDay:N1}h · disponibili per questa voce: {available:N1}h";
        txtDayHint.Foreground = available <= 0
            ? (Brush)FindResource("ShadcnDestructiveBrush")
            : (Brush)FindResource("ShadcnMutedForegroundBrush");
    }

    public TimesheetEntryDialog(DateTime presetDate, TimesheetEntryDto? existing = null, int? presetEmployeeId = null)
    {
        InitializeComponent();
        _existing = existing;
        _presetEmployeeId = presetEmployeeId;
        dpDate.SelectedDate = existing?.WorkDate ?? presetDate;
        ApplyDateConstraints();
        SetModeTexts();
        UpdateHeader();
        dpDate.SelectedDateChanged += async (_, _) =>
        {
            UpdateHeader();
            if (!_loading)
                await RefreshDayTotalAsync();
        };
        Loaded += async (_, _) =>
        {
            await LoadEmployeeDropdown();
            await LoadProjects();
            await RefreshDayTotalAsync();
            _loading = false;
        };
    }

    private async Task LoadEmployeeDropdown()
    {
        if (!App.CurrentUser.IsPm && App.CurrentUser.UserRole != "RESP_REPARTO")
            return;

        try
        {
            List<LookupItem> employees = await ApiClient.GetDataAsync<List<LookupItem>>(
                "/api/timesheet/registrable-employees") ?? new();

            if (employees.Count > 1)
            {
                cmbEmployee.ItemsSource = employees;
                cmbEmployee.SelectedValue = _existing?.EmployeeId ?? _presetEmployeeId ?? App.UserId;
                cmbEmployee.Visibility = Visibility.Visible;
                lblRegistraPer.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading registrable employees: {ex.Message}");
        }
    }

    private async Task LoadProjects()
    {
        try
        {
            int empId = GetSelectedEmployeeId();
            List<TimesheetProjectOption>? projects = await ApiClient.GetDataAsync<List<TimesheetProjectOption>>(
                $"/api/timesheet/projects-for-employee?employeeId={empId}");
            if (projects == null)
                return;

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
            List<TimesheetPhaseOption>? phases = await ApiClient.GetDataAsync<List<TimesheetPhaseOption>>(
                $"/api/timesheet/phases-for-employee?employeeId={empId}&projectId={projectId}");
            if (phases == null)
                return;

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
            int? projectId = await ApiClient.GetDataAsync<int>($"/api/phases/{phaseId}/project-id");
            if (projectId.HasValue)
                return projectId.Value;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error getting project ID for phase {phaseId}: {ex.Message}");
        }
        return 0;
    }

    private int GetSelectedEmployeeId()
    {
        if (cmbEmployee.Visibility == Visibility.Visible && cmbEmployee.SelectedValue is int id)
            return id;
        return _existing?.EmployeeId ?? _presetEmployeeId ?? App.UserId;
    }

    private async void CmbEmployee_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || cmbEmployee.SelectedValue == null) return;
        await LoadProjects();
        await RefreshDayTotalAsync();
    }

    private async void CmbProject_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || cmbProject.SelectedValue == null) return;
        await LoadPhases();
    }

    private bool TryValidate(out DateTime workDate, out decimal hours, out string error)
    {
        workDate = default;
        hours = 0;
        error = "";

        if (cmbPhase.SelectedValue == null) { error = "Seleziona una fase."; return false; }
        if (dpDate.SelectedDate == null) { error = "Seleziona una data."; return false; }

        workDate = dpDate.SelectedDate.Value.Date;
        if (workDate > DateTime.Today)
        {
            error = "Non è possibile registrare o spostare ore in date future.";
            return false;
        }

        if (!decimal.TryParse(txtHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out hours) || hours <= 0)
        {
            error = "Inserisci un numero di ore valido (maggiore di zero).";
            return false;
        }

        if (hours > MaxHoursPerEntry)
        {
            error = $"Ogni singola registrazione non può superare {MaxHoursPerEntry:N0} ore.";
            return false;
        }

        if (_otherHoursOnDay + hours > MaxHoursPerDay)
        {
            decimal available = MaxHoursPerDay - _otherHoursOnDay;
            if (available < 0) available = 0;
            error = _otherHoursOnDay > 0
                ? $"Superato il limite di {MaxHoursPerDay:N0}h per il giorno: altre registrazioni {_otherHoursOnDay:N1}h, puoi inserire al massimo {available:N1}h."
                : $"Il totale ore per il giorno non può superare {MaxHoursPerDay:N0}.";
            return false;
        }

        return true;
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";
        await RefreshDayTotalAsync();

        if (!TryValidate(out DateTime workDate, out decimal hours, out string validationError))
        {
            txtError.Text = validationError;
            return;
        }

        btnSave.IsEnabled = false;
        try
        {
            object obj = new
            {
                id = _existing?.Id ?? 0,
                employeeId = GetSelectedEmployeeId(),
                projectPhaseId = (int)cmbPhase.SelectedValue!,
                workDate = workDate.ToString("yyyy-MM-dd"),
                hours,
                entryType = (cmbType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "REGULAR",
                notes = txtNotes.Text
            };
            string jsonBody = JsonSerializer.Serialize(obj, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync("/api/timesheet", jsonBody);
            if (ApiClient.IsApiSuccess(result, out string msg))
            {
                DialogResult = true;
                Close();
            }
            else txtError.Text = msg;
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
        finally { btnSave.IsEnabled = true; }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_existing == null) return;
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
                $"Eliminare la timbratura di {_existing.Hours:N1}h del {_existing.WorkDate:dd/MM/yyyy}?",
                "Conferma eliminazione", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        btnDelete.IsEnabled = false;
        try
        {
            string result = await ApiClient.DeleteAsync($"/api/timesheet/{_existing.Id}");
            if (ApiClient.IsApiSuccess(result, out string msg))
            {
                DialogResult = true; // la pagina ricarica e la voce sparisce
                Close();
            }
            else txtError.Text = string.IsNullOrWhiteSpace(msg) ? "Errore durante l'eliminazione." : msg;
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
        finally { btnDelete.IsEnabled = true; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
