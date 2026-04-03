using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.BudgetVsCosting;

// ═══════════════════════════════════════════════════════════════
// FASI ASSEGNATE — modelli per colonna centrale
// ═══════════════════════════════════════════════════════════════

public class BvaPhaseGroupVM : INotifyPropertyChanged
{
    public int PhaseId { get; set; }
    public string PhaseName { get; set; } = "";
    public decimal BudgetHours { get; set; }
    public decimal HoursWorked { get; set; }
    public int ProgressPct { get; set; }

    private ObservableCollection<BvaAssignmentRow> _assignments = new();
    public ObservableCollection<BvaAssignmentRow> Assignments
    {
        get => _assignments;
        set { _assignments = value; _assignments.CollectionChanged += (_, _) => Notify(nameof(HasAssignments)); Notify(); Notify(nameof(HasAssignments)); }
    }

    public bool HasAssignments => Assignments.Count > 0;

    public BvaPhaseGroupVM()
    {
        _assignments.CollectionChanged += (_, _) => Notify(nameof(HasAssignments));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public class BvaAssignmentRow : INotifyPropertyChanged
{
    public int AssignmentId { get; set; }
    public string EmployeeName { get; set; } = "";

    private decimal _plannedHours;
    public decimal PlannedHours
    {
        get => _plannedHours;
        set { _plannedHours = value; Notify(); Notify(nameof(Pct)); }
    }

    public decimal HoursWorked { get; set; }
    public string Pct => PlannedHours > 0 ? $"{Math.Round(HoursWorked / PlannedHours * 100, 0)}%" : "0%";
    public bool CanDelete => HoursWorked == 0;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ═══════════════════════════════════════════════════════════════
// VIEW MODEL PRINCIPALE
// ═══════════════════════════════════════════════════════════════

public class BvaViewModel : INotifyPropertyChanged
{
    private bool _isLoaded;
    public bool IsLoaded { get => _isLoaded; set { _isLoaded = value; Notify(); Notify(nameof(ShowContent)); Notify(nameof(ShowEmpty)); } }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; set { _isEmpty = value; Notify(); Notify(nameof(ShowEmpty)); Notify(nameof(ShowContent)); } }

    public bool ShowContent => IsLoaded && !IsEmpty;
    public bool ShowEmpty => IsLoaded && IsEmpty;

    public ObservableCollection<BvaGroupVM> Groups { get; set; } = new();

    public decimal TotalBudgetHours { get; set; }
    public decimal TotalBudgetCost { get; set; }
    public decimal TotalAssignedHours { get; set; }
    public decimal TotalAssignedCost { get; set; }
    public decimal TotalActualHours { get; set; }
    public decimal TotalActualCost { get; set; }

    // Materiali
    public ObservableCollection<BvaMaterialSectionDto> MaterialSections { get; set; } = new();
    public decimal TotalMaterialNetCost { get; set; }
    public decimal TotalMaterialSaleCost { get; set; }
    public bool HasMaterials => MaterialSections.Count > 0;

    // Scheda prezzi
    public BvaPricingDto? Pricing { get; set; }
    public bool HasPricing => Pricing != null;

    public decimal TotalDeltaHours => TotalActualHours - TotalBudgetHours;
    public string TotalDeltaText => $"Δ {(TotalDeltaHours > 0 ? "+" : "")}{TotalDeltaHours:F1} h";
    public string TotalDeltaColor => TotalDeltaHours > 0 ? "#F87171" : "#34D399";

    private string _statusText = "";
    public string StatusText { get => _statusText; set { _statusText = value; Notify(); } }

    public static BvaViewModel FromData(BudgetVsActualData data, List<PhaseListItem>? phases = null, int projectId = 0)
    {
        BvaViewModel vm = new()
        {
            TotalBudgetHours = data.TotalBudgetHours,
            TotalBudgetCost = data.TotalBudgetCost,
            TotalAssignedHours = data.TotalAssignedHours,
            TotalAssignedCost = data.TotalAssignedCost,
            TotalActualHours = data.TotalActualHours,
            TotalActualCost = data.TotalActualCost
        };

        if (data.Groups.Count == 0)
        {
            vm.IsEmpty = true;
            vm.IsLoaded = true;
            return vm;
        }

        // Indice fasi per cost_section_template_id (evita duplicati con nomi uguali)
        Dictionary<int, List<PhaseListItem>> phasesByTemplateId = (phases ?? new())
            .Where(p => p.CostSectionTemplateId.HasValue && p.CostSectionTemplateId.Value > 0)
            .GroupBy(p => p.CostSectionTemplateId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (BvaGroupDto g in data.Groups)
            vm.Groups.Add(BvaGroupVM.FromDto(g, phasesByTemplateId, projectId));

        // Materiali
        foreach (BvaMaterialSectionDto ms in data.MaterialSections)
            vm.MaterialSections.Add(ms);
        vm.TotalMaterialNetCost = data.TotalMaterialNetCost;
        vm.TotalMaterialSaleCost = data.TotalMaterialSaleCost;
        vm.Pricing = data.Pricing;

        int secCount = data.Groups.Sum(g => g.Sections.Count);
        int entryCount = data.Groups.Sum(g => g.Sections.Sum(s => s.ActualEmployees.Sum(e => e.Details.Count)));
        vm.StatusText = $"{data.Groups.Count} gruppi, {secCount} sezioni  —  " +
                        $"Preventivo {data.TotalBudgetHours:F1} h / {data.TotalBudgetCost:N2} €  —  " +
                        $"Consuntivo {data.TotalActualHours:F1} h / {data.TotalActualCost:N2} €  ({entryCount} registrazioni)";

        vm.IsLoaded = true;
        return vm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ═══════════════════════════════════════════════════════════════
// GRUPPO (Gestione, Installazione, ecc.)
// ═══════════════════════════════════════════════════════════════

public class BvaGroupVM : INotifyPropertyChanged
{
    public string GroupName { get; set; } = "";
    public string Color { get; set; } = "#6B7280";
    public int ProjectId { get; set; }

    private bool _isExpanded = true;
    private bool _suppressSave;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value; Notify();
            if (!_suppressSave && ProjectId > 0)
                UserPreferences.Set($"bva.{ProjectId}.grp.{GroupName}", value);
        }
    }

    public ObservableCollection<BvaSectionVM> Sections { get; set; } = new();

    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal AssignedHours { get; set; }
    public decimal AssignedCost { get; set; }
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }
    public decimal DeltaHours => ActualHours - BudgetHours;
    public string DeltaText => DeltaHours == 0 ? "" : $"Δ {(DeltaHours > 0 ? "+" : "")}{DeltaHours:F1} h";
    public string DeltaColor => DeltaHours > 0 ? "#DC2626" : "#059669";
    public bool HasDelta => DeltaHours != 0;

    public static BvaGroupVM FromDto(BvaGroupDto dto, Dictionary<int, List<PhaseListItem>> phasesByTemplateId, int projectId)
    {
        BvaGroupVM vm = new()
        {
            _suppressSave = true,
            GroupName = dto.GroupName,
            Color = dto.Color,
            BudgetHours = dto.BudgetHours,
            BudgetCost = dto.BudgetCost,
            AssignedHours = dto.AssignedHours,
            AssignedCost = dto.AssignedCost,
            ActualHours = dto.ActualHours,
            ActualCost = dto.ActualCost,
            ProjectId = projectId,
            IsExpanded = UserPreferences.GetBool($"bva.{projectId}.grp.{dto.GroupName}", true)
        };
        vm._suppressSave = false;
        foreach (BvaSectionDto s in dto.Sections)
            vm.Sections.Add(BvaSectionVM.FromDto(s, phasesByTemplateId, projectId));
        return vm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

// ═══════════════════════════════════════════════════════════════
// SEZIONE (Documentazione Interna, Sviluppo SW, ecc.)
// ═══════════════════════════════════════════════════════════════

public class BvaSectionVM : INotifyPropertyChanged
{
    public string SectionName { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE";
    public string TypeLabel => SectionType == "DA_CLIENTE" ? "CLIENTE" : "SEDE";
    public string TypeColor => SectionType == "DA_CLIENTE" ? "#D97706" : "#059669";

    private bool _isDetailExpanded;
    private bool _suppressSave;
    public bool IsDetailExpanded
    {
        get => _isDetailExpanded;
        set
        {
            _isDetailExpanded = value; Notify();
            if (!_suppressSave && ProjectId > 0)
                UserPreferences.Set($"bva.{ProjectId}.sec.{SectionName}.{SectionType}", value);
        }
    }

    // Preventivo
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal BudgetSale { get; set; }
    public ObservableCollection<BvaBudgetResourceDto> BudgetResources { get; set; } = new();
    public bool HasBudget => BudgetResources.Count > 0;

    // Trasferta (solo DA_CLIENTE)
    public bool IsClientSide => SectionType == "DA_CLIENTE";
    public decimal BudgetTravelCost { get; set; }
    public decimal BudgetAccommodationCost { get; set; }
    public decimal BudgetAllowanceCost { get; set; }
    public decimal BudgetTotalTravelCost => BudgetTravelCost + BudgetAccommodationCost + BudgetAllowanceCost;
    public bool HasTravel => IsClientSide && BudgetTotalTravelCost > 0;

    // Assegnate
    public decimal AssignedHours { get; set; }
    public decimal AssignedCost { get; set; }

    // Consuntivo (ore totali per il subtotale — dettaglio visibile nelle fasi)
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }

    // Fasi assegnate (colonna centrale)
    public ObservableCollection<BvaPhaseGroupVM> PhaseGroups { get; set; } = new();
    public int ProjectId { get; set; }
    public bool HasPhases => PhaseGroups.Count > 0;

    // Delta
    public decimal DeltaHours => ActualHours - BudgetHours;
    public string DeltaText => DeltaHours == 0 ? "" : $"Δ {(DeltaHours > 0 ? "+" : "")}{DeltaHours:F1} h";
    public string DeltaColor => DeltaHours > 0 ? "#DC2626" : "#059669";
    public bool HasDelta => DeltaHours != 0;

    public static BvaSectionVM FromDto(BvaSectionDto dto, Dictionary<int, List<PhaseListItem>> phasesByTemplateId, int projectId)
    {
        BvaSectionVM vm = new()
        {
            _suppressSave = true,
            SectionName = dto.SectionName,
            SectionType = dto.SectionType,
            BudgetHours = dto.BudgetHours,
            BudgetCost = dto.BudgetCost,
            BudgetSale = dto.BudgetSale,
            AssignedHours = dto.AssignedHours,
            AssignedCost = dto.AssignedCost,
            ActualHours = dto.ActualHours,
            ActualCost = dto.ActualCost,
            ProjectId = projectId,
            BudgetTravelCost = dto.BudgetTravelCost,
            BudgetAccommodationCost = dto.BudgetAccommodationCost,
            BudgetAllowanceCost = dto.BudgetAllowanceCost,
            IsDetailExpanded = UserPreferences.GetBool($"bva.{projectId}.sec.{dto.SectionName}.{dto.SectionType}", false)
        };
        vm._suppressSave = false;

        foreach (BvaBudgetResourceDto r in dto.BudgetResources) vm.BudgetResources.Add(r);

        // Fasi assegnate a questa sezione (match per cost_section_template_id)
        if (dto.TemplateId.HasValue && phasesByTemplateId.TryGetValue(dto.TemplateId.Value, out List<PhaseListItem>? sectionPhases))
        {
            foreach (PhaseListItem p in sectionPhases.OrderBy(x => x.SortOrder))
            {
                BvaPhaseGroupVM grp = new()
                {
                    PhaseId = p.Id,
                    PhaseName = string.IsNullOrEmpty(p.CustomName) ? p.Name : p.CustomName,
                    BudgetHours = p.BudgetHours,
                    HoursWorked = p.HoursWorked,
                    ProgressPct = p.ProgressPct
                };
                foreach (PhaseAssignmentDto a in p.Assignments)
                {
                    grp.Assignments.Add(new BvaAssignmentRow
                    {
                        AssignmentId = a.Id,
                        EmployeeName = a.EmployeeName,
                        PlannedHours = a.PlannedHours,
                        HoursWorked = a.HoursWorked
                    });
                }
                vm.PhaseGroups.Add(grp);
            }
        }

        return vm;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
