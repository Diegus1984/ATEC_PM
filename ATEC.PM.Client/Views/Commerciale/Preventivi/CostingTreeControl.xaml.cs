using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Views.Costing;
using ATEC.PM.Shared.DTOs;
using System.Text.Json;
using ATEC.PM.Client.Views.Commerciale.Preventivi.Models;

namespace ATEC.PM.Client.Views.Commerciale.Preventivi;

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
    private List<ATEC.PM.Shared.DTOs.TariffOptionDto>? _tariffOptionsCache;
    /// <summary>DependencyProperty per binding XAML: nasconde bottoni azione quando true.</summary>
    public static readonly DependencyProperty IsReadOnlyModeProperty =
        DependencyProperty.Register(nameof(IsReadOnlyMode), typeof(bool), typeof(CostingTreeControl),
            new PropertyMetadata(false));

    public bool IsReadOnlyMode
    {
        get => (bool)GetValue(IsReadOnlyModeProperty);
        set => SetValue(IsReadOnlyModeProperty, value);
    }

    // Alias per i guard nei handler
    private bool _readOnly => IsReadOnlyMode;

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
            decimal Offer, decimal MargPct, decimal MargAmt, decimal Final, decimal TotalCost) GetPricingSummary()
    {
        // Costo totale risorse + materiali (senza markup)
        decimal resCost = _resourceRows.Sum(g => g.TotalCost);
        decimal matCost = _materialProducts.Sum(p => p.TotalCost);

        return (_pricingVM.ResourceDistributed, _pricingVM.MaterialDistributed, _pricingVM.TravelDistributed,
                _pricingVM.NetPrice, _pricingVM.ContingencyPct, _pricingVM.ContingencyAmount,
                _pricingVM.OfferPrice, _pricingVM.NegotiationMarginPct, _pricingVM.NegotiationMarginAmount,
                _pricingVM.FinalOfferPrice, resCost + matCost);
    }
    private ProjectCostingData? _lastData;

    // Color mapping for groups
    // Colore uniforme per tutti i gruppi (design system: blu corporate)
    // Colori e ordine gruppi — caricati dal DB
    private static Dictionary<string, string> GroupColors = new();
    private static Dictionary<string, int> GroupSortOrders = new();

    public CostingTreeControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Load costing data for a preventivo.
    /// </summary>
    public void LoadForPreventivo(int quoteId, bool readOnly = false)
    {
        _quoteId = quoteId;
        IsReadOnlyMode = readOnly;
        _apiBasePath = $"/api/quotes/{quoteId}/costing";
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

            // ── Carica tariffe trasferta ──
            await LoadTariffOptionsAsync();

            // ── Carica colori gruppi dal DB ──
            await LoadGroupColorsAsync();

            ProjectCostingData? data = await ApiClient.GetDataAsync<ProjectCostingData>(_apiBasePath);
            if (data == null) return;
            _lastData = data;

            // Build tutto subito (la UI è collassata → nessun costo di rendering)
            BuildResourceTree(data);
            BuildMaterialList(data);

            // Restore: solo i gruppi/sezioni che erano espansi prima del reload
            // Al primo load _isFirstLoad è true → tutto chiuso
            bool isReload = expandedGroups.Count > 0 || expandedSections.Count > 0;
            foreach (var grp in _resourceRows)
            {
                grp.IsExpanded = isReload && expandedGroups.Contains(grp.DisplayName);
                foreach (var sec in grp.Children)
                    sec.IsExpanded = isReload && expandedSections.Contains(sec.DbId);
            }

            groupsItemsControl.ItemsSource = _resourceRows;
            icMaterialProducts.ItemsSource = _materialProducts;

            BuildPricing(data);
            NotifyPricingUpdated();

            // Restore scroll
            _ = Dispatcher.InvokeAsync(() => mainScrollViewer.ScrollToVerticalOffset(scrollOffset),
                System.Windows.Threading.DispatcherPriority.Loaded);

            // Read-only: nessun rendering speciale necessario — i guard nei Click handler bloccano le modifiche
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore caricamento costing: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ══════════════════════════════════════════════════
    // 5.1 AUDIT — strategia reload (maggio 2026)
    // ══════════════════════════════════════════════════
    // FULL LoadDataAsync — GET + BuildResourceTree + BuildMaterialList + BuildPricing:
    //   • LoadForPreventivo (apertura)
    //
    // ReloadResourcesOnlyAsync — GET ma solo ramo risorse + pricing (expand preservato):
    //   • AddCostGroupDialog / AddCostSectionDialog (nuova struttura sezioni)
    //
    // ReloadMaterialsOnlyAsync — GET ma solo ramo materiali + pricing:
    //   • catalogo multi-prodotto, refresh-from-catalog, clone prodotto
    //
    // Patch locale + RefreshPricingFromTree (no GET):
    //   • edit risorsa/materiale (grid LostFocus) — già parziale
    //   • delete risorsa / variante / prodotto materiale
    //   • add risorsa in sezione, add variante manuale
    //   • toggle attivo materiale, rename parent materiale
    // ══════════════════════════════════════════════════

    /// <summary>Ricalcola scheda prezzi e distribuzione dai totali correnti dell'albero.</summary>
    private void RefreshPricingFromTree()
    {
        if (_lastData == null) return;
        BuildPricing(_lastData);
        NotifyPricingUpdated();
    }

    /// <summary>Ricarica solo albero risorse + pricing (evita rebuild materiali).</summary>
    private async Task ReloadResourcesOnlyAsync()
    {
        if (_lastData == null)
        {
            await LoadDataAsync();
            return;
        }

        _isLoading = true;
        try
        {
            var expandedGroups = _resourceRows.Where(g => g.IsExpanded).Select(g => g.DisplayName).ToHashSet();
            var expandedSections = _resourceRows.SelectMany(g => g.Children)
                .Where(s => s.IsExpanded).Select(s => s.DbId).ToHashSet();

            ProjectCostingData? data = await ApiClient.GetDataAsync<ProjectCostingData>(_apiBasePath);
            if (data == null) return;
            _lastData.CostSections = data.CostSections;
            _lastData.Pricing = data.Pricing;
            BuildResourceTree(_lastData);

            foreach (var grp in _resourceRows)
            {
                grp.IsExpanded = expandedGroups.Contains(grp.DisplayName);
                foreach (var sec in grp.Children)
                    sec.IsExpanded = expandedSections.Contains(sec.DbId);
            }

            groupsItemsControl.ItemsSource = _resourceRows;
            RefreshPricingFromTree();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore caricamento risorse: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>Ricarica solo materiali + pricing (evita rebuild albero risorse).</summary>
    private async Task ReloadMaterialsOnlyAsync()
    {
        if (_lastData == null)
        {
            await LoadDataAsync();
            return;
        }

        _isLoading = true;
        try
        {
            ProjectCostingData? data = await ApiClient.GetDataAsync<ProjectCostingData>(_apiBasePath);
            if (data == null) return;
            _lastData.MaterialSections = data.MaterialSections;
            BuildMaterialList(_lastData);
            RefreshPricingFromTree();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore caricamento materiali: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private CostingTreeRow? FindGroupContainingSection(CostingTreeRow sectionRow)
    {
        foreach (CostingTreeRow grp in _resourceRows)
        {
            if (grp.Children.Contains(sectionRow))
                return grp;
        }
        return null;
    }

    private ProjectCostSectionDto? FindCostSectionInLastData(int sectionDbId)
    {
        if (_lastData?.CostSections == null) return null;
        return _lastData.CostSections.FirstOrDefault(s => s.Id == sectionDbId);
    }

    private ProjectMaterialSectionDto? FindMaterialSectionInLastData(int sectionId)
    {
        if (_lastData?.MaterialSections == null) return null;
        return _lastData.MaterialSections.FirstOrDefault(s => s.Id == sectionId);
    }

    private void RemoveResourceFromLastData(int sectionDbId, int resourceDbId)
    {
        ProjectCostSectionDto? sec = FindCostSectionInLastData(sectionDbId);
        if (sec?.Resources == null) return;
        ProjectCostResourceDto? res = sec.Resources.FirstOrDefault(r => r.Id == resourceDbId);
        if (res != null)
            sec.Resources.Remove(res);
    }

    private void AddResourceToLastData(int sectionDbId, ProjectCostResourceDto dto)
    {
        ProjectCostSectionDto? sec = FindCostSectionInLastData(sectionDbId);
        if (sec == null) return;
        sec.Resources ??= new List<ProjectCostResourceDto>();
        sec.Resources.Add(dto);
    }

    private void RemoveMaterialItemFromLastData(int itemDbId)
    {
        if (_lastData?.MaterialSections == null) return;
        foreach (ProjectMaterialSectionDto sec in _lastData.MaterialSections)
        {
            if (sec.Items == null) continue;
            sec.Items.RemoveAll(i => i.Id == itemDbId || i.ParentItemId == itemDbId);
        }
    }

    private void AddMaterialItemToLastData(ProjectMaterialItemDto item)
    {
        ProjectMaterialSectionDto? sec = FindMaterialSectionInLastData(item.SectionId);
        if (sec == null) return;
        sec.Items ??= new List<ProjectMaterialItemDto>();
        sec.Items.Add(item);
    }

    private bool RemoveResourceFromTree(CostingTreeRow resourceRow)
    {
        foreach (CostingTreeRow grp in _resourceRows)
        {
            foreach (CostingTreeRow sec in grp.Children)
            {
                if (!sec.Children.Remove(resourceRow)) continue;
                RemoveResourceFromLastData(sec.DbId, resourceRow.DbId);
                RecalcSection(sec);
                RecalcGroupFromSections(grp);
                RefreshPricingFromTree();
                return true;
            }
        }
        return false;
    }

    private void AddResourceToTree(CostingTreeRow sectionRow, CostingTreeRow groupRow, int newId,
        string acronym, decimal hourlyCost, decimal markup)
    {
        decimal mk = markup > 0 ? markup : 1.450m;
        CostingTreeRow resNode = new CostingTreeRow
        {
            NodeId = _nextNodeId++,
            NodeType = "RESOURCE",
            DbId = newId,
            SectionDbId = sectionRow.DbId,
            DisplayName = acronym,
            WorkDays = 0,
            HoursPerDay = 8,
            HourlyCost = hourlyCost,
            MarkupValue = mk,
            TotalCost = 0,
            TotalSale = 0,
            SectionType = sectionRow.SectionType,
            GroupColor = groupRow.GroupColor,
            DepartmentCode = "",
            CostPerKm = 0.90m
        };
        sectionRow.Children.Add(resNode);
        sectionRow.IsExpanded = true;
        groupRow.IsExpanded = true;

        AddResourceToLastData(sectionRow.DbId, new ProjectCostResourceDto
        {
            Id = newId,
            SectionId = sectionRow.DbId,
            ResourceName = acronym,
            HoursPerDay = 8,
            HourlyCost = hourlyCost,
            MarkupValue = mk
        });

        RecalcSection(sectionRow);
        RecalcGroupFromSections(groupRow);
        RefreshPricingFromTree();
    }

    private bool RemoveMaterialVariantFromTree(MaterialTreeRow mat)
    {
        MaterialProductGroup? group = _materialProducts.FirstOrDefault(g => g.Variants.Contains(mat));
        if (group == null) return false;

        group.Variants.Remove(mat);
        _materialRows.Remove(mat);
        RemoveMaterialItemFromLastData(mat.DbId);

        if (group.Variants.Count == 0)
        {
            _materialProducts.Remove(group);
            RemoveMaterialItemFromLastData(group.ParentId);
        }
        else
            group.NotifyTotals();

        RefreshPricingFromTree();
        return true;
    }

    private bool RemoveMaterialProductFromTree(int parentId)
    {
        MaterialProductGroup? group = _materialProducts.FirstOrDefault(g => g.ParentId == parentId);
        if (group == null) return false;

        foreach (MaterialTreeRow v in group.Variants.ToList())
            _materialRows.Remove(v);
        _materialProducts.Remove(group);
        RemoveMaterialItemFromLastData(parentId);
        RefreshPricingFromTree();
        return true;
    }

    private void AddMaterialVariantToTree(MaterialProductGroup group, int variantId,
        string description, decimal quantity, decimal unitCost, decimal markupValue)
    {
        MaterialTreeRow row = new MaterialTreeRow
        {
            DbId = variantId,
            SectionId = group.SectionId,
            ParentItemId = group.ParentId,
            ProductId = group.ProductId,
            Description = description,
            DescriptionRtf = group.DescriptionRtf,
            Quantity = quantity,
            UnitCost = unitCost,
            MarkupValue = markupValue > 0 ? markupValue : 1.300m,
            ItemType = "MATERIAL",
            IsActive = quantity > 0
        };
        row.ParentGroup = group;
        group.Variants.Add(row);
        _materialRows.Add(row);

        AddMaterialItemToLastData(new ProjectMaterialItemDto
        {
            Id = variantId,
            SectionId = group.SectionId,
            ParentItemId = group.ParentId,
            ProductId = group.ProductId,
            Description = description,
            DescriptionRtf = group.DescriptionRtf,
            Quantity = quantity,
            UnitCost = unitCost,
            MarkupValue = row.MarkupValue,
            ItemType = "MATERIAL",
            IsActive = row.IsActive
        });

        group.NotifyTotals();
        RefreshPricingFromTree();
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
                        ProductId = parent.ProductId,
                        ParentName = parent.Description ?? "",
                        DescriptionRtf = parent.DescriptionRtf,
                        Variants = new ObservableCollection<MaterialTreeRow> { row }
                    };
                    WireMaterialGroup(group);
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
                        ProductId = parent.ProductId,
                        ParentName = parent.Description ?? "",
                        DescriptionRtf = parent.DescriptionRtf,
                        Variants = variants
                    };
                    WireMaterialGroup(group);
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
                WireMaterialGroup(group);
                _materialProducts.Add(group);
            }
        }
        UpdateHeaderTotals();
    }

    private static void WireMaterialGroup(MaterialProductGroup group)
    {
        foreach (MaterialTreeRow variant in group.Variants)
            variant.ParentGroup = group;
    }

    private static MaterialTreeRow BuildMaterialRow(ProjectMaterialItemDto item, int sectionId)
    {
        bool active = item.IsActive;
        decimal mk = item.MarkupValue > 0 ? item.MarkupValue : 1.300m;
        return new MaterialTreeRow
        {
            DbId = item.Id,
            SectionId = sectionId,
            ParentItemId = item.ParentItemId,
            ProductId = item.ProductId,
            VariantId = item.VariantId,
            Code = item.Code ?? "",
            DescriptionRtf = item.DescriptionRtf,
            Description = item.Description ?? "",
            ItemType = item.ItemType ?? "MATERIAL",
            IsActive = active,
            Quantity = item.Quantity,
            UnitCost = item.UnitCost,
            MarkupValue = mk,
            TotalCost = active ? item.Quantity * item.UnitCost : 0,
            TotalSale = active ? item.Quantity * item.UnitCost * mk : 0
        };
    }

    // ══════════════════════════════════════════════════
    // GROUP / SECTION / MATERIAL / PRICING EXPAND/COLLAPSE
    // ══════════════════════════════════════════════════

    private bool _resourcesExpanded = false;
    private bool _materialExpanded = false;  // Lazy: parte collassato
    private bool _pricingExpanded = false;   // Lazy: parte collassato
    private bool _distExpanded = false;      // Lazy: parte collassato


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
                RecalcParentTotalsFor(row);
                RefreshPricingFromTree();
            }
        }, System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private async void MaterialVariantField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _isLoading) return;

        MaterialTreeRow? mat = null;
        if (sender is FrameworkElement fe && fe.Tag is MaterialTreeRow m) mat = m;
        if (mat == null) return;

        mat.ParentGroup?.NotifyTotals();
        if (mat.IsDirty)
        {
            mat.IsDirty = false;
            await SaveMaterialAsync(mat);
            RefreshPricingFromTree();
        }
        else
        {
            UpdateHeaderTotals();
        }
    }

    private async void MaterialParentName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly || _isLoading) return;
        if (sender is not TextBox tb) return;
        if (tb.Tag is not MaterialProductGroup group) return;

        try
        {
            // Aggiorna descrizione del parent item nel DB via PUT
            var body = new
            {
                description = group.ParentName,
                code = "",
                descriptionRtf = group.DescriptionRtf ?? "",
                quantity = 0m,
                unitCost = 0m,
                markupValue = 1.300m,
                itemType = "PRODUCT",
                sortOrder = 0,
                isActive = true
            };
            await ApiClient.PutAsync($"{_apiBasePath}/material-items/{group.ParentId}",
                JsonSerializer.Serialize(body));
            RefreshPricingFromTree();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
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
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore salvataggio: {ex.Message}", "Errore",
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
                code = mat.Code,
                description = mat.Description,
                descriptionRtf = mat.DescriptionRtf,
                quantity = mat.Quantity,
                unitCost = mat.UnitCost,
                markupValue = mat.MarkupValue,
                itemType = mat.ItemType,
                isActive = mat.IsActive
            };

            await ApiClient.PutAsync($"{_apiBasePath}/material-items/{mat.DbId}",
                JsonSerializer.Serialize(body));
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore salvataggio materiale: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Recalculate parent totals after resource edit ──
    /// <summary>Ricalcola TUTTI i totali (usato solo al load iniziale)</summary>
    private void RecalcParentTotals()
    {
        foreach (var grp in _resourceRows)
            RecalcGroup(grp);
    }

    /// <summary>Trova la sezione padre di una risorsa nell'albero.</summary>
    private CostingTreeRow? FindParentSection(CostingTreeRow resourceRow)
    {
        foreach (CostingTreeRow grp in _resourceRows)
        {
            foreach (CostingTreeRow sec in grp.Children)
            {
                if (sec.Children.Contains(resourceRow))
                    return sec;
            }
        }
        return null;
    }

    /// <summary>Genera acronimo anonimo: codice reparto + contatore progressivo per sezione.</summary>
    private static string GenerateAcronym(string deptCode, CostingTreeRow? sectionRow)
    {
        if (string.IsNullOrEmpty(deptCode)) deptCode = "RIS";
        if (sectionRow == null) return $"{deptCode}1";
        // Conta risorse esistenti nella sezione con lo stesso prefisso reparto
        int count = sectionRow.Children
            .Count(c => c.IsResource &&
                        (c.DisplayName ?? "").StartsWith(deptCode, StringComparison.OrdinalIgnoreCase));
        return $"{deptCode}{count + 1}";
    }

    /// <summary>Ricalcola solo la sezione e il suo gruppo padre (usato dopo edit singolo)</summary>
    private void RecalcParentTotalsFor(CostingTreeRow row)
    {
        // Trova la sezione e il gruppo di questa risorsa
        foreach (var grp in _resourceRows)
        {
            foreach (var sec in grp.Children)
            {
                if (sec.Children.Contains(row) || sec == row)
                {
                    RecalcSection(sec);
                    RecalcGroupFromSections(grp);
                    return;
                }
            }
        }
        // Fallback: ricalcola tutto
        RecalcParentTotals();
    }

    private static void RecalcSection(CostingTreeRow sec)
    {
        sec.TotalCost = sec.Children.Sum(r => r.TotalCost);
        sec.TotalSale = sec.Children.Sum(r => r.TotalSale);
        sec.SumWorkDays = sec.Children.Sum(r => r.WorkDays);
        sec.SumTotalHours = sec.Children.Sum(r => r.TotalHours);
        sec.ResourceCount = sec.Children.Count;
    }

    private static void RecalcGroupFromSections(CostingTreeRow grp)
    {
        decimal grpCost = 0, grpSale = 0, grpDays = 0, grpHours = 0;
        int count = 0;
        foreach (var sec in grp.Children)
        {
            grpCost += sec.TotalCost;
            grpSale += sec.TotalSale;
            grpDays += sec.SumWorkDays;
            grpHours += sec.SumTotalHours;
            count += sec.ResourceCount;
        }
        grp.TotalCost = grpCost;
        grp.TotalSale = grpSale;
        grp.SumWorkDays = grpDays;
        grp.SumTotalHours = grpHours;
        grp.ResourceCount = count;
    }

    private static void RecalcGroup(CostingTreeRow grp)
    {
        foreach (var sec in grp.Children)
            RecalcSection(sec);
        RecalcGroupFromSections(grp);
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
                _employeeCache[sectionId] = await ApiClient.GetListAsync<EmployeeCostLookup>(
                    $"{_apiBasePath}/sections/{sectionId}/employees");
            }
            catch { _employeeCache[sectionId] = new(); }
        }

        // Filter out employees already assigned in this section (lookup diretto, no triple-loop)
        var usedEmployeeIds = new HashSet<int>();
        var section = _resourceRows.SelectMany(g => g.Children).FirstOrDefault(s => s.DbId == sectionId);
        if (section != null)
        {
            foreach (var res in section.Children)
            {
                if (res.EmployeeId.HasValue && res.DbId != row.DbId)
                    usedEmployeeIds.Add(res.EmployeeId.Value);
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
        if (_readOnly || _suppressEmployeeChange || _isLoading) return;
        if (sender is not ComboBox cmb) return;
        if (cmb.DataContext is not CostingTreeRow row || !row.IsResource) return;
        if (cmb.SelectedItem is not EmployeeCostLookup emp) return;

        row.EmployeeId = emp.Id;
        row.HourlyCost = emp.HourlyCost;
        row.MarkupValue = emp.DefaultMarkup > 0 ? emp.DefaultMarkup : 1.450m;
        row.DepartmentCode = emp.DepartmentCode;

        // Genera acronimo anonimo basato su codice reparto + contatore per sezione
        CostingTreeRow? parentSection = FindParentSection(row);
        row.DisplayName = GenerateAcronym(emp.DepartmentCode, parentSection);

        row.TotalCost = row.TotalHours * row.HourlyCost;
        row.TotalSale = row.TotalCost * row.MarkupValue;

        await SaveResourceAsync(row);
        RecalcParentTotalsFor(row);
        RefreshPricingFromTree();
    }

    private async Task LoadTariffOptionsAsync()
    {
        try
        {
            _tariffOptionsCache = await ApiClient.GetListAsync<ATEC.PM.Shared.DTOs.TariffOptionDto>("/api/tariff-options");
            if (_tariffOptionsCache.Count == 0) return;
            var items = _tariffOptionsCache;

            RefillTariffOptions(CostingTreeRow.CostPerKmOptions, items.Where(t => t.TariffType == "COST_PER_KM"));
            RefillTariffOptions(CostingTreeRow.DailyFoodOptions, items.Where(t => t.TariffType == "DAILY_FOOD"));
            RefillTariffOptions(CostingTreeRow.DailyHotelOptions, items.Where(t => t.TariffType == "DAILY_HOTEL"));
            RefillTariffOptions(CostingTreeRow.DailyAllowanceOptions, items.Where(t => t.TariffType == "DAILY_ALLOWANCE"));

            if (!CostingTreeRow.DailyAllowanceOptions.Contains(0m))
                CostingTreeRow.DailyAllowanceOptions.Insert(0, 0m);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading tariff options: {ex.Message}");
        }
    }

    private static void RefillTariffOptions(System.Collections.ObjectModel.ObservableCollection<decimal> col,
        IEnumerable<ATEC.PM.Shared.DTOs.TariffOptionDto> items)
    {
        col.Clear();
        foreach (decimal val in items.Select(t => t.Value).OrderBy(v => v))
            col.Add(val);
    }

    private async Task LoadGroupColorsAsync()
    {
        try
        {
            List<ATEC.PM.Shared.DTOs.CostSectionGroupDto> groups =
                await ApiClient.GetListAsync<ATEC.PM.Shared.DTOs.CostSectionGroupDto>("/api/cost-sections/groups");
            if (groups.Count == 0) return;

            foreach (var g in groups)
            {
                if (!string.IsNullOrEmpty(g.BgColor))
                    GroupColors[g.Name] = g.BgColor;
                GroupSortOrders[g.Name] = g.SortOrder;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading group colors: {ex.Message}");
        }
    }

    private static System.Windows.Media.SolidColorBrush HexBrush(string hex) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));

    private static string? PromptTariffInput(string title, string label)
    {
        Window prompt = new()
        {
            Title = title, Width = 300, Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = System.Windows.Media.Brushes.White
        };
        StackPanel sp = new() { Margin = new Thickness(16) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
        TextBox txt = new() { FontSize = 13, Height = 28, Padding = new Thickness(6, 4, 6, 4) };
        sp.Children.Add(txt);
        StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button btnOk = new()
        {
            Content = "Aggiungi", Width = 80, Height = 28,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2563EB")),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold
        };
        Button btnCancel = new()
        {
            Content = "Annulla", Width = 70, Height = 28, Margin = new Thickness(8, 0, 0, 0),
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F3F4F6")),
            BorderThickness = new Thickness(0)
        };
        string? result = null;
        btnOk.Click += (s, e) => { result = txt.Text; prompt.Close(); };
        btnCancel.Click += (s, e) => prompt.Close();
        buttons.Children.Add(btnOk); buttons.Children.Add(btnCancel);
        sp.Children.Add(buttons);
        prompt.Content = sp;
        txt.Focus();
        prompt.ShowDialog();
        return result;
    }

    private static System.Collections.ObjectModel.ObservableCollection<decimal>? GetTariffOptions(string field) => field switch
    {
        "CostPerKm" => CostingTreeRow.CostPerKmOptions,
        "DailyFood" => CostingTreeRow.DailyFoodOptions,
        "DailyHotel" => CostingTreeRow.DailyHotelOptions,
        "DailyAllowance" => CostingTreeRow.DailyAllowanceOptions,
        _ => null
    };

    /// <summary>Mappa nome proprietà C# → tariff_type nel DB</summary>
    private static string ToDbTariffType(string field) => field switch
    {
        "CostPerKm" => "COST_PER_KM",
        "DailyFood" => "DAILY_FOOD",
        "DailyHotel" => "DAILY_HOTEL",
        "DailyAllowance" => "DAILY_ALLOWANCE",
        _ => field
    };

    private void BtnTariffDrop_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string field) return;
        if (btn.DataContext is not CostingTreeRow row) return;

        var options = GetTariffOptions(field);
        if (options == null) return;

        // Costruisci popup con lista valori + × per riga + bottone Aggiungi
        StackPanel popContent = new();
        System.Windows.Controls.Primitives.Popup? popup = null;

        // Righe valori esistenti
        foreach (decimal opt in options.ToList())
        {
            decimal capturedVal = opt;
            DockPanel dp = new() { Margin = new Thickness(0, 1, 0, 1) };

            // Bottone ×
            Button btnX = new()
            {
                Content = "×", Width = 20, Height = 24, FontSize = 10, FontWeight = FontWeights.Bold,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = HexBrush("#DC2626"),
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnX.Click += async (s2, e2) =>
            {
                // Cerca id della tariffa
                string dbType = ToDbTariffType(field);
                var tariffItem = _tariffOptionsCache?.FirstOrDefault(t => t.TariffType == dbType && t.Value == capturedVal);
                if (tariffItem == null) return;
                try
                {
                    string result = await ApiClient.DeleteAsync($"/api/tariff-options/{tariffItem.Id}");
                    if (ApiClient.IsApiSuccess(result, out string msg))
                    {
                        if (popup != null) popup.IsOpen = false;
                        await LoadTariffOptionsAsync();
                    }
                    else
                        ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg ?? "Errore",
                            "Impossibile eliminare", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show(ex.Message); }
            };
            DockPanel.SetDock(btnX, Dock.Right);
            dp.Children.Add(btnX);

            // Valore cliccabile
            string display = field == "CostPerKm"
                ? capturedVal.ToString("N2", System.Globalization.CultureInfo.InvariantCulture)
                : capturedVal.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
            Button btnVal = new()
            {
                Content = display, Height = 24, FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 0, 8, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = HexBrush("#111827"),
                BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand
            };
            btnVal.Click += async (s2, e2) =>
            {
                if (popup != null) popup.IsOpen = false;
                switch (field)
                {
                    case "CostPerKm": row.CostPerKm = capturedVal; break;
                    case "DailyFood": row.DailyFood = capturedVal; break;
                    case "DailyHotel": row.DailyHotel = capturedVal; break;
                    case "DailyAllowance": row.DailyAllowance = capturedVal; break;
                }
                await SaveResourceAsync(row);
                RecalcParentTotalsFor(row);
                RefreshPricingFromTree();
            };
            dp.Children.Add(btnVal);
            popContent.Children.Add(dp);
        }

        // Separatore + bottone Nuovo valore
        popContent.Children.Add(new Border
        {
            Height = 1, Background = HexBrush("#D1D5DB"),
            Margin = new Thickness(4, 2, 4, 2)
        });
        Button btnAdd = new()
        {
            Content = "+ Nuovo valore", Height = 28, FontSize = 12, FontWeight = FontWeights.SemiBold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(8, 0, 8, 0),
            Background = HexBrush("#2563EB"),
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(2)
        };
        btnAdd.Click += async (s2, e2) =>
        {
            if (popup != null) popup.IsOpen = false;

            // Prompt per inserire il nuovo valore
            string? input = PromptTariffInput("Aggiungi Tariffa", "Inserisci il nuovo valore:");
            if (string.IsNullOrWhiteSpace(input)) return;
            if (!decimal.TryParse(input, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out decimal newVal) || newVal <= 0)
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Valore non valido.", "Errore");
                return;
            }

            try
            {
                string json = System.Text.Json.JsonSerializer.Serialize(
                    new { tariffType = ToDbTariffType(field), value = newVal },
                    new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase });
                string result = await ApiClient.PostAsync("/api/tariff-options", json);
                if (ApiClient.IsApiSuccess(result, out string msg))
                {
                    await LoadTariffOptionsAsync();
                    // Seleziona il nuovo valore nella riga
                    switch (field)
                    {
                        case "CostPerKm": row.CostPerKm = newVal; break;
                        case "DailyFood": row.DailyFood = newVal; break;
                        case "DailyHotel": row.DailyHotel = newVal; break;
                        case "DailyAllowance": row.DailyAllowance = newVal; break;
                    }
                    await SaveResourceAsync(row);
                    RecalcParentTotalsFor(row);
                    RefreshPricingFromTree();
                }
                else
                    ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg ?? "Errore", "Errore");
            }
            catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show(ex.Message); }
        };
        popContent.Children.Add(btnAdd);

        popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = btn,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            StaysOpen = false,
            Child = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = HexBrush("#D1D5DB"),
                BorderThickness = new Thickness(1),
                MinWidth = 130,
                Padding = new Thickness(4),
                Child = popContent
            }
        };
        popup.IsOpen = true;
    }

    private async void AllowanceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_readOnly || _isLoading) return;
        if (sender is not ComboBox cmb) return;
        if (cmb.DataContext is not CostingTreeRow row || !row.IsResource) return;
        if (cmb.SelectedItem is decimal val)
        {
            row.DailyAllowance = val;
            await SaveResourceAsync(row);
            RecalcParentTotalsFor(row);
            RefreshPricingFromTree();
        }
    }

    // ══════════════════════════════════════════════════
    // ADD / DELETE BUTTONS
    // ══════════════════════════════════════════════════

    private async void BtnAddGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        try
        {
            AvailableCostTemplatesDto? available = await ApiClient.GetDataAsync<AvailableCostTemplatesDto>(
                $"{_apiBasePath}/available-templates");
            List<CostSectionGroupDto> groups = available?.Groups ?? new();
            List<CostSectionTemplateDto> templates = available?.Templates ?? new();

            if (groups.Count == 0)
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Tutti i gruppi sono gia' presenti.", "Info",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new AddCostGroupDialog(_quoteId, groups, templates, _apiBasePath);
            if (dlg.ShowDialog() == true)
                await ReloadResourcesOnlyAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnAddSection_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        string groupName = "GESTIONE";
        if (_resourceRows.Count > 0)
            groupName = _resourceRows[0].DisplayName;

        try
        {
            AvailableCostTemplatesDto? available = await ApiClient.GetDataAsync<AvailableCostTemplatesDto>(
                $"{_apiBasePath}/available-templates");
            List<CostSectionTemplateDto> templates = available?.Templates ?? new();

            List<CostSectionTemplateDto> groupTemplates = templates.Where(t =>
                string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase)).ToList();

            AddCostSectionDialog dlg = new AddCostSectionDialog(_quoteId, groupName, groupTemplates, _apiBasePath);
            if (dlg.ShowDialog() == true)
                await ReloadResourcesOnlyAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnAddSectionInGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not CostingTreeRow groupRow || !groupRow.IsGroup) return;

        string groupName = groupRow.DisplayName;

        try
        {
            AvailableCostTemplatesDto? available = await ApiClient.GetDataAsync<AvailableCostTemplatesDto>(
                $"{_apiBasePath}/available-templates");
            List<CostSectionTemplateDto> templates = available?.Templates ?? new();

            List<CostSectionTemplateDto> groupTemplates = templates.Where(t =>
                string.Equals(t.GroupName, groupName, StringComparison.OrdinalIgnoreCase)).ToList();

            AddCostSectionDialog dlg = new AddCostSectionDialog(_quoteId, groupName, groupTemplates, _apiBasePath);
            if (dlg.ShowDialog() == true)
                await ReloadResourcesOnlyAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnAddResourceInSection_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not CostingTreeRow row || !row.IsSection) return;

        int sectionId = row.DbId;

        try
        {
            // Carica dipendenti della sezione per estrarre i reparti disponibili
            if (!_employeeCache.ContainsKey(sectionId))
            {
                _employeeCache[sectionId] = await ApiClient.GetListAsync<EmployeeCostLookup>(
                    $"{_apiBasePath}/sections/{sectionId}/employees");
            }

            // Estrai reparti unici con costo orario e markup
            List<EmployeeCostLookup> employees = _employeeCache[sectionId];
            var departments = employees
                .Where(emp => !string.IsNullOrEmpty(emp.DepartmentCode))
                .GroupBy(emp => emp.DepartmentCode)
                .Select(g => new { Code = g.Key, HourlyCost = g.First().HourlyCost, Markup = g.First().DefaultMarkup })
                .OrderBy(d => d.Code)
                .ToList();

            if (!departments.Any())
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Nessun reparto configurato per questa sezione.", "Info");
                return;
            }

            // Dialog per scegliere il reparto
            string selectedDeptCode;
            decimal hourlyCost;
            decimal markup;

            if (departments.Count == 1)
            {
                // Solo un reparto: selezione automatica
                selectedDeptCode = departments[0].Code;
                hourlyCost = departments[0].HourlyCost;
                markup = departments[0].Markup;
            }
            else
            {
                // Dialog per scegliere il reparto
                Window dlg = new()
                {
                    Title = "Seleziona Reparto",
                    Width = 300, Height = 150,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    ResizeMode = ResizeMode.NoResize,
                    Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F7F8FA"))
                };
                StackPanel sp = new() { Margin = new Thickness(16) };
                sp.Children.Add(new TextBlock
                {
                    Text = "REPARTO", FontSize = 11, FontWeight = FontWeights.SemiBold,
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B7280"))
                });
                ComboBox cmbDept = new() { Height = 28, Margin = new Thickness(0, 4, 0, 12), FontSize = 12 };
                foreach (var d in departments) cmbDept.Items.Add(d.Code);
                cmbDept.SelectedIndex = 0;
                sp.Children.Add(cmbDept);
                Button btnOk = new()
                {
                    Content = "Aggiungi", Height = 30, Width = 90,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4F6EF7")),
                    Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0),
                    FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand
                };
                btnOk.Click += (_, _) => { dlg.DialogResult = true; };
                sp.Children.Add(btnOk);
                dlg.Content = sp;
                if (dlg.ShowDialog() != true) return;

                string chosenCode = cmbDept.SelectedItem?.ToString() ?? "";
                var dept = departments.First(d => d.Code == chosenCode);
                selectedDeptCode = dept.Code;
                hourlyCost = dept.HourlyCost;
                markup = dept.Markup;
            }

            // Genera acronimo progressivo per la sezione
            string acronym = GenerateAcronym(selectedDeptCode, row);

            // Crea risorsa con acronimo e costo reparto
            var body = new
            {
                sectionId = sectionId,
                resourceName = acronym,
                hourlyCost = hourlyCost,
                markupValue = markup > 0 ? markup : 1.450m,
                hoursPerDay = 8
            };
            string result = await ApiClient.PostAsync($"{_apiBasePath}/resources",
                JsonSerializer.Serialize(body, _joptCamel));
            if (!ApiClient.TryGetApiData<int>(result, out int newId, out string errMsg))
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(errMsg ?? "Errore creazione risorsa", "Errore",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            CostingTreeRow? groupRow = FindGroupContainingSection(row);
            if (groupRow == null)
            {
                await LoadDataAsync();
                return;
            }

            AddResourceToTree(row, groupRow, newId, acronym, hourlyCost, markup);
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteResourceInRow_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not CostingTreeRow row || !row.IsResource) return;

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare la risorsa '{row.DisplayName}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"{_apiBasePath}/resources/{row.DbId}");
            if (!RemoveResourceFromTree(row))
                await LoadDataAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnAddMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
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
                string secResult = await ApiClient.PostAsync($"{_apiBasePath}/material-sections",
                    JsonSerializer.Serialize(new { name = "Materiali" }, _joptCamel));
                if (!ApiClient.TryGetApiData<int>(secResult, out int newSecId, out string secErr))
                {
                    ATEC.PM.Client.Controls.ShadcnMessageBox.Show(secErr ?? "Errore sezione materiali", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                materialSectionId = newSecId;
            }

            // Group selected variants by product
            var byProduct = picker.SelectedVariants.GroupBy(v => v.ProductId);

            foreach (var productGroup in byProduct)
            {
                int productId = productGroup.Key;
                string productName = productGroup.First().ProductName;

                // Create parent item (product) con FK catalogo
                var parentBody = new
                {
                    sectionId = materialSectionId,
                    productId,
                    description = productName,
                    quantity = 0m,
                    unitCost = 0m,
                    markupValue = 1.300m,
                    itemType = "PRODUCT"
                };
                string parentResult = await ApiClient.PostAsync($"{_apiBasePath}/material-items",
                    JsonSerializer.Serialize(parentBody, _joptCamel));
                if (!ApiClient.TryGetApiData<int>(parentResult, out int parentId, out string parentErr))
                {
                    ATEC.PM.Client.Controls.ShadcnMessageBox.Show(parentErr ?? "Errore creazione prodotto", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Create child items — TUTTE le varianti del prodotto
                foreach (var v in productGroup)
                {
                    var childBody = new
                    {
                        description = v.VariantName,
                        code = v.VariantCode,
                        variantId = v.VariantId,
                        productId,
                        quantity = v.Quantity,
                        unitCost = v.CostPrice,
                        markupValue = v.SellPrice > 0 && v.CostPrice > 0
                            ? Math.Round(v.SellPrice / v.CostPrice, 3)
                            : 1.300m,
                        itemType = "MATERIAL",
                        isActive = v.Quantity > 0  // attiva solo se qty > 0
                    };
                    await ApiClient.PostAsync($"{_apiBasePath}/material-items/{parentId}/variant",
                        JsonSerializer.Serialize(childBody));
                }

                // Refresh da catalogo per ottenere description_rtf
                await ApiClient.PostAsync($"{_apiBasePath}/material-items/{parentId}/refresh-from-catalog",
                    JsonSerializer.Serialize(new { }));
            }

            await ReloadMaterialsOnlyAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteMaterial_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn) return;
        if (btn.Tag is not MaterialTreeRow mat) return;

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare la variante '{mat.Description}'?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.DeleteAsync($"{_apiBasePath}/material-items/{mat.DbId}");
            if (!RemoveMaterialVariantFromTree(mat))
                await ReloadMaterialsOnlyAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteMaterialProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not int parentId) return;
        var group = _materialProducts.FirstOrDefault(g => g.ParentId == parentId);
        string name = group?.ParentName ?? $"#{parentId}";

        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Eliminare '{name}' e tutte le sue varianti?", "Conferma",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            // Delete parent (CASCADE deletes children)
            await ApiClient.DeleteAsync($"{_apiBasePath}/material-items/{parentId}");
            if (!RemoveMaterialProductFromTree(parentId))
                await ReloadMaterialsOnlyAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnAddMaterialVariant_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
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
                string result = await ApiClient.PostAsync($"{_apiBasePath}/material-items/{parentId}/variant",
                    JsonSerializer.Serialize(body, _joptCamel));
                if (!ApiClient.TryGetApiData<int>(result, out int variantId, out string varErr))
                {
                    ATEC.PM.Client.Controls.ShadcnMessageBox.Show(varErr ?? "Errore creazione variante", "Errore",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                AddMaterialVariantToTree(group, variantId, dlg.Description, dlg.Quantity, dlg.UnitCost, dlg.MarkupValue);
            }
            catch (Exception ex)
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
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
    // MATERIAL PRODUCT ACTIONS (5 pulsanti)
    // ══════════════════════════════════════════════════

    /// <summary>Toggle is_active su variante materiale</summary>
    private async void ChkMaterialActive_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not CheckBox chk || chk.Tag is not MaterialTreeRow mat) return;
        try
        {
            await ApiClient.PatchAsync($"{_apiBasePath}/material-items/{mat.DbId}/toggle-active",
                JsonSerializer.Serialize(new { isActive = mat.IsActive }));

            // Aggiorna totali del gruppo e header materiali
            MaterialProductGroup? group = _materialProducts.FirstOrDefault(g => g.Variants.Contains(mat));
            group?.NotifyTotals();
            RefreshPricingFromTree();
        }
        catch (Exception ex)
        {
            mat.IsActive = !mat.IsActive; // rollback
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>1: Aggiorna locale da catalogo</summary>
    private async void BtnRefreshMaterialFromCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not MaterialProductGroup group) return;
        if (!group.HasCatalogLink)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Questo prodotto non è collegato al catalogo.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Sovrascrivere '{group.ParentName}' con i dati del catalogo?\nLe modifiche locali andranno perse.",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.PostAsync($"{_apiBasePath}/material-items/{group.ParentId}/refresh-from-catalog",
                JsonSerializer.Serialize(new { }));
            await ReloadMaterialsOnlyAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>2: Push locale → catalogo</summary>
    private async void BtnPushMaterialToCatalog_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not MaterialProductGroup group) return;
        if (!group.HasCatalogLink)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Questo prodotto non è collegato al catalogo.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Aggiornare il catalogo con i dati locali di '{group.ParentName}'?",
            "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.PostAsync($"{_apiBasePath}/material-items/{group.ParentId}/push-to-catalog",
                JsonSerializer.Serialize(new { }));
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Catalogo aggiornato.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>3: Modifica descrizione RTF</summary>
    private async void BtnEditMaterialRtf_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not MaterialProductGroup group) return;

        var dlg = new MaterialRtfDialog(group.ParentName, group.DescriptionRtf)
        {
            Owner = Window.GetWindow(this)
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                group.DescriptionRtf = dlg.HtmlContent;
                // Salva description_rtf nel DB
                var body = new
                {
                    description = group.ParentName,
                    code = "",
                    descriptionRtf = dlg.HtmlContent,
                    quantity = 0m,
                    unitCost = 0m,
                    markupValue = 1.300m,
                    itemType = "PRODUCT",
                    sortOrder = 0,
                    isActive = true
                };
                await ApiClient.PutAsync($"{_apiBasePath}/material-items/{group.ParentId}",
                    JsonSerializer.Serialize(body));
            }
            catch (Exception ex)
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore salvataggio: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>4: Clona prodotto materiale</summary>
    private async void BtnCloneMaterialProduct_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.Tag is not MaterialProductGroup group) return;

        var result = ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
            $"Duplicare \"{group.ParentName}\" con tutte le varianti?",
            "Conferma duplicazione", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await ApiClient.PostAsync($"{_apiBasePath}/material-items/{group.ParentId}/clone",
                JsonSerializer.Serialize(new { }));
            await ReloadMaterialsOnlyAsync();
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ══════════════════════════════════════════════════
    // HELPERS
    // ══════════════════════════════════════════════════

    private static int GetGroupSortOrder(string groupName) =>
        GroupSortOrders.TryGetValue(groupName, out int order) ? order : 99;

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
        decimal resCost = 0, resSale = 0;
        foreach (var g in _resourceRows) { resCost += g.TotalCost; resSale += g.TotalSale; }
        txtResourceTotals.Text = $"Netto {resCost:N2} EUR  |  Vendita {resSale:N2} EUR";

        decimal matCost = 0, matSale = 0;
        foreach (var m in _materialRows) { matCost += m.TotalCost; matSale += m.TotalSale; }
        txtMaterialTotals.Text = $"Netto {matCost:N2} EUR  |  Vendita {matSale:N2} EUR";
    }

    private void UpdateFinalPriceHeader()
    {
        txtFinalPriceHeader.Text = $"{_pricingVM.FinalOfferPrice:N2} EUR";
    }

    // ══════════════════════════════════════════════════════════════
    // DISTRIBUZIONE COSTI — unico flusso consolidato
    //
    // Regola fondamentale: le righe SHADOWED (occhiolino) sono
    // "assenti" dalla tabella. Non ricevono percentuali, non ricevono
    // importi. Il loro costo base (SaleAmount) viene spalmato
    // proporzionalmente sulle righe visibili come ShadowedAmount.
    //
    // Flusso:
    //   1. RebalanceDistPct()  — distribuisce % tra le righe visibili
    //   2. ApplyAmounts()      — calcola EUR da % (righe visibili)
    //                           + azzera tutto sulle shadowed
    //   3. ApplyShadow()       — spalma il costo delle shadowed sulle visibili
    // ══════════════════════════════════════════════════════════════

    private void BuildDistributionRows(ProjectCostingData data)
    {
        // Preserve shadow state from UI (toggle occhiolino può essere cambiato senza save)
        var shadowState = _pricingVM.DistributionRows.ToDictionary(
            r => $"{r.RowType}_{r.SectionId}_{r.ItemId}",
            r => r.IsShadowed);

        _pricingVM.DistributionRows.Clear();

        // ── Raccogli dati sorgente ──

        var allSections = (data.CostSections ?? new())
            .Where(s => s.IsEnabled)
            .GroupBy(s => s.Name?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var allMatItems = _materialRows
            .Select(r => new ProjectMaterialItemDto
            {
                Id = r.DbId, SectionId = r.SectionId, ParentItemId = r.ParentItemId,
                Description = r.Description, Quantity = r.Quantity,
                UnitCost = r.UnitCost, MarkupValue = r.MarkupValue, ItemType = r.ItemType
            })
            .ToList();

        // Ripristina pin/shadow dai dati originali DB
        var allFlatItems = (data.MaterialSections ?? new()).SelectMany(ms => ms.Items ?? new()).ToList();
        foreach (var mat in allMatItems)
        {
            var orig = allFlatItems.FirstOrDefault(i => i.Id == mat.Id);
            if (orig == null) continue;
            mat.ContingencyPct = orig.ContingencyPct;
            mat.MarginPct = orig.MarginPct;
            mat.ContingencyPinned = orig.ContingencyPinned;
            mat.MarginPinned = orig.MarginPinned;
            mat.IsShadowed = orig.IsShadowed;
        }

        // Vendite da tree rows
        var sectionSales = new Dictionary<int, decimal>();
        foreach (var grp in _resourceRows)
            foreach (var sec in grp.Children)
                sectionSales[sec.DbId] = sec.TotalSale;

        var materialSales = new Dictionary<int, decimal>();
        foreach (var mat in _materialRows)
            materialSales[mat.DbId] = mat.TotalSale;

        // ── 1. Distribuisci percentuali (solo righe visibili) ──
        RebalanceDistPct(allSections, allMatItems, sectionSales, materialSales, "contingency");
        RebalanceDistPct(allSections, allMatItems, sectionSales, materialSales, "margin");

        // ── 2. Crea DistributionRowVM ──
        foreach (var sec in allSections)
        {
            decimal sale = sectionSales.GetValueOrDefault(sec.Id, 0);
            if (sale == 0) continue;

            bool shadowed = sec.IsShadowed;
            string key = $"R_{sec.Id}_0";
            if (shadowState.TryGetValue(key, out bool uiShadowed)) shadowed = uiShadowed;

            _pricingVM.DistributionRows.Add(new DistributionRowVM
            {
                RowType = "R", SectionId = sec.Id, ItemId = 0,
                SectionName = sec.Name ?? "", SaleAmount = sale,
                ContingencyPct = sec.ContingencyPct, IsContingencyPinned = sec.ContingencyPinned,
                MarginPct = sec.MarginPct, IsMarginPinned = sec.MarginPinned,
                IsShadowed = shadowed
            });
        }

        foreach (var item in allMatItems)
        {
            decimal sale = materialSales.GetValueOrDefault(item.Id, 0);
            if (sale == 0) continue;

            bool shadowed = item.IsShadowed;
            string key = $"M_0_{item.Id}";
            if (shadowState.TryGetValue(key, out bool uiShadowed)) shadowed = uiShadowed;

            _pricingVM.DistributionRows.Add(new DistributionRowVM
            {
                RowType = "M", SectionId = 0, ItemId = item.Id,
                SectionName = item.Description ?? "", SaleAmount = sale,
                ContingencyPct = item.ContingencyPct, IsContingencyPinned = item.ContingencyPinned,
                MarginPct = item.MarginPct, IsMarginPinned = item.MarginPinned,
                IsShadowed = shadowed
            });
        }

        // ── 3. Calcola importi + shadow ──
        ApplyAmountsAndShadow();
    }

    /// <summary>
    /// Distribuisce le % di contingency/margin tra le righe NON shadowed.
    /// Le shadowed ricevono 0%. Le pinned mantengono il loro valore.
    /// Le unpinned si dividono il rimanente proporzionalmente alla vendita.
    /// </summary>
    private void RebalanceDistPct(
        List<ProjectCostSectionDto> sections,
        List<ProjectMaterialItemDto> matItems,
        Dictionary<int, decimal> sectionSales,
        Dictionary<int, decimal> materialSales,
        string field)
    {
        bool isCont = field == "contingency";
        decimal pinnedSum = 0;
        var unpinned = new List<(Action<decimal> Set, decimal Sale)>();

        foreach (var s in sections)
        {
            if (s.IsShadowed) { ZeroPct(s, isCont); continue; }
            decimal sale = sectionSales.GetValueOrDefault(s.Id, 0);
            if (sale == 0) continue;
            if (isCont ? s.ContingencyPinned : s.MarginPinned)
                pinnedSum += isCont ? s.ContingencyPct : s.MarginPct;
            else
                unpinned.Add((v => { if (isCont) s.ContingencyPct = v; else s.MarginPct = v; }, sale));
        }

        foreach (var i in matItems)
        {
            if (i.IsShadowed) { ZeroPct(i, isCont); continue; }
            decimal sale = materialSales.GetValueOrDefault(i.Id, 0);
            if (sale == 0) continue;
            if (isCont ? i.ContingencyPinned : i.MarginPinned)
                pinnedSum += isCont ? i.ContingencyPct : i.MarginPct;
            else
                unpinned.Add((v => { if (isCont) i.ContingencyPct = v; else i.MarginPct = v; }, sale));
        }

        decimal remaining = Math.Max(0, 1m - pinnedSum);
        decimal totalSale = unpinned.Sum(u => u.Sale);
        foreach (var (set, sale) in unpinned)
            set(totalSale > 0
                ? Math.Round(sale / totalSale * remaining, 4)
                : Math.Round(remaining / Math.Max(1, unpinned.Count), 4));

        static void ZeroPct(dynamic dto, bool isCont)
        {
            if (isCont) dto.ContingencyPct = 0m; else dto.MarginPct = 0m;
        }
    }

    /// <summary>
    /// Unico punto che calcola gli importi EUR su tutte le righe distribuzione.
    /// Shadowed: tutto a zero. Visibili: importi da %, poi shadow spalmato.
    /// </summary>
    private void ApplyAmountsAndShadow()
    {
        decimal contPool = _pricingVM.ContingencyAmount;
        decimal margPool = _pricingVM.NegotiationMarginAmount;

        // Azzera shadowed, calcola visibili
        foreach (var row in _pricingVM.DistributionRows)
        {
            if (row.IsShadowed)
            {
                row.ContingencyPct = 0; row.ContingencyAmount = 0;
                row.MarginPct = 0; row.MarginAmount = 0;
                row.ShadowedAmount = 0; row.ShadowedPct = 0;
                row.SectionTotal = 0;
            }
            else
            {
                row.ContingencyAmount = row.ContingencyPct * contPool;
                row.MarginAmount = row.MarginPct * margPool;
                row.ShadowedAmount = 0; row.ShadowedPct = 0;
                row.SectionTotal = row.SaleAmount + row.ContingencyAmount + row.MarginAmount;
            }
        }

        // Spalma il costo delle righe shadowed sulle visibili (proporzionale alla vendita)
        decimal totalShadowedSale = _pricingVM.DistributionRows.Where(r => r.IsShadowed).Sum(r => r.SaleAmount);
        decimal totalVisibleSale = _pricingVM.DistributionRows.Where(r => !r.IsShadowed).Sum(r => r.SaleAmount);

        if (totalShadowedSale > 0 && totalVisibleSale > 0)
        {
            foreach (var row in _pricingVM.DistributionRows.Where(r => !r.IsShadowed))
            {
                decimal quota = row.SaleAmount / totalVisibleSale;
                row.ShadowedAmount = Math.Round(totalShadowedSale * quota, 2);
                row.ShadowedPct = quota;
                row.SectionTotal += row.ShadowedAmount;
            }
        }

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
        if (_readOnly) return;
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            await ApplyAndSavePricing(tb);
            Keyboard.ClearFocus();
        }
    }

    private async void PricingPct_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
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

            ApplyAmountsAndShadow();
            NotifyPricingUpdated();
            await SavePricingMarkups();
            await SaveAllDistributions();
        }
        finally { _isPricingUpdating = false; }
    }

    private async void BtnGenerateDistribution_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Resettare tutti i blocchi e ridistribuire proporzionalmente?",
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
        NotifyPricingUpdated();
        await SaveAllDistributions();
    }

    private async void ShadowToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
        if (sender is not Button btn || btn.DataContext is not DistributionRowVM row) return;

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

            // Rebuild completo: RebalanceDistPct ridistribuisce le % includendo/escludendo la riga
            BuildDistributionRows(_lastData);
        }
        else
        {
            ApplyAmountsAndShadow();
        }

        NotifyPricingUpdated();
        await SaveAllDistributions();
    }

    private async void DistPct_KeyDown(object sender, KeyEventArgs e)
    {
        if (_readOnly) return;
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            e.Handled = true;
            await ApplyDistPct(tb);
            Keyboard.ClearFocus();
        }
    }

    private async void DistPct_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_readOnly) return;
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
        NotifyPricingUpdated();
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
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore salvataggio pricing: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveAllDistributions()
    {
        if (_lastData == null) return;

        try
        {
            var batch = new
            {
                sections = (_lastData.CostSections ?? new()).Where(s => s.IsEnabled).Select(s => new
                {
                    id = s.Id, contingencyPct = s.ContingencyPct, marginPct = s.MarginPct,
                    contingencyPinned = s.ContingencyPinned, marginPinned = s.MarginPinned, isShadowed = s.IsShadowed
                }).ToList(),
                materialItems = (_lastData.MaterialSections ?? new()).SelectMany(ms => ms.Items ?? new()).Select(i => new
                {
                    id = i.Id, contingencyPct = i.ContingencyPct, marginPct = i.MarginPct,
                    contingencyPinned = i.ContingencyPinned, marginPinned = i.MarginPinned, isShadowed = i.IsShadowed
                }).ToList()
            };
            await ApiClient.PutAsync($"{_apiBasePath}/distributions/batch",
                JsonSerializer.Serialize(batch, _joptCamel));
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore salvataggio ripartizioni: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
