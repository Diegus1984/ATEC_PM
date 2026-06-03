using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Services;
using ATEC.PM.Client.UserControls;
using ATEC.PM.Client.Views.Templates;
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
            Task<BudgetVsActualData?> bvaTask = ApiClient.GetDataAsync<BudgetVsActualData>(
                $"/api/projects/{projectId}/budget-vs-actual");
            Task<List<PhaseListItem>?> phasesTask = ApiClient.GetDataAsync<List<PhaseListItem>>(
                $"/api/phases/project/{projectId}");
            Task<List<PhaseTemplateDto>?> templatesTask = ApiClient.GetDataAsync<List<PhaseTemplateDto>>(
                "/api/phases/templates");
            await Task.WhenAll(bvaTask, phasesTask, templatesTask);

            BudgetVsActualData data = bvaTask.Result ?? new();
            if (bvaTask.Result == null)
                return;

            List<PhaseListItem> phases = phasesTask.Result ?? new();
            List<PhaseTemplateDto> allTemplates = templatesTask.Result ?? new();

            DataContext = BvaViewModel.FromData(data, phases, projectId);

            // Calcola HasAvailableTemplates per ogni sezione
            HashSet<int> usedTemplateIds = phases
                .Where(p => p.PhaseTemplateId > 0)
                .Select(p => p.PhaseTemplateId)
                .ToHashSet();

            if (DataContext is BvaViewModel vm)
                foreach (BvaGroupVM grp in vm.Groups)
                    foreach (BvaSectionVM sec in grp.Sections)
                    {
                        int count = sec.CostSectionTemplateId.HasValue
                            ? allTemplates.Count(t => t.CostSectionTemplateId == sec.CostSectionTemplateId.Value && !usedTemplateIds.Contains(t.Id))
                            : allTemplates.Count(t => (t.CostSectionTemplateId == null || t.CostSectionTemplateId == 0) && !usedTemplateIds.Contains(t.Id));
                        sec.HasAvailableTemplates = count > 0;
                    }

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
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error updating order price: {ex}");
                MessageBox.Show($"Errore durante l'aggiornamento del prezzo dell'ordine: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error updating actual travel cost: {ex}");
                MessageBox.Show($"Errore durante l'aggiornamento dei costi di viaggio effettivi: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            if (ApiClient.IsApiSuccess(result, out _))
            {
                Load(_projectId); // ricarica BVA per aggiornare totali sezione e Δ
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }

    private async void PlannedHours_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is System.Windows.Controls.TextBox tb)
        {
            e.Handled = true;
            tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            await SavePlannedHoursAsync(tb);
        }
    }

    private async void PlannedHours_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox tb && tb.Tag is BvaAssignmentRow)
            await SavePlannedHoursAsync(tb);
    }

    private async Task SavePlannedHoursAsync(System.Windows.Controls.TextBox tb)
    {
        if (tb.Tag is not BvaAssignmentRow row) return;

        try
        {
            string jsonBody = JsonSerializer.Serialize(new { plannedHours = row.PlannedHours },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PatchAsync($"/api/phases/assignments/{row.AssignmentId}/hours", jsonBody);
            Load(_projectId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore durante il salvataggio delle ore pianificate: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnAssignTecnicoToPhase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BvaPhaseGroupVM phase) return;

        try
        {
            List<LookupItem>? employees = await ApiClient.GetDataAsync<List<LookupItem>>(
                $"/api/employees/by-phase/{phase.PhaseId}");
            if (employees == null)
                return;

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
            if (ApiClient.TryGetApiData<int>(result, out int newId, out _))
            {
                // Ricarica intero BVA per aggiornare totali sezione/header e Δ
                Load(_projectId);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }

    // Promuovi una fase LOCALE a TEMPLATE globale riusabile in altre commesse
    private async void BtnPromoteToTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BvaPhaseGroupVM phase) return;
        if (!phase.IsLocal) return;

        MessageBoxResult ok = MessageBox.Show(
            $"Promuovere la fase \"{phase.PhaseName}\" a template globale?\n\n" +
            "Da questo momento sarà riusabile in altre commesse e visibile in Configurazione Sezioni.",
            "Conferma promozione", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            string result = await ApiClient.PostAsync($"/api/phases/{phase.PhaseId}/promote-to-template", "{}");
            if (ApiClient.IsApiSuccess(result, out string msg))
                Load(_projectId);
            else
                MessageBox.Show(string.IsNullOrWhiteSpace(msg) ? "Errore nella promozione." : msg,
                    "Operazione non riuscita", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }

    // Elimina una fase dalla commessa (solo se non ha ore versate)
    private async void BtnDeletePhase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BvaPhaseGroupVM phase) return;

        MessageBoxResult ok = MessageBox.Show(
            $"Rimuovere la fase \"{phase.PhaseName}\" da questa commessa?\n\nLe assegnazioni verranno eliminate.",
            "Conferma rimozione", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (ok != MessageBoxResult.Yes) return;

        try
        {
            string result = await ApiClient.DeleteAsync($"/api/phases/{phase.PhaseId}");
            if (ApiClient.IsApiSuccess(result, out string msg))
                Load(_projectId);
            else
                MessageBox.Show(string.IsNullOrWhiteSpace(msg) ? "Errore nella rimozione." : msg,
                    "Operazione non riuscita", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }

    // Importa fasi da template globale nella commessa
    private async void BtnImportPhase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BvaSectionVM section) return;

        try
        {
            List<PhaseTemplateDto>? allTemplates = await ApiClient.GetDataAsync<List<PhaseTemplateDto>>(
                "/api/phases/templates");
            if (allTemplates == null || allTemplates.Count == 0)
            {
                MessageBox.Show("Nessun template fase disponibile.", "Info");
                return;
            }

            // Filtra per la sezione corrente
            List<PhaseTemplateDto> sectionTemplates = section.CostSectionTemplateId.HasValue
                ? allTemplates.Where(t => t.CostSectionTemplateId == section.CostSectionTemplateId.Value).ToList()
                : allTemplates.Where(t => t.CostSectionTemplateId == null || t.CostSectionTemplateId == 0).ToList();

            if (sectionTemplates.Count == 0)
            {
                MessageBox.Show("Nessun template disponibile per questa sezione.", "Info");
                return;
            }

            // Escludi fasi già presenti nella commessa
            HashSet<int> existingTemplateIds = new();
            if (DataContext is BvaViewModel vm)
                foreach (BvaGroupVM grp in vm.Groups)
                    foreach (BvaSectionVM sec in grp.Sections)
                        foreach (BvaPhaseGroupVM pg in sec.PhaseGroups)
                            if (!pg.IsLocal) existingTemplateIds.Add(pg.PhaseId);

            // Serve l'ID del template, non della fase. Recupero dalla lista fasi del progetto.
            List<PhaseListItem>? projectPhases = await ApiClient.GetDataAsync<List<PhaseListItem>>(
                $"/api/phases/project/{section.ProjectId}");
            if (projectPhases != null)
                foreach (PhaseListItem pp in projectPhases)
                    if (pp.PhaseTemplateId > 0) existingTemplateIds.Add(pp.PhaseTemplateId);

            List<PhaseTemplateDto> available = sectionTemplates
                .Where(t => !existingTemplateIds.Contains(t.Id))
                .ToList();

            if (available.Count == 0)
            {
                MessageBox.Show("Tutte le fasi template di questa sezione sono già presenti nella commessa.", "Info");
                return;
            }

            // Dialog di selezione multipla
            ImportPhasesDialog dlg = new(available) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true || dlg.SelectedTemplateIds.Count == 0) return;

            string body = JsonSerializer.Serialize(new
            {
                projectId = section.ProjectId,
                templateIds = dlg.SelectedTemplateIds
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/phases/bulk", body);
            if (ApiClient.IsApiSuccess(result, out _))
                Load(section.ProjectId);
            else
                MessageBox.Show("Errore nell'importazione delle fasi.");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }

    // Crea una fase LOCALE alla commessa (non tocca phase_templates)
    private async void BtnAddLocalPhase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not BvaSectionVM section) return;

        InputDialog dlg = new("Nuova fase locale",
            $"Nome fase (visibile solo a questa commessa, sezione \"{section.SectionName}\"):")
        { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || string.IsNullOrWhiteSpace(dlg.InputText)) return;

        try
        {
            string body = JsonSerializer.Serialize(new
            {
                projectId = section.ProjectId,
                costSectionTemplateId = section.CostSectionTemplateId,
                name = dlg.InputText
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            string result = await ApiClient.PostAsync("/api/phases/local", body);
            if (ApiClient.IsApiSuccess(result, out string msg))
            {
                Load(section.ProjectId);   // ricarica BVA per vedere la nuova fase
            }
            else
                MessageBox.Show(string.IsNullOrWhiteSpace(msg) ? "Errore nella creazione della fase locale." : msg,
                    "Fase non creata", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}");
        }
    }
}
