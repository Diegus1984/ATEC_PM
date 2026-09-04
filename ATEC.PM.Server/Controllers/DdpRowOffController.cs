using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// «Righe spente» della Sintesi DDP (piano V41 §4.1): righe escluse dalle stampe di Avanzamento
// e righe già gestite in Dati Mancanti. Sono un dato di commessa CONDIVISO, non una preferenza
// personale: quello che spegne un utente lo vedono spento anche gli altri.
// La presenza del record in ddp_row_off È lo stato spento: off=true → INSERT IGNORE,
// off=false → DELETE. Niente colonne nuove sulle righe di distinta.
[ApiController]
[Route("api/ddp-row-off")]
[Authorize]
// Sotto-funzione della Sintesi DDP: stessa chiave del Gestore DDP.
[RequireFeature("nav.gestore_ddp")]
// #88: anche qui si scrive sempre su UNA commessa (note, righe nascoste, righe spente:
// sono dati suoi, non preferenze personali), quindi vale lo stesso cancello delle altre
// scritture di commessa — in stand-by o chiusa si guarda e basta.
[RequireProjectWritable]
public class DdpRowOffController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public DdpRowOffController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    // Stesso segnale realtime della distinta: la Sintesi ricarica anche le righe spente
    // quando un collega ne accende o spegne una.
    private void NotifyDdpChange(int projectId, string ddpType)
    {
        var payload = new DdpChange { ProjectId = projectId, Action = "row-off", ItemId = 0, DdpType = ddpType };
        foreach (string group in new[] { $"project-{projectId}", ProjectHub.AllGroup })
            _hub.Clients.Group(group).SendAsync("DdpChanged", payload).SenzaAttesa("DdpChanged");
    }

    [HttpGet("{projectId:int}/{ddpType}")]
    public IActionResult Get(int projectId, string ddpType)
    {
        try
        {
            using var c = _db.Open();
            var items = c.Query<DdpRowOffItem>(@"
                SELECT section_key AS SectionKey, item_id AS ItemId
                FROM ddp_row_off
                WHERE project_id = @ProjectId AND ddp_type = @DdpType",
                new { ProjectId = projectId, DdpType = ddpType.ToUpperInvariant() }).ToList();

            return Ok(ApiResponse<List<DdpRowOffItem>>.Ok(items));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpRowOffItem>>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpPut("{projectId:int}/{ddpType}/{sectionKey}/{itemId:int}")]
    public IActionResult SetOne(int projectId, string ddpType, string sectionKey, int itemId, [FromBody] DdpRowOffSetRequest req)
    {
        try
        {
            using var c = _db.Open();
            string type = ddpType.ToUpperInvariant();
            if (req.Off)
            {
                c.Execute(@"
                    INSERT IGNORE INTO ddp_row_off (project_id, ddp_type, section_key, item_id)
                    VALUES (@ProjectId, @DdpType, @SectionKey, @ItemId)",
                    new { ProjectId = projectId, DdpType = type, SectionKey = sectionKey, ItemId = itemId });
            }
            else
            {
                c.Execute(@"
                    DELETE FROM ddp_row_off
                    WHERE project_id = @ProjectId AND ddp_type = @DdpType
                      AND section_key = @SectionKey AND item_id = @ItemId",
                    new { ProjectId = projectId, DdpType = type, SectionKey = sectionKey, ItemId = itemId });
            }

            NotifyDdpChange(projectId, type);
            return Ok(ApiResponse<bool>.Ok(true, "Salvato"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    // «Tutte spente» / «Tutte accese» su una sezione: una sola andata e ritorno invece di N PUT.
    [HttpPost("{projectId:int}/{ddpType}/{sectionKey}/bulk")]
    public IActionResult SetBulk(int projectId, string ddpType, string sectionKey, [FromBody] DdpRowOffBulkRequest req)
    {
        try
        {
            var itemIds = (req.ItemIds ?? new List<int>()).Distinct().ToList();
            if (itemIds.Count == 0)
                return Ok(ApiResponse<bool>.Ok(true, "Nessuna riga"));

            using var c = _db.Open();
            string type = ddpType.ToUpperInvariant();
            if (req.Off)
            {
                // Dapper espande la lista in tante VALUES: l'INSERT IGNORE assorbe le righe già spente.
                c.Execute(@"
                    INSERT IGNORE INTO ddp_row_off (project_id, ddp_type, section_key, item_id)
                    VALUES (@ProjectId, @DdpType, @SectionKey, @ItemId)",
                    itemIds.Select(id => new { ProjectId = projectId, DdpType = type, SectionKey = sectionKey, ItemId = id }));
            }
            else
            {
                c.Execute(@"
                    DELETE FROM ddp_row_off
                    WHERE project_id = @ProjectId AND ddp_type = @DdpType
                      AND section_key = @SectionKey AND item_id IN @ItemIds",
                    new { ProjectId = projectId, DdpType = type, SectionKey = sectionKey, ItemIds = itemIds });
            }

            NotifyDdpChange(projectId, type);
            return Ok(ApiResponse<bool>.Ok(true, req.Off ? "Righe spente" : "Righe riaccese"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }

    // Reset: sezione singola se SectionKey è valorizzata, altrimenti l'intera DDP.
    [HttpPost("{projectId:int}/{ddpType}/reset")]
    public IActionResult Reset(int projectId, string ddpType, [FromBody] DdpRowOffResetRequest? req)
    {
        try
        {
            using var c = _db.Open();
            string type = ddpType.ToUpperInvariant();
            // sectionKey: null (o assente) = riaccendi tutta la DDP.
            string? section = string.IsNullOrWhiteSpace(req?.SectionKey) ? null : req.SectionKey;

            c.Execute(@"
                DELETE FROM ddp_row_off
                WHERE project_id = @ProjectId AND ddp_type = @DdpType
                  AND (@SectionKey IS NULL OR section_key = @SectionKey)",
                new { ProjectId = projectId, DdpType = type, SectionKey = section });

            NotifyDdpChange(projectId, type);
            return Ok(ApiResponse<bool>.Ok(true, "Righe ripristinate"));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}"));
        }
    }
}
