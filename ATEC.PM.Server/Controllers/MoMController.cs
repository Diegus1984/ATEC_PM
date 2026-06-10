using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

// API del modulo MoM (verbali di riunione): verbale → action item.
// Editing aperto a tutti gli utenti autenticati (strumento operativo, non solo ADMIN).
[ApiController]
[Route("api/mom")]
[Authorize]
public class MoMController : ControllerBase
{
    private readonly MoMDbService _mdb;
    public MoMController(MoMDbService mdb)
    {
        _mdb = mdb;
    }

    // ═══════════════════════════════════════════════════════
    // LISTA VERBALI
    // ═══════════════════════════════════════════════════════

    // projectId opzionale: stessa lista filtrata su una singola commessa (sezione MoM nell'albero commesse).
    [HttpGet("list")]
    public IActionResult GetList([FromQuery] int? projectId = null)
    {
        try
        {
            using var c = _mdb.Open();
            var rows = c.Query<MoMListDto>(@"
                SELECT m.id AS Id, m.tipo AS Tipo, m.project_id AS ProjectId,
                       p.code AS ProjectCode, m.title AS Title, m.meeting_date AS MeetingDate,
                       m.in_dashboard AS InDashboard,
                       (SELECT COUNT(*) FROM mom_action_items a WHERE a.mom_id = m.id) AS ItemsCount,
                       (SELECT COUNT(*) FROM mom_action_items a WHERE a.mom_id = m.id AND a.status <> 'CLOSED') AS OpenCount,
                       (SELECT COUNT(*) FROM mom_action_items a WHERE a.mom_id = m.id AND a.priorita = 1) AS P1Count,
                       (SELECT COUNT(*) FROM mom_action_items a WHERE a.mom_id = m.id AND a.priorita = 2) AS P2Count,
                       (SELECT COUNT(*) FROM mom_action_items a WHERE a.mom_id = m.id AND a.priorita = 3) AS P3Count,
                       (SELECT MIN(x.d) FROM (
                            SELECT data_check AS d FROM mom_action_items WHERE mom_id = m.id AND data_check IS NOT NULL
                            UNION ALL
                            SELECT data_close AS d FROM mom_action_items WHERE mom_id = m.id AND data_close IS NOT NULL
                        ) x) AS PeriodStart,
                       (SELECT MAX(x.d) FROM (
                            SELECT data_check AS d FROM mom_action_items WHERE mom_id = m.id AND data_check IS NOT NULL
                            UNION ALL
                            SELECT data_close AS d FROM mom_action_items WHERE mom_id = m.id AND data_close IS NOT NULL
                        ) x) AS PeriodEnd
                FROM mom_records m
                LEFT JOIN projects p ON p.id = m.project_id
                " + (projectId.HasValue ? "WHERE m.project_id = @ProjectId" : "") + @"
                ORDER BY (m.meeting_date IS NULL), m.meeting_date DESC, m.id DESC",
                new { ProjectId = projectId }).ToList();
            return Ok(ApiResponse<List<MoMListDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<MoMListDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // DETTAGLIO VERBALE (header + action item)
    // ═══════════════════════════════════════════════════════

    [HttpGet("{id}")]
    public IActionResult GetDetail(int id)
    {
        try
        {
            using var c = _mdb.Open();
            MoMDetailDto? head = c.QueryFirstOrDefault<MoMDetailDto>(@"
                SELECT m.id AS Id, m.tipo AS Tipo, m.project_id AS ProjectId,
                       p.code AS ProjectCode, p.title AS ProjectTitle,
                       m.title AS Title, m.meeting_date AS MeetingDate, m.in_dashboard AS InDashboard
                FROM mom_records m
                LEFT JOIN projects p ON p.id = m.project_id
                WHERE m.id = @Id", new { Id = id });
            if (head == null) return Ok(ApiResponse<MoMDetailDto>.Fail("Verbale non trovato"));

            head.Items = c.Query<MoMActionItemDto>(@"
                SELECT a.id AS Id, a.mom_id AS MomId, a.attivita AS Attivita, a.descrizione AS Descrizione,
                       a.azione AS Azione, a.priorita AS Priorita, a.status AS Status, a.is_critical AS IsCritical,
                       a.resp1_id AS Resp1Id, CONCAT_WS(' ', e1.first_name, e1.last_name) AS Resp1Name,
                       a.resp2_id AS Resp2Id, CONCAT_WS(' ', e2.first_name, e2.last_name) AS Resp2Name,
                       a.resp3_id AS Resp3Id, CONCAT_WS(' ', e3.first_name, e3.last_name) AS Resp3Name,
                       a.data_check AS DataCheck, a.data_close AS DataClose
                FROM mom_action_items a
                LEFT JOIN employees e1 ON e1.id = a.resp1_id
                LEFT JOIN employees e2 ON e2.id = a.resp2_id
                LEFT JOIN employees e3 ON e3.id = a.resp3_id
                WHERE a.mom_id = @Id
                ORDER BY CASE WHEN a.is_critical = 1 AND a.status <> 'CLOSED' THEN 0
                              WHEN a.status = 'OPEN' THEN 1
                              WHEN a.status = 'STANDBY' THEN 2
                              ELSE 3 END,
                         a.priorita, a.id", new { Id = id }).ToList();
            _mdb.AttachResponsibles(c, head.Items);
            return Ok(ApiResponse<MoMDetailDto>.Ok(head));
        }
        catch (Exception ex) { return Ok(ApiResponse<MoMDetailDto>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // CRUD VERBALE
    // ═══════════════════════════════════════════════════════

    [HttpPost("")]
    public IActionResult Create([FromBody] MoMSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return Ok(ApiResponse<int>.Fail("Titolo obbligatorio"));
            string tipo = req.Tipo == "COMMESSA" ? "COMMESSA" : "RIUNIONE";
            int? projectId = tipo == "COMMESSA" ? req.ProjectId : null;
            if (tipo == "COMMESSA" && projectId == null)
                return Ok(ApiResponse<int>.Fail("Seleziona una commessa"));

            using var c = _mdb.Open();
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO mom_records (tipo, project_id, title, meeting_date, in_dashboard)
                VALUES (@Tipo, @ProjectId, @Title, @MeetingDate, @InDashboard);
                SELECT LAST_INSERT_ID()",
                new { Tipo = tipo, ProjectId = projectId, Title = req.Title.Trim(),
                      req.MeetingDate, InDashboard = req.InDashboard ? 1 : 0 });
            return Ok(ApiResponse<int>.Ok(id, "Verbale creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] MoMSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return Ok(ApiResponse<int>.Fail("Titolo obbligatorio"));
            string tipo = req.Tipo == "COMMESSA" ? "COMMESSA" : "RIUNIONE";
            int? projectId = tipo == "COMMESSA" ? req.ProjectId : null;
            if (tipo == "COMMESSA" && projectId == null)
                return Ok(ApiResponse<int>.Fail("Seleziona una commessa"));

            using var c = _mdb.Open();
            int rows = c.Execute(@"
                UPDATE mom_records SET tipo=@Tipo, project_id=@ProjectId, title=@Title,
                       meeting_date=@MeetingDate, in_dashboard=@InDashboard
                 WHERE id=@Id",
                new { Tipo = tipo, ProjectId = projectId, Title = req.Title.Trim(),
                      req.MeetingDate, InDashboard = req.InDashboard ? 1 : 0, Id = id });
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Verbale non trovato"));
            return Ok(ApiResponse<int>.Ok(id, "Verbale aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        try
        {
            using var c = _mdb.Open();
            // FK ON DELETE CASCADE rimuove le action item (il client conferma prima).
            int rows = c.Execute("DELETE FROM mom_records WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Verbale non trovato"));
            return Ok(ApiResponse<bool>.Ok(true, "Verbale eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // CRUD ACTION ITEM
    // ═══════════════════════════════════════════════════════

    [HttpPost("{momId}/items")]
    public IActionResult AddItem(int momId, [FromBody] MoMActionItemSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Attivita))
                return Ok(ApiResponse<int>.Fail("Attività obbligatoria"));
            using MySqlConnection c = _mdb.Open();
            int momExists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM mom_records WHERE id=@Id", new { Id = momId });
            if (momExists == 0) return Ok(ApiResponse<int>.Fail("Verbale non trovato"));

            int sortOrder = c.ExecuteScalar<int>(@"
                SELECT COALESCE(MAX(sort_order), -1) + 1
                FROM mom_action_items
                WHERE mom_id=@MomId", new { MomId = momId });

            int id = c.ExecuteScalar<int>(@"
                INSERT INTO mom_action_items
                    (mom_id, attivita, descrizione, azione, priorita, status, is_critical,
                     resp1_id, resp2_id, resp3_id, data_check, data_close, sort_order)
                VALUES (@MomId, @Attivita, @Descrizione, @Azione, @Priorita, @Status, @IsCritical,
                        @Resp1Id, @Resp2Id, @Resp3Id, @DataCheck, @DataClose, @SortOrder);
                SELECT LAST_INSERT_ID()", BuildItemParams(momId, req, sortOrder: sortOrder));
            _mdb.SaveItemResponsibles(c, id, ResolveResponsibleIds(req));
            return Ok(ApiResponse<int>.Ok(id, "Azione aggiunta"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("items/{id}")]
    public IActionResult UpdateItem(int id, [FromBody] MoMActionItemSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Attivita))
                return Ok(ApiResponse<int>.Fail("Attività obbligatoria"));
            using var c = _mdb.Open();
            int rows = c.Execute(@"
                UPDATE mom_action_items SET
                    attivita=@Attivita, descrizione=@Descrizione, azione=@Azione,
                    priorita=@Priorita, status=@Status, is_critical=@IsCritical,
                    resp1_id=@Resp1Id, resp2_id=@Resp2Id, resp3_id=@Resp3Id,
                    data_check=@DataCheck, data_close=@DataClose
                 WHERE id=@Id", BuildItemParams(null, req, id));
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Azione non trovata"));
            _mdb.SaveItemResponsibles(c, id, ResolveResponsibleIds(req));
            return Ok(ApiResponse<int>.Ok(id, "Azione aggiornata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("items/{id}")]
    public IActionResult DeleteItem(int id)
    {
        try
        {
            using var c = _mdb.Open();
            int rows = c.Execute("DELETE FROM mom_action_items WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Azione non trovata"));
            return Ok(ApiResponse<bool>.Ok(true, "Azione eliminata"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // Normalizza i parametri di una action item (clamp priorità, stato valido).
    private static object BuildItemParams(int? momId, MoMActionItemSaveRequest req, int? id = null, int? sortOrder = null)
    {
        int pri = req.Priorita < 1 ? 1 : (req.Priorita > 3 ? 3 : req.Priorita);
        string status = req.Status is "OPEN" or "STANDBY" or "CLOSED" ? req.Status : "OPEN";
        List<int> respIds = ResolveResponsibleIds(req);
        return new
        {
            MomId = momId,
            Id = id,
            Attivita = req.Attivita.Trim(),
            req.Descrizione,
            req.Azione,
            Priorita = pri,
            Status = status,
            IsCritical = req.IsCritical ? 1 : 0,
            Resp1Id = respIds.Count > 0 ? respIds[0] : (int?)null,
            Resp2Id = respIds.Count > 1 ? respIds[1] : (int?)null,
            Resp3Id = respIds.Count > 2 ? respIds[2] : (int?)null,
            req.DataCheck,
            req.DataClose,
            SortOrder = sortOrder
        };
    }

    private static List<int> ResolveResponsibleIds(MoMActionItemSaveRequest req)
    {
        if (req.ResponsibleIds != null && req.ResponsibleIds.Count > 0)
        {
            List<int> ids = new List<int>();
            foreach (int empId in req.ResponsibleIds)
            {
                if (empId <= 0 || ids.Contains(empId)) continue;
                ids.Add(empId);
            }
            return ids;
        }

        List<int> legacy = new List<int>();
        if (req.Resp1Id.HasValue) legacy.Add(req.Resp1Id.Value);
        if (req.Resp2Id.HasValue && req.Resp2Id != req.Resp1Id) legacy.Add(req.Resp2Id.Value);
        if (req.Resp3Id.HasValue && !legacy.Contains(req.Resp3Id.Value)) legacy.Add(req.Resp3Id.Value);
        return legacy;
    }

    // ═══════════════════════════════════════════════════════
    // LOOKUP (commesse + dipendenti) per le combo del client
    // ═══════════════════════════════════════════════════════

    [HttpGet("lookups/projects")]
    public IActionResult GetProjectLookups()
    {
        try
        {
            using var c = _mdb.Open();
            var rows = c.Query<MoMProjectLookupDto>(@"
                SELECT id AS Id, code AS Code, title AS Title
                FROM projects
                WHERE status <> 'CANCELLED'
                ORDER BY code DESC").ToList();
            return Ok(ApiResponse<List<MoMProjectLookupDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<MoMProjectLookupDto>>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpGet("lookups/employees")]
    public IActionResult GetEmployeeLookups()
    {
        try
        {
            using var c = _mdb.Open();
            // SOLO utenti reali: niente wildcard "[XXX] Generico", niente ADMIN/cessati.
            var rows = c.Query<LookupItem>(EmployeeLookupQueries.RealEmployeesSql).ToList();
            return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<LookupItem>>.Fail($"Errore: {ex.Message}")); }
    }

    // Wildcard reparto ([PM] Generico, …): usate solo per il pre-assegnamento da filtro
    // reparto, NON mostrate nei combo responsabili.
    [HttpGet("lookups/wildcards")]
    public IActionResult GetWildcardLookups()
    {
        try
        {
            using var c = _mdb.Open();
            _mdb.EnsureWildcardEmployees(c);
            var rows = c.Query<LookupItem>(@"
                SELECT id AS Id, CONCAT_WS(' ', first_name, last_name) AS Name
                FROM employees
                WHERE status <> 'TERMINATED' AND first_name LIKE '[%'
                ORDER BY first_name").ToList();
            return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<LookupItem>>.Fail($"Errore: {ex.Message}")); }
    }
}
