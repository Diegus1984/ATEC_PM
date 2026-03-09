using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

/// <summary>
/// Confronto preventivo vs consuntivo per una commessa,
/// raggruppato per Gruppo → Sezione Costo.
/// </summary>
public class BudgetVsActualData
{
    public int ProjectId { get; set; }
    public List<BvaGroupDto> Groups { get; set; } = new();
    public decimal TotalBudgetHours { get; set; }
    public decimal TotalBudgetCost { get; set; }
    public decimal TotalActualHours { get; set; }
    public decimal TotalActualCost { get; set; }
}

public class BvaGroupDto
{
    public string GroupName { get; set; } = "";
    public int SortOrder { get; set; }
    public List<BvaSectionDto> Sections { get; set; } = new();
    // Totali gruppo
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }
}

public class BvaSectionDto
{
    public string SectionName { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE"; // IN_SEDE / DA_CLIENTE

    // --- Preventivo (da project_cost_resources) ---
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal BudgetSale { get; set; }
    public List<BvaBudgetResourceDto> BudgetResources { get; set; } = new();

    // --- Consuntivo (da timesheet_entries via phase_templates.cost_section_template_id) ---
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }
    public List<BvaActualEmployeeDto> ActualEmployees { get; set; } = new();

    // Delta
    public decimal DeltaHours => ActualHours - BudgetHours;
    public decimal DeltaCost => ActualCost - BudgetCost;
}

/// <summary>Riga preventivo: risorsa pianificata</summary>
public class BvaBudgetResourceDto
{
    public string ResourceName { get; set; } = "";
    public decimal WorkDays { get; set; }
    public decimal HoursPerDay { get; set; }
    public decimal TotalHours { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal TotalCost { get; set; }
    public decimal MarkupValue { get; set; }
    public decimal TotalSale { get; set; }
}

/// <summary>Riga consuntivo: ore versate aggregate per dipendente + fase (causale)</summary>
public class BvaActualEntryDto
{
    public string EmployeeName { get; set; } = "";
    public string PhaseName { get; set; } = "";   // causale = nome fase
    public string EntryType { get; set; } = "";    // REGULAR, OVERTIME, TRAVEL
    public decimal Hours { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal TotalCost { get; set; }
}

/// <summary>Raggruppamento per dipendente con dettaglio entry</summary>
public class BvaActualEmployeeDto
{
    public string EmployeeName { get; set; } = "";
    public decimal TotalHours { get; set; }
    public decimal TotalCost { get; set; }
    public List<BvaActualDetailDto> Details { get; set; } = new();
}

public class BvaActualDetailDto
{
    public DateTime WorkDate { get; set; }
    public string PhaseName { get; set; } = "";
    public string EntryType { get; set; } = "";
    public decimal Hours { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal TotalCost { get; set; }
}

