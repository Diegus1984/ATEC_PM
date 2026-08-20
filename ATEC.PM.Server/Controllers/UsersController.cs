using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/users")]
[Authorize]
[RequireFeature("nav.utenti")]
public class UsersController : ControllerBase
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;
    private readonly PermissionAdminService _permessi;
    public UsersController(DbService db, FeatureAccessService access, PermissionAdminService permessi)
    {
        _db = db;
        _access = access;
        _permessi = permessi;
    }

    // ── Lista utenti ─────────────────────────────────────────────────
    [HttpGet]
    public IActionResult GetAll([FromQuery] bool includeTerminated = false)
    {
        try
        {
            // I cessati sono nascosti di default; col flag tornano visibili per poterli riattivare.
            string terminatedFilter = includeTerminated ? "" : "AND status <> 'TERMINATED'";

            using var c = _db.Open();

            var employees = c.Query<UserListItem>(
                $@"SELECT id,
                         CONCAT(first_name,' ',last_name) AS FullName,
                         email, user_role AS UserRole, status,
                         username,
                         (username IS NOT NULL AND username <> '') AS HasCredentials
                  FROM employees
                  WHERE first_name NOT LIKE '[%'   -- esclude le wildcard reparto ([XXX] Generico, usate solo dalla MoM)
                    {terminatedFilter}
                  ORDER BY last_name").ToList();

            // Reparti e competenze in due sole query (prima erano 2 per dipendente: con
            // l'anagrafica intera diventavano decine di round-trip per un solo caricamento).
            if (employees.Count > 0)
            {
                List<int> ids = employees.Select(e => e.Id).ToList();

                ILookup<int, string> departments = c.Query<EmployeeCodeRow>(
                    @"SELECT ed.employee_id AS EmployeeId, d.code AS Code
                      FROM employee_departments ed
                      JOIN departments d ON d.id = ed.department_id
                      WHERE ed.employee_id IN @Ids", new { Ids = ids })
                    .ToLookup(r => r.EmployeeId, r => r.Code);

                ILookup<int, string> competences = c.Query<EmployeeCodeRow>(
                    @"SELECT ec.employee_id AS EmployeeId, d.code AS Code
                      FROM employee_competences ec
                      JOIN departments d ON d.id = ec.department_id
                      WHERE ec.employee_id IN @Ids", new { Ids = ids })
                    .ToLookup(r => r.EmployeeId, r => r.Code);

                foreach (UserListItem emp in employees)
                {
                    emp.DepartmentCodes = departments[emp.Id].ToList();
                    emp.CompetenceCodes = competences[emp.Id].ToList();
                }
            }

            return Ok(ApiResponse<List<UserListItem>>.Ok(employees));
        }
        // Senza questo un errore qualsiasi tornava un 500 grezzo: il client resta con la
        // tabella vuota invece di mostrare il motivo.
        catch (Exception ex) { return Ok(ApiResponse<List<UserListItem>>.Fail($"Errore: {ex.Message}")); }
    }

    private sealed class EmployeeCodeRow
    {
        public int EmployeeId { get; set; }
        public string Code { get; set; } = "";
    }

    // ── Dettaglio utente (reparti + competenze) ───────────────────────
    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();

        UserDetailDto? user = c.QueryFirstOrDefault<UserDetailDto>(
            "SELECT id, CONCAT(first_name,' ',last_name) AS FullName, user_role AS UserRole, username FROM employees WHERE id=@Id",
            new { Id = id });

        if (user == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

        user.Departments = c.Query<EmployeeDepartmentDto>(
            @"SELECT ed.id, ed.department_id AS DepartmentId, d.code AS DepartmentCode,
                     d.name AS DepartmentName, ed.is_responsible AS IsResponsible, ed.is_primary AS IsPrimary
              FROM employee_departments ed
              JOIN departments d ON d.id = ed.department_id
              WHERE ed.employee_id = @Id
              ORDER BY d.sort_order", new { Id = id }).ToList();

        user.Competences = c.Query<EmployeeCompetenceDto>(
            @"SELECT ec.id, ec.department_id AS DepartmentId, d.code AS DepartmentCode,
                     d.name AS DepartmentName, ec.notes
              FROM employee_competences ec
              JOIN departments d ON d.id = ec.department_id
              WHERE ec.employee_id = @Id
              ORDER BY d.sort_order", new { Id = id }).ToList();

        return Ok(ApiResponse<UserDetailDto>.Ok(user));
    }

    // ── Cambia ruolo ─────────────────────────────────────────────────
    [HttpPut("role")]
    public IActionResult SetRole([FromBody] SaveUserRoleRequest req)
    {
        using var c = _db.Open();

        // Il ruolo dev'essere uno di quelli che esistono davvero (gerarchia o profilo della #63).
        // Prima si accettava qualunque stringa, e un nome sbagliato non dava errore: l'utente
        // ricadeva silenziosamente nel livello 0 col fallback permissivo — cioè con PIÙ accesso
        // del previsto, non meno. Un refuso qui è indistinguibile da una scelta.
        bool ruoloValido = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM auth_levels WHERE role_name = @UserRole", new { req.UserRole }) > 0;
        if (!ruoloValido)
        {
            return Ok(ApiResponse<bool>.Fail(
                $"Il ruolo «{req.UserRole}» non esiste: scegline uno dall'elenco."));
        }

        c.Execute("UPDATE employees SET user_role=@UserRole WHERE id=@EmployeeId", req);
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Reset credenziali a quelle iniziali (username e password = n.cognome) ──
    [HttpPost("reset-password")]
    public IActionResult ResetPassword([FromBody] ResetPasswordRequest req)
    {
        using var c = _db.Open();
        EmployeeNameRow? row = c.QueryFirstOrDefault<EmployeeNameRow>(
            "SELECT first_name AS FirstName, last_name AS LastName FROM employees WHERE id=@EmployeeId",
            new { req.EmployeeId });

        if (row == null)
            return NotFound(ApiResponse<string>.Fail("Utente non trovato"));

        string initialLogin = InitialPasswordHelper.Build(row.FirstName, row.LastName);
        if (string.IsNullOrEmpty(initialLogin))
            return BadRequest(ApiResponse<string>.Fail("Impossibile calcolare la password iniziale"));

        string hash = BCrypt.Net.BCrypt.HashPassword(initialLogin);
        c.Execute(
            "UPDATE employees SET username=@Username, password_hash=@Hash WHERE id=@EmployeeId",
            new { Username = initialLogin, Hash = hash, req.EmployeeId });

        return Ok(ApiResponse<string>.Ok(initialLogin, $"Password reimpostata a {initialLogin}"));
    }

    private class EmployeeNameRow
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
    }

    // ── Cambia stato (riattivazione cessati / disattivazione) ─────────
    //
    // ⚠️ Anche QUI vale «non ci si chiude fuori» (PIANO-PERMESSI.md §5.1). Cessare una persona
    // non tocca nessuna riga di permesso, ma la toglie dall'insieme di chi può amministrarli: se
    // era l'ultima, il gestionale resta senza nessuno in grado di concedere niente — e senza
    // nessuno che possa rimediare dall'applicazione. È il percorso che ci si dimenta, perché
    // sembra una modifica di anagrafica e non di permessi.
    [HttpPut("status")]
    public IActionResult SetStatus([FromBody] SaveUserStatusRequest req)
    {
        string status = req.IsActive ? "ACTIVE" : "TERMINATED";

        try
        {
            int rows = _permessi.EseguiConGaranzia(
                (c, tx, _) => c.Execute(
                    "UPDATE employees SET status=@Status WHERE id=@EmployeeId",
                    new { Status = status, req.EmployeeId }, tx),
                "Non si può cessare questa persona: è l'ultima in forza che può gestire i permessi. Assegna prima «Gestione Permessi» a qualcun altro.");

            if (rows == 0)
                return NotFound(ApiResponse<string>.Fail("Utente non trovato"));
        }
        catch (PermissionAdminService.UltimoAmministratoreException ex)
        {
            return Conflict(ApiResponse<string>.Fail(ex.Message));
        }

        // Lo stato è in cache per mezzo minuto (lo si guarda a ogni richiesta autenticata):
        // buttandola via qui, il cessato prende 401 alla richiesta successiva invece di
        // continuare fino alla scadenza — è il «fuori subito» del piano (§4.3).
        _access.DimenticaPersona(req.EmployeeId);

        return Ok(ApiResponse<bool>.Ok(true, req.IsActive ? "Utente riattivato" : "Utente cessato"));
    }

    // ── Salva reparti dipendente (replace completo) ───────────────────
    [HttpPut("departments")]
    public IActionResult SaveDepartments([FromBody] SaveEmployeeDepartmentsRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM employee_departments WHERE employee_id=@EmployeeId", new { req.EmployeeId }, tx);

        foreach (EmployeeDepartmentDto d in req.Departments)
        {
            c.Execute(
                @"INSERT INTO employee_departments (employee_id, department_id, is_responsible, is_primary)
                  VALUES (@EmployeeId, @DepartmentId, @IsResponsible, @IsPrimary)",
                new { req.EmployeeId, d.DepartmentId, d.IsResponsible, d.IsPrimary }, tx);
        }

        tx.Commit();

        // I reparti di una persona decidono anche i suoi PERMESSI: chi sta in Contabilità vede
        // quattro funzioni e basta. Quell'appartenenza è tenuta in memoria (mezzo minuto), e
        // senza questa riga spostare qualcuno di reparto aveva effetto solo allo scadere della
        // cache — la persona restava per un minuto con i permessi del reparto di prima.
        _access.DimenticaPersona(req.EmployeeId);

        return Ok(ApiResponse<bool>.Ok(true));
    }

    // ── Salva competenze dipendente (replace completo) ────────────────
    [HttpPut("competences")]
    public IActionResult SaveCompetences([FromBody] SaveEmployeeCompetencesRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM employee_competences WHERE employee_id=@EmployeeId", new { req.EmployeeId }, tx);

        foreach (EmployeeCompetenceDto comp in req.Competences)
        {
            c.Execute(
                @"INSERT INTO employee_competences (employee_id, department_id, notes)
                  VALUES (@EmployeeId, @DepartmentId, @Notes)",
                new { req.EmployeeId, comp.DepartmentId, comp.Notes }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
