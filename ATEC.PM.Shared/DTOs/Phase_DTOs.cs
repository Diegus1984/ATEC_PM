using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class PhaseTemplateDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int? DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
}

public class PhaseListItem
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int? DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public string Status { get; set; } = "";
    public int ProgressPct { get; set; }
    public int SortOrder { get; set; }
    public decimal HoursWorked { get; set; }
    public List<PhaseAssignmentDto> Assignments { get; set; } = new();
    public int PhaseTemplateId { get; set; }
    public string CustomName { get; set; } = "";
}

public class PhaseAssignmentDto
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = "";
    public string AssignRole { get; set; } = "MEMBER";
    public decimal PlannedHours { get; set; }
    public decimal HoursWorked { get; set; }
}

public class PhaseSaveRequest
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int PhaseTemplateId { get; set; }
    public string CustomName { get; set; } = "";
    public int? DepartmentId { get; set; }
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public string Status { get; set; } = "NOT_STARTED";
    public int ProgressPct { get; set; }
    public int SortOrder { get; set; }
    public List<PhaseAssignmentDto> Assignments { get; set; } = new();
}

public class PhaseTemplateSaveRequest
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int? DepartmentId { get; set; }
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; }
}

public class BulkPhaseRequest
{
    public int ProjectId { get; set; }
    public List<int> TemplateIds { get; set; } = new();
}

public class PlannedHoursUpdate
{
    public decimal PlannedHours { get; set; }
}
