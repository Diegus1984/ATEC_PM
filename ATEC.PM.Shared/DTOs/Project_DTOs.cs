using System;

namespace ATEC.PM.Shared.DTOs;

public class ProjectListItem
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public decimal Revenue { get; set; }
    public decimal BudgetHoursTotal { get; set; }
}

public class ProjectSaveRequest
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int CustomerId { get; set; }
    public int PmId { get; set; }
    public string Description { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public decimal BudgetTotal { get; set; }
    public decimal BudgetHoursTotal { get; set; }
    public decimal Revenue { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string Priority { get; set; } = "MEDIUM";
    public string ServerPath { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool CreateDefaultPhases { get; set; } = true;
}
