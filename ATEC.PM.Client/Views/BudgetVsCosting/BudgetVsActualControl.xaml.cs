using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.UserControls;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.BudgetVsCosting;

public partial class BudgetVsActualControl : UserControl
{
    private int _projectId;
    private bool _suppressExpanderSave;

    public BudgetVsActualControl()
    {
        InitializeComponent();
    }

    public async void Load(int projectId)
    {
        _projectId = projectId;
        try
        {
            Task<string> bvaTask = ApiClient.GetAsync($"/api/projects/{projectId}/budget-vs-actual");
            Task<string> phasesTask = ApiClient.GetAsync($"/api/phases/project/{projectId}");
            await Task.WhenAll(bvaTask, phasesTask);

            JsonDocument bvaDoc = JsonDocument.Parse(bvaTask.Result);
            if (!bvaDoc.RootElement.GetProperty("success").GetBoolean()) return;

            BudgetVsActualData data = JsonSerializer.Deserialize<BudgetVsActualData>(
                bvaDoc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            List<PhaseListItem> phases = new();
            try
            {
                JsonDocument phasesDoc = JsonDocument.Parse(phasesTask.Result);
                if (phasesDoc.RootElement.GetProperty("success").GetBoolean())
                    phases = JsonSerializer.Deserialize<List<PhaseListItem>>(
                        phasesDoc.RootElement.GetProperty("data").GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            }
            catch { }

            DataContext = BvaViewModel.FromData(data, phases, projectId);

            // Ripristina stato expander principali
            _suppressExpanderSave = true;
            expEconomic.IsExpanded = Services.UserPreferences.GetBool($"bva.{projectId}.exp.economic", true);
            expResources.IsExpanded = Services.UserPreferences.GetBool($"bva.{projectId}.exp.resources", true);
            expMaterials.IsExpanded = Services.UserPreferences.GetBool($"bva.{projectId}.exp.materials", true);
            expPricing.IsExpanded = Services.UserPreferences.GetBool($"bva.{projectId}.exp.pricing", true);
            _suppressExpanderSave = false;

            txtLoading.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            txtLoading.Text = $"Errore: {ex.Message}";
        }
    }

    private async void OrderPrice_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_projectId <= 0 || sender is not System.Windows.Controls.TextBox tb) return;
        if (decimal.TryParse(tb.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal val))
        {
            try
            {
                string json = JsonSerializer.Serialize(val);
                await ApiClient.PatchAsync($"/api/projects/{_projectId}/revenue", json);
                if (DataContext is BvaViewModel vm && vm.Economic != null)
                    vm.Economic.OrderPrice = val;
            }
            catch { }
        }
    }

    private async void ActualTravelCost_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_projectId <= 0 || sender is not System.Windows.Controls.TextBox tb) return;
        if (decimal.TryParse(tb.Text, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal val))
        {
            try
            {
                string json = JsonSerializer.Serialize(val);
                await ApiClient.PatchAsync($"/api/projects/{_projectId}/budget-vs-actual/actual-travel-cost", json);
                if (DataContext is BvaViewModel vm && vm.Economic != null)
                    vm.Economic.ActualTravelCost = val;
            }
            catch { }
        }
    }

    private void MainExpander_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressExpanderSave || _projectId <= 0) return;
        if (sender is not Expander exp) return;

        string key = exp.Name switch
        {
            "expEconomic" => "economic",
            "expResources" => "resources",
            "expMaterials" => "materials",
            "expPricing" => "pricing",
            _ => ""
        };
        if (key != "")
            Services.UserPreferences.Set($"bva.{_projectId}.exp.{key}", exp.IsExpanded);
    }

    private void SectionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BvaSectionVM section)
            section.IsDetailExpanded = !section.IsDetailExpanded;
    }

    private async void BtnRemoveBvaAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BvaAssignmentRow row) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/phases/assignments/{row.AssignmentId}");
            JsonDocument doc = JsonDocument.Parse(result);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                if (DataContext is BvaViewModel vm)
                    foreach (BvaGroupVM grp in vm.Groups)
                        foreach (BvaSectionVM sec in grp.Sections)
                            foreach (BvaPhaseGroupVM pg in sec.PhaseGroups)
                                if (pg.Assignments.Contains(row))
                                { pg.Assignments.Remove(row); return; }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }

    private async void PlannedHours_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox tb || tb.Tag is not BvaAssignmentRow row) return;

        try
        {
            string jsonBody = JsonSerializer.Serialize(new { plannedHours = row.PlannedHours },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PatchAsync($"/api/phases/assignments/{row.AssignmentId}/hours", jsonBody);
        }
        catch { }
    }

    private async void BtnAssignTecnicoToPhase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BvaPhaseGroupVM phase) return;

        try
        {
            string json = await ApiClient.GetAsync($"/api/employees/by-phase/{phase.PhaseId}");
            JsonDocument doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            List<LookupItem> employees = JsonSerializer.Deserialize<List<LookupItem>>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            HashSet<string> assignedNames = phase.Assignments.Select(a => a.EmployeeName).ToHashSet();
            List<LookupItem> available = employees.Where(emp => !assignedNames.Contains(emp.Name)).ToList();

            if (!available.Any())
            {
                MessageBox.Show("Nessun tecnico disponibile per questo reparto.", "Info");
                return;
            }

            AddAssignmentDialog dlg = new(available) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;

            string jsonBody = JsonSerializer.Serialize(new
            {
                employeeId = dlg.SelectedEmployeeId,
                assignRole = dlg.AssignRole,
                plannedHours = dlg.PlannedHours
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync($"/api/phases/{phase.PhaseId}/assignments", jsonBody);
            JsonDocument resultDoc = JsonDocument.Parse(result);
            if (resultDoc.RootElement.GetProperty("success").GetBoolean())
            {
                int newId = resultDoc.RootElement.GetProperty("data").GetInt32();
                phase.Assignments.Add(new BvaAssignmentRow
                {
                    AssignmentId = newId,
                    EmployeeName = dlg.SelectedEmployeeName,
                    PlannedHours = dlg.PlannedHours,
                    HoursWorked = 0
                });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }
}
