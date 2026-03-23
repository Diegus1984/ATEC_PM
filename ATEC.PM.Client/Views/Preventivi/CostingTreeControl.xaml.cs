using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Views.Costing;
using ATEC.PM.Shared.DTOs;
using System.Text.Json;

namespace ATEC.PM.Client.Views.Preventivi;

public partial class CostingTreeControl : UserControl
{
    private static readonly JsonSerializerOptions _jopt = new() { PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions _joptCamel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private int _quoteId;
    private string _apiBasePath = "";
    private ObservableCollection<CostingTreeRow> _resourceRows = new();
    private ObservableCollection<MaterialTreeRow> _materialRows = new();
    private ObservableCollection<MaterialProductGroup> _materialProducts = new();
    private int _nextNodeId = 1;
    private bool _isLoading;

    private Dictionary<int, List<EmployeeCostLookup>> _employeeCache = new();
    private bool _suppressEmployeeChange;
    private PricingVM _pricingVM = new();
    private bool _isPricingUpdating;

    /// <summary>Fired after pricing data is loaded/recalculated. Subscribers can read GetPricingSummary().</summary>
    public event Action? PricingUpdated;

    /// <summary>Returns distribution rows for external riepilogo.</summary>
    public IReadOnlyList<DistributionRowVM> GetDistributionRows()
        => _pricingVM.DistributionRows.ToList();

    /// <summary>Returns current pricing summary for external UI.</summary>
    public (decimal Resources, decimal Materials, decimal Travel, decimal Net, decimal ContPct, decimal ContAmt,
            decimal Offer, decimal MargPct, decimal MargAmt, decimal Final) GetPricingSummary()
    {
        return (_pricingVM.ResourceDistributed, _pricingVM.MaterialDistributed, _pricingVM.TravelDistributed,
                _pricingVM.NetPrice, _pricingVM.ContingencyPct, _pricingVM.ContingencyAmount,
                _pricingVM.OfferPrice, _pricingVM.NegotiationMarginPct, _pricingVM.NegotiationMarginAmount,
                _pricingVM.FinalOfferPrice);
    }
    private ProjectCostingData? _lastData;

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
            // Save expanded state
            var expandedGroups = _resourceRows.Where(g => g.IsExpanded).Select(g => g.DisplayName).ToHashSet();
            var expandedSections = _resourceRows.SelectMany(g => g.Children)
                .Where(s => s.IsExpanded).Select(s => s.DbId).ToHashSet();
            double scrollOffset = mainScrollViewer.VerticalOffset;

            var json = await ApiClient.GetAsync(_apiBasePath);
            if (json == null) return;

            var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("data", out var dataEl)) return;

            var data = JsonSerializer.Deserialize<ProjectCostingData>(dataEl.GetRawText(),
                _jopt);
            if (data == null) return;

            _lastData = data;

            BuildResourceTree(data);
            BuildMaterialList(data);

            // Restore expanded state
            foreach (var grp in _resourceRows)
            {
                grp.IsExpanded = expandedGroups.Contains(grp.DisplayName) || expandedGroups.Count == 0;
                foreach (var sec in grp.Children)
                {
                    sec.IsExpanded = expandedSections.Contains(sec.DbId) || expandedSections.Count == 0;
                }
            }

            groupsItemsControl.ItemsSource = _resourceRows;
            icMaterialProducts.ItemsSource = _materialProducts;

            // Build pricing
            BuildPricing(data);
            NotifyPricingUpdated();

            // Restore scroll position
            _ = Dispatcher.InvokeAsync(() => mainScrollViewer.ScrollToVerticalOffset(scrollOffset),
                System.Windows.Threading.DispatcherPriority.Loaded);
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
                GroupColor = groupColor,
                IsExpanded = true
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
                    GroupColor = groupColor,
                    IsExpanded = true
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
                            DepartmentCode = "",
                            NumTrips = res.NumTrips,
                            KmPerTrip = res.KmPerTrip,
                            CostPerKm = res.CostPerKm > 0 ? res.CostPerKm : 0.90m,
                            DailyFood = res.DailyFood,
                            DailyHotel = res.DailyHotel,
                            AllowanceDays = res.AllowanceDays,
                            DailyAllowance = res.DailyAllowance
                        };
                        sectionNode.Children.Add(resNode);


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

        }
    }

    private void BuildMaterialList(ProjectCostingData data)
    {
        _materialRows.Clear();
        _materialProducts.Clear();
        if (data.MaterialSections == null) return;

        foreach (var section in data.MaterialSections)
        {
            if (section.Items == null) continue;
            var allItems = section.Items.OrderBy(i => i.SortOrder).ToList();

            // Parents = items without parent_item_id
            var parents = allItems.Where(i => i.ParentItemId == null).ToList();
            // Orphan children (legacy data without parent) become standalone rows
            var orphans = allItems.Where(i => i.ParentItemId != null && !parents.Any(p => p.Id == i.ParentItemId)).ToList();

            foreach (var parent in parents)
            {
                var children = allItems.Where(i => i.ParentItemId == parent.Id).ToList();

                if (children.Count == 0)
                {
                    // Legacy item: no children, treat as single-variant product
                    var row = BuildMaterialRow(parent, section.Id);
                    _materialRows.Add(row);
                    var group = new MaterialProductGroup
                    {
                        ParentId = parent.Id,
                        SectionId = section.Id,
                        ParentName = parent.Description ?? "",
                        Variants = new ObservableCollection<MaterialTreeRow> { row }
                    };
                    _materialProducts.Add(group);
                }
                else
                {
                    // Product with variants
                    var variants = new ObservableCollection<MaterialTreeRow>();
                    foreach (var child in children)
                    {
                        var row = BuildMaterialRow(child, section.Id);
                        _materialRows.Add(row);
                        variants.Add(row);
                    }
                    var group = new MaterialProductGroup
                    {
                        ParentId = parent.Id,
                        SectionId = section.Id,
                        ParentName = parent.Description ?? "",
                        Variants = variants
                    };
                    _materialProducts.Add(group);
                }
            }

            // Orphan items (legacy without parents)
            foreach (var orphan in orphans)
            {
                var row = BuildMaterialRow(orphan, section.Id);
                _materialRows.Add(row);
                var group = new MaterialProductGroup
                {
                    ParentId = orphan.Id,
                    SectionId = section.Id,
                    ParentName = orphan.Description ?? "",
                    Variants = new ObservableCollection<MaterialTreeRow> { row }
                };
                _materialProducts.Add(group);
            }
        }
    }

    private static MaterialTreeRow BuildMaterialRow(ProjectMaterialItemDto item, int sectionId)
    {
        return new MaterialTreeRow
        {
            DbId = item.Id,
            SectionId = sectionId,
            ParentItemId = item.ParentItemId,
            Description = item.Description ?? "",
            ItemType = item.ItemType ?? "MATERIAL",
            Quantity = item.Quantity,
            UnitCost = item.UnitCost,
            MarkupValue = item.MarkupValue > 0 ? item.MarkupValue : 1.300m,
            TotalCost = item.Quantity * item.UnitCost,
            TotalSale = item.Quantity * item.UnitCost * (item.MarkupValue > 0 ? item.MarkupValue : 1.300m)
        };
    }

    // ══════════════════════════════════════════════════
    // GROUP / SECTION / MATERIAL / PRICING EXPAND/COLLAPSE
    // ══════════════════════════════════════════════════

    private bool _resourcesExpanded = true;
    private bool _materialExpanded = true;
    private bool _pricingExpanded = true;
    private bool _distExpanded = true;

    private void ResourcesHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _resourcesExpanded = !_resourcesExpanded;
        resourcesContent.Visibility = _resourcesExpanded ? Visibility.Visible : Visibility.Collapsed;
        ((RotateTransform)resourcesArrow.RenderTransform).Angle = _resourcesExpanded ? 90 : 0;
    }

    private void GroupHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CostingTreeRow row && row.IsGroup)
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }

    private void MaterialHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _materialExpanded = !_materialExpanded;
        materialContent.Visibility = _materialExpanded ? Visibility.Visible : Visibility.Collapsed;
        ((RotateTransform)materialArrow.RenderTransform).Angle = _materialExpanded ? 90 : 0;
    }

    private void PricingHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _pricingExpanded = !_pricingExpanded;
        pricingContent.Visibility = _pricingExpanded ? Visibility.Visible : Visibility.Collapsed;
        ((RotateTransform)pricingArrow.RenderTransform).Angle = _pricingExpanded ? 90 : 0;
    }

    private void DistributionHeader_Click(object sender, MouseButtonEventArgs e)
    {
        _distExpanded = !_distExpanded;
        distContent.Visibility = _distExpanded ? Visibility.Visible : Visibility.Collapsed;
        ((RotateTransform)distArrow.RenderTransform).Angle = _distExpanded ? 90 : 0;
    }

    private void SectionHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CostingTreeRow row && row.IsSection)
        {
            row.IsExpanded = !row.IsExpanded;
        }
    }

    // ══════════════════════════════════════════════════
    // RESOURCE GRID EDITING (auto-save on LostFocus)
    // ══════════════════════════════════════════════════

    private async void ResourceGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (_isLoading) return;
        if (e.EditAction == DataGridEditAction.Cancel) return;
        if (e.Row.Item is not CostingTreeRow row || !row.IsResource) return;

        // Defer save to after binding updates
        await Dispatcher.InvokeAsync(async () =>
        {
            if (row.IsDirty)
            {
                row.IsDirty = false;
                await SaveResourceAsync(row);
                RecalcParentTotals();
            }
        }, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private async void MaterialVariantField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        if (sender is not TextBox tb) return;
        if (tb.Tag is not MaterialTreeRow mat) return;

        if (mat.IsDirty)
        {
            mat.IsDirty = false;
            await SaveMaterialAsync(mat);
            // Rebuild pricing after material change
            if (_lastData != null)
            {
                BuildPricing(_lastData);
                NotifyPricingUpdated();
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

    // ══════════════════════════════════════════════════
    // EMPLOYEE COMBOBOX
    // ══════════════════════════════════════════════════

    private async void EmployeeCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ComboBox cmb) return;
        if (cmb.DataContext is not CostingTreeRow row || !row.IsResource) return;

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
                        _jopt) ?? new();
                    _employeeCache[sectionId] = employees;
                }
                else
                {
                    _employeeCache[sectionId] = new();
                }
            }
            catch { _employeeCache[sectionId] = new(); }
        }

        // Filter out employees already assigned in this section
        var usedEmployeeIds = new HashSet<int>();
        foreach (var grp in _resourceRows)
        {
            foreach (var sec in grp.Children)
            {
                if (sec.DbId == sectionId)
                {
                    foreach (var res in sec.Children)
                    {
                        if (res.EmployeeId.HasValue && res.DbId != row.DbId)
                            usedEmployeeIds.Add(res.EmployeeId.Value);
                    }
                }
            }
        }
        var filteredEmployees = _employeeCache[sectionId]
            .Where(emp => !usedEmployeeIds.Contains(emp.Id))
            .ToList();

        _suppressEmployeeChange = true;
        cmb.ItemsSource = filteredEmployees;
        if (row.EmployeeId.HasValue)
            cmb.SelectedItem = filteredEmployees.FirstOrDefault(emp => emp.Id == row.EmployeeId.Value);
        _suppressEmployeeChange = false;
    }

    private async void EmployeeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEmployeeChange || _isLoading) return;
        if (sender is not ComboBox cmb) return;
        if (cmb.DataContext is not CostingTreeRow row || !row.IsResource) return;
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

    private async void AllowanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;
        if (sender is not ComboBox cmb) return;
        if (cmb.DataContext is not CostingTreeRow row || !row.IsResource) return;
        if (cmb.SelectedItem is decimal val)
        {
            row.DailyAllowance = val;
            await SaveResourceAsync(row);
            RecalcParentTotals();
        }
    }

    // ══════════════════════════════════════════════════
    // ADD / DELETE BUTTONS
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
                            grpEl.GetRawText(), _jopt) ?? new();
                    if (dataEl.TryGetProperty("templates", out var tmplEl))
                        templates = JsonSerializer.Deserialize<List<CostSectionTemplateDto>>(
                            tmplEl.GetRawText(), _jopt) ?? new();
                }
            }

            if (groups.Count == 0)
            {
                MessageBox.Show("Tutti i gruppi sono gia' presenti.", "Info",
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

    private async void BtnAddSection_Click(object sender, RoutedEventArgs e)
    {
        string groupName = "GESTIONE";
        if (_resourceRows.Count > 0)
            groupName = _resourceRows[0].DisplayName;

        try
        {
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
                        _jopt) ?? new();
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

    private async void BtnAddResourceInSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
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

    private async void BtnDeleteResourceInRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not CostingTreeRow row || !row.IsResource) return;

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

    private async void BtnAddMaterial_Click(object sender, RoutedEventArgs e)
    {
        var picker = new CatalogPickerDialog();
        if (picker.ShowDialog() != true || picker.SelectedVariants.Count == 0) return;

        try
        {
            int materialSectionId;
            if (_lastData?.MaterialSections?.Count > 0)
            {
                materialSectionId = _lastData.MaterialSections[0].Id;
            }
            else
            {
                var secResult = await ApiClient.PostAsync($"{_apiBasePath}/material-sections",
                    JsonSerializer.Serialize(new { name = "Materiali" }));
                var secObj = JsonSerializer.Deserialize<JsonElement>(secResult!);
                materialSectionId = secObj.GetProperty("id").GetInt32();
            }

            // Group selected variants by product
            var byProduct = picker.SelectedVariants.GroupBy(v => v.ProductId);

            foreach (var productGroup in byProduct)
            {
                string productName = productGroup.First().ProductName;

                // Create parent item (product)
                var parentBody = new
                {
                    sectionId = materialSectionId,
                    description = productName,
                    quantity = 0m,
                    unitCost = 0m,
                    markupValue = 1.300m,
                    itemType = "PRODUCT"
                };
                var parentResult = await ApiClient.PostAsync($"{_apiBasePath}/material-items",
                    JsonSerializer.Serialize(parentBody));
                var parentObj = JsonSerializer.Deserialize<JsonElement>(parentResult!);
                int parentId = parentObj.GetProperty("data").GetInt32();

                // Create child items (variants)
                foreach (var v in productGroup)
                {
                    var childBody = new
                    {
                        description = v.VariantName,
                        quantity = v.Quantity,
                        unitCost = v.CostPrice,
                        markupValue = v.SellPrice > 0 && v.CostPrice > 0
                            ? Math.Round(v.SellPrice / v.CostPrice, 3)
                            : 1.300m,
                        itemType = "MATERIAL"
                    };
                    await ApiClient.PostAsync($"{_apiBasePath}/material-items/{parentId}/variant",
                        JsonSerializer.Serialize(childBody));
                }
            }

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (btn.Tag is not MaterialTreeRow mat) return;

        if (MessageBox.Show($"Eliminare la variante '{mat.Description}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"{_apiBasePath}/material-items/{mat.DbId}");
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteMaterialProduct_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int parentId) return;
        var group = _materialProducts.FirstOrDefault(g => g.ParentId == parentId);
        string name = group?.ParentName ?? $"#{parentId}";

        if (MessageBox.Show($"Eliminare '{name}' e tutte le sue varianti?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            // Delete parent (CASCADE deletes children)
            await ApiClient.DeleteAsync($"{_apiBasePath}/material-items/{parentId}");
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnAddMaterialVariant_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not int parentId) return;
        var group = _materialProducts.FirstOrDefault(g => g.ParentId == parentId);
        if (group == null) return;

        var dlg = new AddMaterialVariantDialog(group.ParentName)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                // If the parent is a legacy item (no children in DB), convert it first:
                // create a child copy of the parent's data so it doesn't get lost
                await EnsureParentHasChildren(parentId);

                var body = new
                {
                    description = dlg.Description,
                    quantity = dlg.Quantity,
                    unitCost = dlg.UnitCost,
                    markupValue = dlg.MarkupValue,
                    itemType = "MATERIAL"
                };
                await ApiClient.PostAsync($"{_apiBasePath}/material-items/{parentId}/variant",
                    JsonSerializer.Serialize(body));
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// If a parent material item has no children in DB (legacy flat item),
    /// create a child copy of its data so the original info is preserved as a variant.
    /// Then clear the parent's cost data (it becomes a pure header).
    /// </summary>
    private async Task EnsureParentHasChildren(int parentId)
    {
        // Check locally if parent already has children
        var group = _materialProducts.FirstOrDefault(g => g.ParentId == parentId);
        if (group == null || group.Variants.Count > 1) return; // already has multiple children

        // Use _lastData to get parent's original values
        if (_lastData?.MaterialSections == null) return;

        foreach (var section in _lastData.MaterialSections)
        {
            if (section.Items == null) continue;
            var parent = section.Items.FirstOrDefault(i => i.Id == parentId);
            if (parent == null) continue;

            bool hasChildren = section.Items.Any(i => i.ParentItemId == parentId);
            if (hasChildren) return;

            if (parent.Quantity > 0 || parent.UnitCost > 0)
            {
                var childBody = new
                {
                    description = parent.Description,
                    quantity = parent.Quantity,
                    unitCost = parent.UnitCost,
                    markupValue = parent.MarkupValue > 0 ? parent.MarkupValue : 1.300m,
                    itemType = "MATERIAL"
                };
                await ApiClient.PostAsync($"{_apiBasePath}/material-items/{parentId}/variant",
                    JsonSerializer.Serialize(childBody));

                // Clear parent's cost data (it's now just a header)
                var clearBody = new
                {
                    description = parent.Description,
                    quantity = 0m,
                    unitCost = 0m,
                    markupValue = 1.300m,
                    itemType = "PRODUCT",
                    sortOrder = parent.SortOrder
                };
                await ApiClient.PutAsync($"{_apiBasePath}/material-items/{parentId}",
                    JsonSerializer.Serialize(clearBody));
            }
            return;
        }
    }

    // ══════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════

    private static int GetGroupSortOrder(string groupName) => groupName switch
    {
        "GESTIONE" => 1,
        "PRESCHIERAMENTO" => 2,
        "INSTALLAZIONE" => 3,
        "OPZIONE" => 4,
        _ => 99
    };

    // ══════════════════════════════════════════════════
    // SCHEDA PREZZI + DISTRIBUZIONE PREZZO
    // ══════════════════════════════════════════════════

    private void BuildPricing(ProjectCostingData data)
    {
        // Load pricing percentages from data
        decimal contingencyPct = NormalizePct(data.Pricing?.ContingencyPct ?? 0.130m);
        decimal negotiationPct = NormalizePct(data.Pricing?.NegotiationMarginPct ?? 0.050m);

        // Calculate totals from tree rows
        decimal totalResourceSale = _resourceRows.Sum(g => g.TotalSale);
        decimal totalMaterialSale = _materialRows.Sum(m => m.TotalSale);

        // Travel totals from DA_CLIENTE sections
        decimal totalTravelSale = 0;
        foreach (var grp in _resourceRows)
        {
            foreach (var sec in grp.Children)
            {
                if (sec.IsDaCliente)
                {
                    foreach (var res in sec.Children)
                    {
                        totalTravelSale += res.TravelTotal + res.AccommodationTotal + res.AllowanceTotal;
                    }
                }
            }
        }

        _pricingVM = new PricingVM
        {
            TotalResourceSale = totalResourceSale,
            TotalMaterialSale = totalMaterialSale,
            TotalTravelSale = totalTravelSale,
            ContingencyPct = contingencyPct,
            NegotiationMarginPct = negotiationPct
        };

        // Build distribution rows
        BuildDistributionRows(data);

        // Bind pricing sections
        pricingSection.DataContext = _pricingVM;
        distributionSection.DataContext = _pricingVM;
        UpdateFinalPriceHeader();
    }

    private void NotifyPricingUpdated()
    {
        UpdateHeaderTotals();
        UpdateFinalPriceHeader();
        PricingUpdated?.Invoke();
    }

    private void UpdateHeaderTotals()
    {
        var resCost = _resourceRows.Sum(g => g.TotalCost);
        var resSale = _resourceRows.Sum(g => g.TotalSale);
        txtResourceTotals.Text = $"Netto {resCost:N2} EUR  |  Vendita {resSale:N2} EUR";

        var matCost = _materialRows.Sum(m => m.TotalCost);
        var matSale = _materialRows.Sum(m => m.TotalSale);
        txtMaterialTotals.Text = $"Netto {matCost:N2} EUR  |  Vendita {matSale:N2} EUR";
    }

    private void UpdateFinalPriceHeader()
    {
        txtFinalPriceHeader.Text = $"{_pricingVM.FinalOfferPrice:N2} EUR";
    }

    private void BuildDistributionRows(ProjectCostingData data)
    {
        // Preserve shadow state
        var shadowState = _pricingVM.DistributionRows.ToDictionary(
            r => $"{r.RowType}_{r.SectionId}_{r.ItemId}",
            r => r.IsShadowed);

        _pricingVM.DistributionRows.Clear();

        // Resource sections (unique by name)
        var allSections = (data.CostSections ?? new())
            .Where(s => s.IsEnabled)
            .GroupBy(s => s.Name?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Material items: use the same leaf items from _materialRows to ensure consistency
        var allMatItems = _materialRows
            .Select(r => new ProjectMaterialItemDto
            {
                Id = r.DbId,
                SectionId = r.SectionId,
                ParentItemId = r.ParentItemId,
                Description = r.Description,
                Quantity = r.Quantity,
                UnitCost = r.UnitCost,
                MarkupValue = r.MarkupValue,
                ItemType = r.ItemType
            })
            .ToList();
        // Restore distribution data (contingency/margin pins) from original data
        var allFlatItems = (data.MaterialSections ?? new()).SelectMany(ms => ms.Items ?? new()).ToList();
        foreach (var mat in allMatItems)
        {
            var orig = allFlatItems.FirstOrDefault(i => i.Id == mat.Id);
            if (orig != null)
            {
                mat.ContingencyPct = orig.ContingencyPct;
                mat.MarginPct = orig.MarginPct;
                mat.ContingencyPinned = orig.ContingencyPinned;
                mat.MarginPinned = orig.MarginPinned;
                mat.IsShadowed = orig.IsShadowed;
            }
        }

        // Calculate sale amounts from tree rows
        var sectionSales = new Dictionary<int, decimal>();
        foreach (var grp in _resourceRows)
            foreach (var sec in grp.Children)
                sectionSales[sec.DbId] = sec.TotalSale;

        var materialSales = new Dictionary<int, decimal>();
        foreach (var mat in _materialRows)
            materialSales[mat.DbId] = mat.TotalSale;

        // Step 1: Rebalance contingency %
        RebalanceDistPct(allSections, allMatItems, sectionSales, materialSales, "contingency");

        // Step 2: Rebalance margin %
        RebalanceDistPct(allSections, allMatItems, sectionSales, materialSales, "margin");

        // Step 3: Build rows
        foreach (var sec in allSections)
        {
            decimal sale = sectionSales.GetValueOrDefault(sec.Id, 0);
            if (sale == 0) continue;

            var row = new DistributionRowVM
            {
                RowType = "R",
                SectionId = sec.Id,
                ItemId = 0,
                SectionName = sec.Name ?? "",
                SaleAmount = sale,
                ContingencyPct = sec.ContingencyPct,
                ContingencyAmount = sec.ContingencyPct * _pricingVM.ContingencyAmount,
                IsContingencyPinned = sec.ContingencyPinned,
                MarginPct = sec.MarginPct,
                MarginAmount = sec.MarginPct * _pricingVM.NegotiationMarginAmount,
                IsMarginPinned = sec.MarginPinned,
                IsShadowed = sec.IsShadowed,
                SectionTotal = sale + (sec.ContingencyPct * _pricingVM.ContingencyAmount) + (sec.MarginPct * _pricingVM.NegotiationMarginAmount)
            };

            string key = $"R_{sec.Id}_0";
            if (shadowState.TryGetValue(key, out bool wasShadowed))
                row.IsShadowed = wasShadowed;

            _pricingVM.DistributionRows.Add(row);
        }

        foreach (var item in allMatItems)
        {
            decimal sale = materialSales.GetValueOrDefault(item.Id, 0);
            if (sale == 0) continue;

            var row = new DistributionRowVM
            {
                RowType = "M",
                SectionId = 0,
                ItemId = item.Id,
                SectionName = item.Description ?? "",
                SaleAmount = sale,
                ContingencyPct = item.ContingencyPct,
                ContingencyAmount = item.ContingencyPct * _pricingVM.ContingencyAmount,
                IsContingencyPinned = item.ContingencyPinned,
                MarginPct = item.MarginPct,
                MarginAmount = item.MarginPct * _pricingVM.NegotiationMarginAmount,
                IsMarginPinned = item.MarginPinned,
                IsShadowed = item.IsShadowed,
                SectionTotal = sale + (item.ContingencyPct * _pricingVM.ContingencyAmount) + (item.MarginPct * _pricingVM.NegotiationMarginAmount)
            };

            string key = $"M_0_{item.Id}";
            if (shadowState.TryGetValue(key, out bool wasShadowed))
                row.IsShadowed = wasShadowed;

            _pricingVM.DistributionRows.Add(row);
        }

        // Step 4: Recalc shadow
        RecalcShadow();
        _pricingVM.NotifyDistributionTotals();
    }

    private void RebalanceDistPct(
        List<ProjectCostSectionDto> sections,
        List<ProjectMaterialItemDto> matItems,
        Dictionary<int, decimal> sectionSales,
        Dictionary<int, decimal> materialSales,
        string field)
    {
        bool isContingency = field == "contingency";

        decimal pinnedSum = 0;
        var unpinned = new List<(Action<decimal> Set, decimal Sale)>();

        foreach (var s in sections)
        {
            decimal sale = sectionSales.GetValueOrDefault(s.Id, 0);
            if (sale == 0) continue;

            bool pinned = isContingency ? s.ContingencyPinned : s.MarginPinned;
            decimal pct = isContingency ? s.ContingencyPct : s.MarginPct;

            if (pinned)
                pinnedSum += pct;
            else
                unpinned.Add((v => { if (isContingency) s.ContingencyPct = v; else s.MarginPct = v; }, sale));
        }

        foreach (var i in matItems)
        {
            decimal sale = materialSales.GetValueOrDefault(i.Id, 0);
            if (sale == 0) continue;

            bool pinned = isContingency ? i.ContingencyPinned : i.MarginPinned;
            decimal pct = isContingency ? i.ContingencyPct : i.MarginPct;

            if (pinned)
                pinnedSum += pct;
            else
                unpinned.Add((v => { if (isContingency) i.ContingencyPct = v; else i.MarginPct = v; }, sale));
        }

        decimal remaining = Math.Max(0, 1m - pinnedSum);
        decimal totalSale = unpinned.Sum(u => u.Sale);

        foreach (var (set, sale) in unpinned)
            set(totalSale > 0 ? Math.Round(sale / totalSale * remaining, 4) : Math.Round(remaining / Math.Max(1, unpinned.Count), 4));
    }

    private void RecalcShadow()
    {
        var shadowed = _pricingVM.DistributionRows.Where(r => r.IsShadowed).ToList();
        var visible = _pricingVM.DistributionRows.Where(r => !r.IsShadowed).ToList();

        decimal totalShadowedSale = shadowed.Sum(r => r.SaleAmount);
        decimal totalVisibleSale = visible.Sum(r => r.SaleAmount);

        foreach (var row in _pricingVM.DistributionRows)
        {
            if (row.IsShadowed)
            {
                row.ShadowedAmount = 0;
                row.ShadowedPct = 0;
                row.SectionTotal = 0;
            }
            else if (totalVisibleSale > 0 && totalShadowedSale > 0)
            {
                decimal quota = row.SaleAmount / totalVisibleSale;
                row.ShadowedAmount = Math.Round(totalShadowedSale * quota, 2);
                row.ShadowedPct = quota;
                row.SectionTotal = row.SaleAmount + row.ContingencyAmount + row.MarginAmount + row.ShadowedAmount;
            }
            else
            {
                row.ShadowedAmount = 0;
                row.ShadowedPct = 0;
                row.SectionTotal = row.SaleAmount + row.ContingencyAmount + row.MarginAmount;
            }
        }
    }

    private void RecalcDistributionInPlace()
    {
        foreach (var row in _pricingVM.DistributionRows)
        {
            row.ContingencyAmount = row.ContingencyPct * _pricingVM.ContingencyAmount;
            row.MarginAmount = row.MarginPct * _pricingVM.NegotiationMarginAmount;
            row.SectionTotal = row.SaleAmount + row.ContingencyAmount + row.MarginAmount;
        }
        RecalcShadow();
        _pricingVM.NotifyDistributionTotals();
    }

    private static decimal NormalizePct(decimal value) => value > 1m ? value / 100m : value;

    // ── Pricing event handlers ──

    private void MarkupTextBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb) tb.SelectAll();
    }

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

            if (tag == "contingency" && val != _pricingVM.ContingencyPct)
            { _pricingVM.ContingencyPct = val; changed = true; }
            else if (tag == "negotiation" && val != _pricingVM.NegotiationMarginPct)
            { _pricingVM.NegotiationMarginPct = val; changed = true; }

            if (!changed) return;

            RecalcDistributionInPlace();
            NotifyPricingUpdated();
            await SavePricingMarkups();
            await SaveAllDistributions();
        }
        finally { _isPricingUpdating = false; }
    }

    private async void BtnGenerateDistribution_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("Resettare tutti i blocchi e ridistribuire proporzionalmente?",
            "Ridistribuisci", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (_lastData == null) return;

        // Reset all pins in underlying data
        foreach (var sec in _lastData.CostSections ?? new())
        { sec.ContingencyPinned = false; sec.MarginPinned = false; }
        foreach (var ms in _lastData.MaterialSections ?? new())
            foreach (var item in ms.Items ?? new())
            { item.ContingencyPinned = false; item.MarginPinned = false; }

        BuildDistributionRows(_lastData);
        await SaveAllDistributions();
    }

    private async void ShadowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is DistributionRowVM row)
        {
            row.IsShadowed = !row.IsShadowed;

            // Sync shadow state back to underlying data
            if (_lastData != null)
            {
                if (row.RowType == "R")
                {
                    var sec = (_lastData.CostSections ?? new()).FirstOrDefault(s => s.Id == row.SectionId);
                    if (sec != null) sec.IsShadowed = row.IsShadowed;
                }
                else
                {
                    var item = (_lastData.MaterialSections ?? new())
                        .SelectMany(ms => ms.Items ?? new())
                        .FirstOrDefault(i => i.Id == row.ItemId);
                    if (item != null) item.IsShadowed = row.IsShadowed;
                }
            }

            RecalcShadow();
            _pricingVM.NotifyDistributionTotals();
            await SaveAllDistributions();
        }
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

    private async Task ApplyDistPct(TextBox tb)
    {
        if (tb.DataContext is not DistributionRowVM distRow) return;

        string field = tb.Tag?.ToString() ?? "";
        string raw = tb.Text.Replace("%", "").Replace(",", ".").Trim();
        if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal val)) return;
        val /= 100m;

        if (_lastData == null) return;

        // PIN the edited row and sync to underlying data
        if (distRow.RowType == "R")
        {
            var sec = (_lastData.CostSections ?? new()).FirstOrDefault(s => s.Id == distRow.SectionId);
            if (sec == null) return;

            if (field == "contingency")
            {
                decimal otherPinned = (_lastData.CostSections ?? new())
                    .Where(s => s.ContingencyPinned && s.Id != sec.Id).Sum(s => s.ContingencyPct)
                    + (_lastData.MaterialSections ?? new()).SelectMany(ms => ms.Items ?? new())
                    .Where(i => i.ContingencyPinned).Sum(i => i.ContingencyPct);
                val = Math.Min(val, Math.Max(0, 1m - otherPinned));
                sec.ContingencyPinned = true;
                sec.ContingencyPct = val;
            }
            else
            {
                decimal otherPinned = (_lastData.CostSections ?? new())
                    .Where(s => s.MarginPinned && s.Id != sec.Id).Sum(s => s.MarginPct)
                    + (_lastData.MaterialSections ?? new()).SelectMany(ms => ms.Items ?? new())
                    .Where(i => i.MarginPinned).Sum(i => i.MarginPct);
                val = Math.Min(val, Math.Max(0, 1m - otherPinned));
                sec.MarginPinned = true;
                sec.MarginPct = val;
            }
        }
        else
        {
            var item = (_lastData.MaterialSections ?? new())
                .SelectMany(ms => ms.Items ?? new())
                .FirstOrDefault(i => i.Id == distRow.ItemId);
            if (item == null) return;

            if (field == "contingency")
            {
                decimal otherPinned = (_lastData.CostSections ?? new())
                    .Where(s => s.ContingencyPinned).Sum(s => s.ContingencyPct)
                    + (_lastData.MaterialSections ?? new()).SelectMany(ms => ms.Items ?? new())
                    .Where(i => i.ContingencyPinned && i.Id != item.Id).Sum(i => i.ContingencyPct);
                val = Math.Min(val, Math.Max(0, 1m - otherPinned));
                item.ContingencyPinned = true;
                item.ContingencyPct = val;
            }
            else
            {
                decimal otherPinned = (_lastData.CostSections ?? new())
                    .Where(s => s.MarginPinned).Sum(s => s.MarginPct)
                    + (_lastData.MaterialSections ?? new()).SelectMany(ms => ms.Items ?? new())
                    .Where(i => i.MarginPinned && i.Id != item.Id).Sum(i => i.MarginPct);
                val = Math.Min(val, Math.Max(0, 1m - otherPinned));
                item.MarginPinned = true;
                item.MarginPct = val;
            }
        }

        BuildDistributionRows(_lastData);
        await SaveAllDistributions();
    }

    // ── API save methods ──

    private async Task SavePricingMarkups()
    {
        try
        {
            var req = new
            {
                contingencyPct = _pricingVM.ContingencyPct,
                negotiationMarginPct = _pricingVM.NegotiationMarginPct,
                travelMarkup = 1.000m,
                allowanceMarkup = 1.000m
            };
            string json = JsonSerializer.Serialize(req, _joptCamel);
            await ApiClient.PutAsync($"{_apiBasePath}/pricing", json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore salvataggio pricing: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveAllDistributions()
    {
        if (_lastData == null) return;

        foreach (var sec in (_lastData.CostSections ?? new()).Where(s => s.IsEnabled))
        {
            try
            {
                var req = new
                {
                    contingencyPct = sec.ContingencyPct,
                    marginPct = sec.MarginPct,
                    contingencyPinned = sec.ContingencyPinned,
                    marginPinned = sec.MarginPinned,
                    isShadowed = sec.IsShadowed
                };
                string json = JsonSerializer.Serialize(req, _joptCamel);
                await ApiClient.PutAsync($"{_apiBasePath}/sections/{sec.Id}/distribution", json);
            }
            catch { }
        }

        foreach (var item in (_lastData.MaterialSections ?? new()).SelectMany(ms => ms.Items ?? new()))
        {
            try
            {
                var req = new
                {
                    contingencyPct = item.ContingencyPct,
                    marginPct = item.MarginPct,
                    contingencyPinned = item.ContingencyPinned,
                    marginPinned = item.MarginPinned,
                    isShadowed = item.IsShadowed
                };
                string json = JsonSerializer.Serialize(req, _joptCamel);
                await ApiClient.PutAsync($"{_apiBasePath}/material-items/{item.Id}/distribution", json);
            }
            catch { }
        }
    }
}
