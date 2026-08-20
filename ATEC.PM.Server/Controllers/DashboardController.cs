using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;
    private readonly IHubContext<ProjectHub> _hub;
    private readonly ProjectWriteGuard _guard;

    public DashboardController(
        DbService db, FeatureAccessService access, IHubContext<ProjectHub> hub, ProjectWriteGuard guard)
    {
        _db = db;
        _access = access;
        _hub = hub;
        _guard = guard;
    }

    private const string MaxCardsKey = "dashboard_max_cards";
    private const int DefaultMaxCards = 10;

    /// <summary>
    /// Perimetro della dashboard a cartelle: le commesse APERTE, con la stessa regola di
    /// <c>GET /api/projects</c> (fuori COMPLETED e CANCELLED — l'eliminazione è un soft
    /// delete verso CANCELLED). Le BOZZE restano dentro: una commessa appena creata deve
    /// trovarsi subito in dashboard, altrimenti il flag «In dashboard» non avrebbe niente
    /// su cui agire il giorno in cui serve.
    /// </summary>
    private const string OpenScope = "p.status NOT IN ('COMPLETED','CANCELLED')";
    private const string ClosedScope = "p.status <> 'CANCELLED'";

    // #88: le 4 card e il grafico ore della vecchia «Panoramica» non esistono più
    // (GET /api/dashboard, DashboardData, SqlOreMese/SqlOreSettimana): la pagina d'ingresso
    // sono le due tabelle Commesse / Altre Attività, servite da GET /api/projects.

    // ══════════════════════════════════════════════════════════════
    // DASHBOARD A CARTELLE (blocco 7)
    // ══════════════════════════════════════════════════════════════

    /// <summary>
    /// Cartelle della pagina d'ingresso: una per commessa, con le tre statistiche già
    /// calcolate (milestone attive, avanzamento medio, periodo). Le stesse regole della
    /// scheda commessa — <c>avgAvanz</c> conta come 0 le milestone senza avanzamento e
    /// <c>periodo</c> prende il minimo e il massimo su ENTRAMBE le date — ma in SQL, per
    /// non dover caricare le milestone di venti commesse solo per mostrarne il riassunto.
    /// </summary>
    [HttpGet("folders")]
    public IActionResult GetFolders([FromQuery] bool includeClosed = false)
    {
        try
        {
            using var c = _db.Open();
            string scope = includeClosed ? ClosedScope : OpenScope;

            var rows = c.Query<DashboardFolderDto>($@"
                SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title,
                       COALESCE(cu.company_name, '') AS CustomerName,
                       COALESCE(CONCAT(e.first_name,' ',e.last_name), '') AS PmName,
                       p.status AS Status,
                       p.in_dashboard AS InDashboard,
                       COALESCE(m.cnt, 0) AS MilestoneCount,
                       m.avg_progress AS AvgProgress,
                       m.period_start AS PeriodStart,
                       m.period_end AS PeriodEnd
                FROM projects p
                LEFT JOIN customers cu ON cu.id = p.customer_id
                LEFT JOIN employees e ON e.id = p.pm_id
                LEFT JOIN (
                    SELECT project_id,
                           COUNT(*) AS cnt,
                           CAST(ROUND(AVG(COALESCE(avanzamento, 0))) AS SIGNED) AS avg_progress,
                           COALESCE(LEAST(MIN(data_inizio), MIN(data_fine)),
                                    MIN(data_inizio), MIN(data_fine)) AS period_start,
                           COALESCE(GREATEST(MAX(data_inizio), MAX(data_fine)),
                                    MAX(data_inizio), MAX(data_fine)) AS period_end
                    FROM project_milestones
                    WHERE spento = 0
                    GROUP BY project_id
                ) m ON m.project_id = p.id
                WHERE {scope}{_guard.FiltroBozzeSql(User)}
                ORDER BY {ProjectSorting.OrderBy("p")}").ToList();

            return Ok(ApiResponse<DashboardFoldersResponse>.Ok(new DashboardFoldersResponse
            {
                MaxCards = ReadMaxCards(c),
                Projects = rows.Where(r => r.InDashboard).ToList(),
                Hidden = rows.Where(r => !r.InDashboard).ToList(),
            }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DashboardFoldersResponse>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Spunta «In dashboard» di una commessa. È una scelta CONDIVISA (colonna su
    /// <c>projects</c>): chi toglie una commessa la toglie a tutti, come nel prototipo.
    /// Per questo la scrittura vuole la chiave <c>action.toggle_dashboard_folder</c> — chi
    /// sfoltisce la propria vista la sfoltirebbe anche a chi quella commessa la deve seguire.
    /// La chiave sta SOLO sulla PUT: <c>GET folders</c> è la pagina d'ingresso di tutti.
    /// </summary>
    [RequireProjectWritable]
    [HttpPut("folders/{projectId}")]
    [RequireFeature("action.toggle_dashboard_folder")]
    public IActionResult SetFolderFlag(int projectId, [FromBody] DashboardFolderFlagRequest req)
    {
        try
        {
            using var c = _db.Open();
            string? code = c.ExecuteScalar<string?>(
                "SELECT code FROM projects WHERE id = @Id", new { Id = projectId });
            if (code == null)
                return Ok(ApiResponse<bool>.Fail("Commessa non trovata"));

            c.Execute("UPDATE projects SET in_dashboard = @Flag WHERE id = @Id",
                new { Flag = req.InDashboard ? 1 : 0, Id = projectId });

            // Ambiente condiviso: la dashboard aperta sugli altri PC si riallinea da sola.
            // Stesso evento dell'anagrafica commesse (`projects-all`), già ascoltato altrove.
            _ = _hub.Clients.Group(ProjectHub.ProjectsGroup)
                .SendAsync("ProjectsChanged", new ProjectChange
                {
                    ProjectId = projectId,
                    Action = "dashboard",
                    Code = code
                });

            return Ok(ApiResponse<bool>.Ok(true,
                req.InDashboard ? "Commessa in dashboard" : "Commessa tolta dalla dashboard"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        try
        {
            using var c = _db.Open();
            return Ok(ApiResponse<DashboardSettingsDto>.Ok(
                new DashboardSettingsDto { MaxCards = ReadMaxCards(c) }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DashboardSettingsDto>.Fail($"Errore: {ex.Message}"));
        }
    }

    [ScritturaNonDiCommessa("Impostazioni della Dashboard, valgono per tutti")]
    [HttpPut("settings")]
    [RequireFeature("action.edit_dashboard_settings")]
    public IActionResult SaveSettings([FromBody] DashboardSettingsDto dto)
    {
        try
        {
            if (dto.MaxCards < 1 || dto.MaxCards > 100)
                return Ok(ApiResponse<bool>.Fail("Il numero di cartelle deve essere compreso fra 1 e 100."));

            using var c = _db.Open();
            c.Execute(
                "INSERT INTO res_settings (`key`, `value`) VALUES (@K, @V) ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
                new { K = MaxCardsKey, V = dto.MaxCards.ToString(CultureInfo.InvariantCulture) });

            return Ok(ApiResponse<bool>.Ok(true, "Numero di cartelle salvato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Numero massimo di cartelle a video (DASH_MAX = 10 cablato nel prototipo). Sta in
    /// res_settings come la soglia del Bilancio: lo store che chiunque può LEGGERE e che
    /// scrive solo chi ha <c>action.edit_dashboard_settings</c>. Default cablato = nessun
    /// seed da migrare.
    /// </summary>
    private static int ReadMaxCards(System.Data.IDbConnection c)
    {
        string? raw = c.ExecuteScalar<string?>(
            "SELECT `value` FROM res_settings WHERE `key` = @K", new { K = MaxCardsKey });

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
               && parsed >= 1 && parsed <= 100
            ? parsed
            : DefaultMaxCards;
    }
}
