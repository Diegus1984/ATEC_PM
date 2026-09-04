using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Sezioni di costo: lettura libera (serve ai picker e alle griglie di ogni livello),
// scrittura riservata al livello della feature «nav.config_sezioni».
[ApiController]
[Route("api/cost-sections")]
[Authorize]
public class CostSectionsController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public CostSectionsController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    /// <summary>
    /// Avvisa chi ha aperto la Configurazione sezioni: gruppi, sezioni, reparti collegati.
    /// Fire-and-forget, niente self-exclusion — chi ha fatto la modifica ricarica comunque già,
    /// e un giro in più non si vede. Stesso evento usato da <c>PhasesController</c> per
    /// l'anagrafica delle fasi e da <c>DepartmentsController</c>/<c>TravelTariffsController</c>.
    /// </summary>
    private void NotifyCostSectionsChanged(string action) =>
        _hub.Clients.Group(ProjectHub.CostSectionsGroup)
            .SendAsync("CostSectionsChanged", new { action }).SenzaAttesa("CostSectionsChanged");

    // ══════════════════════════════════════════════════════════════
    // GRUPPI
    // ══════════════════════════════════════════════════════════════

    [HttpGet("groups")]
    public IActionResult GetGroups()
    {
        using var c = _db.Open();
        var rows = c.Query<CostSectionGroupDto>(
            "SELECT id, name, bg_color AS BgColor, text_color AS TextColor, sort_order AS SortOrder, is_active AS IsActive FROM cost_section_groups ORDER BY sort_order").ToList();
        return Ok(ApiResponse<List<CostSectionGroupDto>>.Ok(rows));
    }

    [RequireFeature("nav.config_sezioni")]
    [HttpPost("groups")]
    public IActionResult CreateGroup([FromBody] CostSectionGroupSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();
        int id = (int)c.ExecuteScalar<long>(
            @"INSERT INTO cost_section_groups (name, sort_order, is_active) VALUES (@Name, @SortOrder, @IsActive);
              SELECT LAST_INSERT_ID();", req);
        NotifyCostSectionsChanged("group-created");
        return Ok(ApiResponse<int>.Ok(id, "Gruppo creato"));
    }

    [RequireFeature("nav.config_sezioni")]
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
        NotifyCostSectionsChanged("group-updated");
        return Ok(ApiResponse<string>.Ok("", "Gruppo aggiornato"));
    }

    [RequireFeature("nav.config_sezioni")]
    [HttpPatch("groups/{id}/field")]
    public IActionResult UpdateGroupField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "bg_color", "text_color", "sort_order", "is_active" };
        string? error = _db.UpdateField("cost_section_groups", id, req.Field, req.Value, allowed);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        NotifyCostSectionsChanged("group-field");
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [RequireFeature("nav.config_sezioni")]
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
        NotifyCostSectionsChanged("group-deleted");
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
                     g.name AS GroupName, t.is_default_project AS IsDefault, t.is_default_quote AS IsDefaultQuote, t.sort_order AS SortOrder, t.is_active AS IsActive
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

    [RequireFeature("nav.config_sezioni")]
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
            @"INSERT INTO cost_section_templates (name, section_type, group_id, is_default_project, is_default_quote, sort_order, is_active)
              VALUES (@Name, @SectionType, @GroupId, @IsDefault, @IsDefaultQuote, @SortOrder, @IsActive);
              SELECT LAST_INSERT_ID();", req, tx);

        // Salva reparti associati
        foreach (int deptId in req.DepartmentIds)
        {
            c.Execute("INSERT INTO cost_section_template_departments (section_template_id, department_id) VALUES (@id, @deptId)",
                new { id, deptId }, tx);
        }

        tx.Commit();
        NotifyCostSectionsChanged("section-created");
        return Ok(ApiResponse<int>.Ok(id, "Sezione template creata"));
    }

    [RequireFeature("nav.config_sezioni")]
    [HttpPut("templates/{id}")]
    public IActionResult UpdateTemplate(int id, [FromBody] CostSectionTemplateSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(ApiResponse<string>.Fail("Nome obbligatorio"));

        using var c = _db.Open();
        using var tx = c.BeginTransaction();

        int rows = c.Execute(
            @"UPDATE cost_section_templates SET name=@Name, section_type=@SectionType,
              group_id=@GroupId, is_default_project=@IsDefault, is_default_quote=@IsDefaultQuote, sort_order=@SortOrder, is_active=@IsActive
              WHERE id=@id",
            new { req.Name, req.SectionType, req.GroupId, req.IsDefault, req.IsDefaultQuote, req.SortOrder, req.IsActive, id }, tx);

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
        NotifyCostSectionsChanged("section-updated");
        return Ok(ApiResponse<string>.Ok("", "Template aggiornato"));
    }

    // Endpoint dedicato per aggiornare solo i reparti di una sezione
    [RequireFeature("nav.config_sezioni")]
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
        NotifyCostSectionsChanged("section-departments");
        return Ok(ApiResponse<string>.Ok("", "Reparti aggiornati"));
    }

    [RequireFeature("nav.config_sezioni")]
    [HttpPatch("templates/{id}/field")]
    public IActionResult UpdateTemplateField(int id, [FromBody] FieldUpdateRequest req)
    {
        var allowed = new HashSet<string> { "name", "section_type", "group_id", "is_default_project", "is_default_quote", "sort_order", "is_active" };
        string? error = _db.UpdateField("cost_section_templates", id, req.Field, req.Value, allowed);
        if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        NotifyCostSectionsChanged("section-field");
        return Ok(ApiResponse<string>.Ok("", "Aggiornato"));
    }

    [RequireFeature("nav.config_sezioni")]
    [HttpDelete("templates/{id}")]
    public IActionResult DeleteTemplate(int id)
    {
        using var c = _db.Open();

        // Non cancellare se ancora referenziata: il FK SET NULL sulle sezioni di
        // progetto/preventivo creerebbe orfani e spezzerebbe il BVA.
        // Le fasi si contano sui LEGAMI (v73): una fase può stare su più sezioni, e togliere
        // questa sezione la lascerebbe viva altrove — ma qui interessa sapere che c'è.
        int phaseCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM phase_template_sections WHERE cost_section_template_id=@id",
            new { id });
        int projectCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_cost_sections WHERE template_id=@id",
            new { id });
        int quoteCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM quote_cost_sections WHERE template_id=@id",
            new { id });
        if (phaseCount + projectCount + quoteCount > 0)
        {
            var parts = new List<string>();
            if (phaseCount > 0) parts.Add($"{phaseCount} fas{(phaseCount == 1 ? "e" : "i")} anagrafica");
            if (projectCount > 0) parts.Add($"{projectCount} sezion{(projectCount == 1 ? "e" : "i")} di commessa");
            if (quoteCount > 0) parts.Add($"{quoteCount} sezion{(quoteCount == 1 ? "e" : "i")} di preventivo");
            return BadRequest(ApiResponse<string>.Fail(
                "Impossibile eliminare: usata da " + string.Join(", ", parts) +
                ". Scollega o disattiva la sezione invece di cancellarla."));
        }

        int rows = c.Execute("DELETE FROM cost_section_templates WHERE id=@id", new { id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Template non trovato"));
        NotifyCostSectionsChanged("section-deleted");
        return Ok(ApiResponse<string>.Ok("", "Template eliminato"));
    }
}
