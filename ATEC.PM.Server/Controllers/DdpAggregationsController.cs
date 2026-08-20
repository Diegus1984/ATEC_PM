using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using MySqlConnector;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Aggregazioni di stato DDP (matrice Stati × Aggregazioni). Editabile: nome/descrizione + appartenenze.
// Code/Kind sono strutturali (non modificabili da UI). Niente create/delete (set A1-A8 fisso).
// Lettura libera, scrittura per livello: è il pattern già usato da DdpStatusesController e
// DdpDestinationsController, e qui era mancato.
//
// 🪤 Difetto trovato con la segnalazione #63 (Fase 1) e attivo in produzione: il gate stava
// sulla CLASSE con `nav.ddp_aggregazioni` (livello 2), ma questa GET non serve solo la pagina
// di configurazione — la chiama la sezione «DDP Commerciali» dentro la commessa per
// l'aggregazione A9 (stati esclusi dal totale e quantità bloccata). Per TECH e RESP_REPARTO
// tornava 403 e la logica A9 lavorava su un insieme VUOTO: totali e conteggi sbagliati, senza
// un errore a video. Registrare `project.ddp_commerciale` avrebbe «concesso» ufficialmente una
// sezione difettosa proprio a quel pubblico.
[ApiController]
[Route("api/ddp-aggregations")]
[Authorize]
public class DdpAggregationsController : ControllerBase
{
    private readonly DbService _db;
    private readonly AnagraficheCache _cache;
    public DdpAggregationsController(DbService db, AnagraficheCache cache)
    {
        _db = db;
        _cache = cache;
    }

    /// <summary>
    /// Elenco delle aggregazioni con i loro stati. Lettura libera: è una lookup, serve alle
    /// griglie DDP di commessa oltre che alla pagina di configurazione.
    /// </summary>
    [HttpGet]
    public IActionResult GetAll()
    {
        try
        {
            using var c = _db.Open();
            List<DdpAggregation> aggs = c.Query<DdpAggregation>(@"
                SELECT id, code, name, description, kind, sort_order AS SortOrder, is_active AS IsActive
                FROM ddp_aggregations
                ORDER BY sort_order, code").ToList();

            List<MemberRow> members = c.Query<MemberRow>(
                "SELECT aggregation_id AS AggregationId, status_key AS StatusKey FROM ddp_aggregation_states").ToList();

            Dictionary<int, DdpAggregation> byId = aggs.ToDictionary(a => a.Id);
            foreach (MemberRow m in members)
                if (byId.TryGetValue(m.AggregationId, out DdpAggregation? a))
                    a.StatusKeys.Add(m.StatusKey ?? "");

            return Ok(ApiResponse<List<DdpAggregation>>.Ok(aggs));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<DdpAggregation>>.Fail($"Errore: {ex.Message}")); }
    }

    private class MemberRow { public int AggregationId { get; set; } public string? StatusKey { get; set; } }

    /// <summary>Modifica di configurazione: resta riservata alla pagina Aggregazioni DDP.</summary>
    [RequireFeature("nav.ddp_aggregazioni")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] DdpAggregationSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return Ok(ApiResponse<int>.Fail("Nome obbligatorio"));
        try
        {
            using var c = _db.Open();
            using MySqlTransaction tx = c.BeginTransaction();
            try
            {
                int rows = c.Execute("UPDATE ddp_aggregations SET name=@Name, description=@Description WHERE id=@Id",
                    new { req.Name, req.Description, Id = id }, tx);
                if (rows == 0) { tx.Rollback(); return Ok(ApiResponse<int>.Fail("Aggregazione non trovata")); }

                c.Execute("DELETE FROM ddp_aggregation_states WHERE aggregation_id=@Id", new { Id = id }, tx);
                foreach (string key in req.StatusKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct())
                    c.Execute("INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A,@S)",
                        new { A = id, S = key }, tx);

                tx.Commit();

                // DOPO il commit, mai prima: invalidare dentro la transazione farebbe
                // rileggere a un'altra richiesta i valori di PRIMA (per lei il commit non
                // c'è ancora) e ripopolare la cache con quelli.
                _cache.Invalida(Anagrafica.AggregazioniDdp);

                return Ok(ApiResponse<int>.Ok(id, "Aggregazione aggiornata"));
            }
            catch { tx.Rollback(); throw; }
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }
}
