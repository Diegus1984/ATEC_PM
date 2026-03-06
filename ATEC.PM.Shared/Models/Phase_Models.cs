namespace ATEC.PM.Shared.Models;

public class PhaseTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public int? DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsDefault { get; set; } = true;
}

public class ProjectPhase
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int PhaseTemplateId { get; set; }
    public string PhaseName { get; set; } = "";
    public string CustomName { get; set; } = "";
    public int? DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = "";
    public decimal BudgetHours { get; set; }
    public decimal BudgetCost { get; set; }
    public string Status { get; set; } = "NOT_STARTED";
    public int ProgressPct { get; set; }
    public int SortOrder { get; set; }
}

public class Skill
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
}
