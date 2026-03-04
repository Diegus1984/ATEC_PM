using System;

namespace ATEC.PM.Shared.Models;

public class Department
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class EmployeeDepartment
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int DepartmentId { get; set; }
    public bool IsResponsible { get; set; }
    public bool IsPrimary { get; set; }
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
}

public class EmployeeCompetence
{
    public int Id { get; set; }
    public int EmployeeId { get; set; }
    public int DepartmentId { get; set; }
    public string Notes { get; set; } = "";
    public string DepartmentCode { get; set; } = "";
    public string DepartmentName { get; set; } = "";
}

public class Employee
{
    public int Id { get; set; }
    public string BadgeNumber { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string EmpType { get; set; } = "INTERNAL";
    public int? SupplierId { get; set; }
    public decimal HourlyCost { get; set; }
    public decimal WeeklyHours { get; set; } = 40;
    public DateTime? HireDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string Notes { get; set; } = "";
    public string FullName => $"{FirstName} {LastName}";
}

public class Skill
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
}

public class Customer
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

public class Supplier
{
    public int Id { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string VatNumber { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

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
