namespace ATEC.PM.Shared.DTOs;

public class EmployeeListItem
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EmpType { get; set; } = "";
    public string Status { get; set; } = "";
    public string Username { get; set; } = "";
}

public class EmployeeSaveRequest
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EmpType { get; set; } = "INTERNAL";
    public int? SupplierId { get; set; }
    public string Status { get; set; } = "ACTIVE";
}
