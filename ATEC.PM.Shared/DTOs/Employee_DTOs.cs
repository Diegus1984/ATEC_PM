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

    /// <summary>Codice badge EcosAgile (<c>EmplCode</c>). NULL = non collegato.</summary>
    public string? EcosEmplCode { get; set; }

    /// <summary>false = forfait: employee does not punch in (VB <c>IsForfait</c>).</summary>
    public bool HrMustPunch { get; set; } = true;

    /// <summary>Contract daily hours: 4, 6 or 8 (VB <c>ForfaitHours</c>).</summary>
    public decimal HrDailyHours { get; set; } = 8m;

    /// <summary>false = overtime zeroed on the timesheet (VB <c>IncludeOvertime</c>).</summary>
    public bool HrCountsOvertime { get; set; } = true;
}
