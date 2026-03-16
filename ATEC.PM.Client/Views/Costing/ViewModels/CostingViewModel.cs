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

    private decimal _structureCostsPct = 0.020m;
    public decimal StructureCostsPct
    {
        get => _structureCostsPct;
        set
        {
            _structureCostsPct = value;
            Notify(); Notify(nameof(StructureCostsAmount));
            Notify(nameof(OfferPrice)); Notify(nameof(NegotiationMarginAmount)); Notify(nameof(FinalOfferPrice));
        }
    }

    private decimal _contingencyPct = 0.050m;
    public decimal ContingencyPct
    {
        get => _contingencyPct;
        set
        {
            _contingencyPct = value;
            Notify(); Notify(nameof(ContingencyAmount));
            Notify(nameof(OfferPrice)); Notify(nameof(NegotiationMarginAmount)); Notify(nameof(FinalOfferPrice));
        }
    }

    private decimal _riskWarrantyPct = 0.050m;
    public decimal RiskWarrantyPct
    {
        get => _riskWarrantyPct;
        set
        {
            _riskWarrantyPct = value;
            Notify(); Notify(nameof(RiskWarrantyAmount));
            Notify(nameof(OfferPrice)); Notify(nameof(NegotiationMarginAmount)); Notify(nameof(FinalOfferPrice));
        }
    }

    private decimal _negotiationMarginPct = 0.100m;
    public decimal NegotiationMarginPct
    {
        get => _negotiationMarginPct;
        set
        {
            _negotiationMarginPct = value;
            Notify(); Notify(nameof(NegotiationMarginAmount)); Notify(nameof(FinalOfferPrice));
        }
    }

    // NET PRICE = totale vendita combinato
    public decimal NetPrice => TotalCombinedSale;

    // Importi calcolati dalle %
    public decimal StructureCostsAmount => NetPrice * StructureCostsPct;
    public decimal ContingencyAmount => NetPrice * ContingencyPct;
    public decimal RiskWarrantyAmount => NetPrice * RiskWarrantyPct;

    // OFFER = NET + struttura + contingency + rischi
    public decimal OfferPrice => NetPrice + StructureCostsAmount + ContingencyAmount + RiskWarrantyAmount;

    // Margine trattativa
    public decimal NegotiationMarginAmount => OfferPrice * NegotiationMarginPct;

    // FINAL = OFFER + margine
    public decimal FinalOfferPrice => OfferPrice + NegotiationMarginAmount;

    // ══════════════════════════════════════════════════════════════

    public void RecalcGrandTotals()
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
        Notify(nameof(StructureCostsAmount));
        Notify(nameof(ContingencyAmount));
        Notify(nameof(RiskWarrantyAmount));
        Notify(nameof(OfferPrice));
        Notify(nameof(NegotiationMarginAmount));
        Notify(nameof(FinalOfferPrice));

        int secCount = Groups.Sum(g => g.Sections.Count);
        StatusText = $"{secCount} sezioni risorse, {MaterialSections.Count} categorie materiali — " +
                     $"Netto {TotalCombinedCost:N2} € — Vendita {TotalCombinedSale:N2} €";
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
        // DB può avere formato percentuale (8.00 = 8%) o decimale (0.08 = 8%)
        // Il ViewModel lavora in decimale (0.08), normalizza se necessario
        vm.StructureCostsPct = NormalizePct(data.Pricing.StructureCostsPct);
        vm.ContingencyPct = NormalizePct(data.Pricing.ContingencyPct);
        vm.RiskWarrantyPct = NormalizePct(data.Pricing.RiskWarrantyPct);
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
                    DepartmentIds = sec.DepartmentIds
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
