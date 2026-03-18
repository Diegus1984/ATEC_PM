using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Views.Costing.ViewModels;

namespace ATEC.PM.Client.Views.Costing;

public partial class ProjectCostingControl : UserControl
{
    private ProjectCostingData _data = new();
    private int _projectId;
    private string _apiBasePath = "";
    private bool _readOnly;
    private bool IsOfferMode => _apiBasePath.Contains("/offers/");
    private Dictionary<int, List<EmployeeCostLookup>> _sectionEmployeesCache = new();
    private CostingViewModel _vm = new();

    // Timer per debounce
    private Timer _saveTimer;
    private const int SaveDelayMs = 800;

    public ProjectCostingControl()
    {
        InitializeComponent();
        VerticalAlignment = System.Windows.VerticalAlignment.Stretch;
    }

    public void Load(int projectId, string tab = "risorse")
    {
        if (_projectId != projectId)
            _sectionEmployeesCache.Clear();
        _projectId = projectId;
        if (string.IsNullOrEmpty(_apiBasePath))
            _apiBasePath = $"/api/projects/{_projectId}/costing";
        _ = LoadData();
    }

    public void LoadForOffer(int offerId, bool readOnly = false)
    {
        _sectionEmployeesCache.Clear();
        _projectId = offerId;
        _readOnly = readOnly;
        _apiBasePath = $"/api/offers/{offerId}/costing";
        _ = LoadData();
    }

    // ══════════════════════════════════════════════════════════════
    // LOAD DATA CON STATI PRESERVATI
    // ══════════════════════════════════════════════════════════════

    private async Task LoadData()
    {
        try
        {
            // Salva stati di espansione prima del reload
            var expandedGroups = _vm.Groups?.Where(g => g.IsExpanded).Select(g => g.Name).ToHashSet() ?? new();
            var expandedSections = _vm.Groups?.SelectMany(g => g.Sections)
                .Where(s => s.IsDetailExpanded).Select(s => s.Id).ToHashSet() ?? new();
            var expandedMaterialSections = _vm.MaterialSections?
                .Where(s => s.IsDetailExpanded).Select(s => s.Id).ToHashSet() ?? new();

            _vm.StatusText = "Caricamento in corso...";
            DataContext = null; // Sospendi binding durante il caricamento
            await Task.Delay(10); // Permetti UI di aggiornarsi

            string json = await ApiClient.GetAsync($"{_apiBasePath}");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean())
            {
                _vm.StatusText = "Errore nel caricamento dati";
                return;
            }

            _data = JsonSerializer.Deserialize<ProjectCostingData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            _vm = CostingViewModel.FromData(_data);
            _vm.IsOfferMode = IsOfferMode;

            // Ripristina stati di espansione
            foreach (var g in _vm.Groups)
            {
                if (expandedGroups.Contains(g.Name))
                    g.IsExpanded = true;

                foreach (var s in g.Sections)
                    if (expandedSections.Contains(s.Id))
                        s.IsDetailExpanded = true;
            }

            foreach (var ms in _vm.MaterialSections)
                if (expandedMaterialSections.Contains(ms.Id))
                    ms.IsDetailExpanded = true;

            DataContext = _vm;

            // RecalcGrandTotals chiama RecalcDistribution che ribilancia automaticamente
            if (IsOfferMode)
                await SaveAllDistributions();

            _vm.StatusText = "Dati caricati";
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Errore: {ex.Message}";
            ShowError("Errore nel caricamento", ex.Message);
        }
    }

    private async Task<List<EmployeeCostLookup>> LoadEmployeesForSection(int sectionId)
    {
        // Cache semplice
        if (_sectionEmployeesCache.TryGetValue(sectionId, out var cached))
            return cached;

        try
        {
            string json = await ApiClient.GetAsync($"{_apiBasePath}/sections/{sectionId}/employees");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var employees = JsonSerializer.Deserialize<List<EmployeeCostLookup>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                _sectionEmployeesCache[sectionId] = employees;
                return employees;
            }
        }
        catch { }

        return new List<EmployeeCostLookup>();
    }

    // ══════════════════════════════════════════════════════════════
    // MARKUPS CON FEEDBACK MIGLIORATO
    // ══════════════════════════════════════════════════════════════

    private async void AllowanceMarkup_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != _vm.AllowanceMarkup)
            {
                _vm.AllowanceMarkup = newK;
                await SavePricingMarkups();
                ShowTemporaryMessage("Modifica salvata");
            }
            Keyboard.ClearFocus();
        }
    }

    private async void AllowanceMarkup_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != _vm.AllowanceMarkup)
        {
            _vm.AllowanceMarkup = newK;
            await SavePricingMarkups();
            ShowTemporaryMessage("Modifica salvata");
        }
    }

    private bool _isPricingUpdating;

    private async void PricingPct_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            await ApplyAndSavePricing(tb);
            Keyboard.ClearFocus();
        }
    }

    private async void PricingPct_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            await ApplyAndSavePricing(tb);
    }

    private async Task ApplyAndSavePricing(TextBox tb)
    {
        if (_isPricingUpdating) return;
        _isPricingUpdating = true;

        try
        {
            string raw = tb.Text.Replace("%", "").Replace(",", ".").Trim();
            if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
            if (val > 1m) val /= 100m;

            string tag = tb.Tag?.ToString() ?? "";
            bool changed = false;

            if (tag == "contingency" && val != _vm.ContingencyPct)
            { _vm.ContingencyPct = val; changed = true; }
            else if (tag == "negotiation" && val != _vm.NegotiationMarginPct)
            { _vm.NegotiationMarginPct = val; changed = true; }

            if (!changed) return;

            _vm.RecalcGrandTotals();
            await SavePricingMarkups();
            await SaveAllDistributions();
            ShowTemporaryMessage("Percentuale aggiornata");
        }
        finally { _isPricingUpdating = false; }
    }

    private async void TravelMarkup_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != _vm.TravelMarkup)
            {
                _vm.TravelMarkup = newK;
                await SavePricingMarkups();
                ShowTemporaryMessage("Modifica salvata");
            }
            Keyboard.ClearFocus();
        }
    }

    private async void TravelMarkup_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != _vm.TravelMarkup)
        {
            _vm.TravelMarkup = newK;
            await SavePricingMarkups();
            ShowTemporaryMessage("Modifica salvata");
        }
    }

    // ══════════════════════════════════════════════════════════════
    // CRUD CON CONFERME
    // ══════════════════════════════════════════════════════════════

    private async void BtnAddCommission_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int secId) return;

        if (!await ConfirmAction("Aggiungi provvigione", "Aggiungere una nuova provvigione?"))
            return;

        MaterialSectionVM? sec = FindMaterialSection(secId);
        if (sec == null) return;

        await ExecuteWithLoading(async () =>
        {
            var req = new ProjectMaterialItemSaveRequest
            {
                SectionId = secId,
                Description = "Provvigione",
                Quantity = 1,
                UnitCost = 0,
                MarkupValue = sec.DefaultCommissionMarkup,
                ItemType = "COMMISSION"
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PostAsync($"{_apiBasePath}/material-items", json);
            await LoadData();
            ShowTemporaryMessage("Provvigione aggiunta");
        });
    }

    private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = await ApiClient.GetAsync($"{_apiBasePath}/available-templates");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var data = doc.RootElement.GetProperty("data");
            var availableGroups = JsonSerializer.Deserialize<List<CostSectionGroupDto>>(
                data.GetProperty("groups").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
            var availableTemplates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                data.GetProperty("templates").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            var existingGroupNames = _vm.Groups.Select(g => g.Name).ToHashSet();
            var newGroups = availableGroups.Where(g => !existingGroupNames.Contains(g.Name)).ToList();

            if (newGroups.Count == 0 && availableTemplates.Count == 0)
            {
                ShowInfo("Info", "Tutti i gruppi template sono già presenti nella commessa.\nPuoi creare un gruppo personalizzato.");
                return;
            }

            var dlg = new AddCostGroupDialog(_projectId, newGroups, availableTemplates, _apiBasePath)
            { Owner = Window.GetWindow(this) };

            if (dlg.ShowDialog() == true)
            {
                await LoadData();
                ShowTemporaryMessage("Gruppo aggiunto");
            }
        }
        catch (Exception ex)
        {
            ShowError("Errore", ex.Message);
        }
    }

    private async void BtnAddMaterialItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int secId) return;

        if (!await ConfirmAction("Aggiungi materiale", "Aggiungere un nuovo materiale?"))
            return;

        MaterialSectionVM? sec = FindMaterialSection(secId);
        if (sec == null) return;

        await ExecuteWithLoading(async () =>
        {
            var req = new ProjectMaterialItemSaveRequest
            {
                SectionId = secId,
                Description = "",
                Quantity = 1,
                UnitCost = 0,
                MarkupValue = sec.DefaultMarkup,
                ItemType = "MATERIAL"
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PostAsync($"{_apiBasePath}/material-items", json);
            await LoadData();
            ShowTemporaryMessage("Materiale aggiunto");
        });
    }

    private async void BtnAddResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int secId) return;

        if (!await ConfirmAction("Aggiungi risorsa", "Aggiungere una nuova risorsa?"))
            return;

        CostSectionVM? sec = FindSection(secId);
        if (sec == null) return;

        await ExecuteWithLoading(async () =>
        {
            if (!_sectionEmployeesCache.ContainsKey(secId))
            {
                var emps = await LoadEmployeesForSection(secId);
                _sectionEmployeesCache[secId] = emps;
            }

            var allEmps = _sectionEmployeesCache[secId];

            if (IsOfferMode)
            {
                await AddOfferResource(sec, allEmps);
            }
            else
            {
                await AddProjectResource(sec, allEmps);
            }

            await LoadData();
            ShowTemporaryMessage("Risorsa aggiunta");
        });
    }

    private async Task AddOfferResource(CostSectionVM sec, List<EmployeeCostLookup> allEmps)
    {
        var anyEmp = allEmps.FirstOrDefault();
        string deptCode = anyEmp?.DepartmentCode ?? "RIS";
        decimal hourlyCost = anyEmp?.HourlyCost ?? 0;
        decimal markup = anyEmp?.DefaultMarkup ?? 1.450m;
        int counter = sec.Resources.Count + 1;

        var req = new ProjectCostResourceSaveRequest
        {
            SectionId = sec.Id,
            EmployeeId = null,
            ResourceName = $"{deptCode} {counter}",
            HourlyCost = hourlyCost,
            MarkupValue = markup,
            HoursPerDay = 8,
            CostPerKm = 0.90m
        };
        string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await ApiClient.PostAsync($"{_apiBasePath}/resources", json);
    }

    private async Task AddProjectResource(CostSectionVM sec, List<EmployeeCostLookup> allEmps)
    {
        var usedIds = sec.Resources.Where(r => r.EmployeeId.HasValue).Select(r => r.EmployeeId!.Value).ToHashSet();
        var available = allEmps.Where(emp => !usedIds.Contains(emp.Id)).ToList();

        if (available.Count == 0)
        {
            if (await ConfirmAction("Attenzione", "Tutti i dipendenti sono già assegnati. Aggiungere risorsa generica?"))
            {
                var req = new ProjectCostResourceSaveRequest
                {
                    SectionId = sec.Id,
                    EmployeeId = null,
                    ResourceName = $"Risorsa {sec.Resources.Count + 1}",
                    HourlyCost = allEmps.FirstOrDefault()?.HourlyCost ?? 0,
                    MarkupValue = allEmps.FirstOrDefault()?.DefaultMarkup ?? 1.450m,
                    HoursPerDay = 8,
                    CostPerKm = 0.90m
                };
                string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                await ApiClient.PostAsync($"{_apiBasePath}/resources", json);
            }
            return;
        }

        var first = available.First();
        var req2 = new ProjectCostResourceSaveRequest
        {
            SectionId = sec.Id,
            EmployeeId = first.Id,
            ResourceName = first.FullName,
            HourlyCost = first.HourlyCost,
            MarkupValue = first.DefaultMarkup,
            HoursPerDay = 8,
            CostPerKm = 0.90m
        };
        string json2 = JsonSerializer.Serialize(req2, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await ApiClient.PostAsync($"{_apiBasePath}/resources", json2);
    }

    private async void BtnAddSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string groupName) return;

        try
        {
            string json = await ApiClient.GetAsync($"{_apiBasePath}/available-templates");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var data = doc.RootElement.GetProperty("data");
            var allTemplates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                data.GetProperty("templates").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            var groupTemplates = allTemplates.Where(t => t.GroupName == groupName).ToList();
            var dlg = new AddCostSectionDialog(_projectId, groupName, groupTemplates, _apiBasePath)
            { Owner = Window.GetWindow(this) };

            if (dlg.ShowDialog() == true)
            {
                await LoadData();
                ShowTemporaryMessage("Sezione aggiunta");
            }
        }
        catch (Exception ex)
        {
            ShowError("Errore", ex.Message);
        }
    }

    private async void BtnDeleteMaterialItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int itemId || itemId <= 0) return;

        if (!await ConfirmAction("Conferma eliminazione", "Eliminare questo elemento?"))
            return;

        await ExecuteWithLoading(async () =>
        {
            await ApiClient.DeleteAsync($"{_apiBasePath}/material-items/{itemId}");
            await LoadData();
            ShowTemporaryMessage("Elemento eliminato");
        });
    }

    private async void BtnDeleteResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int resourceId || resourceId <= 0) return;

        if (!await ConfirmAction("Conferma eliminazione", "Eliminare questa risorsa?"))
            return;

        await ExecuteWithLoading(async () =>
        {
            await ApiClient.DeleteAsync($"{_apiBasePath}/resources/{resourceId}");
            await LoadData();
            ShowTemporaryMessage("Risorsa eliminata");
        });
    }

    private async void BtnInit_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAction("Inizializza configurazione",
            "Questa operazione creerà la struttura base dei costi. Continuare?"))
            return;

        try
        {
            _vm.StatusText = "Inizializzazione in corso...";
            string json = await ApiClient.PostAsync($"{_apiBasePath}/init", "{}");
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                await LoadData();
                ShowTemporaryMessage("Configurazione inizializzata");
            }
            else
            {
                ShowError("Errore", doc.RootElement.GetProperty("message").GetString()!);
            }
        }
        catch (Exception ex)
        {
            ShowError("Errore", ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // COMBO DIPENDENTI MIGLIORATA
    // ══════════════════════════════════════════════════════════════

    private async void EmployeeCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not CostResourceVM row) return;

        // Carica dipendenti se non in cache
        if (!_sectionEmployeesCache.TryGetValue(row.SectionId, out var allEmployees))
        {
            allEmployees = await LoadEmployeesForSection(row.SectionId);
            _sectionEmployeesCache[row.SectionId] = allEmployees;
        }

        if (IsOfferMode)
        {
            SetupOfferModeCombo(combo, row, allEmployees);
        }
        else
        {
            SetupProjectModeCombo(combo, row, allEmployees);
        }
    }

    private void SetupOfferModeCombo(ComboBox combo, CostResourceVM row, List<EmployeeCostLookup> allEmployees)
    {
        string deptCode = allEmployees.FirstOrDefault()?.DepartmentCode ?? "RIS";
        var aliased = allEmployees.Select((emp, idx) => new EmployeeCostLookup
        {
            Id = emp.Id,
            FullName = $"{deptCode} {idx + 1}",
            DepartmentCode = emp.DepartmentCode,
            HourlyCost = emp.HourlyCost,
            DefaultMarkup = emp.DefaultMarkup
        }).ToList();
        combo.ItemsSource = aliased;
    }

    private void SetupProjectModeCombo(ComboBox combo, CostResourceVM row, List<EmployeeCostLookup> allEmployees)
    {
        CostSectionVM? sec = FindSection(row.SectionId);
        var usedIds = sec?.Resources
            .Where(r => r.EmployeeId.HasValue && r.Id != row.Id)
            .Select(r => r.EmployeeId!.Value)
            .ToHashSet() ?? new();

        var available = allEmployees.Where(emp => !usedIds.Contains(emp.Id)).ToList();
        combo.ItemsSource = available;

        if (row.EmployeeId.HasValue)
            combo.SelectedItem = available.FirstOrDefault(emp => emp.Id == row.EmployeeId);
    }

    private async void EmployeeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not CostResourceVM row) return;
        if (combo.SelectedItem is not EmployeeCostLookup emp) return;

        if (IsOfferMode)
        {
            row.EmployeeId = null;
            row.ResourceName = emp.FullName;
            row.HourlyCost = emp.HourlyCost;
            row.MarkupValue = emp.DefaultMarkup;
        }
        else
        {
            row.EmployeeId = emp.Id;
            row.ResourceName = emp.FullName;
            row.HourlyCost = emp.HourlyCost;
            row.MarkupValue = emp.DefaultMarkup;
        }

        if (row.Id > 0)
            await SaveResource(row);
    }

    // ══════════════════════════════════════════════════════════════
    // SAVE CON DEBOUNCE
    // ══════════════════════════════════════════════════════════════

    private async void ResourceGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.Item is not CostResourceVM row || row.Id <= 0) return;

        _saveTimer?.Dispose();
        _saveTimer = new Timer(async _ =>
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await SaveResource(row);
                if (IsOfferMode) await SaveAllDistributions();
            });
        }, null, SaveDelayMs, Timeout.Infinite);
    }

    private async void MaterialGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.Item is not MaterialItemVM row || row.Id <= 0) return;

        _saveTimer?.Dispose();
        _saveTimer = new Timer(async _ =>
        {
            await Dispatcher.InvokeAsync(async () =>
            {
                await SaveMaterialItem(row);
                if (IsOfferMode) await SaveAllDistributions();
            });
        }, null, SaveDelayMs, Timeout.Infinite);
    }

    private async Task SaveResource(CostResourceVM row)
    {
        try
        {
            var req = new ProjectCostResourceSaveRequest
            {
                Id = row.Id,
                SectionId = row.SectionId,
                EmployeeId = row.EmployeeId,
                ResourceName = row.ResourceName,
                WorkDays = row.WorkDays,
                HoursPerDay = row.HoursPerDay,
                HourlyCost = row.HourlyCost,
                MarkupValue = row.MarkupValue,
                NumTrips = row.NumTrips,
                KmPerTrip = row.KmPerTrip,
                CostPerKm = row.CostPerKm,
                DailyFood = row.DailyFood,
                DailyHotel = row.DailyHotel,
                AllowanceDays = row.AllowanceDays,
                DailyAllowance = row.DailyAllowance
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"{_apiBasePath}/resources/{row.Id}", json);

            // Feedback visivo (opzionale)
            ShowTemporaryMessage("✓", 500);
        }
        catch (Exception ex)
        {
            ShowError("Errore salvataggio", ex.Message);
        }
    }

    private async Task SaveMaterialItem(MaterialItemVM row)
    {
        try
        {
            var req = new ProjectMaterialItemSaveRequest
            {
                Id = row.Id,
                SectionId = row.SectionId,
                Description = row.Description,
                Quantity = row.Quantity,
                UnitCost = row.UnitCost,
                MarkupValue = row.MarkupValue,
                ItemType = row.ItemType
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"{_apiBasePath}/material-items/{row.Id}", json);

            // Feedback visivo
            ShowTemporaryMessage("✓", 500);
        }
        catch (Exception ex)
        {
            ShowError("Errore salvataggio", ex.Message);
        }
    }

    private async Task SavePricingMarkups()
    {
        try
        {
            var req = new
            {
                contingencyPct = _vm.ContingencyPct,
                negotiationMarginPct = _vm.NegotiationMarginPct,
                travelMarkup = _vm.TravelMarkup,
                allowanceMarkup = _vm.AllowanceMarkup
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"{_apiBasePath}/pricing", json);
        }
        catch (Exception ex)
        {
            ShowError("Errore salvataggio markups", ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // DISTRIBUZIONE % SEZIONI
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Ridistribuisci: resetta tutti i pin e ricalcola proporzionale.
    /// </summary>
    private async void BtnGenerateDistribution_Click(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmAction("Ridistribuisci",
            "Resettare tutti i blocchi e ridistribuire proporzionalmente?"))
            return;

        await ExecuteWithLoading(async () =>
        {
            // Annulla tutti i pin — LA PROC ridistribuisce
            foreach (var sec in _vm.Groups.SelectMany(g => g.Sections))
            { sec.IsContingencyPinned = false; sec.IsMarginPinned = false; }
            foreach (var item in _vm.MaterialSections.SelectMany(s => s.Items))
            { item.IsContingencyPinned = false; item.IsMarginPinned = false; }

            _vm.RecalcGrandTotals();
            await SaveAllDistributions();
            ShowTemporaryMessage("Distribuzione ridistribuita — pin resettati");
        });
    }

    private async void DistPct_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            await ApplyDistPct(tb);
            Keyboard.ClearFocus();
        }
    }

    private async void DistPct_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
            await ApplyDistPct(tb);
    }

    /// <summary>
    /// L'utente modifica manualmente una % nella tabella distribuzione.
    /// La sezione viene pinnata, le altre non-pinned si ribilanciano.
    /// </summary>
    private async Task ApplyDistPct(TextBox tb)
    {
        if (tb.DataContext is not DistributionRowVM distRow) return;

        string field = tb.Tag?.ToString() ?? "";
        string raw = tb.Text.Replace("%", "").Replace(",", ".").Trim();
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
        if (val > 1m) val /= 100m;

        // PIN PRIMA, VALORE DOPO — il setter triggera RecalcGrandTotals via wiring
        // e RecalcDistribution deve già vedere il pin attivo
        if (distRow.RowType == "R")
        {
            CostSectionVM? sec = FindSection(distRow.SectionId);
            if (sec == null) return;
            if (field == "contingency") { sec.IsContingencyPinned = true; sec.ContingencyPct = val; }
            else { sec.IsMarginPinned = true; sec.MarginPct = val; }
        }
        else
        {
            MaterialItemVM? item = FindMaterialItem(distRow.ItemId);
            if (item == null) return;
            if (field == "contingency") { item.IsContingencyPinned = true; item.ContingencyPct = val; }
            else { item.IsMarginPinned = true; item.MarginPct = val; }
        }

        // LA PROC fa il resto
        _vm.RecalcGrandTotals();
        await SaveAllDistributions();
        ShowTemporaryMessage("Distribuzione aggiornata — bloccata 🔒");
    }

    private MaterialItemVM? FindMaterialItem(int itemId)
    {
        foreach (var ms in _vm.MaterialSections)
        {
            var item = ms.Items.FirstOrDefault(i => i.Id == itemId);
            if (item != null) return item;
        }
        return null;
    }

    /// <summary>Salva distribuzione di tutte le sezioni + tutti i material items.</summary>
    private async Task SaveAllDistributions()
    {
        foreach (var s in _vm.Groups.SelectMany(g => g.Sections))
            await SaveSectionDistribution(s);
        foreach (var item in _vm.MaterialSections.SelectMany(ms => ms.Items))
            await SaveMaterialItemDistribution(item);
    }

    private async Task SaveMaterialItemDistribution(MaterialItemVM item)
    {
        try
        {
            var req = new
            {
                contingencyPct = item.ContingencyPct,
                marginPct = item.MarginPct,
                contingencyPinned = item.IsContingencyPinned,
                marginPinned = item.IsMarginPinned
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"{_apiBasePath}/material-items/{item.Id}/distribution", json);
        }
        catch (Exception ex) { ShowError("Errore salvataggio distribuzione materiale", ex.Message); }
    }

    private void RebalanceSections(List<CostSectionVM> allSections, int fixedId, string field, decimal fixedValue)
    {
        decimal remaining = 1m - fixedValue;
        var others = allSections.Where(s => s.Id != fixedId).ToList();
        decimal othersTotal = field == "contingency"
            ? others.Sum(s => s.ContingencyPct)
            : others.Sum(s => s.MarginPct);

        foreach (var s in others)
        {
            decimal currentVal = field == "contingency" ? s.ContingencyPct : s.MarginPct;
            decimal newVal = othersTotal > 0
                ? currentVal / othersTotal * remaining
                : remaining / others.Count;

            if (field == "contingency")
                s.ContingencyPct = Math.Round(newVal, 4);
            else
                s.MarginPct = Math.Round(newVal, 4);
        }
    }

    private async Task SaveSectionDistribution(CostSectionVM sec)
    {
        try
        {
            var req = new
            {
                contingencyPct = sec.ContingencyPct,
                marginPct = sec.MarginPct,
                contingencyPinned = sec.IsContingencyPinned,
                marginPinned = sec.IsMarginPinned
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"{_apiBasePath}/sections/{sec.Id}/distribution", json);
        }
        catch (Exception ex)
        {
            ShowError("Errore salvataggio distribuzione", ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // FUNZIONI DI SUPPORTO (UX)
    // ══════════════════════════════════════════════════════════════

    private MaterialSectionVM? FindMaterialSection(int sectionId)
        => _vm.MaterialSections.FirstOrDefault(s => s.Id == sectionId);

    private CostSectionVM? FindSection(int sectionId)
    {
        foreach (var g in _vm.Groups)
        {
            var sec = g.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (sec != null) return sec;
        }
        return null;
    }

    // Helper per mostrare messaggi temporanei
    private void ShowTemporaryMessage(string message, int durationMs = 2000)
    {
        _vm.StatusText = message;

        // Timer per cancellare il messaggio
        var timer = new System.Timers.Timer(durationMs);
        timer.Elapsed += (s, e) =>
        {
            Dispatcher.Invoke(() => { if (_vm.StatusText == message) _vm.StatusText = ""; });
            timer.Stop();
            timer.Dispose();
        };
        timer.Start();
    }

    private Task<bool> ConfirmAction(string title, string message)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Invoke(() =>
        {
            var result = MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            tcs.SetResult(result == MessageBoxResult.Yes);
        });

        return tcs.Task;
    }

    private void ShowError(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    private void ShowInfo(string title, string message)
    {
        Dispatcher.Invoke(() =>
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async Task ExecuteWithLoading(Func<Task> action)
    {
        try
        {
            _vm.StatusText = "Operazione in corso...";
            await action();
        }
        finally
        {
            _vm.StatusText = "";
        }
    }
    private async void BtnSaveAll_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.StatusText = "Salvataggio in corso...";

            // Salva markups
            await SavePricingMarkups();

            // Salva tutte le risorse modificate
            foreach (var group in _vm.Groups)
            {
                foreach (var section in group.Sections)
                {
                    foreach (var resource in section.Resources.Where(r => r.IsDirty))
                    {
                        await SaveResource(resource);
                    }
                }
            }

            // Salva tutti i materiali modificati
            foreach (var section in _vm.MaterialSections)
            {
                foreach (var item in section.Items.Where(i => i.IsDirty))
                {
                    await SaveMaterialItem(item);
                }
            }

            _vm.StatusText = "Salvataggio completato";
            ShowTemporaryMessage("✓ Tutto salvato", 2000);
        }
        catch (Exception ex)
        {
            ShowError("Errore salvataggio", ex.Message);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // EVENTI UI (invariati)
    // ══════════════════════════════════════════════════════════════

    private void MarkupTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    private void GroupHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border && border.DataContext is CostGroupVM group)
            group.IsExpanded = !group.IsExpanded;
    }

    private void SectionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox || (e.OriginalSource as FrameworkElement)?.TemplatedParent is TextBox) return;
        if (sender is Grid grid && grid.DataContext is CostSectionVM sec)
            sec.IsDetailExpanded = !sec.IsDetailExpanded;
    }

    private void MaterialSectionRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is MaterialSectionVM sec)
            sec.IsDetailExpanded = !sec.IsDetailExpanded;
    }

    // Metodi vuoti mantenuti per compatibilità
    private void MarkupTextBox_KeyDown(object sender, KeyEventArgs e) { }
    private void MarkupTextBox_LostFocus(object sender, RoutedEventArgs e) { }
    private void MarkupTextBox_PreviewMouseDown(object sender, MouseButtonEventArgs e) { }
}