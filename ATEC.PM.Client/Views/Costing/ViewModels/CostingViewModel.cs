using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Serilog;

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

            // LA PROC — ribilancia + ricalcola importi + aggiorna righe
            RecalcDistribution();

            Notify(nameof(TotalDistContingencyCheck));
            Notify(nameof(TotalDistMarginCheck));

            int secCount = Groups.Sum(g => g.Sections.Count);
            StatusText = $"{secCount} sezioni risorse, {MaterialSections.Count} categorie materiali — " +
                         $"Netto {TotalCombinedCost:N2} € — Vendita {TotalCombinedSale:N2} €";
        }
        finally
        {
            _isRecalculating = false;
        }
    }

    /// <summary>
    /// LA PROC. Unico punto di ricalcolo distribuzione.
    /// </summary>
    private static int _recalcCounter = 0;

    private void RecalcDistribution()
    {
        _recalcCounter++;
        int callNum = _recalcCounter;

        var stack = new System.Diagnostics.StackTrace(1, false);
        var frames = stack.GetFrames()?.Take(5).Select(f => f.GetMethod()?.Name ?? "?") ?? Array.Empty<string>();
        Log.Debug("═══ RecalcDistribution #{CallNum} da: {Caller}", callNum, string.Join(" → ", frames));

        var allSections = Groups.SelectMany(g => g.Sections)
            .Where(s => s.TotalSale != 0)
            .GroupBy(s => s.Name?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        var allMatItems = MaterialSections.SelectMany(s => s.Items)
            .Where(i => i.TotalSale != 0)
            .GroupBy(i => i.Description?.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        // Log PRIMA
        foreach (var s in allSections)
            Log.Debug("  PRIMA R [{Id}] {Name}: Sale={Sale:N2} ContPct={Cont:P1} ContPin={ContPin} MargPct={Marg:P1} MargPin={MargPin}",
                s.Id, s.Name, s.TotalSale, s.ContingencyPct, s.IsContingencyPinned, s.MarginPct, s.IsMarginPinned);
        foreach (var i in allMatItems)
            Log.Debug("  PRIMA M [{Id}] {Desc}: Sale={Sale:N2} ContPct={Cont:P1} ContPin={ContPin} MargPct={Marg:P1} MargPin={MargPin}",
                i.Id, i.Description, i.TotalSale, i.ContingencyPct, i.IsContingencyPinned, i.MarginPct, i.IsMarginPinned);

        // ── STEP 1: ribilancia CONT % ──
        {
            decimal pinnedSum = allSections.Where(s => s.IsContingencyPinned).Sum(s => s.ContingencyPct)
                              + allMatItems.Where(i => i.IsContingencyPinned).Sum(i => i.ContingencyPct);
            decimal remaining = Math.Max(0, 1m - pinnedSum);

            var unpinned = new List<(Action<decimal> Set, decimal Sale)>();
            foreach (var s in allSections.Where(s => !s.IsContingencyPinned))
                unpinned.Add((v => s.ContingencyPct = v, s.TotalSale));
            foreach (var i in allMatItems.Where(i => !i.IsContingencyPinned))
                unpinned.Add((v => i.ContingencyPct = v, i.TotalSale));

            decimal totalSale = unpinned.Sum(u => u.Sale);
            Log.Debug("  CONT: pinnedSum={PinnedSum:P1} remaining={Remaining:P1} unpinned={Count} totalSale={Total:N2}",
                pinnedSum, remaining, unpinned.Count, totalSale);
            foreach (var (set, sale) in unpinned)
                set(totalSale > 0 ? Math.Round(sale / totalSale * remaining, 4) : Math.Round(remaining / Math.Max(1, unpinned.Count), 4));
        }

        // ── STEP 2: ribilancia MARG % ──
        {
            decimal pinnedSum = allSections.Where(s => s.IsMarginPinned).Sum(s => s.MarginPct)
                              + allMatItems.Where(i => i.IsMarginPinned).Sum(i => i.MarginPct);
            decimal remaining = Math.Max(0, 1m - pinnedSum);

            var unpinned = new List<(Action<decimal> Set, decimal Sale)>();
            foreach (var s in allSections.Where(s => !s.IsMarginPinned))
                unpinned.Add((v => s.MarginPct = v, s.TotalSale));
            foreach (var i in allMatItems.Where(i => !i.IsMarginPinned))
                unpinned.Add((v => i.MarginPct = v, i.TotalSale));

            decimal totalSale = unpinned.Sum(u => u.Sale);
            Log.Debug("  MARG: pinnedSum={PinnedSum:P1} remaining={Remaining:P1} unpinned={Count} totalSale={Total:N2}",
                pinnedSum, remaining, unpinned.Count, totalSale);
            foreach (var (set, sale) in unpinned)
                set(totalSale > 0 ? Math.Round(sale / totalSale * remaining, 4) : Math.Round(remaining / Math.Max(1, unpinned.Count), 4));
        }

        // Log DOPO
        foreach (var s in allSections)
            Log.Debug("  DOPO R [{Id}] {Name}: ContPct={Cont:P1} ContPin={ContPin} MargPct={Marg:P1} MargPin={MargPin}",
                s.Id, s.Name, s.ContingencyPct, s.IsContingencyPinned, s.MarginPct, s.IsMarginPinned);
        foreach (var i in allMatItems)
            Log.Debug("  DOPO M [{Id}] {Desc}: ContPct={Cont:P1} ContPin={ContPin} MargPct={Marg:P1} MargPin={MargPin}",
                i.Id, i.Description, i.ContingencyPct, i.IsContingencyPinned, i.MarginPct, i.IsMarginPinned);

        // ── STEP 3: aggiorna tabella DistributionRows ──
        int expectedCount = allSections.Count + allMatItems.Count;

        // Preserva stato shadow dalle righe esistenti
        var shadowState = DistributionRows.ToDictionary(
            r => $"{r.RowType}_{r.SectionId}_{r.ItemId}",
            r => r.IsShadowed);

        if (DistributionRows.Count != expectedCount)
        {
            Log.Debug("  REBUILD righe: {Old} → {New}", DistributionRows.Count, expectedCount);
            DistributionRows.Clear();
            foreach (var sec in allSections)
                DistributionRows.Add(MakeDistRow("R", sec.Id, 0, sec.Name, sec.TotalSale, sec.ContingencyPct, sec.IsContingencyPinned, sec.MarginPct, sec.IsMarginPinned));
            foreach (var item in allMatItems)
                DistributionRows.Add(MakeDistRow("M", 0, item.Id, item.Description, item.TotalSale, item.ContingencyPct, item.IsContingencyPinned, item.MarginPct, item.IsMarginPinned));
        }
        else
        {
            Log.Debug("  UPDATE in-place: {Count} righe", expectedCount);
            int idx = 0;
            foreach (var sec in allSections)
                UpdateDistRow(DistributionRows[idx++], "R", sec.Id, 0, sec.Name, sec.TotalSale, sec.ContingencyPct, sec.IsContingencyPinned, sec.MarginPct, sec.IsMarginPinned);
            foreach (var item in allMatItems)
                UpdateDistRow(DistributionRows[idx++], "M", 0, item.Id, item.Description, item.TotalSale, item.ContingencyPct, item.IsContingencyPinned, item.MarginPct, item.IsMarginPinned);
        }

        // Ripristina stato shadow (da dict precedente o dal VM sottostante al primo load)
        foreach (var row in DistributionRows)
        {
            string key = $"{row.RowType}_{row.SectionId}_{row.ItemId}";
            if (shadowState.TryGetValue(key, out bool wasShadowed))
                row.IsShadowed = wasShadowed;
            else if (row.RowType == "R")
            {
                var sec = allSections.FirstOrDefault(s => s.Id == row.SectionId);
                if (sec != null) row.IsShadowed = sec.IsShadowed;
            }
            else
            {
                var item = allMatItems.FirstOrDefault(i => i.Id == row.ItemId);
                if (item != null) row.IsShadowed = item.IsShadowed;
            }
        }

        // ── STEP 4: calcola spalmo shadow ──
        RecalcShadow();

        Log.Debug("═══ Fine RecalcDistribution #{CallNum}", callNum);
    }

    private DistributionRowVM MakeDistRow(string type, int secId, int itemId, string name, decimal sale,
        decimal contPct, bool contPin, decimal margPct, bool margPin)
    {
        return new DistributionRowVM
        {
            RowType = type, SectionId = secId, ItemId = itemId,
            SectionName = name, SaleAmount = sale,
            ContingencyPct = contPct, ContingencyAmount = contPct * ContingencyAmount, IsContingencyPinned = contPin,
            MarginPct = margPct, MarginAmount = margPct * NegotiationMarginAmount, IsMarginPinned = margPin,
            SectionTotal = sale + (contPct * ContingencyAmount) + (margPct * NegotiationMarginAmount)
        };
    }

    private void UpdateDistRow(DistributionRowVM row, string type, int secId, int itemId, string name, decimal sale,
        decimal contPct, bool contPin, decimal margPct, bool margPin)
    {
        row.RowType = type; row.SectionId = secId; row.ItemId = itemId;
        row.SectionName = name; row.SaleAmount = sale;
        row.ContingencyPct = contPct; row.ContingencyAmount = contPct * ContingencyAmount; row.IsContingencyPinned = contPin;
        row.MarginPct = margPct; row.MarginAmount = margPct * NegotiationMarginAmount; row.IsMarginPinned = margPin;
        row.SectionTotal = sale + (contPct * ContingencyAmount) + (margPct * NegotiationMarginAmount);
    }

    /// <summary>
    /// Toggle shadow su una riga e ricalcola lo spalmo.
    /// </summary>
    public void ToggleShadow(DistributionRowVM row)
    {
        row.IsShadowed = !row.IsShadowed;

        // Sincronizza stato shadow con il VM sottostante
        if (row.RowType == "R")
        {
            var sec = Groups.SelectMany(g => g.Sections).FirstOrDefault(s => s.Id == row.SectionId);
            if (sec != null) sec.IsShadowed = row.IsShadowed;
        }
        else
        {
            var item = MaterialSections.SelectMany(s => s.Items).FirstOrDefault(i => i.Id == row.ItemId);
            if (item != null) item.IsShadowed = row.IsShadowed;
        }

        RecalcShadow();
    }

    /// <summary>
    /// Calcola lo spalmo shadow: le righe shadowed (non pinned) distribuiscono
    /// la loro vendita proporzionalmente sulle righe visibili.
    /// </summary>
    private void RecalcShadow()
    {
        // Righe shadowed → il loro SaleAmount viene spalmato sulle visibili
        var shadowed = DistributionRows.Where(r => r.IsShadowed).ToList();
        var visible = DistributionRows.Where(r => !r.IsShadowed).ToList();

        decimal totalShadowedSale = shadowed.Sum(r => r.SaleAmount);
        decimal totalVisibleSale = visible.Sum(r => r.SaleAmount);

        Log.Debug("RecalcShadow: shadowed={ShadowCount} visible={VisibleCount} totalShadowed={TotalSh:N2} totalVisible={TotalVis:N2}",
            shadowed.Count, visible.Count, totalShadowedSale, totalVisibleSale);

        foreach (var row in DistributionRows)
        {
            if (row.IsShadowed)
            {
                row.ShadowedAmount = 0;
                row.ShadowedPct = 0;
                // Riga nascosta: totale sezione = 0
                row.SectionTotal = 0;
            }
            else if (totalVisibleSale > 0 && totalShadowedSale > 0)
            {
                decimal quota = row.SaleAmount / totalVisibleSale;
                row.ShadowedAmount = Math.Round(totalShadowedSale * quota, 2);
                row.ShadowedPct = quota;
                // Totale = vendita + contingency + margine + shadow ricevuto
                row.SectionTotal = row.SaleAmount + row.ContingencyAmount + row.MarginAmount + row.ShadowedAmount;
            }
            else
            {
                row.ShadowedAmount = 0;
                row.ShadowedPct = 0;
                // Totale normale senza shadow
                row.SectionTotal = row.SaleAmount + row.ContingencyAmount + row.MarginAmount;
            }
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
                Color = grp.First().GroupColor
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
                    IsMarginPinned = sec.MarginPinned,
                    IsShadowed = sec.IsShadowed
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
                    ItemType = item.ItemType,
                    ContingencyPct = item.ContingencyPct,
                    MarginPct = item.MarginPct,
                    IsContingencyPinned = item.ContingencyPinned,
                    IsMarginPinned = item.MarginPinned,
                    IsShadowed = item.IsShadowed
                });
            }

            vm.MaterialSections.Add(matVM);
        }

        // LOG: cosa ha letto FromData dal DB
        Log.Debug("═══ FromData COMPLETATO ═══");
        foreach (var g in vm.Groups)
            foreach (var s in g.Sections)
                Log.Debug("  FromData R [{Id}] {Name}: ContPct={Cont:P1} ContPin={ContPin} MargPct={Marg:P1} MargPin={MargPin}",
                    s.Id, s.Name, s.ContingencyPct, s.IsContingencyPinned, s.MarginPct, s.IsMarginPinned);
        foreach (var ms in vm.MaterialSections)
            foreach (var i in ms.Items)
                Log.Debug("  FromData M [{Id}] {Desc}: ContPct={Cont:P1} ContPin={ContPin} MargPct={Marg:P1} MargPin={MargPin}",
                    i.Id, i.Description, i.ContingencyPct, i.IsContingencyPinned, i.MarginPct, i.IsMarginPinned);
        Log.Debug("  → Ora chiamo WireAllChanges...");

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

    // ── Shadow ──
    private bool _isShadowed;
    public bool IsShadowed
    {
        get => _isShadowed;
        set { _isShadowed = value; Notify(); Notify(nameof(EyeIcon)); Notify(nameof(DisplaySaleAmount)); }
    }
    public string EyeIcon => IsShadowed ? "👁‍🗨" : "👁";
    public decimal DisplaySaleAmount => IsShadowed ? 0m : SaleAmount;

    private decimal _shadowedAmount;
    public decimal ShadowedAmount { get => _shadowedAmount; set { _shadowedAmount = value; Notify(); } }

    private decimal _shadowedPct;
    public decimal ShadowedPct { get => _shadowedPct; set { _shadowedPct = value; Notify(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}