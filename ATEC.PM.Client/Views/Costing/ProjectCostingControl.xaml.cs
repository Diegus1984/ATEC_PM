using System.Globalization;
using ATEC.PM.Client.Views.Costing.ViewModels;

namespace ATEC.PM.Client.Views.Costing;

public partial class ProjectCostingControl : UserControl
{
    private int _projectId;
    private CostingViewModel _vm = new();
    private ProjectCostingData _data = new(); // per riepilogo/materiali
    private Dictionary<int, List<EmployeeCostLookup>> _sectionEmployeesCache = new();

    public ProjectCostingControl()
    {
        InitializeComponent();
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

    // ══════════════════════════════════════════════════════════════
    // EVENT HANDLERS
    // ══════════════════════════════════════════════════════════════

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

    private void SectionRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox || (e.OriginalSource as FrameworkElement)?.TemplatedParent is TextBox)
            return;
        if (sender is Grid grid && grid.DataContext is CostSectionVM sec)
            sec.IsDetailExpanded = !sec.IsDetailExpanded;
    }

    private async void ResourceGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction == DataGridEditAction.Cancel) return;
        await Task.Delay(100);
        if (e.Row.Item is CostResourceVM row && row.Id > 0)
            await SaveResource(row);
    }

    // ── ComboBox dipendente ──

    private void EmployeeCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        if (combo.DataContext is not CostResourceVM row) return;

        if (!_sectionEmployeesCache.TryGetValue(row.SectionId, out var allEmployees))
            return;

        // Trova la sezione nel VM per escludere dipendenti già assegnati
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

        if (row.Id > 0)
            await SaveResource(row);
    }

    // ── Elimina risorsa ──

    private async void BtnDeleteResource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int resourceId || resourceId <= 0)
            return;

        await ApiClient.DeleteAsync($"/api/projects/{_projectId}/costing/resources/{resourceId}");
        await LoadData();
    }

    // ── K Ricarico ──

    private void MarkupTextBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is TextBox tb) { tb.Focus(); e.Handled = true; }
    }

    private void MarkupTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

    private async void MarkupTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.DataContext is CostSectionVM sec)
        {
            if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != sec.MarkupValue)
            {
                sec.MarkupValue = newK;
                await SaveSectionMarkup(sec.Id, newK);
            }
        }
    }

    private async void MarkupTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.DataContext is CostSectionVM sec)
        {
            e.Handled = true;
            if (decimal.TryParse(tb.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal newK) && newK != sec.MarkupValue)
            {
                sec.MarkupValue = newK;
                await SaveSectionMarkup(sec.Id, newK);
            }
            Keyboard.ClearFocus();
        }
    }

    // ── + Risorsa ──

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
        var usedIds = sec.Resources
            .Where(r => r.EmployeeId.HasValue)
            .Select(r => r.EmployeeId!.Value)
            .ToHashSet();
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
            HoursPerDay = 8,
            CostPerKm = 0.90m
        };

        string json = JsonSerializer.Serialize(req,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await ApiClient.PostAsync($"/api/projects/{_projectId}/costing/resources", json);
        await LoadData();
    }

    // ══════════════════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════════════════

    private CostSectionVM? FindSection(int sectionId)
    {
        foreach (var g in _vm.Groups)
        {
            var sec = g.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (sec != null) return sec;
        }
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    // SAVE
    // ══════════════════════════════════════════════════════════════

    private async Task SaveSectionMarkup(int sectionId, decimal markupValue)
    {
        try
        {
            var req = new { Field = "markup_value", Value = markupValue.ToString(CultureInfo.InvariantCulture) };
            string json = JsonSerializer.Serialize(req,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PatchAsync($"/api/projects/{_projectId}/costing/sections/{sectionId}/field", json);
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
                NumTrips = row.NumTrips,
                KmPerTrip = row.KmPerTrip,
                CostPerKm = row.CostPerKm,
                DailyFood = row.DailyFood,
                DailyHotel = row.DailyHotel,
                AllowanceDays = row.AllowanceDays,
                DailyAllowance = row.DailyAllowance
            };
            string json = JsonSerializer.Serialize(req,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await ApiClient.PutAsync($"/api/projects/{_projectId}/costing/resources/{row.Id}", json);
        }
        catch { }
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
}
