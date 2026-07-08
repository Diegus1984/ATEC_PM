using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/sal")]
[Authorize]
public class SalController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public SalController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    public const string ConflictMessage = "CONFLITTO: record SAL modificato da un altro utente";

    private void NotifyChanged(string action, int projectId)
    {
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId))
            .SendAsync("SalChanged", new { action, projectId });
        _ = _hub.Clients.All.SendAsync("GlobalSalChanged", new { action, projectId });
    }

    [HttpGet]
    public IActionResult GetBundle([FromQuery] int projectId)
    {
        if (projectId <= 0) return Ok(ApiResponse<SalBundleDto>.Fail("projectId obbligatorio"));
        using var c = _db.Open();

        // 1. Carica o crea l'header SAL al volo
        var header = c.QueryFirstOrDefault<SalHeaderDto>(@"
            SELECT project_id AS ProjectId, cliente AS Cliente, valore AS Valore, row_version AS RowVersion
            FROM project_sal WHERE project_id=@Pid", new { Pid = projectId });

        if (header == null)
        {
            c.Execute("INSERT IGNORE INTO project_sal (project_id, cliente, valore) VALUES (@Pid, '', NULL)", new { Pid = projectId });
            header = new SalHeaderDto { ProjectId = projectId, Cliente = "", Valore = null, RowVersion = 0 };
        }

        // 2. Carica le righe SAL ordinate per sort_order, id
        var rows = c.Query<SalRowDto>(@"
            SELECT id AS Id, project_id AS ProjectId, step AS Step, perc AS Perc,
                   condizione AS Condizione, data_fatt AS DataFatt, stato AS Stato,
                   sort_order AS SortOrder, row_version AS RowVersion,
                   paid_by AS PaidBy, paid_at AS PaidAt
            FROM sal_rows WHERE project_id=@Pid ORDER BY sort_order, id", new { Pid = projectId }).ToList();

        var bundle = new SalBundleDto
        {
            Header = header,
            Rows = rows
        };

        return Ok(ApiResponse<SalBundleDto>.Ok(bundle));
    }

    [HttpPut("header")]
    public IActionResult UpdateHeader([FromQuery] int projectId, [FromBody] SalHeaderSaveRequest req)
    {
        if (projectId <= 0) return Ok(ApiResponse<int>.Fail("projectId obbligatorio"));
        using var c = _db.Open();

        int rows = c.Execute(@"
            UPDATE project_sal SET
                cliente=@Cliente, valore=@Valore,
                row_version = row_version + 1, updated_at = CURRENT_TIMESTAMP
             WHERE project_id=@Pid AND (@RowVersion IS NULL OR row_version=@RowVersion)",
            new
            {
                Cliente = (req.Cliente ?? "").Trim(),
                req.Valore,
                Pid = projectId,
                req.RowVersion
            });

        if (rows == 0)
        {
            int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM project_sal WHERE project_id=@Pid", new { Pid = projectId });
            return Ok(ApiResponse<int>.Fail(exists > 0 ? ConflictMessage : "Header SAL non trovato"));
        }

        NotifyChanged("header", projectId);
        return Ok(ApiResponse<int>.Ok(projectId, "Header SAL aggiornato"));
    }

    [HttpPost("rows")]
    public IActionResult CreateRow([FromQuery] int projectId, [FromBody] SalRowSaveRequest req)
    {
        if (projectId <= 0) return Ok(ApiResponse<int>.Fail("projectId obbligatorio"));
        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<int>.Fail("Commessa non trovata"));

        int sortOrder = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM sal_rows WHERE project_id=@Pid",
            new { Pid = projectId });

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO sal_rows
                (project_id, step, perc, condizione, data_fatt, stato, sort_order, created_by)
            VALUES (@Pid, @Step, @Perc, @Condizione, @DataFatt, @Stato, @SortOrder, @CreatedBy);
            SELECT LAST_INSERT_ID()",
            new
            {
                Pid = projectId,
                Step = (req.Step ?? "").Trim(),
                req.Perc,
                Condizione = req.Condizione ?? "",
                req.DataFatt,
                Stato = req.Stato ?? "",
                SortOrder = sortOrder,
                CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
            });

        NotifyChanged("create_row", projectId);
        return Ok(ApiResponse<int>.Ok(id, "Step SAL aggiunto"));
    }

    [HttpPut("rows/{id}")]
    public IActionResult UpdateRow(int id, [FromBody] SalRowSaveRequest req)
    {
        using var c = _db.Open();
        string? role = User.FindFirst(ClaimTypes.Role)?.Value;

        // Recupera lo stato attuale della riga per verificare se è già pagata
        var current = c.QueryFirstOrDefault<dynamic>(
            "SELECT stato, project_id FROM sal_rows WHERE id=@Id", new { Id = id });
        if (current != null)
        {
            string currentStato = (string)current.stato;
            if (currentStato == "pagata" && role != "ADMIN")
            {
                return Ok(ApiResponse<int>.Fail("La riga è già stata pagata e non può essere modificata se non da un utente ADMIN."));
            }
        }

        string? currentStatoStr = current?.stato;
        int? paidBy = null;
        DateTime? paidAt = null;

        if (req.Stato == "pagata")
        {
            if (currentStatoStr == "pagata")
            {
                var audit = c.QueryFirstOrDefault<dynamic>("SELECT paid_by, paid_at FROM sal_rows WHERE id=@Id", new { Id = id });
                paidBy = (int?)audit?.paid_by;
                paidAt = (DateTime?)audit?.paid_at;
            }
            else
            {
                paidBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null;
                paidAt = DateTime.Now;
            }
        }

        int rows = c.Execute(@"
            UPDATE sal_rows SET
                step=@Step, perc=@Perc, condizione=@Condizione, data_fatt=@DataFatt, stato=@Stato,
                paid_by=@PaidBy, paid_at=@PaidAt,
                row_version = row_version + 1, updated_at = CURRENT_TIMESTAMP
             WHERE id=@Id AND (@RowVersion IS NULL OR row_version=@RowVersion)",
            new
            {
                Step = (req.Step ?? "").Trim(),
                req.Perc,
                Condizione = req.Condizione ?? "",
                req.DataFatt,
                Stato = req.Stato ?? "",
                PaidBy = paidBy,
                PaidAt = paidAt,
                Id = id,
                req.RowVersion
            });

        if (rows == 0)
        {
            int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_rows WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<int>.Fail(exists > 0 ? ConflictMessage : "Step SAL non trovato"));
        }

        int projectId = current != null ? (int)current.project_id : 0;
        NotifyChanged("update_row", projectId);
        return Ok(ApiResponse<int>.Ok(id, "Step SAL aggiornato"));
    }

    [HttpDelete("rows/{id}")]
    public IActionResult DeleteRow(int id)
    {
        using var c = _db.Open();
        string? role = User.FindFirst(ClaimTypes.Role)?.Value;

        var current = c.QueryFirstOrDefault<dynamic>(
            "SELECT stato, project_id FROM sal_rows WHERE id=@Id", new { Id = id });
        if (current != null)
        {
            string currentStato = (string)current.stato;
            if (currentStato == "pagata" && role != "ADMIN")
            {
                return Ok(ApiResponse<bool>.Fail("La riga è già stata pagata e non può essere eliminata se non da un utente ADMIN."));
            }
        }

        int projectId = current != null ? (int)current.project_id : 0;
        int rows = c.Execute("DELETE FROM sal_rows WHERE id=@Id", new { Id = id });
        if (rows == 0) return Ok(ApiResponse<bool>.Fail("Step SAL non trovato"));

        NotifyChanged("delete_row", projectId);
        return Ok(ApiResponse<bool>.Ok(true, "Step SAL eliminato"));
    }

    [HttpPost("rows/reorder")]
    public IActionResult Reorder([FromQuery] int projectId, [FromBody] SalReorderRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0) return Ok(ApiResponse<bool>.Ok(true));
        using var c = _db.Open();
        int order = 0;
        foreach (int id in req.Ids)
        {
            c.Execute("UPDATE sal_rows SET sort_order=@Sort WHERE id=@Id AND project_id=@Pid",
                new { Sort = order++, Id = id, Pid = projectId });
        }
        NotifyChanged("reorder_rows", projectId);
        return Ok(ApiResponse<bool>.Ok(true, "Ordine step aggiornato"));
    }

    [HttpPost("project/{projectId}/seed-template")]
    public IActionResult SeedTemplate(int projectId)
    {
        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<int>.Fail("Commessa non trovata"));

        int existing = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_rows WHERE project_id=@Pid", new { Pid = projectId });
        if (existing > 0) return Ok(ApiResponse<int>.Fail("La commessa contiene già degli step SAL"));

        var steps = new[]
        {
            new { Step = "1° acconto all'ordine", Perc = 15.0m, Cond = "A Vista" },
            new { Step = "2° acconto ad approvazione disegni", Perc = 15.0m, Cond = "A Vista" },
            new { Step = "3° acconto ad avviso merce pronta", Perc = 10.0m, Cond = "A Vista" },
            new { Step = "4° acconto a consegna/installazione", Perc = 20.0m, Cond = "30 gg. dffm." },
            new { Step = "5° acconto a collaudo", Perc = 20.0m, Cond = "30 gg. dffm." },
            new { Step = "Saldo a 30 gg. fine collaudo", Perc = 20.0m, Cond = "30 gg. dffm." }
        };

        int sortOrder = 0;
        foreach (var s in steps)
        {
            c.Execute(@"
                INSERT INTO sal_rows (project_id, step, perc, condizione, sort_order, created_by)
                VALUES (@Pid, @Step, @Perc, @Condizione, @Sort, @CreatedBy)",
                new
                {
                    Pid = projectId,
                    s.Step,
                    s.Perc,
                    Condizione = s.Cond,
                    Sort = sortOrder++,
                    CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
                });
        }

        NotifyChanged("seed_template", projectId);
        return Ok(ApiResponse<int>.Ok(steps.Length, $"{steps.Length} step SAL inseriti"));
    }

    [HttpGet("conditions")]
    public IActionResult GetConditions()
    {
        using var c = _db.Open();
        var rows = c.Query<SalConditionDto>(@"
            SELECT id AS Id, label AS Label, sort_order AS SortOrder, is_active AS IsActive
            FROM sal_conditions ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<SalConditionDto>>.Ok(rows));
    }

    [HttpGet("conditions/active")]
    public IActionResult GetActiveConditions()
    {
        using var c = _db.Open();
        var rows = c.Query<SalConditionDto>(@"
            SELECT id AS Id, label AS Label, sort_order AS SortOrder, is_active AS IsActive
            FROM sal_conditions WHERE is_active=TRUE ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<SalConditionDto>>.Ok(rows));
    }

    [HttpPost("conditions")]
    public IActionResult CreateCondition([FromBody] SalConditionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label)) return Ok(ApiResponse<int>.Fail("Etichetta obbligatoria"));
        using var c = _db.Open();

        int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_conditions WHERE LOWER(label)=LOWER(@Lbl)", new { Lbl = req.Label.Trim() });
        if (exists > 0) return Ok(ApiResponse<int>.Fail("Condizione già esistente"));

        int sortOrder = c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM sal_conditions");

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO sal_conditions (label, sort_order, is_active)
            VALUES (@Label, @Sort, TRUE);
            SELECT LAST_INSERT_ID()",
            new { Label = req.Label.Trim(), Sort = sortOrder });

        return Ok(ApiResponse<int>.Ok(id, "Condizione creata"));
    }

    [HttpPut("conditions/{id}")]
    public IActionResult UpdateCondition(int id, [FromBody] SalConditionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label)) return Ok(ApiResponse<int>.Fail("Etichetta obbligatoria"));
        using var c = _db.Open();

        int rows = c.Execute("UPDATE sal_conditions SET label=@Label WHERE id=@Id", new { Label = req.Label.Trim(), Id = id });
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Condizione non trovata"));

        return Ok(ApiResponse<int>.Ok(id, "Condizione aggiornata"));
    }

    [HttpPut("conditions/{id}/toggle-active")]
    public IActionResult ToggleActiveCondition(int id, [FromQuery] bool active)
    {
        using var c = _db.Open();
        int rows = c.Execute("UPDATE sal_conditions SET is_active=@Active WHERE id=@Id", new { Active = active, Id = id });
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Condizione non trovata"));

        return Ok(ApiResponse<int>.Ok(id, "Condizione aggiornata"));
    }

    [HttpDelete("conditions/{id}")]
    public IActionResult DeleteCondition(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM sal_conditions WHERE id=@Id", new { Id = id });
        if (rows == 0) return Ok(ApiResponse<bool>.Fail("Condizione non trovata"));
        return Ok(ApiResponse<bool>.Ok(true, "Condizione eliminata"));
    }

    [HttpPost("conditions/reorder")]
    public IActionResult ReorderConditions([FromBody] SalReorderRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0) return Ok(ApiResponse<bool>.Ok(true));
        using var c = _db.Open();
        int order = 0;
        foreach (int id in req.Ids)
        {
            c.Execute("UPDATE sal_conditions SET sort_order=@Sort WHERE id=@Id",
                new { Sort = order++, Id = id });
        }
        return Ok(ApiResponse<bool>.Ok(true, "Ordine condizioni aggiornato"));
    }

    [HttpPost("conditions/reset")]
    public IActionResult ResetConditions()
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM sal_conditions");
        string[] standardConditions = new[] { "A Vista", "30 gg. dffm.", "60 gg. dffm.", "90 gg. dffm." };
        int order = 1;
        foreach (string cond in standardConditions)
        {
            c.Execute("INSERT INTO sal_conditions (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                new { Label = cond, Sort = order++ });
        }
        return Ok(ApiResponse<bool>.Ok(true, "Condizioni di pagamento ripristinate allo standard"));
    }


    [HttpGet("prospetto")]
    public IActionResult GetProspetto()
    {
        using var c = _db.Open();
        var rows = c.Query<SalProspettoRowDto>(@"
            SELECT t.project_id AS ProjectId, p.code AS Code, ps.cliente AS Cliente,
                   t.step AS Step, t.perc AS Perc, t.condizione AS Condizione, t.data_fatt AS DataFatt,
                   (ps.valore * t.perc / 100) AS Importo,
                   t.row_num AS Ord,
                   CASE
                       WHEN t.data_fatt <= CURDATE() THEN 'warn'
                       WHEN CURDATE() >= DATE_SUB(DATE_SUB(t.data_fatt, INTERVAL WEEKDAY(t.data_fatt) DAY), INTERVAL 7 DAY) THEN 'pre'
                       ELSE ''
                   END AS Alert
            FROM (
                SELECT id, project_id, step, perc, condizione, data_fatt, stato,
                       ROW_NUMBER() OVER (PARTITION BY project_id ORDER BY data_fatt ASC, id ASC) AS row_num
                FROM sal_rows
                WHERE data_fatt IS NOT NULL AND stato = ''
            ) t
            JOIN projects p ON p.id = t.project_id
            LEFT JOIN project_sal ps ON ps.project_id = t.project_id
            WHERE t.row_num <= 2 AND p.status = 'ACTIVE'
            ORDER BY p.code, t.data_fatt ASC").ToList();

        return Ok(ApiResponse<List<SalProspettoRowDto>>.Ok(rows));
    }

    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        using var c = _db.Open();
        var rows = c.Query<SalSummaryDto>(@"
            SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title,
                   COUNT(*) AS Total,
                   COALESCE(SUM(sr.data_fatt IS NOT NULL AND sr.stato = ''), 0) AS Open,
                   COALESCE(SUM(sr.stato = '' AND sr.data_fatt IS NOT NULL
                                AND sr.data_fatt <= CURDATE()), 0) AS Warn,
                   COALESCE(SUM(sr.stato = '' AND sr.data_fatt IS NOT NULL AND sr.data_fatt > CURDATE()
                                AND CURDATE() >= DATE_SUB(DATE_SUB(sr.data_fatt,
                                     INTERVAL WEEKDAY(sr.data_fatt) DAY), INTERVAL 7 DAY)), 0) AS Pre
            FROM sal_rows sr
            JOIN projects p ON p.id = sr.project_id
            GROUP BY p.id, p.code, p.title
            HAVING COUNT(*) > 0
            ORDER BY p.code").ToList();
        return Ok(ApiResponse<List<SalSummaryDto>>.Ok(rows));
    }
}

