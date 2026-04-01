using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/projects/{projectId}/cashflow")]
[Authorize]
public class CashFlowController : ControllerBase
{
    private readonly DbService _db;
    public CashFlowController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult Get(int projectId)
    {
        using var c = _db.Open();

        var proj = c.QueryFirstOrDefault<dynamic>(
            "SELECT code, revenue, start_date FROM projects WHERE id=@id", new { id = projectId });
        if (proj == null) return NotFound();

        var header = c.QueryFirstOrDefault<dynamic>(
            "SELECT payment_amount, month_count, start_date FROM project_cashflow WHERE project_id=@pid",
            new { pid = projectId });

        // Sync automatica categorie robot se il flusso è inizializzato
        if (header != null)
            SyncRobotCategories(c, projectId);

        var categories = c.Query<CashFlowCategoryDto>(@"
            SELECT id AS Id, name AS Name, total_amount AS TotalAmount,
                   notes AS Notes, sort_order AS SortOrder, linked_source AS LinkedSource
            FROM project_cashflow_categories
            WHERE project_id=@pid ORDER BY sort_order",
            new { pid = projectId }).ToList();

        var dataItems = c.Query<CashFlowDataItemDto>(@"
            SELECT data_type AS DataType, ref_id AS RefId, month_number AS MonthNumber,
                   num_value AS NumValue, date_value AS DateValue
            FROM project_cashflow_data
            WHERE project_id=@pid
            ORDER BY data_type, ref_id, month_number",
            new { pid = projectId }).ToList();

        // Usa start_date del cashflow se impostata, altrimenti quella del progetto
        DateTime? cfStartDate = header != null ? header.start_date as DateTime? : null;
        DateTime? effectiveStartDate = cfStartDate ?? proj.start_date as DateTime?;

        return Ok(ApiResponse<CashFlowData>.Ok(new CashFlowData
        {
            ProjectId = projectId,
            ProjectCode = (string)(proj.code ?? ""),
            ProjectRevenue = (decimal)(proj.revenue ?? 0m),
            StartDate = effectiveStartDate,
            PaymentAmount = header != null ? (decimal)(header.payment_amount ?? 0m) : 0m,
            MonthCount = header != null ? (int)(header.month_count ?? 13) : 13,
            IsInitialized = header != null,
            Categories = categories,
            DataItems = dataItems
        }));
    }

    /// <summary>
    /// Sincronizza le categorie del flusso di cassa con i PRODUCT (robot) della commessa.
    /// - Crea categorie mancanti per nuovi robot
    /// - Aggiorna nome e totale netto per robot esistenti
    /// - Elimina categorie di robot che non esistono più
    /// </summary>
    private void SyncRobotCategories(System.Data.IDbConnection c, int projectId)
    {
        // Legge tutti i PRODUCT della commessa con il loro totale netto figli
        var robots = c.Query<(int Id, string Description, decimal NetTotal)>(@"
            SELECT p.id, p.description,
                   COALESCE(SUM(ch.quantity * ch.unit_cost), 0) AS net_total
            FROM project_material_items p
            JOIN project_material_sections s ON s.id = p.section_id
            LEFT JOIN project_material_items ch ON ch.parent_item_id = p.id
            WHERE s.project_id = @pid AND p.item_type = 'PRODUCT'
            GROUP BY p.id, p.description",
            new { pid = projectId }).ToList();

        // Legge le categorie linkate esistenti
        var linked = c.Query<(int Id, string LinkedSource)>(@"
            SELECT id, linked_source FROM project_cashflow_categories
            WHERE project_id=@pid AND linked_source IS NOT NULL AND linked_source <> ''",
            new { pid = projectId }).ToList();

        var linkedBySource = linked.ToDictionary(x => x.LinkedSource, x => x.Id);
        var robotSources = new HashSet<string>();

        int maxSort = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order),0) FROM project_cashflow_categories WHERE project_id=@pid",
            new { pid = projectId });

        foreach (var robot in robots)
        {
            string source = $"MAT_PRODUCT:{robot.Id}";
            robotSources.Add(source);

            if (linkedBySource.TryGetValue(source, out int catId))
            {
                // Aggiorna nome e totale
                c.Execute(@"UPDATE project_cashflow_categories
                            SET name=@name, total_amount=@amt
                            WHERE id=@id",
                    new { name = robot.Description, amt = robot.NetTotal, id = catId });
            }
            else
            {
                // Crea nuova categoria linkata
                maxSort++;
                c.Execute(@"INSERT INTO project_cashflow_categories
                                (project_id, name, total_amount, notes, sort_order, linked_source)
                            VALUES (@pid, @name, @amt, '', @sort, @src)",
                    new { pid = projectId, name = robot.Description, amt = robot.NetTotal, sort = maxSort, src = source });
            }
        }

        // Elimina categorie di robot che non esistono più
        foreach (var (catId, source) in linked.Select(x => (x.Id, x.LinkedSource)))
        {
            if (!robotSources.Contains(source))
            {
                c.Execute("DELETE FROM project_cashflow_data WHERE project_id=@pid AND data_type='CAT_PCT' AND ref_id=@cid",
                    new { pid = projectId, cid = catId });
                c.Execute("DELETE FROM project_cashflow_categories WHERE id=@id",
                    new { id = catId });
            }
        }
    }

    [HttpPost("init")]
    public IActionResult Initialize(int projectId, [FromBody] CashFlowInitRequest req)
    {
        using var c = _db.Open();

        int exists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_cashflow WHERE project_id=@pid", new { pid = projectId });
        if (exists > 0) return BadRequest(ApiResponse<string>.Fail("Già inizializzato"));

        c.Execute(@"INSERT INTO project_cashflow (project_id, payment_amount, month_count) 
                    VALUES (@pid, @amt, @mc)",
            new { pid = projectId, amt = req.PaymentAmount, mc = req.MonthCount });

        string[] defaults = { "Robot", "Pinza Schunk", "Fornitore 3", "Materiale Meccanico",
                              "Materiale Elettrico", "Ingegneria", "Lavorazione Interna", "Lavorazione Esterna" };
        for (int i = 0; i < defaults.Length; i++)
            c.Execute(@"INSERT INTO project_cashflow_categories (project_id, name, total_amount, sort_order) 
                        VALUES (@pid, @name, 0, @sort)",
                new { pid = projectId, name = defaults[i], sort = i + 1 });

        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("header")]
    public IActionResult UpdateHeader(int projectId, [FromBody] CashFlowInitRequest req)
    {
        using var c = _db.Open();
        c.Execute("UPDATE project_cashflow SET payment_amount=@amt, month_count=@mc, start_date=@sd WHERE project_id=@pid",
            new { amt = req.PaymentAmount, mc = req.MonthCount, sd = req.StartDate, pid = projectId });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPut("data")]
    public IActionResult SaveData(int projectId, [FromBody] CashFlowDataSaveRequest req)
    {
        using var c = _db.Open();
        c.Execute(@"INSERT INTO project_cashflow_data 
                        (project_id, data_type, ref_id, month_number, num_value, date_value)
                    VALUES (@pid, @dt, @rid, @mn, @nv, @dv)
                    ON DUPLICATE KEY UPDATE num_value=@nv, date_value=@dv",
            new { pid = projectId, dt = req.DataType, rid = req.RefId, mn = req.MonthNumber, nv = req.NumValue, dv = req.DateValue });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpPost("categories")]
    public IActionResult AddCategory(int projectId, [FromBody] CashFlowCategorySaveRequest req)
    {
        using var c = _db.Open();
        int maxSort = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order),0) FROM project_cashflow_categories WHERE project_id=@pid",
            new { pid = projectId });
        int newId = c.ExecuteScalar<int>(@"
            INSERT INTO project_cashflow_categories (project_id, name, total_amount, notes, sort_order) 
            VALUES (@pid, @name, @amt, @notes, @sort); SELECT LAST_INSERT_ID()",
            new { pid = projectId, name = req.Name, amt = req.TotalAmount, notes = req.Notes, sort = maxSort + 1 });
        return Ok(ApiResponse<int>.Ok(newId));
    }

    [HttpPut("categories/{catId}")]
    public IActionResult UpdateCategory(int projectId, int catId, [FromBody] CashFlowCategorySaveRequest req)
    {
        using var c = _db.Open();
        c.Execute(@"UPDATE project_cashflow_categories SET name=@name, total_amount=@amt, notes=@notes 
                    WHERE id=@id AND project_id=@pid",
            new { name = req.Name, amt = req.TotalAmount, notes = req.Notes, id = catId, pid = projectId });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    [HttpDelete("categories/{catId}")]
    public IActionResult DeleteCategory(int projectId, int catId)
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM project_cashflow_data WHERE project_id=@pid AND data_type='CAT_PCT' AND ref_id=@cid",
            new { pid = projectId, cid = catId });
        c.Execute("DELETE FROM project_cashflow_categories WHERE id=@id AND project_id=@pid",
            new { id = catId, pid = projectId });
        return Ok(ApiResponse<bool>.Ok(true));
    }
}
