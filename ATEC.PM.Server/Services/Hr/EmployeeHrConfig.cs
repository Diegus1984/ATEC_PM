using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// Shared validation for HR fields on the employee record.
/// Used by <c>EmployeesController</c> and <c>HrPresenzeService</c>.
/// </summary>
public static class EmployeeHrConfig
{
    /// <summary>Allowed values from the original Timbrature combo box.</summary>
    public static readonly decimal[] AllowedDailyHours = [4m, 6m, 8m];

    public static string? ValidateDailyHours(decimal hours) =>
        AllowedDailyHours.Contains(hours)
            ? null
            : "Ore giornaliere ammesse: 4, 6 o 8.";

    /// <summary>Normalize badge code: blank → NULL (consistent with M108).</summary>
    public static string? NormalizeEcosCode(string? ecosEmplCode) =>
        string.IsNullOrWhiteSpace(ecosEmplCode) ? null : ecosEmplCode.Trim();

    /// <summary>
    /// Ensure the Ecos badge code is not already assigned to another employee.
    /// The UNIQUE index (M108) is the real guard; this returns a readable message.
    /// </summary>
    public static string? ValidateEcosCode(MySqlConnection c, int employeeId, string? ecosEmplCode)
    {
        string? code = NormalizeEcosCode(ecosEmplCode);
        if (code == null)
            return null;

        string? takenBy = c.ExecuteScalar<string?>(
            @"SELECT CONCAT_WS(' ', first_name, last_name) FROM employees
              WHERE ecos_empl_code = @Code AND id <> @Id LIMIT 1",
            new { Code = code, Id = employeeId });

        return takenBy == null
            ? null
            : $"Il codice Ecos {code} è già collegato a {takenBy}.";
    }
}
