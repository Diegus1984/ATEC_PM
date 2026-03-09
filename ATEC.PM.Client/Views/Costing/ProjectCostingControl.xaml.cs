using System.Globalization;
using ATEC.PM.Client.Views.Costing.ViewModels;

namespace ATEC.PM.Client.Views.Costing;

public partial class ProjectCostingControl : UserControl
{
    private ProjectCostingData _data = new();
    private int _projectId;
    private Dictionary<int, List<EmployeeCostLookup>> _sectionEmployeesCache = new();
    private CostingViewModel _vm = new();
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
        _ = LoadData();
    }

    // ══════════════════════════════════════════════════════════════
    // LOAD DATA
    // ══════════════════════════════════════════════════════════════
    // ── K Trasferta ──

    private async void AllowanceMarkup_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != _vm.AllowanceMarkup)
            {
                _vm.AllowanceMarkup = newK;
                await SavePricingMarkups();
            }
            Keyboard.ClearFocus();
        }
    }

    // ── Aggiungi sezione a gruppo esistente ──

    private async void AllowanceMarkup_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != _vm.AllowanceMarkup)
        {
            _vm.AllowanceMarkup = newK;
            await SavePricingMarkups();
        }
    }

    // ── Scheda Prezzi — handler % editabili ──

    private async void PricingPct_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            ApplyPricingPct(tb);
            await SavePricingMarkups();
            Keyboard.ClearFocus();
        }
    }

    private async void PricingPct_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb)
        {
            ApplyPricingPct(tb);
            await SavePricingMarkups();
        }
    }

    private void ApplyPricingPct(TextBox tb)
    {
        string raw = tb.Text.Replace("%", "").Replace(",", ".").Trim();
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;

        // L'utente digita "5.0" → il PctToStringConverter fa ConvertBack → 0.050
        // Ma se arriva qui dal KeyDown/LostFocus diretto, convertiamo manualmente
        if (val > 1m) val /= 100m;

        string tag = tb.Tag?.ToString() ?? "";
        switch (tag)
        {
            case "structure": _vm.StructureCostsPct = val; break;
            case "contingency": _vm.ContingencyPct = val; break;
            case "risk": _vm.RiskWarrantyPct = val; break;
            case "negotiation": _vm.NegotiationMarginPct = val; break;
        }
    }

    private async void BtnAddCommission_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int secId) return;
        MaterialSectionVM? sec = FindMaterialSection(secId);
        if (sec == null) return;

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
        await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/material-items", json);
        await LoadData();
    }

    private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/costing/available-templates");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var data = doc.RootElement.GetProperty("data");

            var availableGroups = JsonSerializer.Deserialize<List<CostSectionGroupDto>>(
                data.GetProperty("groups").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            var availableTemplates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                data.GetProperty("templates").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Filtra: gruppi non ancora nella commessa
            var existingGroupNames = _vm.Groups.Select(g => g.Name).ToHashSet();
            var newGroups = availableGroups.Where(g => !existingGroupNames.Contains(g.Name)).ToList();

            if (newGroups.Count == 0 && availableTemplates.Count == 0)
            {
                MessageBox.Show("Tutti i gruppi template sono già presenti nella commessa.\nPuoi creare un gruppo personalizzato.", "Info");
            }

            var dlg = new AddCostGroupDialog(_projectId, newGroups, availableTemplates)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() == true)
                await LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    // ── Aggiungi gruppo ──
    private async void BtnAddMaterialItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int secId) return;
        MaterialSectionVM? sec = FindMaterialSection(secId);
        if (sec == null) return;

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
        await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/material-items", json);
        await LoadData();
    }

    private async void BtnAddResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int secId) return;
        CostSectionVM? sec = FindSection(secId);
        if (sec == null) return;

        if (!_sectionEmployeesCache.ContainsKey(secId))
        {
            var emps = await LoadEmployeesForSection(secId);
            _sectionEmployeesCache[secId] = emps;
        }

        var allEmps = _sectionEmployeesCache[secId];
        var usedIds = sec.Resources.Where(r => r.EmployeeId.HasValue).Select(r => r.EmployeeId!.Value).ToHashSet();
        var available = allEmps.Where(emp => !usedIds.Contains(emp.Id)).ToList();

        if (available.Count == 0)
        {
            MessageBox.Show("Tutti i dipendenti disponibili sono già assegnati a questa sezione.", "Attenzione");
            return;
        }

        var first = available.First();
        var req = new ProjectCostResourceSaveRequest
        {
            SectionId = secId,
            EmployeeId = first.Id,
            ResourceName = first.FullName,
            HourlyCost = first.HourlyCost,
            MarkupValue = first.DefaultMarkup,
            HoursPerDay = 8,
            CostPerKm = 0.90m
        };
        string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/resources", json);
        await LoadData();
    }

    private async void BtnAddSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string groupName) return;

        try
        {
            // Carica template disponibili
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/costing/available-templates");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            var data = doc.RootElement.GetProperty("data");

            var allTemplates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                data.GetProperty("templates").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Filtra per il gruppo corrente
            var groupTemplates = allTemplates.Where(t => t.GroupName == groupName).ToList();

            if (groupTemplates.Count == 0)
            {
                // Nessun template disponibile, ma permetti sezione personalizzata
            }

            var dlg = new AddCostSectionDialog(_projectId, groupName, groupTemplates)
            {
                Owner = Window.GetWindow(this)
            };

            if (dlg.ShowDialog() == true)
                await LoadData();
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }
    private async void BtnDeleteMaterialItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int itemId || itemId <= 0) return;
        await ApiClient.DeleteAsync($"/api/projects/{_projectId}/costing/material-items/{itemId}");
        await LoadData();
    }

    private async void BtnDeleteResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int resourceId || resourceId <= 0) return;
        await ApiClient.DeleteAsync($"/api/projects/{_projectId}/costing/resources/{resourceId}");
        await LoadData();
    }

    private async void BtnInit_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string json = await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/init", "{}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                await LoadData();
            else
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString(), "Errore");
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void EmployeeCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not CostResourceVM row) return;
        if (!_sectionEmployeesCache.TryGetValue(row.SectionId, out var allEmployees)) return;

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
        row.EmployeeId = emp.Id;
        row.ResourceName = emp.FullName;
        row.HourlyCost = emp.HourlyCost;
        row.MarkupValue = emp.DefaultMarkup;
        if (row.Id > 0) await SaveResource(row);
    }

    private MaterialSectionVM? FindMaterialSection(int sectionId)
    {
        return _vm.MaterialSections.FirstOrDefault(s => s.Id == sectionId);
    }

    private CostSectionVM? FindSection(int sectionId)
    {
        foreach (var g in _vm.Groups)
        {
            var sec = g.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (sec != null) return sec;
        }
        return null;
    }

    private async Task LoadData()
    {
        try
        {
            _vm.StatusText = "Caricamento...";
            DataContext = _vm;

            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/costing");
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.GetProperty("success").GetBoolean()) return;

            _data = JsonSerializer.Deserialize<ProjectCostingData>(
                doc.RootElement.GetProperty("data").GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

            // Preserva stato espansione
            var groupStates = _vm.Groups.ToDictionary(g => g.Name, g => g.IsExpanded);
            var sectionStates = _vm.Groups
                .SelectMany(g => g.Sections)
                .ToDictionary(s => s.Id, s => s.IsDetailExpanded);
            var matSectionStates = _vm.MaterialSections
                .ToDictionary(s => s.Id, s => s.IsDetailExpanded);

            _vm = CostingViewModel.FromData(_data);

            // Ripristina stato espansione
            foreach (var g in _vm.Groups)
            {
                if (groupStates.TryGetValue(g.Name, out bool expanded))
                    g.IsExpanded = expanded;
                foreach (var s in g.Sections)
                    if (sectionStates.TryGetValue(s.Id, out bool secExpanded))
                        s.IsDetailExpanded = secExpanded;
            }
            foreach (var ms in _vm.MaterialSections)
                if (matSectionStates.TryGetValue(ms.Id, out bool matExpanded))
                    ms.IsDetailExpanded = matExpanded;

            DataContext = _vm;

            // Precarica dipendenti
            _sectionEmployeesCache.Clear();
            foreach (var g in _vm.Groups)
                foreach (var sec in g.Sections)
                {
                    var emps = await LoadEmployeesForSection(sec.Id);
                    _sectionEmployeesCache[sec.Id] = emps;
                }
        }
        catch (Exception ex)
        {
            _vm.StatusText = $"Errore: {ex.Message}";
        }
    }

    private async Task<List<EmployeeCostLookup>> LoadEmployeesForSection(int sectionId)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/projects/{_projectId}/costing/sections/{sectionId}/employees");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
                return JsonSerializer.Deserialize<List<EmployeeCostLookup>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { }
        return new();
    }

    private void MarkupTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    private void MarkupTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { }
    private void MarkupTextBox_LostFocus(object sender, RoutedEventArgs e) { }
    private void MarkupTextBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
    private async void MaterialGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        await Task.Delay(100);
        if (e.Row.Item is MaterialItemVM row && row.Id > 0)
            await SaveMaterialItem(row);
    }

    private void MaterialSectionRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Grid grid && grid.DataContext is MaterialSectionVM sec)
            sec.IsDetailExpanded = !sec.IsDetailExpanded;
    }

    private async void ResourceGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        await Task.Delay(100);
        if (e.Row.Item is CostResourceVM row && row.Id > 0)
            await SaveResource(row);
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
            await ApiClient.PutAsync($"/api/projects/{_projectId}/costing/material-items/{row.Id}", json);
        }
        catch { }
    }

    private async Task SavePricingMarkups()
    {
        try
        {
            var req = new
            {
                structureCostsPct = _vm.StructureCostsPct,
                contingencyPct = _vm.ContingencyPct,
                riskWarrantyPct = _vm.RiskWarrantyPct,
                negotiationMarginPct = _vm.NegotiationMarginPct,
                travelMarkup = _vm.TravelMarkup,
                allowanceMarkup = _vm.AllowanceMarkup
            };
            string json = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"/api/projects/{_projectId}/costing/pricing", json);
        }
        catch { }
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
            await ApiClient.PutAsync($"/api/projects/{_projectId}/costing/resources/{row.Id}", json);
        }
        catch { }
    }
    private void SectionRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox || (e.OriginalSource as FrameworkElement)?.TemplatedParent is TextBox)
            return;
        if (sender is Grid grid && grid.DataContext is CostSectionVM sec)
            sec.IsDetailExpanded = !sec.IsDetailExpanded;
    }

    private async void TravelMarkup_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != _vm.TravelMarkup)
            {
                _vm.TravelMarkup = newK;
                await SavePricingMarkups();
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
        }
    }
    // ══════════════════════════════════════════════════════════════
    // EVENT HANDLERS — GENERALI
    // ══════════════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════════════
    // EVENT HANDLERS — RISORSE
    // ══════════════════════════════════════════════════════════════
    // ── K Ricarico risorse ──
    // ══════════════════════════════════════════════════════════════
    // EVENT HANDLERS — MATERIALI
    // ══════════════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════
    // ══════════════════════════════════════════════════════════════
    // SAVE
    // ══════════════════════════════════════════════════════════════
}
