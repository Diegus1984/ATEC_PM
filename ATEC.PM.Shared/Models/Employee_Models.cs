namespace ATEC.PM.Shared.Models;

public class Employee
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string EmpType { get; set; } = "INTERNAL";
    public int? SupplierId { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public string FullName => $"{FirstName} {LastName}";
}
