namespace ATEC.PM.Shared.Models;

public class Department
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal HourlyCost { get; set; }
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
