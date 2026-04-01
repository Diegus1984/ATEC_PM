using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class PhasesDialog : Window
{
    private readonly int _projectId;
    private readonly PhaseListItem? _existing;
    private List<PhaseTemplateDto> _templates = new();
    private List<DepartmentDto> _departments = new();
    private List<LookupItem> _employees = new();
    private ObservableCollection<PhaseAssignmentDto> _assignments = new();

    public PhasesDialog(int projectId, PhaseListItem? existing = null)
    {
        InitializeComponent();
        _projectId = projectId;
        _existing  = existing;
        Title      = existing == null ? "Nuova Fase" : "Modifica Fase";
        dgAssignments.ItemsSource = _assignments;
        Loaded += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        await LoadTemplates();
        await LoadDepartments();
        if (_existing != null) PopulateForm();
        // Carica tecnici per il reparto attualmente selezionato
        await LoadEmployeesForCurrentDepartment();
    }

    private async Task LoadTemplates()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/phases/templates");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;
            _templates = JsonSerializer.Deserialize<List<PhaseTemplateDto>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Raggruppa per reparto nel ComboBox
            cmbTemplate.Items.Clear();
            string lastCategory = "";
            foreach (PhaseTemplateDto t in _templates.OrderBy(t => t.SortOrder))
            {
                string cat = string.IsNullOrEmpty(t.Category) ? "TRASVERSALE" : t.Category;
                if (cat != lastCategory)
                {
                    cmbTemplate.Items.Add(new ComboBoxItem
                    {
                        Content    = $"── {cat} ──",
                        IsEnabled  = false,
                        FontWeight = FontWeights.Bold,
                        Foreground = System.Windows.Media.Brushes.Gray
                    });
                    lastCategory = cat;
                }
                cmbTemplate.Items.Add(new ComboBoxItem
                {
                    Content = t.Name,
                    Tag     = t
                });
            }
        }
        catch { }
    }

    private async Task LoadDepartments()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/departments");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;
            _departments = JsonSerializer.Deserialize<List<DepartmentDto>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            cmbDepartment.Items.Clear();
            cmbDepartment.Items.Add(new ComboBoxItem { Content = "— Nessun reparto (trasversale) —", Tag = 0 });
            foreach (DepartmentDto d in _departments)
                cmbDepartment.Items.Add(new ComboBoxItem { Content = $"{d.Code} — {d.Name}", Tag = d.Id });
            cmbDepartment.SelectedIndex = 0;
        }
        catch { }
    }

    /// <summary>
    /// Carica i tecnici filtrati per il reparto attualmente selezionato.
    /// Fase trasversale (deptId=0/null) → tutti i tecnici.
    /// </summary>
    private async Task LoadEmployeesForCurrentDepartment()
    {
        try
        {
            int deptId = GetSelectedDepartmentId();
            string url = $"/api/employees/by-department?departmentId={deptId}";
            string json = await ApiClient.GetAsync(url);
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;
            _employees = JsonSerializer.Deserialize<List<LookupItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            cmbEmployee.ItemsSource = _employees;
            if (_employees.Count > 0) cmbEmployee.SelectedIndex = 0;
        }
        catch { }
    }

    private int GetSelectedDepartmentId()
    {
        if (cmbDepartment.SelectedItem is ComboBoxItem ci && ci.Tag is int d)
            return d;
        return 0;
    }

    private void PopulateForm()
    {
        if (_existing == null) return;

        // Seleziona template per ID
        foreach (object item in cmbTemplate.Items)
        {
            if (item is ComboBoxItem ci && ci.Tag is PhaseTemplateDto t && t.Id == _existing.PhaseTemplateId)
            {
                cmbTemplate.SelectedItem = ci;
                break;
            }
        }

        txtCustomName.Text   = _existing.CustomName;
        txtBudgetHours.Text  = _existing.BudgetHours.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        txtBudgetCost.Text   = _existing.BudgetCost.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        txtProgress.Text     = _existing.ProgressPct.ToString();
        txtSortOrder.Text    = _existing.SortOrder.ToString();

        SelectComboByTag(cmbStatus, _existing.Status);
        // Department selection removed — no longer used on phases

        _assignments.Clear();
        foreach (PhaseAssignmentDto a in _existing.Assignments)
            _assignments.Add(a);
    }

    private async void CmbTemplate_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbTemplate.SelectedItem is ComboBoxItem ci && ci.Tag is PhaseTemplateDto)
        {
            // Ricarica tecnici per il reparto selezionato
            await LoadEmployeesForCurrentDepartment();
        }
    }

    private async void CmbDepartment_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Ricarica tecnici quando si cambia reparto manualmente
        await LoadEmployeesForCurrentDepartment();
    }

    private void BtnAddAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (cmbEmployee.SelectedItem is not LookupItem emp) return;
        if (_assignments.Any(a => a.EmployeeId == emp.Id))
        {
            txtError.Text = "Tecnico già aggiunto.";
            return;
        }
        txtError.Text = "";
        decimal.TryParse(txtPlannedHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal hours);
        _assignments.Add(new PhaseAssignmentDto
        {
            EmployeeId    = emp.Id,
            EmployeeName  = emp.Name,
            AssignRole    = (cmbAssignRole.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "MEMBER",
            PlannedHours  = hours
        });
    }

    private void BtnRemoveAssignment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is PhaseAssignmentDto a)
            _assignments.Remove(a);
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        txtError.Text = "";

        if (cmbTemplate.SelectedItem is not ComboBoxItem ci || ci.Tag is not PhaseTemplateDto tmpl)
        {
            txtError.Text = "Seleziona un tipo di fase.";
            return;
        }

        btnSave.IsEnabled = false;
        btnSave.Content   = "Salvataggio...";

        try
        {
            decimal.TryParse(txtBudgetHours.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal budgetHours);
            decimal.TryParse(txtBudgetCost.Text,  NumberStyles.Any, CultureInfo.InvariantCulture, out decimal budgetCost);
            int.TryParse(txtProgress.Text,  out int progress);
            int.TryParse(txtSortOrder.Text, out int sortOrder);

            PhaseSaveRequest req = new()
            {
                Id              = _existing?.Id ?? 0,
                ProjectId       = _projectId,
                PhaseTemplateId = tmpl.Id,
                CustomName      = txtCustomName.Text.Trim(),
                BudgetHours     = budgetHours,
                BudgetCost      = budgetCost,
                Status          = (cmbStatus.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "NOT_STARTED",
                ProgressPct     = progress,
                SortOrder       = sortOrder,
                Assignments     = _assignments.ToList()
            };

            string json = JsonSerializer.Serialize(req,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result;
            if (_existing == null)
                result = await ApiClient.PostAsync("/api/phases", json);
            else
                result = await ApiClient.PutAsync($"/api/phases/{_existing.Id}", json);

            JsonDocument doc = JsonDocument.Parse(result);
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
            btnSave.Content   = "Salva";
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private static void SelectComboByTag(ComboBox cmb, object tag)
    {
        foreach (object item in cmb.Items)
            if (item is ComboBoxItem ci && ci.Tag?.Equals(tag) == true)
            {
                cmb.SelectedItem = ci;
                return;
            }
    }

    private static void SelectComboByContent(ComboBox cmb, string content)
    {
        foreach (object item in cmb.Items)
            if (item is ComboBoxItem ci && ci.Content?.ToString() == content)
            {
                cmb.SelectedItem = ci;
                return;
            }
    }
}
