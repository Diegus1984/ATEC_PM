using System;

namespace ATEC.PM.Shared.Models;

public class Project
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public int CustomerId { get; set; }
    public int PmId { get; set; }
    public string Description { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public decimal Revenue { get; set; }
    public decimal BudgetHoursTotal { get; set; }
    public string Status { get; set; } = "DRAFT";
    public string Priority { get; set; } = "MEDIUM";
    public string ServerPath { get; set; } = "";
}
