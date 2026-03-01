using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/projects")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly DbService _db;
    public ProjectsController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<ProjectListItem>(@"
            SELECT p.id, p.code, p.title, cu.company_name AS CustomerName,
                   CONCAT(e.first_name,' ',e.last_name) AS PmName,
                   p.status, p.priority, p.start_date AS StartDate, p.end_date_planned AS EndDatePlanned,
                   p.revenue, p.budget_hours_total AS BudgetHoursTotal
            FROM projects p
            JOIN customers cu ON cu.id = p.customer_id
            JOIN employees e ON e.id = p.pm_id
            ORDER BY p.created_at DESC").ToList();
        return Ok(ApiResponse<List<ProjectListItem>>.Ok(rows));
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var proj = c.QueryFirstOrDefault<ProjectSaveRequest>(@"
            SELECT id, code, title, customer_id AS CustomerId, pm_id AS PmId, description,
                   start_date AS StartDate, end_date_planned AS EndDatePlanned,
                   budget_total AS BudgetTotal, budget_hours_total AS BudgetHoursTotal,
                   revenue, status, priority, server_path AS ServerPath, notes
            FROM projects WHERE id=@Id", new { Id = id });
        if (proj == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<ProjectSaveRequest>.Ok(proj));
    }

    [HttpPost]
    public IActionResult Create([FromBody] ProjectSaveRequest req)
    {
        using var c = _db.Open();
        using var trx = c.BeginTransaction();
        try
        {
            var newId = c.ExecuteScalar<int>(@"
                INSERT INTO projects (code,title,customer_id,pm_id,description,start_date,end_date_planned,budget_total,budget_hours_total,revenue,status,priority,server_path,notes)
                VALUES (@Code,@Title,@CustomerId,@PmId,@Description,@StartDate,@EndDatePlanned,@BudgetTotal,@BudgetHoursTotal,@Revenue,@Status,@Priority,@ServerPath,@Notes);
                SELECT LAST_INSERT_ID()", req, trx);

            // Crea fasi di default
            if (req.CreateDefaultPhases)
            {
                var templates = c.Query("SELECT id, name, sort_order FROM phase_templates WHERE is_default=1 ORDER BY sort_order", transaction: trx);
                foreach (var t in templates)
                {
                    c.Execute("INSERT INTO project_phases (project_id,phase_template_id,custom_name,sort_order) VALUES (@ProjId,@TplId,@Name,@Sort)",
                        new { ProjId = newId, TplId = (int)t.id, Name = (string)t.name, Sort = (int)t.sort_order }, trx);
                }
            }

            trx.Commit();
            return Ok(ApiResponse<int>.Ok(newId, "Creato"));
        }
        catch
        {
            trx.Rollback();
            throw;
        }
    }

    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] ProjectSaveRequest req)
    {
        using var c = _db.Open();
        req.Id = id;
        c.Execute(@"UPDATE projects SET code=@Code,title=@Title,customer_id=@CustomerId,pm_id=@PmId,
            description=@Description,start_date=@StartDate,end_date_planned=@EndDatePlanned,
            budget_total=@BudgetTotal,budget_hours_total=@BudgetHoursTotal,revenue=@Revenue,
            status=@Status,priority=@Priority,server_path=@ServerPath,notes=@Notes WHERE id=@Id", req);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();
        c.Execute("UPDATE projects SET status='CANCELLED' WHERE id=@Id", new { Id = id });
        return Ok(ApiResponse<bool>.Ok(true));
    }

    // --- FASI ---
    [HttpGet("{id}/phases")]
    public IActionResult GetPhases(int id)
    {
        using var c = _db.Open();
        var rows = c.Query<PhaseListItem>(@"
            SELECT pp.id, pp.custom_name AS Name, pp.budget_hours AS BudgetHours,
                   pp.budget_cost AS BudgetCost, pp.status, pp.progress_pct AS ProgressPct, pp.sort_order AS SortOrder,
                   COALESCE(SUM(te.hours),0) AS HoursWorked
            FROM project_phases pp
            LEFT JOIN timesheet_entries te ON te.project_phase_id = pp.id
            WHERE pp.project_id = @Id
            GROUP BY pp.id
            ORDER BY pp.sort_order", new { Id = id }).ToList();
        return Ok(ApiResponse<List<PhaseListItem>>.Ok(rows));
    }

    // --- CODICE AUTO ---
    [HttpGet("next-code")]
    public IActionResult NextCode()
    {
        using var c = _db.Open();
        var year = DateTime.Now.Year;
        var prefix = $"AT{year}";
        var maxCode = c.ExecuteScalar<string?>($"SELECT MAX(code) FROM projects WHERE code LIKE '{prefix}%'");
        int next = 1;
        if (maxCode != null && maxCode.Length >= 9 && int.TryParse(maxCode.Substring(6), out var n))
            next = n + 1;
        return Ok(ApiResponse<string>.Ok($"{prefix}{next:D3}"));
    }

    // --- FILE SYSTEM ---
    [HttpPost("{id}/create-folder")]
    public IActionResult CreateFolder(int id)
    {
        using var c = _db.Open();
        var proj = c.QueryFirstOrDefault<dynamic>("SELECT code, server_path FROM projects WHERE id=@Id", new { Id = id });
        if (proj == null) return NotFound();

        string basePath = "C:\\ATEC_Commesse";
        string year = DateTime.Now.Year.ToString();
        string code = (string)proj.code;
        string fullPath = Path.Combine(basePath, year, code);

        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
            // Sottocartelle standard
            Directory.CreateDirectory(Path.Combine(fullPath, "01_Offerta"));
            Directory.CreateDirectory(Path.Combine(fullPath, "02_Progettazione"));
            Directory.CreateDirectory(Path.Combine(fullPath, "03_Software"));
            Directory.CreateDirectory(Path.Combine(fullPath, "04_Acquisti"));
            Directory.CreateDirectory(Path.Combine(fullPath, "05_Produzione"));
            Directory.CreateDirectory(Path.Combine(fullPath, "06_Installazione"));
            Directory.CreateDirectory(Path.Combine(fullPath, "07_Collaudo"));
            Directory.CreateDirectory(Path.Combine(fullPath, "08_Documentazione"));
        }

        // Aggiorna server_path nel DB
        c.Execute("UPDATE projects SET server_path=@Path WHERE id=@Id", new { Path = fullPath, Id = id });
        return Ok(ApiResponse<string>.Ok(fullPath));
    }

    [HttpGet("{id}/files")]
    public IActionResult GetFiles(int id, [FromQuery] string? subPath)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath)) return Ok(ApiResponse<List<FileItem>>.Ok(new()));

        var targetPath = string.IsNullOrEmpty(subPath) ? serverPath : Path.Combine(serverPath, subPath);
        if (!Directory.Exists(targetPath)) return Ok(ApiResponse<List<FileItem>>.Ok(new()));

        var items = new List<FileItem>();

        foreach (var dir in Directory.GetDirectories(targetPath).OrderBy(d => d))
        {
            var di = new DirectoryInfo(dir);
            items.Add(new FileItem { Name = di.Name, IsFolder = true, RelativePath = Path.GetRelativePath(serverPath, dir) });
        }
        foreach (var file in Directory.GetFiles(targetPath).OrderBy(f => f))
        {
            var fi = new FileInfo(file);
            items.Add(new FileItem { Name = fi.Name, IsFolder = false, Size = fi.Length, RelativePath = Path.GetRelativePath(serverPath, file), Modified = fi.LastWriteTime });
        }

        return Ok(ApiResponse<List<FileItem>>.Ok(items));
    }

    [HttpGet("{id}/file-tree")]
    public IActionResult GetFileTree(int id)
    {
        using var c = _db.Open();
        var serverPath = c.ExecuteScalar<string?>("SELECT server_path FROM projects WHERE id=@Id", new { Id = id });
        if (string.IsNullOrEmpty(serverPath) || !Directory.Exists(serverPath))
            return Ok(ApiResponse<List<FileTreeItem>>.Ok(new()));

        var tree = BuildFileTree(serverPath, serverPath);
        return Ok(ApiResponse<List<FileTreeItem>>.Ok(tree));
    }

    private List<FileTreeItem> BuildFileTree(string rootPath, string currentPath)
    {
        var items = new List<FileTreeItem>();

        foreach (var dir in Directory.GetDirectories(currentPath).OrderBy(d => d))
        {
            var di = new DirectoryInfo(dir);
            var node = new FileTreeItem
            {
                Name = di.Name,
                IsFolder = true,
                RelativePath = Path.GetRelativePath(rootPath, dir),
                Children = BuildFileTree(rootPath, dir)
            };
            items.Add(node);
        }
        foreach (var file in Directory.GetFiles(currentPath).OrderBy(f => f))
        {
            var fi = new FileInfo(file);
            items.Add(new FileTreeItem
            {
                Name = fi.Name,
                IsFolder = false,
                Size = fi.Length,
                RelativePath = Path.GetRelativePath(rootPath, file),
                Modified = fi.LastWriteTime
            });
        }

        return items;
    }

    // --- LOOKUP ---
    [HttpGet("/api/lookup/customers")]
    public IActionResult LookupCustomers()
    {
        using var c = _db.Open();
        var rows = c.Query<LookupItem>("SELECT id AS Id, company_name AS Name FROM customers WHERE is_active=1 ORDER BY company_name").ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }

    [HttpGet("/api/lookup/employees")]
    public IActionResult LookupEmployees()
    {
        using var c = _db.Open();
        var rows = c.Query<LookupItem>("SELECT id AS Id, CONCAT(first_name,' ',last_name) AS Name FROM employees WHERE status='ACTIVE' ORDER BY last_name").ToList();
        return Ok(ApiResponse<List<LookupItem>>.Ok(rows));
    }
}
