using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

// === GRUPPI SEZIONI COSTO ===
public class CostSectionGroupDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string BgColor { get; set; } = "#3B82F6";
    public string TextColor { get; set; } = "#FFFFFF";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CostSectionGroupSaveRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

// === TEMPLATE SEZIONI COSTO ===
public class CostSectionTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE";
    public int GroupId { get; set; }
    public string GroupName { get; set; } = "";
    public bool IsDefault { get; set; } = true;
    public bool IsDefaultQuote { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<int> DepartmentIds { get; set; } = new();
    public List<string> DepartmentCodes { get; set; } = new();
}

public class CostSectionTemplateSaveRequest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string SectionType { get; set; } = "IN_SEDE";
    public int GroupId { get; set; }
    public bool IsDefault { get; set; } = true;
    public bool IsDefaultQuote { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public List<int> DepartmentIds { get; set; } = new();
}

// === SAVE REPARTI PER SEZIONE ===
public class SectionDepartmentsRequest
{
    public List<int> DepartmentIds { get; set; } = new();
}

// === TARIFFE TRASFERTA ===

/// <summary>
/// Tipi di tariffa gestiti in <c>tariff_options</c>. Le prime quattro finiscono in una
/// colonna di <c>project_cost_resources</c>; <see cref="HourlyRate"/> (blocco 5) invece
/// alimenta il costo orario delle Officine interne nelle finestre di calcolo a righe.
/// </summary>
public static class TariffTypes
{
    public const string CostPerKm = "COST_PER_KM";
    public const string DailyFood = "DAILY_FOOD";
    public const string DailyHotel = "DAILY_HOTEL";
    public const string DailyAllowance = "DAILY_ALLOWANCE";
    public const string HourlyRate = "HOURLY_RATE";

    public static readonly string[] All =
        { CostPerKm, DailyFood, DailyHotel, DailyAllowance, HourlyRate };

    public static bool IsKnown(string? type) => type != null && All.Contains(type);
}

public class TariffOptionDto
{
    public int Id { get; set; }
    public string TariffType { get; set; } = "";
    /// <summary>
    /// Nome della tariffa (#87): «Meccanica», «Stampa 3D», «Carpenteria»… Vuoto = tariffa
    /// senza nome, che si legge dal solo importo (è com'erano tutte prima della v95).
    /// </summary>
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
}

public class TariffOptionSaveRequest
{
    public string TariffType { get; set; } = "";
    public string Label { get; set; } = "";
    public decimal Value { get; set; }
}
