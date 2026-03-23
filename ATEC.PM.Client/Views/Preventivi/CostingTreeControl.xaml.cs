using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Views.Costing;
using ATEC.PM.Shared.DTOs;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.TreeGrid;
using System.Text.Json;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class CostingTreeControl : UserControl
{
    private int _quoteId;
    private string _apiBasePath = "";
    private ObservableCollection<CostingTreeRow> _resourceRows = new();
    private ObservableCollection<MaterialTreeRow> _materialRows = new();
    private int _nextNodeId = 1;
    private bool _isLoading;
    private List<CostingTreeRow> _allRows = new();
    private Dictionary<int, List<EmployeeCostLookup>> _employeeCache = new();
    private bool _suppressEmployeeChange;

    // Color mapping for groups
    private static readonly Dictionary<string, string> GroupColors = new()
    {
        ["GESTIONE"] = "#3B82F6",
        ["PRESCHIERAMENTO"] = "#F59E0B",
        ["INSTALLAZIONE"] = "#8B5CF6",
        ["OPZIONE"] = "#EF4444"
    };

    public CostingTreeControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Load costing data for a preventivo.
    /// </summary>
    public void LoadForPreventivo(int quoteId)
    {
        _quoteId = quoteId;
        _apiBasePath = $"/api/preventivi/{quoteId}/costing";
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        _isLoading = true;
        try
        {
            var json = await ApiClient.GetAsync(_apiBasePath);
            if (json == null) return;

            // API returns wrapped response: { success, data, message }
            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataEl)) return;

            var data = JsonSerializer.Deserialize<ProjectCostingData>(dataEl.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (data == null) return;

            treeGridResources.ItemsSource = null;
            dataGridMaterials.ItemsSource = null;

            BuildResourceTree(data);
            BuildMaterialList(data);

            treeGridResources.ItemsSource = _resourceRows;
            dataGridMaterials.ItemsSource = _materialRows;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento costing: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void BuildResourceTree(ProjectCostingData data)
    {
        _resourceRows.Clear();
        _nextNodeId = 1;
        _allRows.Clear();

        if (data.CostSections == null) return;

        var groups = data.CostSections
            .GroupBy(s => s.GroupName ?? "Altro")
            .OrderBy(g => GetGroupSortOrder(g.Key));

        foreach (var group in groups)
        {
            var groupColor = GroupColors.GetValueOrDefault(group.Key, "#6B7280");
            decimal groupCost = 0, groupSale = 0, groupDays = 0, groupHours = 0;

            var groupNode = new CostingTreeRow
            {
                NodeId = _nextNodeId++,
                NodeType = "GROUP",
                DisplayName = group.Key,
                GroupColor = groupColor
            };

            foreach (var section in group.OrderBy(s => s.SortOrder))
            {
                decimal secCost = 0, secSale = 0;

                var sectionNode = new CostingTreeRow
                {
                    NodeId = _nextNodeId++,
                    NodeType = "SECTION",
                    DbId = section.Id,
                    DisplayName = section.Name ?? "",
                    SectionType = section.SectionType ?? "IN_SEDE",
                    GroupColor = groupColor
                };

                if (section.Resources != null)
                {
                    foreach (var res in section.Resources.OrderBy(r => r.SortOrder))
                    {
                        var resNode = new CostingTreeRow
                        {
                            NodeId = _nextNodeId++,
                            NodeType = "RESOURCE",
                            DbId = res.Id,
                            SectionDbId = section.Id,
                            EmployeeId = res.EmployeeId,
                            DisplayName = res.ResourceName ?? "(vuoto)",
                            WorkDays = res.WorkDays,
                            HoursPerDay = res.HoursPerDay > 0 ? res.HoursPerDay : 8,
                            HourlyCost = res.HourlyCost,
                            MarkupValue = res.MarkupValue > 0 ? res.MarkupValue : 1.450m,
                            TotalCost = res.WorkDays * res.HoursPerDay * res.HourlyCost,
                            TotalSale = res.WorkDays * res.HoursPerDay * res.HourlyCost * res.MarkupValue,
                            SectionType = section.SectionType ?? "IN_SEDE",
                            GroupColor = groupColor,
                            NumTrips = res.NumTrips,
                            KmPerTrip = res.KmPerTrip,
                            CostPerKm = res.CostPerKm > 0 ? res.CostPerKm : 0.90m,
                            DailyFood = res.DailyFood,
                            DailyHotel = res.DailyHotel,
                            AllowanceDays = res.AllowanceDays,
                            DailyAllowance = res.DailyAllowance
                        };
                        sectionNode.Children.Add(resNode);
                        _allRows.Add(resNode);

                        secCost += resNode.TotalCost;
                        secSale += resNode.TotalSale;
                    }
                }

                sectionNode.TotalCost = secCost;
                sectionNode.TotalSale = secSale;
                sectionNode.SumWorkDays = sectionNode.Children.Sum(r => r.WorkDays);
                sectionNode.SumTotalHours = sectionNode.Children.Sum(r => r.TotalHours);
                sectionNode.ResourceCount = sectionNode.Children.Count;
                groupNode.Children.Add(sectionNode);
                _allRows.Add(sectionNode);

                groupCost += secCost;
                groupSale += secSale;
                groupDays += sectionNode.SumWorkDays;
                groupHours += sectionNode.SumTotalHours;
            }

            groupNode.TotalCost = groupCost;
            groupNode.TotalSale = groupSale;
            groupNode.SumWorkDays = groupDays;
            groupNode.SumTotalHours = groupHours;
            groupNode.ResourceCount = groupNode.Children.Sum(s => s.ResourceCount);
            _resourceRows.Add(groupNode);
            _allRows.Add(groupNode);
        }
    }

    private void BuildMaterialList(ProjectCostingData data)
    {
        _materialRows.Clear();
        if (data.MaterialSections == null) return;

        foreach (var section in data.MaterialSections)
        {
            if (section.Items == null) continue;
            foreach (var item in section.Items.OrderBy(i => i.SortOrder))
            {
                _materialRows.Add(new MaterialTreeRow
                {
                    DbId = item.Id,
                    SectionId = section.Id,
                    Description = item.Description ?? "",
                    ItemType = item.ItemType ?? "MATERIAL",
                    Quantity = item.Quantity,
                    UnitCost = item.UnitCost,
                    MarkupValue = item.MarkupValue > 0 ? item.MarkupValue : 1.300m,
                    TotalCost = item.Quantity * item.UnitCost,
                    TotalSale = item.Quantity * item.UnitCost * (item.MarkupValue > 0 ? item.MarkupValue : 1.300m)
                });
            }
        }
    }

    // ── Editing control: only resources can be edited ──
    private void TreeGrid_CurrentCellBeginEdit(object sender, TreeGridCurrentCellBeginEditEventArgs e)
    {
        var treeNode = treeGridResources.GetNodeAtRowIndex(e.RowColumnIndex.RowIndex);
        if (treeNode?.Item is CostingTreeRow row)
        {
            if (!row.IsResource)
            {
                e.Cancel = true;
                return;
            }

            var colIndex = e.RowColumnIndex.ColumnIndex;
            var col = treeGridResources.Columns.ElementAtOrDefault(colIndex);
            var colName = col?.MappingName;
            var editableColumns = new[] { "WorkDays", "HoursPerDay", "MarkupValue" };
            if (colName != null && !editableColumns.Contains(colName))
            {
                e.Cancel = true;
            }
        }
    }

    // ── Auto-save on edit end ──
    private async void TreeGrid_CurrentCellEndEdit(object sender, CurrentCellEndEditEventArgs e)
    {
        if (_isLoading) return;
        // Get the edited row from the tree grid
        var treeNode = treeGridResources.GetNodeAtRowIndex(e.RowColumnIndex.RowIndex);
        if (treeNode?.Item is CostingTreeRow row && row.IsResource && row.IsDirty)
        {
            row.IsDirty = false;
            await SaveResourceAsync(row);
            RecalcParentTotals();
        }
    }

    private async void MaterialGrid_CurrentCellEndEdit(object sender, CurrentCellEndEditEventArgs e)
    {
        if (_isLoading) return;
        var rowIndex = e.RowColumnIndex.RowIndex;
        var recordIndex = dataGridMaterials.ResolveToRecordIndex(rowIndex);
        if (recordIndex >= 0 && recordIndex < _materialRows.Count)
        {
            var mat = _materialRows[recordIndex];
            if (mat.IsDirty)
            {
                mat.IsDirty = false;
                await SaveMaterialAsync(mat);
            }
        }
    }

    // ── Save resource to API ──
    private async Task SaveResourceAsync(CostingTreeRow row)
    {
        try
        {
            var body = new
            {
                sectionId = row.SectionDbId,
                employeeId = row.EmployeeId,
                resourceName = row.DisplayName,
                workDays = row.WorkDays,
                hoursPerDay = row.HoursPerDay,
                hourlyCost = row.HourlyCost,
                markupValue = row.MarkupValue,
                numTrips = row.NumTrips,
                kmPerTrip = row.KmPerTrip,
                costPerKm = row.CostPerKm,
                dailyFood = row.DailyFood,
                dailyHotel = row.DailyHotel,
                allowanceDays = row.AllowanceDays,
                dailyAllowance = row.DailyAllowance
            };

            await ApiClient.PutAsync($"{_apiBasePath}/resources/{row.DbId}",
                JsonSerializer.Serialize(body));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore salvataggio: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Save material to API ──
    private async Task SaveMaterialAsync(MaterialTreeRow mat)
    {
        try
        {
            var body = new
            {
                sectionId = mat.SectionId,
                description = mat.Description,
                quantity = mat.Quantity,
                unitCost = mat.UnitCost,
                markupValue = mat.MarkupValue,
                itemType = mat.ItemType
            };

            await ApiClient.PutAsync($"{_apiBasePath}/material-items/{mat.DbId}",
                JsonSerializer.Serialize(body));
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore salvataggio materiale: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Recalculate parent totals after resource edit ──
    private void RecalcParentTotals()
    {
        foreach (var grp in _resourceRows)
        {
            decimal grpCost = 0, grpSale = 0, grpDays = 0, grpHours = 0;
            foreach (var sec in grp.Children)
            {
                sec.TotalCost = sec.Children.Sum(r => r.TotalCost);
                sec.TotalSale = sec.Children.Sum(r => r.TotalSale);
                sec.SumWorkDays = sec.Children.Sum(r => r.WorkDays);
                sec.SumTotalHours = sec.Children.Sum(r => r.TotalHours);
                sec.ResourceCount = sec.Children.Count;
                grpCost += sec.TotalCost;
                grpSale += sec.TotalSale;
                grpDays += sec.SumWorkDays;
                grpHours += sec.SumTotalHours;
            }
            grp.TotalCost = grpCost;
            grp.TotalSale = grpSale;
            grp.SumWorkDays = grpDays;
            grp.SumTotalHours = grpHours;
            grp.ResourceCount = grp.Children.Sum(s => s.ResourceCount);
        }
    }

    // ── Add section ──
    private async void BtnAddSection_Click(object sender, RoutedEventArgs e)
    {
        string groupName = FindGroupNameFromSelection();

        try
        {
            // Get available templates (wrapped in ApiResponse)
            var json = await ApiClient.GetAsync($"{_apiBasePath}/available-templates");
            var templates = new List<CostSectionTemplateDto>();
            if (json != null)
            {
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataEl) &&
                    dataEl.TryGetProperty("templates", out var tmplEl))
                {
                    templates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                        tmplEl.GetRawText(),
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
            }

            var groupTemplates = templates.Where(t =>
                string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase)).ToList();

            var dlg = new AddCostSectionDialog(_quoteId, groupName, groupTemplates, _apiBasePath);
            if (dlg.ShowDialog() == true)
                await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Add resource ──
    private async void BtnAddResource_Click(object sender, RoutedEventArgs e)
    {
        int? sectionId = FindSectionIdFromSelection();

        if (sectionId == null)
        {
            MessageBox.Show("Nessuna sezione disponibile. Crea prima una sezione.", "Info",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var body = new { sectionId = sectionId.Value };
            await ApiClient.PostAsync($"{_apiBasePath}/resources",
                JsonSerializer.Serialize(body));
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Add material ──
    private async void BtnAddMaterial_Click(object sender, RoutedEventArgs e)
    {
        var picker = new CatalogPickerDialog();
        if (picker.ShowDialog() != true || picker.SelectedVariants.Count == 0) return;

        try
        {
            // Find or create material section
            var json = await ApiClient.GetAsync(_apiBasePath);
            var rawDoc = JsonDocument.Parse(json!);
            var dataJson = rawDoc.RootElement.TryGetProperty("data", out var dEl) ? dEl.GetRawText() : json!;
            var data = JsonSerializer.Deserialize<ProjectCostingData>(dataJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            int materialSectionId;
            if (data?.MaterialSections?.Count > 0)
            {
                materialSectionId = data.MaterialSections[0].Id;
            }
            else
            {
                var secResult = await ApiClient.PostAsync($"{_apiBasePath}/material-sections",
                    JsonSerializer.Serialize(new { name = "Materiali" }));
                var secObj = JsonSerializer.Deserialize<JsonElement>(secResult!);
                materialSectionId = secObj.GetProperty("id").GetInt32();
            }

            foreach (var v in picker.SelectedVariants)
            {
                var itemBody = new
                {
                    sectionId = materialSectionId,
                    description = $"{v.ProductName} - {v.VariantName}",
                    quantity = v.Quantity,
                    unitCost = v.SellPrice > 0 ? v.SellPrice : v.CostPrice,
                    markupValue = 1.300m,
                    itemType = "MATERIAL"
                };

                await ApiClient.PostAsync($"{_apiBasePath}/material-items",
                    JsonSerializer.Serialize(itemBody));
            }

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Add resource directly from section row button ──
    private async void BtnAddResourceInSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Tag is not CostingTreeRow row || !row.IsSection) return;

        try
        {
            var body = new { sectionId = row.DbId };
            await ApiClient.PostAsync($"{_apiBasePath}/resources",
                JsonSerializer.Serialize(body));
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ══════════════════════════════════════════════════
    // EMPLOYEE COMBOBOX
    // ══════════════════════════════════════════════════

    private async void EmployeeCombo_DropDownOpened(object sender, EventArgs e)
    {
        if (sender is not System.Windows.Controls.ComboBox cmb) return;
        if (cmb.Tag is not CostingTreeRow row || !row.IsResource) return;

        int sectionId = row.SectionDbId ?? 0;
        if (sectionId == 0) return;

        // Load employees for this section (cached)
        if (!_employeeCache.ContainsKey(sectionId))
        {
            try
            {
                var json = await ApiClient.GetAsync($"{_apiBasePath}/sections/{sectionId}/employees");
                if (json != null)
                {
                    var doc = JsonDocument.Parse(json);
                    var dataJson = doc.RootElement.TryGetProperty("data", out var dEl) ? dEl.GetRawText() : json;
                    var employees = JsonSerializer.Deserialize<List<EmployeeCostLookup>>(dataJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                    _employeeCache[sectionId] = employees;
                }
                else
                {
                    _employeeCache[sectionId] = new();
                }
            }
            catch { _employeeCache[sectionId] = new(); }
        }

        _suppressEmployeeChange = true;
        cmb.ItemsSource = _employeeCache[sectionId];
        // Select current employee
        if (row.EmployeeId.HasValue)
            cmb.SelectedItem = _employeeCache[sectionId].FirstOrDefault(emp => emp.Id == row.EmployeeId.Value);
        _suppressEmployeeChange = false;
    }

    private async void EmployeeCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressEmployeeChange || _isLoading) return;
        if (sender is not System.Windows.Controls.ComboBox cmb) return;
        if (cmb.Tag is not CostingTreeRow row || !row.IsResource) return;
        if (cmb.SelectedItem is not EmployeeCostLookup emp) return;

        row.EmployeeId = emp.Id;
        row.DisplayName = emp.FullName;
        row.HourlyCost = emp.HourlyCost;
        row.MarkupValue = emp.DefaultMarkup > 0 ? emp.DefaultMarkup : 1.450m;
        row.DepartmentCode = emp.DepartmentCode;
        row.TotalCost = row.TotalHours * row.HourlyCost;
        row.TotalSale = row.TotalCost * row.MarkupValue;

        await SaveResourceAsync(row);
        RecalcParentTotals();
    }

    // ══════════════════════════════════════════════════
    // CONTEXT MENU
    // ══════════════════════════════════════════════════

    private void TreeContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var row = treeGridResources.SelectedItem as CostingTreeRow;
        bool isGroup = row?.IsGroup == true;
        bool isSection = row?.IsSection == true;
        bool isResource = row?.IsResource == true;

        menuAddResource.Visibility = (isSection || isResource) ? Visibility.Visible : Visibility.Collapsed;
        menuAddSection.Visibility = (isGroup || isSection) ? Visibility.Visible : Visibility.Collapsed;
        menuSep1.Visibility = (isSection || isResource) ? Visibility.Visible : Visibility.Collapsed;
        menuDeleteResource.Visibility = isResource ? Visibility.Visible : Visibility.Collapsed;
        menuDeleteSection.Visibility = isSection ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MenuAddResource_Click(object sender, RoutedEventArgs e) => BtnAddResource_Click(sender, e);
    private void MenuAddSection_Click(object sender, RoutedEventArgs e) => BtnAddSection_Click(sender, e);

    private async void MenuDeleteResource_Click(object sender, RoutedEventArgs e)
    {
        if (treeGridResources.SelectedItem is not CostingTreeRow row || !row.IsResource) return;

        if (MessageBox.Show($"Eliminare la risorsa '{row.DisplayName}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"{_apiBasePath}/resources/{row.DbId}");
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MenuDeleteSection_Click(object sender, RoutedEventArgs e)
    {
        if (treeGridResources.SelectedItem is not CostingTreeRow row || !row.IsSection) return;

        string msg = row.Children.Count > 0
            ? $"La sezione '{row.DisplayName}' contiene {row.Children.Count} risorse. Eliminare tutto?"
            : $"Eliminare la sezione '{row.DisplayName}'?";

        if (MessageBox.Show(msg, "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"{_apiBasePath}/sections/{row.DbId}");
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ══════════════════════════════════════════════════
    // ADD GROUP
    // ══════════════════════════════════════════════════

    private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var json = await ApiClient.GetAsync($"{_apiBasePath}/available-templates");
            var groups = new List<CostSectionGroupDto>();
            var templates = new List<CostSectionTemplateDto>();

            if (json != null)
            {
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var dataEl))
                {
                    if (dataEl.TryGetProperty("groups", out var grpEl))
                        groups = JsonSerializer.Deserialize<List<CostSectionGroupDto>>(
                            grpEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                    if (dataEl.TryGetProperty("templates", out var tmplEl))
                        templates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                            tmplEl.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
            }

            if (groups.Count == 0)
            {
                MessageBox.Show("Tutti i gruppi sono già presenti.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new AddCostGroupDialog(_quoteId, groups, templates, _apiBasePath);
            if (dlg.ShowDialog() == true)
                await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ══════════════════════════════════════════════════
    // FIND SECTION FROM SELECTION
    // ══════════════════════════════════════════════════

    private string FindGroupNameFromSelection()
    {
        var selected = treeGridResources.SelectedItem as CostingTreeRow;
        if (selected == null) return "GESTIONE";

        if (selected.IsGroup) return selected.DisplayName;

        // Walk up nested structure
        foreach (var grp in _resourceRows)
        {
            if (grp.Children.Any(s => s.NodeId == selected.NodeId)) return grp.DisplayName;
            foreach (var sec in grp.Children)
            {
                if (sec.Children.Any(r => r.NodeId == selected.NodeId)) return grp.DisplayName;
            }
        }
        return "GESTIONE";
    }

    private int? FindSectionIdFromSelection()
    {
        var selected = treeGridResources.SelectedItem as CostingTreeRow;
        if (selected == null) return null;
        if (selected.IsSection) return selected.DbId;
        if (selected.IsResource) return selected.SectionDbId;

        // If group selected, return first section
        if (selected.IsGroup)
            return selected.Children.FirstOrDefault()?.DbId;
        return null;
    }

    private static int GetGroupSortOrder(string groupName) => groupName switch
    {
        "GESTIONE" => 1,
        "PRESCHIERAMENTO" => 2,
        "INSTALLAZIONE" => 3,
        "OPZIONE" => 4,
        _ => 99
    };
}
