using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/employees")]
[Authorize]
public class EmployeesController : ControllerBase
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;
    public EmployeesController(DbService db, FeatureAccessService access)
    {
        _db = db;
        _access = access;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<EmployeeListItem>(
            "SELECT id, CONCAT(first_name,' ',last_name) AS FullName, email, emp_type AS EmpType, status, username FROM employees WHERE status<>'TERMINATED' ORDER BY last_name").ToList();
        return Ok(ApiResponse<List<EmployeeListItem>>.Ok(rows));
    }

    /// <summary>
    /// Solo dipendenti reali: esclude ADMIN, cessati e wildcard reparto ([PM] Generico, …).
    /// </summary>
    [HttpGet("real")]
    public IActionResult GetRealEmployees()
    {
        using var c = _db.Open();
        List<LookupItem> rows = c.Query<LookupItem>(EmployeeLookupQueries.RealEmployeesSql).ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    /// <summary>
    /// Tecnici che appartengono a un reparto (employee_departments).
    /// Se departmentId è null/0 (fase trasversale) → restituisce tutti.
    /// </summary>
    [HttpGet("by-department")]
    public IActionResult GetByDepartment([FromQuery] int? departmentId)
    {
        using var c = _db.Open();
        List<LookupItem> rows;

        if (departmentId == null || departmentId == 0)
        {
            // Fase trasversale: tutti i dipendenti attivi (escluso ADMIN)
            rows = c.Query<LookupItem>(
                "SELECT id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status<>'TERMINATED' AND user_role<>'ADMIN' ORDER BY last_name").ToList();
        }
        else
        {
            // Solo tecnici che appartengono al reparto
            rows = c.Query<LookupItem>(@"
                SELECT e.id, CONCAT(e.first_name,' ',e.last_name) AS Name
                FROM employees e
                JOIN employee_departments ed ON ed.employee_id = e.id
                WHERE e.status <> 'TERMINATED' AND e.user_role <> 'ADMIN' AND ed.department_id = @DeptId
                ORDER BY e.last_name",
                new { DeptId = departmentId }).ToList();
        }

        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    /// <summary>
    /// Tecnici dai reparti interessati alla sezione costo di una fase.
    /// Percorso: phase_template → cost_section_template → cost_section_template_departments → employee_departments → employees
    /// </summary>
    [HttpGet("by-phase/{phaseId}")]
    public IActionResult GetByPhase(int phaseId)
    {
        using var c = _db.Open();
        // Eredita reparti dalla SEZIONE DI COSTO DELLA COMMESSA (project_cost_sections),
        // non dal template globale. Così se i reparti della sezione sono stati modificati
        // localmente nella commessa, la fase rispetta quei reparti.
        // v74: la sezione è quella scritta sulla riga della fase di commessa. Il vecchio ripiego
        // su phase_templates darebbe una sola delle N sezioni della fase di anagrafica, quindi i
        // tecnici eleggibili di una sezione a caso.
        List<LookupItem> rows = c.Query<LookupItem>(@"
            SELECT DISTINCT e.id, CONCAT(e.first_name,' ',e.last_name) AS Name, e.last_name AS SortKey
            FROM project_phases pp
            JOIN project_cost_sections pcs
                 ON pcs.project_id = pp.project_id
                AND pcs.template_id = pp.cost_section_template_id
            JOIN project_cost_section_departments pcsd
                 ON pcsd.project_cost_section_id = pcs.id
            JOIN employee_departments ed ON ed.department_id = pcsd.department_id
            JOIN employees e ON e.id = ed.employee_id
            WHERE pp.id = @PhaseId
              AND e.status <> 'TERMINATED'
              AND e.user_role <> 'ADMIN'
              -- Fasi assegnate = SOLO persone reali: esclude i wildcard reparto ([PM] Generico, …),
              -- che sono usati solo a sinistra nel preventivo (risorse fittizie).
              AND e.first_name NOT LIKE '[%'
            -- Si ordina per cognome, ma con DISTINCT la colonna deve stare anche nella
            -- SELECT: MySQL in ONLY_FULL_GROUP_BY rifiuta l'ORDER BY su una colonna
            -- fuori dalla SELECT (in produzione era un 500). SortKey non arriva al
            -- client: LookupItem ha solo id e name.
            ORDER BY e.last_name",
            new { PhaseId = phaseId }).ToList();

        // Fallback: se la fase non ha sezione costo configurata, restituisce tutti (escluso admin e wildcard)
        if (rows.Count == 0)
        {
            rows = c.Query<LookupItem>(
                "SELECT id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status<>'TERMINATED' AND user_role<>'ADMIN' AND first_name NOT LIKE '[%' ORDER BY last_name").ToList();
        }

        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    [HttpGet("pm-list")]
    public IActionResult GetPmList()
    {
        using var c = _db.Open();
        var rows = c.Query<LookupItem>(@"
            SELECT id, CONCAT(first_name,' ',last_name) AS Name
            FROM employees
            WHERE status<>'TERMINATED' AND user_role IN ('PM','ADMIN') AND username<>'admin'
            ORDER BY last_name").ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var emp = c.QueryFirstOrDefault<EmployeeSaveRequest>(
            "SELECT id, first_name AS FirstName, last_name AS LastName, email, emp_type AS EmpType, supplier_id AS SupplierId, status FROM employees WHERE id=@Id",
            new { Id = id });
        if (emp == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<EmployeeSaveRequest>.Ok(emp));
    }

    // Anagrafica dipendenti in scrittura: solo a chi ha la funzione «Utenti». Le GET qui sopra
    // restano aperte a ogni autenticato perché alimentano le tendine di mezzo gestionale.
    [HttpPost]
    [Authorize]
    [RequireFeature("nav.utenti")]
    public IActionResult Create([FromBody] EmployeeSaveRequest req)
    {
        using var c = _db.Open();
        if (NameTaken(c, req, excludeId: null))
            return Ok(ApiResponse<int>.Fail(DuplicateMessage(req)));

        var newId = c.ExecuteScalar<int>(
            "INSERT INTO employees (first_name,last_name,email,emp_type,supplier_id,status) VALUES (@FirstName,@LastName,@Email,@EmpType,@SupplierId,@Status); SELECT LAST_INSERT_ID()",
            req);
        return Ok(ApiResponse<int>.Ok(newId, "Creato"));
    }

    /// <summary>
    /// Esiste già un dipendente NON cessato con lo stesso nome e cognome?
    /// <para>
    /// `employees` non ha un indice UNIQUE sul nominativo: senza questa guardia si possono creare
    /// due "Mario Rossi" indistinguibili in tutte le combo risorse (che filtrano solo
    /// <c>status &lt;&gt; 'TERMINATED'</c>). Il confronto ignora maiuscole e spazi ai bordi.
    /// I cessati non bloccano: un omonimo assunto dopo una cessazione deve poter entrare.
    /// </para>
    /// </summary>
    private static bool NameTaken(System.Data.IDbConnection c, EmployeeSaveRequest req, int? excludeId) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM employees
            WHERE status <> 'TERMINATED'
              AND LOWER(TRIM(first_name)) = LOWER(TRIM(@FirstName))
              AND LOWER(TRIM(last_name))  = LOWER(TRIM(@LastName))
              AND (@ExcludeId IS NULL OR id <> @ExcludeId)",
            new { req.FirstName, req.LastName, ExcludeId = excludeId }) > 0;

    private static string DuplicateMessage(EmployeeSaveRequest req) =>
        $"Esiste già un dipendente attivo di nome {(req.FirstName ?? "").Trim()} {(req.LastName ?? "").Trim()}: "
        + "usa quello esistente oppure distingui il nominativo.";

    [HttpPut("{id}")]
    [Authorize]
    [RequireFeature("nav.utenti")]
    public IActionResult Update(int id, [FromBody] EmployeeSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        // Stessa guardia della POST: senza di questa una rinomina crea l'omonimo che la POST impedisce.
        if (NameTaken(c, req, excludeId: id))
            return Ok(ApiResponse<int>.Fail(DuplicateMessage(req)));

        c.Execute(
            "UPDATE employees SET first_name=@FirstName,last_name=@LastName,email=@Email,emp_type=@EmpType,supplier_id=@SupplierId,status=@Status WHERE id=@Id",
            req);
        // Qui si scrive anche `status`, che è la cosa guardata a ogni richiesta autenticata:
        // senza svuotare la cache, una disattivazione dall'anagrafica resterebbe senza effetto
        // per mezzo minuto.
        _access.DimenticaPersona(id);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    [Authorize]
    [RequireFeature("nav.utenti")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("UPDATE employees SET status='TERMINATED' WHERE id=@Id", new { Id = id });
        // Cessazione: fuori subito, senza aspettare la scadenza della cache dello stato.
        _access.DimenticaPersona(id);
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
