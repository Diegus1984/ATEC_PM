namespace ATEC.PM.Server.Services;

/// <summary>
/// Lookup dipendenti: solo persone reali (no ADMIN, no cessati, no esterni, no wildcard reparto).
/// </summary>
public static class EmployeeLookupQueries
{
    public const string RealEmployeesSql = @"
        SELECT id AS Id, CONCAT_WS(' ', first_name, last_name) AS Name
        FROM employees
        WHERE status = 'ACTIVE'
          AND emp_type = 'INTERNAL'
          AND user_role <> 'ADMIN'
          AND first_name NOT LIKE '[%'
        ORDER BY last_name, first_name";
}
