using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

public class BvaAssignmentRow
{
    public int AssignmentId { get; set; }
    public string EmployeeName { get; set; } = "";
    public decimal PlannedHours { get; set; }
    public decimal HoursWorked { get; set; }
    public string Pct => PlannedHours > 0 ? $"{Math.Round(HoursWorked / PlannedHours * 100, 0)}%" : "0%";
    public bool CanDelete => HoursWorked == 0;
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
    public decimal TotalActualHours { get; set; }
    public decimal TotalActualCost { get; set; }

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
            TotalActualHours = data.TotalActualHours,
            TotalActualCost = data.TotalActualCost
        };

        if (data.Groups.Count == 0)
        {
            vm.IsEmpty = true;
            vm.IsLoaded = true;
            return vm;
        }

        // Indice fasi per nome sezione
        Dictionary<string, List<PhaseListItem>> phasesBySectionName = (phases ?? new())
            .GroupBy(p => p.CostSectionName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (BvaGroupDto g in data.Groups)
            vm.Groups.Add(BvaGroupVM.FromDto(g, phasesBySectionName, projectId));

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

    private bool _isExpanded = true;
    public bool IsExpanded { get => _isExpanded; set { _isExpanded = value; Notify(); } }

    public ObservableCollection<BvaSectionVM> Sections { get; set; } = new();

    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }
    public decimal DeltaHours => ActualHours - BudgetHours;
    public string DeltaText => DeltaHours == 0 ? "" : $"Δ {(DeltaHours > 0 ? "+" : "")}{DeltaHours:F1} h";
    public string DeltaColor => DeltaHours > 0 ? "#DC2626" : "#059669";
    public bool HasDelta => DeltaHours != 0;

    public static BvaGroupVM FromDto(BvaGroupDto dto, Dictionary<string, List<PhaseListItem>> phasesBySectionName, int projectId)
    {
        BvaGroupVM vm = new()
        {
            GroupName = dto.GroupName,
            Color = dto.Color,
            BudgetHours = dto.BudgetHours,
            BudgetCost = dto.BudgetCost,
            ActualHours = dto.ActualHours,
            ActualCost = dto.ActualCost
        };
        foreach (BvaSectionDto s in dto.Sections)
            vm.Sections.Add(BvaSectionVM.FromDto(s, phasesBySectionName, projectId));
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
    public bool IsDetailExpanded { get => _isDetailExpanded; set { _isDetailExpanded = value; Notify(); } }

    // Preventivo
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal BudgetSale { get; set; }
    public ObservableCollection<BvaBudgetResourceDto> BudgetResources { get; set; } = new();
    public bool HasBudget => BudgetResources.Count > 0;

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

    public static BvaSectionVM FromDto(BvaSectionDto dto, Dictionary<string, List<PhaseListItem>> phasesBySectionName, int projectId)
    {
        BvaSectionVM vm = new()
        {
            SectionName = dto.SectionName,
            SectionType = dto.SectionType,
            BudgetHours = dto.BudgetHours,
            BudgetCost = dto.BudgetCost,
            BudgetSale = dto.BudgetSale,
            ActualHours = dto.ActualHours,
            ActualCost = dto.ActualCost,
            ProjectId = projectId
        };

        foreach (BvaBudgetResourceDto r in dto.BudgetResources) vm.BudgetResources.Add(r);

        // Fasi assegnate a questa sezione (match per nome sezione)
        if (phasesBySectionName.TryGetValue(dto.SectionName, out List<PhaseListItem>? sectionPhases))
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
