using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ATEC.PM.Client.Views.Costing.ViewModels;

public class CostingViewModel : INotifyPropertyChanged
{
    private bool _isInitialized;
    public bool IsInitialized { get => _isInitialized; set { _isInitialized = value; Notify(); Notify(nameof(ShowInit)); Notify(nameof(ShowContent)); } }

    public bool ShowInit => !IsInitialized;
    public bool ShowContent => IsInitialized;

    public bool IsOfferMode { get; set; }

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; Notify(); } }

    // Risorse
    public ObservableCollection<CostGroupVM> Groups { get; set; } = new();

    private decimal _grandTotalCost;
    public decimal GrandTotalCost { get => _grandTotalCost; private set { _grandTotalCost = value; Notify(); Notify(nameof(TotalCombinedCost)); } }

    private decimal _grandTotalSale;
    public decimal GrandTotalSale { get => _grandTotalSale; private set { _grandTotalSale = value; Notify(); Notify(nameof(TotalCombinedSale)); } }

    // Materiali
    public ObservableCollection<MaterialSectionVM> MaterialSections { get; set; } = new();

    private decimal _grandMaterialCost;
    public decimal GrandMaterialCost { get => _grandMaterialCost; private set { _grandMaterialCost = value; Notify(); Notify(nameof(TotalCombinedCost)); } }

    private decimal _grandMaterialSale;
    public decimal GrandMaterialSale { get => _grandMaterialSale; private set { _grandMaterialSale = value; Notify(); Notify(nameof(TotalCombinedSale)); } }

    // Trasferte calcolate dalle risorse DA_CLIENTE
    private decimal _totalTravelCost;
    public decimal TotalTravelCost { get => _totalTravelCost; private set { _totalTravelCost = value; Notify(); Notify(nameof(TotalTravelSale)); Notify(nameof(HasTravel)); Notify(nameof(TotalCombinedCost)); } }

    private decimal _totalAllowanceCost;
    public decimal TotalAllowanceCost { get => _totalAllowanceCost; private set { _totalAllowanceCost = value; Notify(); Notify(nameof(TotalAllowanceSale)); Notify(nameof(HasAllowance)); Notify(nameof(TotalCombinedCost)); } }

    private decimal _travelMarkup = 1.000m;
    public decimal TravelMarkup
    {
        get => _travelMarkup;
        set { _travelMarkup = value; Notify(); Notify(nameof(TotalTravelSale)); Notify(nameof(GrandMaterialSaleWithTravel)); Notify(nameof(TotalCombinedSale)); }
    }

    private decimal _allowanceMarkup = 1.000m;
    public decimal AllowanceMarkup
    {
        get => _allowanceMarkup;
        set { _allowanceMarkup = value; Notify(); Notify(nameof(TotalAllowanceSale)); Notify(nameof(GrandMaterialSaleWithTravel)); Notify(nameof(TotalCombinedSale)); }
    }

    public decimal TotalTravelSale => TotalTravelCost * TravelMarkup;
    public decimal TotalAllowanceSale => TotalAllowanceCost * AllowanceMarkup;

    public bool HasTravel => TotalTravelCost > 0;
    public bool HasAllowance => TotalAllowanceCost > 0;

    // Totale materiali + trasferte
    public decimal GrandMaterialCostWithTravel => GrandMaterialCost + TotalTravelCost + TotalAllowanceCost;
    public decimal GrandMaterialSaleWithTravel => GrandMaterialSale + TotalTravelSale + TotalAllowanceSale;

    // Totali combinati
    public decimal TotalCombinedCost => GrandTotalCost + GrandMaterialCostWithTravel;
    public decimal TotalCombinedSale => GrandTotalSale + GrandMaterialSaleWithTravel;

    // ══════════════════════════════════════════════════════════════
    // SCHEDA PREZZI — NET → OFFER → FINAL
    // ══════════════════════════════════════════════════════════════

    private decimal _contingencyPct = 0.130m;
    public decimal ContingencyPct
    {
        get => _contingencyPct;
        set
        {
            _contingencyPct = value;
            Notify(); Notify(nameof(ContingencyAmount));
            Notify(nameof(OfferPrice)); Notify(nameof(NegotiationMarginAmount)); Notify(nameof(FinalOfferPrice));
            Notify(nameof(ResourceDistributed)); Notify(nameof(MaterialDistributed)); Notify(nameof(TravelDistributed));
            Notify(nameof(TotalDistContingencyCheck)); Notify(nameof(TotalDistMarginCheck));
        }
    }

    private decimal _negotiationMarginPct = 0.050m;
    public decimal NegotiationMarginPct
    {
        get => _negotiationMarginPct;
        set
        {
            _negotiationMarginPct = value;
            Notify(); Notify(nameof(NegotiationMarginAmount)); Notify(nameof(FinalOfferPrice));
            Notify(nameof(ResourceDistributed)); Notify(nameof(MaterialDistributed)); Notify(nameof(TravelDistributed));
            Notify(nameof(TotalDistContingencyCheck)); Notify(nameof(TotalDistMarginCheck));
        }
    }

    // NET PRICE = totale vendita combinato
    public decimal NetPrice => TotalCombinedSale;

    // Importi calcolati dalle %
    public decimal ContingencyAmount => NetPrice * ContingencyPct;

    // OFFER = NET + contingency
    public decimal OfferPrice => NetPrice + ContingencyAmount;

    // Margine trattativa
    public decimal NegotiationMarginAmount => OfferPrice * NegotiationMarginPct;

    // FINAL = OFFER + margine
    public decimal FinalOfferPrice => OfferPrice + NegotiationMarginAmount;

    // ══════════════════════════════════════════════════════════════
    // DISTRIBUZIONE PREZZO — pesi per macro-categoria
    // ══════════════════════════════════════════════════════════════

    public decimal ResourceDistributed => TotalCombinedSale > 0 ? FinalOfferPrice * (GrandTotalSale / TotalCombinedSale) : 0;
    public decimal MaterialDistributed => TotalCombinedSale > 0 ? FinalOfferPrice * (GrandMaterialSale / TotalCombinedSale) : 0;
    public decimal TravelDistributed => TotalCombinedSale > 0 ? FinalOfferPrice * ((TotalTravelSale + TotalAllowanceSale) / TotalCombinedSale) : 0;

    // Distribuzione per sezione: importi contingency/margine calcolati dalle % sezione
    // (le % sono editabili nelle righe sezione, qui esponiamo gli importi calcolati)
    public decimal TotalDistContingencyCheck =>
        Groups.SelectMany(g => g.Sections).Sum(s => s.ContingencyPct * ContingencyAmount) +
        MaterialSections.SelectMany(s => s.Items).Sum(i => i.ContingencyPct * ContingencyAmount);
    public decimal TotalDistMarginCheck =>
        Groups.SelectMany(g => g.Sections).Sum(s => s.MarginPct * NegotiationMarginAmount) +
        MaterialSections.SelectMany(s => s.Items).Sum(i => i.MarginPct * NegotiationMarginAmount);

    // Tabella distribuzione per sezione
    public ObservableCollection<DistributionRowVM> DistributionRows { get; set; } = new();

    // ══════════════════════════════════════════════════════════════

    private bool _isRecalculating;

    public void RecalcGrandTotals()
    {
        if (_isRecalculating) return;
        _isRecalculating = true;

        try
        {
            GrandTotalCost = Groups.Sum(g => g.TotalCost);
            GrandTotalSale = Groups.Sum(g => g.TotalSale);
            GrandMaterialCost = MaterialSections.Sum(s => s.TotalCost);
            GrandMaterialSale = MaterialSections.Sum(s => s.TotalSale);

            // Trasferte dalle risorse DA_CLIENTE
            TotalTravelCost = Groups.SelectMany(g => g.Sections)
                .Where(s => s.IsDaCliente)
                .Sum(s => s.TotalTravelExpenses);
            TotalAllowanceCost = Groups.SelectMany(g => g.Sections)
                .Where(s => s.IsDaCliente)
                .Sum(s => s.TotalAllowanceExpenses);

            Notify(nameof(GrandMaterialCostWithTravel));
            Notify(nameof(GrandMaterialSaleWithTravel));
            Notify(nameof(TotalCombinedCost));
            Notify(nameof(TotalCombinedSale));

            // Scheda prezzi — ricalcolo a cascata
            Notify(nameof(NetPrice));
            Notify(nameof(ContingencyAmount));
            Notify(nameof(OfferPrice));
            Notify(nameof(NegotiationMarginAmount));
            Notify(nameof(FinalOfferPrice));

            // Distribuzione prezzo
            Notify(nameof(ResourceDistributed));
            Notify(nameof(MaterialDistributed));
            Notify(nameof(TravelDistributed));
            Notify(nameof(TotalDistContingencyCheck));
            Notify(nameof(TotalDistMarginCheck));

            RebuildDistributionRows();

            int secCount = Groups.Sum(g => g.Sections.Count);
            StatusText = $"{secCount} sezioni risorse, {MaterialSections.Count} categorie materiali — " +
                         $"Netto {TotalCombinedCost:N2} € — Vendita {TotalCombinedSale:N2} €";
        }
        finally
        {
            _isRecalculating = false;
        }
    }

    private void RebuildDistributionRows()
    {
        var allSections = Groups.SelectMany(g => g.Sections).ToList();
        var allMatItems = MaterialSections.SelectMany(s => s.Items).ToList();
        int expectedCount = allSections.Count + allMatItems.Count;

        if (DistributionRows.Count != expectedCount)
        {
            DistributionRows.Clear();
            foreach (var sec in allSections)
            {
                DistributionRows.Add(new DistributionRowVM
                {
                    RowType = "R", SectionId = sec.Id, ItemId = 0,
                    SectionName = sec.Name, SaleAmount = sec.TotalSale,
                    ContingencyPct = sec.ContingencyPct, ContingencyAmount = sec.ContingencyPct * ContingencyAmount,
                    IsContingencyPinned = sec.IsContingencyPinned,
                    MarginPct = sec.MarginPct, MarginAmount = sec.MarginPct * NegotiationMarginAmount,
                    IsMarginPinned = sec.IsMarginPinned,
                    SectionTotal = sec.TotalSale + (sec.ContingencyPct * ContingencyAmount) + (sec.MarginPct * NegotiationMarginAmount)
                });
            }
            foreach (var item in allMatItems)
            {
                DistributionRows.Add(new DistributionRowVM
                {
                    RowType = "M", SectionId = 0, ItemId = item.Id,
                    SectionName = item.Description, SaleAmount = item.TotalSale,
                    ContingencyPct = item.ContingencyPct, ContingencyAmount = item.ContingencyPct * ContingencyAmount,
                    IsContingencyPinned = item.IsContingencyPinned,
                    MarginPct = item.MarginPct, MarginAmount = item.MarginPct * NegotiationMarginAmount,
                    IsMarginPinned = item.IsMarginPinned,
                    SectionTotal = item.TotalSale + (item.ContingencyPct * ContingencyAmount) + (item.MarginPct * NegotiationMarginAmount)
                });
            }
            return;
        }

        int idx = 0;
        foreach (var sec in allSections)
        {
            var row = DistributionRows[idx++];
            row.RowType = "R"; row.SectionId = sec.Id; row.ItemId = 0;
            row.SectionName = sec.Name; row.SaleAmount = sec.TotalSale;
            row.ContingencyPct = sec.ContingencyPct; row.ContingencyAmount = sec.ContingencyPct * ContingencyAmount;
            row.IsContingencyPinned = sec.IsContingencyPinned;
            row.MarginPct = sec.MarginPct; row.MarginAmount = sec.MarginPct * NegotiationMarginAmount;
            row.IsMarginPinned = sec.IsMarginPinned;
            row.SectionTotal = sec.TotalSale + (sec.ContingencyPct * ContingencyAmount) + (sec.MarginPct * NegotiationMarginAmount);
        }
        foreach (var item in allMatItems)
        {
            var row = DistributionRows[idx++];
            row.RowType = "M"; row.SectionId = 0; row.ItemId = item.Id;
            row.SectionName = item.Description; row.SaleAmount = item.TotalSale;
            row.ContingencyPct = item.ContingencyPct; row.ContingencyAmount = item.ContingencyPct * ContingencyAmount;
            row.IsContingencyPinned = item.IsContingencyPinned;
            row.MarginPct = item.MarginPct; row.MarginAmount = item.MarginPct * NegotiationMarginAmount;
            row.IsMarginPinned = item.IsMarginPinned;
            row.SectionTotal = item.TotalSale + (item.ContingencyPct * ContingencyAmount) + (item.MarginPct * NegotiationMarginAmount);
        }
    }

    /// <summary>
    /// Ribilancia le % non-pinned per un campo (contingency o margin).
    /// Pool unificato: sezioni risorse + singoli item materiale.
    /// </summary>
    public void RebalanceUnpinned(string field)
    {
        var allSections = Groups.SelectMany(g => g.Sections).ToList();
        var allMatItems = MaterialSections.SelectMany(s => s.Items).ToList();

        decimal pinnedTotal = 0;
        if (field == "contingency")
        {
            pinnedTotal += allSections.Where(s => s.IsContingencyPinned).Sum(s => s.ContingencyPct);
            pinnedTotal += allMatItems.Where(i => i.IsContingencyPinned).Sum(i => i.ContingencyPct);
        }
        else
        {
            pinnedTotal += allSections.Where(s => s.IsMarginPinned).Sum(s => s.MarginPct);
            pinnedTotal += allMatItems.Where(i => i.IsMarginPinned).Sum(i => i.MarginPct);
        }

        decimal remaining = Math.Max(0, 1m - pinnedTotal);
        var unpinned = new List<(Action<decimal> SetPct, decimal Sale)>();

        foreach (var s in allSections)
        {
            bool pinned = field == "contingency" ? s.IsContingencyPinned : s.IsMarginPinned;
            if (!pinned)
                unpinned.Add((val => { if (field == "contingency") s.ContingencyPct = val; else s.MarginPct = val; }, s.TotalSale));
        }
        foreach (var i in allMatItems)
        {
            bool pinned = field == "contingency" ? i.IsContingencyPinned : i.IsMarginPinned;
            if (!pinned)
                unpinned.Add((val => { if (field == "contingency") i.ContingencyPct = val; else i.MarginPct = val; }, i.TotalSale));
        }

        if (unpinned.Count == 0) return;

        decimal totalSaleUnpinned = unpinned.Sum(u => u.Sale);
        foreach (var (setPct, sale) in unpinned)
        {
            decimal newVal = totalSaleUnpinned > 0
                ? Math.Round(sale / totalSaleUnpinned * remaining, 4)
                : Math.Round(remaining / unpinned.Count, 4);
            setPct(newVal);
        }
    }

    public void WireAllChanges()
    {
        // Risorse
        foreach (var group in Groups)
        {
            foreach (var sec in group.Sections)
            {
                sec.WireResourceChanges();
                sec.RecalcTotals();
            }
            group.WireSectionChanges();
            group.RecalcTotals();

            group.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(CostGroupVM.TotalCost) or nameof(CostGroupVM.TotalSale))
                    RecalcGrandTotals();
            };
        }

        // Materiali
        foreach (var matSec in MaterialSections)
        {
            matSec.WireItemChanges();
            matSec.RecalcTotals();

            matSec.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(MaterialSectionVM.TotalCost) or nameof(MaterialSectionVM.TotalSale))
                    RecalcGrandTotals();
            };
        }

        RecalcGrandTotals();
    }

    public static CostingViewModel FromData(ProjectCostingData data)
    {
        var vm = new CostingViewModel { IsInitialized = data.IsInitialized };
        if (!data.IsInitialized) return vm;

        // K trasferta dal pricing
        vm.TravelMarkup = data.Pricing.TravelMarkup;
        vm.AllowanceMarkup = data.Pricing.AllowanceMarkup;

        // Scheda prezzi dal pricing
        vm.ContingencyPct = NormalizePct(data.Pricing.ContingencyPct);
        vm.NegotiationMarginPct = NormalizePct(data.Pricing.NegotiationMarginPct);

        var colorMap = new Dictionary<string, string>
        {
            { "GESTIONE", "#2563EB" }, { "PRESCHIERAMENTO", "#7C3AED" },
            { "INSTALLAZIONE", "#D97706" }, { "OPZIONE", "#DC2626" }
        };

        // Risorse
        var groups = data.CostSections
            .Where(s => s.IsEnabled)
            .GroupBy(s => s.GroupName)
            .OrderBy(g => data.CostSections.Where(s => s.GroupName == g.Key).Min(s => s.SortOrder));

        foreach (var grp in groups)
        {
            var groupVM = new CostGroupVM
            {
                Name = grp.Key,
                Color = colorMap.TryGetValue(grp.Key, out string? c) ? c : "#6B7280"
            };

            foreach (var sec in grp.OrderBy(s => s.SortOrder))
            {
                var secVM = new CostSectionVM
                {
                    Id = sec.Id,
                    TemplateId = sec.TemplateId,
                    Name = sec.Name,
                    SectionType = sec.SectionType,
                    DepartmentIds = sec.DepartmentIds,
                    ContingencyPct = sec.ContingencyPct,
                    MarginPct = sec.MarginPct,
                    IsContingencyPinned = sec.ContingencyPinned,
                    IsMarginPinned = sec.MarginPinned
                };

                foreach (var res in sec.Resources.OrderBy(r => r.SortOrder))
                {
                    secVM.Resources.Add(new CostResourceVM
                    {
                        Id = res.Id,
                        SectionId = res.SectionId,
                        EmployeeId = res.EmployeeId,
                        ResourceName = res.ResourceName,
                        WorkDays = res.WorkDays,
                        HoursPerDay = res.HoursPerDay,
                        HourlyCost = res.HourlyCost,
                        MarkupValue = res.MarkupValue,
                        NumTrips = res.NumTrips,
                        KmPerTrip = res.KmPerTrip,
                        CostPerKm = res.CostPerKm,
                        DailyFood = res.DailyFood,
                        DailyHotel = res.DailyHotel,
                        AllowanceDays = res.AllowanceDays,
                        DailyAllowance = res.DailyAllowance
                    });
                }

                groupVM.Sections.Add(secVM);
            }

            vm.Groups.Add(groupVM);
        }

        // Materiali
        foreach (var matSec in data.MaterialSections.Where(s => s.IsEnabled).OrderBy(s => s.SortOrder))
        {
            var matVM = new MaterialSectionVM
            {
                Id = matSec.Id,
                CategoryId = matSec.CategoryId,
                Name = matSec.Name,
                DefaultMarkup = matSec.MarkupValue,
                DefaultCommissionMarkup = matSec.CommissionMarkup
            };

            foreach (var item in matSec.Items.OrderBy(i => i.SortOrder))
            {
                matVM.Items.Add(new MaterialItemVM
                {
                    Id = item.Id,
                    SectionId = item.SectionId,
                    Description = item.Description,
                    Quantity = item.Quantity,
                    UnitCost = item.UnitCost,
                    MarkupValue = item.MarkupValue,
                    ItemType = item.ItemType
                });
            }

            vm.MaterialSections.Add(matVM);
        }

        vm.WireAllChanges();
        return vm;
    }

    /// <summary>
    /// Se il valore è > 1 assume formato percentuale (es. 8.00 = 8%) e lo converte in decimale (0.08).
    /// Se è già ≤ 1 lo lascia invariato (già in formato decimale).
    /// </summary>
    private static decimal NormalizePct(decimal value) => value > 1m ? value / 100m : value;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class DistributionRowVM : INotifyPropertyChanged
{
    /// "R" = sezione risorse, "M" = item materiale
    public string RowType { get; set; } = "R";
    public int SectionId { get; set; }
    /// Per material items: MaterialItemVM.Id
    public int ItemId { get; set; }
    public string TypeBadge => RowType == "M" ? "M" : "R";
    public string TypeBadgeColor => RowType == "M" ? "#7C3AED" : "#2563EB";

    private string _sectionName = "";
    public string SectionName { get => _sectionName; set { _sectionName = value; Notify(); } }

    private decimal _saleAmount;
    public decimal SaleAmount { get => _saleAmount; set { _saleAmount = value; Notify(); } }

    private decimal _contingencyPct;
    public decimal ContingencyPct { get => _contingencyPct; set { _contingencyPct = value; Notify(); } }

    private decimal _contingencyAmount;
    public decimal ContingencyAmount { get => _contingencyAmount; set { _contingencyAmount = value; Notify(); } }

    private bool _isContingencyPinned;
    public bool IsContingencyPinned { get => _isContingencyPinned; set { _isContingencyPinned = value; Notify(); Notify(nameof(ContingencyPinIcon)); } }
    public string ContingencyPinIcon => IsContingencyPinned ? "🔒" : "";

    private decimal _marginPct;
    public decimal MarginPct { get => _marginPct; set { _marginPct = value; Notify(); } }

    private decimal _marginAmount;
    public decimal MarginAmount { get => _marginAmount; set { _marginAmount = value; Notify(); } }

    private bool _isMarginPinned;
    public bool IsMarginPinned { get => _isMarginPinned; set { _isMarginPinned = value; Notify(); Notify(nameof(MarginPinIcon)); } }
    public string MarginPinIcon => IsMarginPinned ? "🔒" : "";

    private decimal _sectionTotal;
    public decimal SectionTotal { get => _sectionTotal; set { _sectionTotal = value; Notify(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}