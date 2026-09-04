using System.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;


/// <summary>
/// Tendine leggere di clienti e dipendenti (<c>GET /api/lookup/…</c>), aperte a chiunque sia
/// autenticato: le usano i dialoghi di commessa. Stavano dentro <c>ProjectsController</c> con
/// rotta assoluta; spostate qui il 04/09/2026, nessun percorso cambiato.
/// </summary>
[ApiController]
[Route("api/lookup")]
[Authorize]
public class LookupController : ControllerBase
{
    private readonly DbService _db;

    public LookupController(DbService db)
    {
        _db = db;
    }

    // --- LOOKUP ---
    [HttpGet("/api/lookup/customers")]
    public IActionResult LookupCustomers()
    {
        using var c = _db.Open();
        // Il cliente tecnico «ATEC — Sistema» esiste solo per la commessa INTERNA:
        // non deve essere assegnabile a una commessa vera.
        var rows = c.Query<LookupItem>(@"
            SELECT id AS Id, company_name AS Name FROM customers
            WHERE is_active=1 AND (vat_number IS NULL OR vat_number <> @SystemVat)
            ORDER BY company_name",
            new { SystemVat = ATEC.PM.Shared.SystemProjects.SystemCustomerVat }).ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    [HttpGet("/api/lookup/employees")]
    public IActionResult LookupEmployees([FromQuery] string? role = null)
    {
        using var c = _db.Open();
        string sql = "SELECT id AS Id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status='ACTIVE'";
        if (!string.IsNullOrEmpty(role))
        {
            sql += " AND user_role=@Role";
        }
        sql += " ORDER BY last_name";
        var rows = c.Query<LookupItem>(sql, new { Role = role }).ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }
}
