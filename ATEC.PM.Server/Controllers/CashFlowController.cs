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
            "SELECT payment_amount, month_count FROM project_cashflow WHERE project_id=@pid",
            new { pid = projectId });

        var categories = c.Query<CashFlowCategoryDto>(@"
            SELECT id AS Id, name AS Name, total_amount AS TotalAmount, 
                   notes AS Notes, sort_order AS SortOrder
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

        return Ok(ApiResponse<CashFlowData>.Ok(new CashFlowData
        {
            ProjectId = projectId,
            ProjectCode = (string)(proj.code ?? ""),
            ProjectRevenue = (decimal)(proj.revenue ?? 0m),
            StartDate = proj.start_date as DateTime?,
            PaymentAmount = header != null ? (decimal)(header.payment_amount ?? 0m) : 0m,
            MonthCount = header != null ? (int)(header.month_count ?? 13) : 13,
            IsInitialized = header != null,
            Categories = categories,
            DataItems = dataItems
        }));
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
        c.Execute("UPDATE project_cashflow SET payment_amount=@amt, month_count=@mc WHERE project_id=@pid",
            new { amt = req.PaymentAmount, mc = req.MonthCount, pid = projectId });
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
