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
