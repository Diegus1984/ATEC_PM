using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Pagina «Bilancio Commessa» cross-commessa: una riga per commessa con i due KPI di
/// redditività a consuntivo. Endpoint di RIEPILOGO, volutamente leggero — il conto
/// economico completo resta quello della singola commessa
/// (<c>GET /api/projects/{id}/budget-vs-actual</c>), che la card carica solo se aperta.
///
/// I numeri qui devono coincidere con quelli del tab «Preventivo vs Consuntivo»: le
/// sottoquery replicano riga per riga le stesse regole (stessa vista timesheet, stessa
/// esclusione degli stati A9, stesso dedup dei padri officina).
/// </summary>
[ApiController]
[Route("api/bilancio")]
[Authorize]
[RequireFeature("nav.bilancio")]
public class BilancioController : ControllerBase
{
    private readonly DbService _db;
    private readonly AnagraficheCache _cache;
    public BilancioController(DbService db, AnagraficheCache cache)
    {
        _db = db;
        _cache = cache;
    }

    private const string ThresholdKey = "bilancio_profit_threshold";
    private const decimal DefaultThresholdPct = 20m;

    /// <summary>
    /// Perimetro della pagina: fuori le ANNULLATE (lavoro annullato) e le BOZZE (commessa non
    /// avviata) — stessa esclusione decisa il 04/08/2026 per il Prospetto SAL. Le COMPLETATE
    /// entrano solo con <c>includeClosed</c>, per la convenzione «liste = solo commesse aperte».
    /// </summary>
    private const string OpenScope = "p.status IN ('ACTIVE','ON_HOLD')";
    private const string ClosedScope = "p.status IN ('ACTIVE','ON_HOLD','COMPLETED')";

    [HttpGet("summary")]
    public IActionResult GetSummary([FromQuery] bool includeClosed = false)
    {
        try
        {
            using var c = _db.Open();

            string[] ddpExcluded = DdpAggregationSet.Load(c, "A9", _cache);
            string scope = includeClosed ? ClosedScope : OpenScope;

            // Costo risorse: TUTTE le ore imputate (tranne l'Extra Lavoro). Fino al 09/08/2026
            // qui c'era un LEFT JOIN su project_cost_sections che scartava le ore la cui
            // sezione non è fra quelle della commessa: le stesse ore che il Bilancio commessa
            // perdeva. Ora il Bilancio le mette nel gruppo «NON ASSEGNATO» e qui si somma e
            // basta — un'ora lavorata è un costo anche se la sezione manca; il filtro dei
            // costi è UNO solo, l'Extra Lavoro (#39).
            var rows = c.Query<BilancioSummaryDto>($@"
                SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title,
                       COALESCE(cust.company_name, '') AS CustomerName,
                       COALESCE(CONCAT(pm.first_name,' ',pm.last_name), '') AS PmName,
                       p.status AS Status,
                       COALESCE(p.revenue, 0) AS OrderTotal,
                       COALESCE((
                           SELECT SUM(vt.hours * vt.hourly_cost)
                           FROM v_timesheet_with_section vt
                           WHERE vt.project_id = p.id
                             -- Extra Lavoro (#39): stesso filtro del Bilancio commessa, o le
                             -- due pagine direbbero due costi diversi sulla stessa commessa.
                             AND vt.counts_in_project = 1
                       ), 0) AS ActualResourceCost,
                       COALESCE((
                           -- #135: la quota dei grezzi, come in ProjectEconomics — questa
                           -- pagina ricopia la somma invece di chiamarla, e senza la quota
                           -- direbbe un materiale diverso dal conto economico.
                           SELECT SUM(b.quantity * b.unit_cost * COALESCE(b.raw_internal_share, 1))
                           FROM bom_items b
                           WHERE b.project_id = p.id
                             AND COALESCE(b.item_status,'') NOT IN @Excluded
                             AND b.{ProjectEconomics.CommercialeParentDedup}
                       ), 0) AS ActualMaterialCost,
                       COALESCE((
                           SELECT SUM(o.quantity * o.unit_cost)
                           FROM ddp_officina_items o
                           WHERE o.project_id = p.id
                             AND COALESCE(o.item_status,'') NOT IN @Excluded
                             AND o.{ProjectEconomics.OfficinaParentDedup}
                       ), 0) AS ActualWorkshopCost,
                       {TravelPlanService.ActualTravelCostSql} AS ActualTravelCost
                FROM projects p
                LEFT JOIN customers cust ON cust.id = p.customer_id
                LEFT JOIN employees pm ON pm.id = p.pm_id
                WHERE {scope}
                -- #97: con includeClosed le COMPLETATE vanno in coda, dopo le aperte.
                ORDER BY {ProjectSorting.OrderBy("p", "p.status")}",
                new { Excluded = ddpExcluded }).ToList();

            foreach (BilancioSummaryDto row in rows)
            {
                row.ActualTotalCost = row.ActualResourceCost + row.ActualMaterialCost
                                    + row.ActualWorkshopCost + row.ActualTravelCost;

                // Redditività = Totale Ordine − Totale Costi Consuntivati. Senza ordine non è
                // «zero», è «non calcolabile»: la card mostra «—» e non finisce fra le rosse.
                if (row.OrderTotal > 0)
                {
                    row.Profitability = row.OrderTotal - row.ActualTotalCost;
                    row.ProfitabilityPct = Math.Round(
                        row.Profitability.Value / row.OrderTotal * 100, 2);
                }
            }

            return Ok(ApiResponse<BilancioSummaryResponse>.Ok(new BilancioSummaryResponse
            {
                ThresholdPct = ReadThreshold(c),
                Projects = rows,
            }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<BilancioSummaryResponse>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        try
        {
            using var c = _db.Open();
            return Ok(ApiResponse<BilancioSettingsDto>.Ok(
                new BilancioSettingsDto { ThresholdPct = ReadThreshold(c) }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<BilancioSettingsDto>.Fail($"Errore: {ex.Message}"));
        }
    }

    // La soglia la scrive solo chi ha `action.edit_bilancio_settings`. Il
    // [RequireFeature("nav.bilancio")] di CLASSE resta e si somma in AND: due attributi
    // distinti sono due filtri, quindi serve vedere la pagina E poter cambiare la soglia.
    // La GET resta com'è: la soglia la deve leggere chiunque veda il Bilancio.
    [ScritturaNonDiCommessa("Soglia di redditivita: impostazione globale del Bilancio")]
    [HttpPut("settings")]
    [RequireFeature("action.edit_bilancio_settings")]
    public IActionResult SaveSettings([FromBody] BilancioSettingsDto dto)
    {
        try
        {
            if (dto.ThresholdPct < 0 || dto.ThresholdPct > 100)
                return Ok(ApiResponse<bool>.Fail("La soglia deve essere compresa fra 0 e 100."));

            using var c = _db.Open();
            c.Execute(
                "INSERT INTO res_settings (`key`, `value`) VALUES (@K, @V) ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
                new { K = ThresholdKey, V = dto.ThresholdPct.ToString(CultureInfo.InvariantCulture) });

            return Ok(ApiResponse<bool>.Ok(true, "Soglia di redditività salvata"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Soglia sotto la quale la card diventa rossa. Sta in res_settings (stesso store di
    /// email.* e digest_*) e NON in app_config, che è ADMIN-only anche in lettura: qui la
    /// soglia deve poterla leggere chiunque veda la pagina. Default cablato = nessun seed
    /// da migrare.
    /// </summary>
    private static decimal ReadThreshold(System.Data.IDbConnection c)
    {
        string? raw = c.ExecuteScalar<string?>(
            "SELECT `value` FROM res_settings WHERE `key` = @K", new { K = ThresholdKey });

        return decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal parsed)
            ? parsed
            : DefaultThresholdPct;
    }
}
