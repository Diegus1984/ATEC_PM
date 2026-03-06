using System;
using System.Collections.Generic;

namespace ATEC.PM.Shared.DTOs;

public class DashboardData
{
    public int ActiveProjects { get; set; }
    public int DraftProjects { get; set; }
    public int CompletedProjects { get; set; }
    public int TotalEmployees { get; set; }
    public int TotalCustomers { get; set; }
    public decimal HoursThisMonth { get; set; }
    public decimal HoursThisWeek { get; set; }
    public decimal TotalRevenue { get; set; }
    public List<DashboardProjectRow> RecentProjects { get; set; } = new();
}

public class DashboardProjectRow
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string Status { get; set; } = "";
    public decimal HoursWorked { get; set; }
    public decimal BudgetHours { get; set; }
}

public class ProjectDashboardData
{
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string PmName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Priority { get; set; } = "";
    public DateTime? StartDate { get; set; }
    public DateTime? EndDatePlanned { get; set; }
    public string ServerPath { get; set; } = "";
    public string Notes { get; set; } = "";

    public decimal BudgetTotal { get; set; }
    public decimal BudgetHoursTotal { get; set; }
    public decimal Revenue { get; set; }
    public decimal HoursWorked { get; set; }
    public decimal CostWorked { get; set; }
    public int TotalPhases { get; set; }
    public int CompletedPhases { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal TotalCost { get; set; }

    public List<DeptSummary> DepartmentSummaries { get; set; } = new();
    public List<RecentTimesheetEntry> RecentEntries { get; set; } = new();
    public List<ActiveTechSummary> ActiveTechnicians { get; set; } = new();
}

public class DeptSummary
{
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
    public decimal BudgetHours { get; set; }
    public decimal HoursWorked { get; set; }
    public int TotalPhases { get; set; }
    public int CompletedPhases { get; set; }
    public decimal MaterialCost { get; set; }
}

public class RecentTimesheetEntry
{
    public string EmployeeName { get; set; } = "";
    public string PhaseName { get; set; } = "";
    public DateTime WorkDate { get; set; }
    public decimal Hours { get; set; }
    public string EntryType { get; set; } = "";
}

public class ActiveTechSummary
{
    public string EmployeeName { get; set; } = "";
    public string DepartmentCode { get; set; } = "";
    public decimal TotalHours { get; set; }
    public int PhaseCount { get; set; }
}
