using System;

namespace ATEC.PM.Shared.DTOs;

public class TimesheetEntryDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int ProjectPhaseId { get; set; }
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string EntryType { get; set; } = "REGULAR";
    public string Notes { get; set; } = "";
    public string PhaseDisplay { get; set; } = "";
}

public class TimesheetSaveRequest
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int ProjectPhaseId { get; set; }
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string EntryType { get; set; } = "REGULAR";
    public string Notes { get; set; } = "";
}

public class TimesheetPhaseOption
{
    public int PhaseId { get; set; }
    public string Display { get; set; } = "";

    // Sezione di costo della fase (segnalazione #42): è quella che decide se le ore
    // finiscono IN SEDE o DA CLIENTE nel Bilancio. Vuota se la fase non è collegata.
    public string CostSectionName { get; set; } = "";
    /// <summary>IN_SEDE / DA_CLIENTE.</summary>
    public string CostSectionType { get; set; } = "";
    public string CostSectionGroup { get; set; } = "";
    /// <summary>Colore del gruppo dall'anagrafica sezioni (#105), es. "#2563EB".</summary>
    public string CostSectionGroupColor { get; set; } = "";
    public int CostSectionSort { get; set; }
}

public class TimesheetSummaryRow
{
    public string PhaseDisplay { get; set; } = "";
    public decimal RegularHours { get; set; }
    public decimal OvertimeHours { get; set; }
    public decimal TravelHours { get; set; }
    public decimal TotalHours { get; set; }
}

public class TimesheetProjectOption
{
    public int ProjectId { get; set; }
    public string Display { get; set; } = "";
}
