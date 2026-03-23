using System.Collections.Generic;
using System.Linq;

namespace ATEC.PM.Shared.DTOs;


public class ProjectCostSectionDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? TemplateId { get; set; }
    public string Name { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE";
    public string GroupName { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    // NO MarkupValue — il K è sulla singola risorsa
    public decimal ContingencyPct { get; set; }
    public decimal MarginPct { get; set; }
    public bool ContingencyPinned { get; set; }
    public bool MarginPinned { get; set; }
    public bool IsShadowed { get; set; }
    public List<int> DepartmentIds { get; set; } = new();
    public List<ProjectCostResourceDto> Resources { get; set; } = new();
    // Calcolati
    public decimal TotalHours => Resources?.Sum(r => r.TotalHours) ?? 0;
    public decimal TotalCost => Resources?.Sum(r => r.TotalCost) ?? 0;
    public decimal TotalSale => Resources?.Sum(r => r.TotalSale) ?? 0;
    public decimal TotalTravel => Resources?.Sum(r => r.TravelTotal + r.AccommodationTotal + r.AllowanceTotal) ?? 0;
}

public class ProjectCostResourceDto
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public int? EmployeeId { get; set; }
    public string ResourceName { get; set; } = "";
    public decimal WorkDays { get; set; }
    public decimal HoursPerDay { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal MarkupValue { get; set; } = 1.450m;
    // Trasferta
    public int NumTrips { get; set; }
    public decimal KmPerTrip { get; set; }
    public decimal CostPerKm { get; set; } = 0.90m;
    public decimal DailyFood { get; set; }
    public decimal DailyHotel { get; set; }
    public decimal AllowanceDays { get; set; }
    public decimal DailyAllowance { get; set; }
    public int SortOrder { get; set; }
    // Calcolati
    public decimal TotalHours => WorkDays * HoursPerDay;
    public decimal TotalCost => TotalHours * HourlyCost;
    public decimal TotalSale => TotalCost * MarkupValue;
    public decimal TravelTotal => NumTrips * KmPerTrip * CostPerKm;
    public decimal AccommodationTotal => WorkDays * (DailyFood + DailyHotel);
    public decimal AllowanceTotal => AllowanceDays * DailyAllowance;
}

public class ProjectCostResourceSaveRequest
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public int? EmployeeId { get; set; }
    public string ResourceName { get; set; } = "";
    public decimal WorkDays { get; set; }
    public decimal HoursPerDay { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal MarkupValue { get; set; } = 1.450m;
    public int NumTrips { get; set; }
    public decimal KmPerTrip { get; set; }
    public decimal CostPerKm { get; set; } = 0.90m;
    public decimal DailyFood { get; set; }
    public decimal DailyHotel { get; set; }
    public decimal AllowanceDays { get; set; }
    public decimal DailyAllowance { get; set; }
    public int SortOrder { get; set; }
}

// === SEZIONI MATERIALI ===
public class ProjectMaterialSectionDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int? CategoryId { get; set; }
    public string Name { get; set; } = "";
    public decimal MarkupValue { get; set; } = 1.300m;       // K default materiale
    public decimal CommissionMarkup { get; set; } = 1.100m;   // K default provvigione
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;
    public List<ProjectMaterialItemDto> Items { get; set; } = new();
    // Calcolati dalle righe
    public decimal TotalCost => Items?.Sum(i => i.Quantity * i.UnitCost) ?? 0;
    public decimal TotalSale => Items?.Sum(i => i.Quantity * i.UnitCost * i.MarkupValue) ?? 0;
}

public class ProjectMaterialItemDto
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public int? ParentItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public decimal MarkupValue { get; set; } = 1.300m;
    public string ItemType { get; set; } = "MATERIAL";
    public int SortOrder { get; set; }
    public decimal ContingencyPct { get; set; }
    public decimal MarginPct { get; set; }
    public bool ContingencyPinned { get; set; }
    public bool MarginPinned { get; set; }
    public bool IsShadowed { get; set; }
    public decimal TotalCost => Quantity * UnitCost;
    public decimal TotalSale => Quantity * UnitCost * MarkupValue;
}

public class ProjectMaterialItemSaveRequest
{
    public int Id { get; set; }
    public int SectionId { get; set; }
    public int? ParentItemId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; } = 1;
    public decimal UnitCost { get; set; }
    public decimal MarkupValue { get; set; } = 1.300m;
    public string ItemType { get; set; } = "MATERIAL";
    public int SortOrder { get; set; }
}


// === SCHEDA PREZZI ===
public class ProjectPricingDto
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public decimal ContingencyPct { get; set; } = 0.130m;
    public decimal NegotiationMarginPct { get; set; } = 0.050m;
    public decimal TravelMarkup { get; set; } = 1.000m;
    public decimal AllowanceMarkup { get; set; } = 1.000m;
}

// === DIPENDENTE PER COMBO ===
public class EmployeeCostLookup
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string DepartmentCode { get; set; } = "";
    public decimal HourlyCost { get; set; }
    public decimal DefaultMarkup { get; set; } = 1.450m;
}


// === RIEPILOGO COMPLETO CONFIGURA COMMESSA ===
public class ProjectCostingData
{
    public int ProjectId { get; set; }
    public List<ProjectCostSectionDto> CostSections { get; set; } = new();
    public List<ProjectMaterialSectionDto> MaterialSections { get; set; } = new();
    public ProjectPricingDto Pricing { get; set; } = new();
    public bool IsInitialized { get; set; }
}

public class PricingDistributionRow
{
    public int Id { get; set; }
    public int OfferId { get; set; }
    public string SectionType { get; set; } = "COST";
    public int SectionId { get; set; }
    public string SectionName { get; set; } = "";
    public decimal SaleAmount { get; set; }
    public decimal ContingencyPct { get; set; }
    public decimal MarginPct { get; set; }
    // Calcolati client-side
    public decimal ContingencyAmount { get; set; }
    public decimal MarginAmount { get; set; }
    public decimal ClientPrice { get; set; }
}

public class RebalanceRequest
{
    public int FixedRowId { get; set; }
    public string Field { get; set; } = "contingency";
    public decimal NewValue { get; set; }
}

public class SectionDistributionDto
{
    public decimal ContingencyPct { get; set; }
    public decimal MarginPct { get; set; }
    public bool ContingencyPinned { get; set; }
    public bool MarginPinned { get; set; }
    public bool IsShadowed { get; set; }
}