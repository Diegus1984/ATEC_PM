using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/cost-sections")]
[Authorize]
public class CostSectionsController : ControllerBase
{
    private readonly DbService _db;
    public CostSectionsController(DbService db) => _db = db;

    // ══════════════════════════════════════════════════════════════
    // GRUPPI
    // ══════════════════════════════════════════════════════════════

    [HttpGet("groups")]
    public IActionResult GetGroups()
    {
        using var c = _db.Open();
        var rows = c.Query<CostSectionGroupDto>(
            "SELECT id, name, sort_order AS SortOrder, is_active AS IsActive FROM cost_section_groups ORDER BY sort_order").ToList();
        return Ok(ApiResponse<List<CostSectionGroupDto>>.Ok(rows));
    }

    [HttpPost("groups")]
    public IActionResult CreateGroup([FromBody] CostSectionGroupSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(
            @"INSERT INTO cost_section_groups (name, sort_order, is_active) VALUES (@Name, @SortOrder, @IsActive);
              SELECT LAST_INSERT_ID();", req);
        return Ok(ApiResponse<int>.Ok(id, "Gruppo creato"));
    }

    [HttpPut("groups/{id}")]
    public IActionResult UpdateGroup(int id, [FromBody] CostSectionGroupSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();
        int rows = c.Execute(
            "UPDATE cost_section_groups SET name=@Name, sort_order=@SortOrder, is_active=@IsActive WHERE id=@id",
            new { req.Name, req.SortOrder, req.IsActive, id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Gruppo non trovato"));
        return Ok(ApiResponse<string>.Ok("", "Gruppo aggiornato"));
    }

    [HttpPatch("groups/{id}/field")]
    public IActionResult UpdateGroupField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "sort_order", "is_active" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE cost_section_groups SET {req.Field}=@Value WHERE id=@id", new { Value = req.Value, id });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("groups/{id}")]
    public IActionResult DeleteGroup(int id)
    {
        using var c = _db.Open();
        int used = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM cost_section_templates WHERE group_id=@id", new { id });
        if (used > 0)
            return BadRequest(ApiResponse<string>.Fail(
                $"Impossibile eliminare: {used} sezioni template usano questo gruppo."));

        int rows = c.Execute("DELETE FROM cost_section_groups WHERE id=@id", new { id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Gruppo non trovato"));
        return Ok(ApiResponse<string>.Ok("", "Gruppo eliminato"));
    }

    // ══════════════════════════════════════════════════════════════
    // TEMPLATE SEZIONI
    // ══════════════════════════════════════════════════════════════

    [HttpGet("templates")]
    public IActionResult GetTemplates()
    {
        using var c = _db.Open();
        var rows = c.Query<CostSectionTemplateDto>(
            @"SELECT t.id, t.name, t.section_type AS SectionType, t.group_id AS GroupId,
                     g.name AS GroupName, t.is_default AS IsDefault, t.sort_order AS SortOrder, t.is_active AS IsActive
              FROM cost_section_templates t
              JOIN cost_section_groups g ON g.id = t.group_id
              ORDER BY t.sort_order").ToList();

        // Carica reparti per ogni template
        var allDepts = c.Query<(int SectionTemplateId, int DepartmentId, string DepartmentCode)>(
            @"SELECT sd.section_template_id AS SectionTemplateId, sd.department_id AS DepartmentId, d.code AS DepartmentCode
              FROM cost_section_template_departments sd
              JOIN departments d ON d.id = sd.department_id").ToList();

        var deptsByTemplate = allDepts.ToLookup(d => d.SectionTemplateId);
        foreach (var tmpl in rows)
        {
            var depts = deptsByTemplate[tmpl.Id].ToList();
            tmpl.DepartmentIds = depts.Select(d => d.DepartmentId).ToList();
            tmpl.DepartmentCodes = depts.Select(d => d.DepartmentCode).ToList();
        }

        return Ok(ApiResponse<List<CostSectionTemplateDto>>.Ok(rows));
    }

    [HttpPost("templates")]
    public IActionResult CreateTemplate([FromBody] CostSectionTemplateSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));
        if (req.GroupId <= 0)
            return BadRequest(ApiResponse<string>.Fail("Gruppo obbligatorio"));

        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        int id = (int)c.ExecuteScalar<long>(
            @"INSERT INTO cost_section_templates (name, section_type, group_id, is_default, sort_order, is_active)
              VALUES (@Name, @SectionType, @GroupId, @IsDefault, @SortOrder, @IsActive);
              SELECT LAST_INSERT_ID();", req, tx);

        // Salva reparti associati
        foreach (int deptId in req.DepartmentIds)
        {
            c.Execute("INSERT INTO cost_section_template_departments (section_template_id, department_id) VALUES (@id, @deptId)",
                new { id, deptId }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<int>.Ok(id, "Sezione template creata"));
    }

    [HttpPut("templates/{id}")]
    public IActionResult UpdateTemplate(int id, [FromBody] CostSectionTemplateSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        int rows = c.Execute(
            @"UPDATE cost_section_templates SET name=@Name, section_type=@SectionType,
              group_id=@GroupId, is_default=@IsDefault, sort_order=@SortOrder, is_active=@IsActive
              WHERE id=@id",
            new { req.Name, req.SectionType, req.GroupId, req.IsDefault, req.SortOrder, req.IsActive, id }, tx);

        if (rows == 0)
        {
            tx.Rollback();
            return NotFound(ApiResponse<string>.Fail("Template non trovato"));
        }

        // Aggiorna reparti: delete + re-insert
        c.Execute("DELETE FROM cost_section_template_departments WHERE section_template_id=@id", new { id }, tx);
        foreach (int deptId in req.DepartmentIds)
        {
            c.Execute("INSERT INTO cost_section_template_departments (section_template_id, department_id) VALUES (@id, @deptId)",
                new { id, deptId }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<string>.Ok("", "Template aggiornato"));
    }

    // Endpoint dedicato per aggiornare solo i reparti di una sezione
    [HttpPut("templates/{id}/departments")]
    public IActionResult UpdateTemplateDepartments(int id, [FromBody] SectionDepartmentsRequest req)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM cost_section_template_departments WHERE section_template_id=@id", new { id }, tx);
        foreach (int deptId in req.DepartmentIds)
        {
            c.Execute("INSERT INTO cost_section_template_departments (section_template_id, department_id) VALUES (@id, @deptId)",
                new { id, deptId }, tx);
        }

        tx.Commit();
        return Ok(ApiResponse<string>.Ok("", "Reparti aggiornati"));
    }

    [HttpPatch("templates/{id}/field")]
    public IActionResult UpdateTemplateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "section_type", "group_id", "is_default", "sort_order", "is_active" };
        if (!allowed.Contains(req.Field))
            return BadRequest(ApiResponse<string>.Fail($"Campo '{req.Field}' non consentito"));

        using var c = _db.Open();
        c.Execute($"UPDATE cost_section_templates SET {req.Field}=@Value WHERE id=@id", new { Value = req.Value, id });
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [HttpDelete("templates/{id}")]
    public IActionResult DeleteTemplate(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM cost_section_templates WHERE id=@id", new { id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Template non trovato"));
        return Ok(ApiResponse<string>.Ok("", "Template eliminato"));
    }
}
