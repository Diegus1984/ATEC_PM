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
    public decimal TotalAssignedHours { get; set; }
    public decimal TotalAssignedCost { get; set; }
    public decimal TotalActualHours { get; set; }
    public decimal TotalActualCost { get; set; }

    // Materiali
    public List<BvaMaterialSectionDto> MaterialSections { get; set; } = new();
    public decimal TotalMaterialNetCost { get; set; }
    public decimal TotalMaterialSaleCost { get; set; }

    // Scheda prezzi
    public BvaPricingDto? Pricing { get; set; }
}

public class BvaGroupDto
{
    public string GroupName { get; set; } = "";
    public string Color { get; set; } = "#6B7280";
    public int SortOrder { get; set; }
    public List<BvaSectionDto> Sections { get; set; } = new();
    // Totali gruppo
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal AssignedHours { get; set; }
    public decimal AssignedCost { get; set; }
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }
}

public class BvaSectionDto
{
    public string SectionName { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE"; // IN_SEDE / DA_CLIENTE
    public int? TemplateId { get; set; }

    // --- Preventivo (da project_cost_resources) ---
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public decimal BudgetSale { get; set; }
    public List<BvaBudgetResourceDto> BudgetResources { get; set; } = new();

    // --- Assegnate (da phase_assignments.planned_hours) ---
    public decimal AssignedHours { get; set; }
    public decimal AssignedCost { get; set; }

    // --- Consuntivo (da timesheet_entries via phase_templates.cost_section_template_id) ---
    public decimal ActualHours { get; set; }
    public decimal ActualCost { get; set; }
    public List<BvaActualEmployeeDto> ActualEmployees { get; set; } = new();

    // Totali trasferta (solo DA_CLIENTE)
    public decimal BudgetTravelCost { get; set; }
    public decimal BudgetAccommodationCost { get; set; }
    public decimal BudgetAllowanceCost { get; set; }
    public decimal BudgetTotalTravelCost => BudgetTravelCost + BudgetAccommodationCost + BudgetAllowanceCost;

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

    // Trasferta (solo DA_CLIENTE)
    public int NumTrips { get; set; }
    public decimal KmPerTrip { get; set; }
    public decimal CostPerKm { get; set; }
    public decimal DailyFood { get; set; }
    public decimal DailyHotel { get; set; }
    public int AllowanceDays { get; set; }
    public decimal DailyAllowance { get; set; }

    public decimal TravelCost => NumTrips * KmPerTrip * CostPerKm;
    public decimal AccommodationCost => WorkDays * (DailyFood + DailyHotel);
    public decimal AllowanceCost => AllowanceDays * DailyAllowance;
    public decimal TotalTravelCost => TravelCost + AccommodationCost + AllowanceCost;
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

// ═══════════════════════════════════════════════════════════════
// MATERIALI
// ═══════════════════════════════════════════════════════════════

public class BvaMaterialSectionDto
{
    public string SectionName { get; set; } = "";
    public decimal MarkupValue { get; set; }
    public decimal CommissionMarkup { get; set; }
    public List<BvaMaterialItemDto> Items { get; set; } = new();
    public decimal TotalNetCost { get; set; }
    public decimal TotalSaleCost { get; set; }
}

public class BvaMaterialItemDto
{
    public int Id { get; set; }
    public int? ParentItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal MarkupValue { get; set; }
    public string ItemType { get; set; } = "MATERIAL";
    public decimal NetCost => Quantity * UnitCost;
    public decimal SaleCost => NetCost * MarkupValue;
}

public class BvaPricingDto
{
    public decimal NetCost { get; set; }           // Risorse + materiali + trasferte
    public decimal ContingencyPct { get; set; }
    public decimal ContingencyAmount { get; set; }
    public decimal OfferPrice { get; set; }        // NetCost + Contingency
    public decimal NegotiationPct { get; set; }
    public decimal NegotiationAmount { get; set; }
    public decimal FinalPrice { get; set; }        // OfferPrice + Negotiation
}

