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
            _customers = await ApiClient.GetDataAsync<List<LookupItem>>("/api/lookup/customers") ?? new();
            cmbCustomer.ItemsSource = _customers;

            _employees = await ApiClient.GetDataAsync<List<LookupItem>>("/api/lookup/employees?role=PM") ?? new();
            cmbPm.ItemsSource = _employees;

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
            txtCode.Text = await ApiClient.GetDataAsync<string>("/api/projects/next-code") ?? "";
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading next project code: {ex.Message}");
        }
    }

    private async Task LoadProjectData()
    {
        ProjectSaveRequest? d = await ApiClient.GetDataAsync<ProjectSaveRequest>($"/api/projects/{_id}");
        if (d == null)
            return;

        txtCode.Text = d.Code;
        txtTitle.Text = d.Title;
        txtRevenue.Text = d.Revenue.ToString("F0");
        txtBudget.Text = d.BudgetTotal.ToString("F0");
        txtHours.Text = d.BudgetHoursTotal.ToString("F0");
        txtDescription.Text = d.Description;
        txtServerPath.Text = d.ServerPath;
        txtNotes.Text = d.Notes;

        if (d.LinkedQuoteId > 0)
        {
            System.Windows.Media.SolidColorBrush grayBrush = (System.Windows.Media.SolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFrom("#F3F4F6")!;
            txtRevenue.IsReadOnly = true;
            txtRevenue.Background = grayBrush;
            txtBudget.IsReadOnly = true;
            txtBudget.Background = grayBrush;
            txtHours.IsReadOnly = true;
            txtHours.Background = grayBrush;
            txtQuoteLocked.Visibility = Visibility.Visible;
        }
        cmbCustomer.SelectedValue = d.CustomerId;
        cmbPm.SelectedValue = d.PmId;

        string currentStatus = d.Status;
        SelectComboItem(cmbStatus, currentStatus);
        ApplyStatusTransitions(currentStatus);
        SelectComboItem(cmbPriority, d.Priority);

        if (d.StartDate.HasValue)
            dpStart.SelectedDate = d.StartDate;
        if (d.EndDatePlanned.HasValue)
            dpEnd.SelectedDate = d.EndDatePlanned;
    }

    private void SelectComboItem(ComboBox cmb, string value)
    {
        foreach (ComboBoxItem item in cmb.Items)
            if (item.Content?.ToString() == value) { item.IsSelected = true; break; }
    }

    /// <summary>
    /// Disabilita le transizioni di stato non valide.
    /// DRAFT → ACTIVE, CANCELLED
    /// ACTIVE → ON_HOLD, COMPLETED, CANCELLED
    /// ON_HOLD → ACTIVE, CANCELLED
    /// COMPLETED / CANCELLED → bloccato
    /// </summary>
    private void ApplyStatusTransitions(string currentStatus)
    {
        Dictionary<string, HashSet<string>> allowed = new()
        {
            ["DRAFT"] = new() { "DRAFT", "ACTIVE", "CANCELLED" },
            ["ACTIVE"] = new() { "ACTIVE", "ON_HOLD", "COMPLETED", "CANCELLED" },
            ["ON_HOLD"] = new() { "ON_HOLD", "ACTIVE", "CANCELLED" },
            ["COMPLETED"] = new() { "COMPLETED" },
            ["CANCELLED"] = new() { "CANCELLED" }
        };

        HashSet<string> valid = allowed.GetValueOrDefault(currentStatus, new() { currentStatus });

        foreach (ComboBoxItem item in cmbStatus.Items)
        {
            string val = item.Content?.ToString() ?? "";
            item.IsEnabled = valid.Contains(val);
        }
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

            if (ApiClient.IsApiSuccess(result, out string msg))
            { DialogResult = true; Close(); }
            else
                txtError.Text = msg;
        }
        catch (Exception ex) { txtError.Text = $"Errore: {ex.Message}"; }
        finally { btnSave.IsEnabled = true; }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
}
