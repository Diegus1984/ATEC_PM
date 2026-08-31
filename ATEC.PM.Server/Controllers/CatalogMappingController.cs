using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Mapping Danea ↔ codice ATEC (piano Acquisti, 21/07/2026): associa gli articoli del
// catalogo (specchio Danea) al codice NUOVO Codex. Master del mapping = Extra1 in Danea:
// ogni scrittura va PRIMA sul Firebird di Danea (fail-fast) e solo a successo sullo
// specchio locale. Con DaneaSync:MappingMaster=false ("gestione interna") si scrive solo
// in locale. Regole: solo codici nuovi; 1 articolo = 1 codice ATEC; riassegnazione solo
// con Force (conferma esplicita del client); sgancio con conferma client.
[ApiController]
[Route("api/catalog-mapping")]
[Authorize]
// Le letture di questo controller portano COSTI e LISTINI (unit_cost, list_price di
// catalog_items): senza serratura erano di qualunque utente autenticato, timesheet
// compresi. Più chiavi = OR: bastano i profili che usano davvero questi dati — Catalogo,
// ricodifica/gestione Codex, DDP commerciale di commessa (picker/alternative), Inbox
// Acquisti e chi assegna i codici. Le scritture restano dietro la loro chiave dedicata
// (`action.assign_atec_code`, sugli endpoint), che qui compare perché i due filtri sono
// in AND: senza, chi ha SOLO la chiave di assegnazione non passerebbe la porta di classe.
[RequireFeature("nav.catalogo", "nav.acquisti_inbox", "action.assign_atec_code",
    "action.manage_codex", "action.recode_codex", "project.ddp_commerciale",
    // Il picker unico delle DDP risolve le alternative Danea anche dal lato officina.
    "project.ddp_officina")]
public class CatalogMappingController : ControllerBase
{
    private readonly DbService _db;
    private readonly DaneaMappingService _danea;
    private readonly IHubContext<ProjectHub> _hub;
    public CatalogMappingController(DbService db, DaneaMappingService danea, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _danea = danea;
        _hub = hub;
    }

    // Extra1 / atec_code valorizzati che NON corrispondono a nessun codice_nuovo Codex (refusi).
    [HttpGet("orphans")]
    public IActionResult GetOrphans(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? search = null)
    {
        (page, pageSize, int offset) = PagedQueryHelper.Normalize(page, pageSize);
        var clauses = new List<string>
        {
            "i.is_active = 1",
            "i.atec_code IS NOT NULL",
            "i.atec_code <> ''",
            @"NOT EXISTS (
                SELECT 1 FROM codex_items cx
                WHERE cx.codice_nuovo IS NOT NULL AND cx.codice_nuovo <> ''
                  AND cx.codice_nuovo = i.atec_code)",
        };
        var dp = new DynamicParameters();
        string? searchPat = PagedQueryHelper.ToLikePattern(search);
        if (searchPat != null)
        {
            clauses.Add(@"(i.code LIKE @Search OR i.description LIKE @Search
                OR s.company_name LIKE @Search OR i.atec_code LIKE @Search)");
            dp.Add("Search", searchPat);
        }
        string where = "WHERE " + string.Join(" AND ", clauses);
        string from = @"FROM catalog_items i LEFT JOIN suppliers s ON s.id = i.supplier_id";

        using var c = _db.Open();
        int total = c.ExecuteScalar<int>($"SELECT COUNT(*) {from} {where}", dp);
        dp.Add("Limit", pageSize);
        dp.Add("Offset", offset);
        var rows = c.Query<CatalogItemListItem>($@"
            SELECT i.id, i.code, i.description, i.category, i.unit,
                   i.unit_cost AS UnitCost, i.list_price AS ListPrice,
                   s.company_name AS SupplierName, i.manufacturer,
                   COALESCE(i.atec_code,'') AS AtecCode, i.codex_item_id AS CodexItemId,
                   i.easyfatt_id AS EasyfattId
            {from} {where}
            ORDER BY i.atec_code, i.code
            LIMIT @Limit OFFSET @Offset", dp).ToList();
        foreach (CatalogItemListItem row in rows)
            if (row.AtecCode.Length > 0)
                row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);

        int loaded = offset + rows.Count;
        return Ok(ApiResponse<PagedResult<CatalogItemListItem>>.Ok(new PagedResult<CatalogItemListItem>
        {
            Items = rows,
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            HasMore = loaded < total,
        }));
    }

    // Articoli Danea associati al codice nuovo della riga Codex (pannello dal Codex).
    [HttpGet("by-codex/{codexItemId}")]
    public IActionResult GetByCodex(int codexItemId)
    {
        using var c = _db.Open();
        var rows = c.Query<CatalogItemListItem>(@"
            SELECT i.id, i.code, i.description, i.category, i.unit,
                   i.unit_cost AS UnitCost, i.list_price AS ListPrice,
                   i.supplier_id AS SupplierId,
                   s.company_name AS SupplierName, i.manufacturer,
                   COALESCE(i.atec_code,'') AS AtecCode, i.codex_item_id AS CodexItemId,
                   i.easyfatt_id AS EasyfattId
            FROM catalog_items i
            LEFT JOIN suppliers s ON s.id = i.supplier_id
            WHERE i.is_active = 1 AND i.codex_item_id = @Id
            ORDER BY s.company_name, i.code", new { Id = codexItemId }).ToList();
        foreach (CatalogItemListItem row in rows)
            if (row.AtecCode.Length > 0)
                row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);
        return Ok(ApiResponse<List<CatalogItemListItem>>.Ok(rows));
    }

    // Alternative fornitore per un codice ATEC (stringa, con o senza punti).
    [HttpGet("by-atec/{atecCode}")]
    public IActionResult GetByAtec(string atecCode)
    {
        string normalized = (atecCode ?? "").Replace(".", "").Trim();
        if (normalized.Length == 0)
            return Ok(ApiResponse<List<CatalogItemListItem>>.Ok(new List<CatalogItemListItem>()));

        using var c = _db.Open();
        var rows = c.Query<CatalogItemListItem>(@"
            SELECT i.id, i.code, i.description, i.category, i.unit,
                   i.unit_cost AS UnitCost, i.list_price AS ListPrice,
                   i.supplier_id AS SupplierId,
                   s.company_name AS SupplierName, i.manufacturer,
                   COALESCE(i.atec_code,'') AS AtecCode, i.codex_item_id AS CodexItemId,
                   i.easyfatt_id AS EasyfattId
            FROM catalog_items i
            LEFT JOIN suppliers s ON s.id = i.supplier_id
            WHERE i.is_active = 1 AND i.atec_code = @Code
            ORDER BY s.company_name, i.code", new { Code = normalized }).ToList();
        foreach (CatalogItemListItem row in rows)
            if (row.AtecCode.Length > 0)
                row.AtecCode = CodexListItem.FormatCodice(row.AtecCode);
        return Ok(ApiResponse<List<CatalogItemListItem>>.Ok(rows));
    }

    [HttpPost("assign")]
    [Authorize]
    [RequireFeature("action.assign_atec_code")]
    public IActionResult Assign([FromBody] CatalogMappingAssignRequest req)
    {
        using var c = _db.Open();
        var (response, _) = AssignCore(c, req.CatalogItemId, req.CodexItemId, req.Force);
        return Ok(response);
    }

    // Associazione dalla Inbox Acquisti: parte dalla riga distinta, il server risolve
    // l'articolo Danea (link catalogo o match ESATTO sul codice — niente ricerche
    // paginate dal client) e a successo aggiorna snapshot e link della riga.
    [HttpPost("assign-from-bom")]
    [Authorize]
    [RequireFeature("action.assign_atec_code")]
    public IActionResult AssignFromBom([FromBody] CatalogMappingAssignFromBomRequest req)
    {
        using var c = _db.Open();
        var bom = c.QueryFirstOrDefault<(int Id, int ProjectId, int? CatalogItemId, string PartNumber, string DdpType)>(@"
            SELECT id, project_id, catalog_item_id, COALESCE(part_number,''), COALESCE(ddp_type,'')
            FROM bom_items WHERE id = @Id", new { Id = req.BomItemId });
        if (bom.Id == 0)
            return Ok(ApiResponse<CatalogMappingAssignResult>.Fail("Riga distinta non trovata."));
        if (bom.DdpType != "COMMERCIAL")
            return Ok(ApiResponse<CatalogMappingAssignResult>.Fail("La riga non è della distinta commerciale."));

        string partNumber = bom.PartNumber.Trim();
        int? catalogId = null;
        if (bom.CatalogItemId.HasValue)
            catalogId = c.ExecuteScalar<int?>(
                "SELECT id FROM catalog_items WHERE id = @Id", new { Id = bom.CatalogItemId.Value });
        if (!catalogId.HasValue && partNumber.Length > 0)
            catalogId = c.ExecuteScalar<int?>(
                "SELECT id FROM catalog_items WHERE is_active = 1 AND code = @Code ORDER BY id LIMIT 1",
                new { Code = partNumber });
        if (!catalogId.HasValue)
        {
            // #119: sulle righe nate da una composizione il «codice» è un codice Codex, non un
            // CodArticolo Danea — cercarlo a catalogo non lo troverà mai. Il messaggio storico
            // («sincronizzare Danea») manderebbe a caccia della cosa sbagliata: qui si dice
            // quella vera, cioè che l'articolo commerciale per quel pezzo non esiste ancora.
            bool eCodiceCodex = partNumber.Length > 0 && c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM codex_items WHERE REPLACE(codice,'.','') = @Code",
                new { Code = partNumber.Replace(".", "").Trim() }) > 0;
            return Ok(ApiResponse<CatalogMappingAssignResult>.Fail(
                eCodiceCodex
                    ? $"«{partNumber}» è un codice Codex, non un articolo Danea: questo pezzo non ha ancora un articolo commerciale. Va creato a catalogo e ricodificato (201/211/221), poi la riga si aggancia da sola."
                    : partNumber.Length > 0
                        ? $"Articolo «{partNumber}» non trovato nel catalogo Danea: sincronizzare Danea o correggere il codice."
                        : "Riga senza articolo Danea (né link catalogo né codice): impossibile associare."));
        }

        var (response, newCode) = AssignCore(c, catalogId.Value, req.CodexItemId, req.Force);
        if (response.Success && response.Data?.Assigned == true && newCode != null)
        {
            // Snapshot + link sulla riga di partenza; link anche alle righe commerciali con
            // lo stesso codice Danea rimaste senza articolo. Il COALESCE in lettura fa il
            // resto per tutte le commesse: nessun backfill riga per riga.
            c.Execute(@"UPDATE bom_items SET atec_code = @Code, catalog_item_id = COALESCE(catalog_item_id, @Cat)
                        WHERE id = @Id", new { Code = newCode, Cat = catalogId.Value, Id = bom.Id });
            if (partNumber.Length > 0)
                c.Execute(@"UPDATE bom_items SET catalog_item_id = @Cat
                            WHERE ddp_type = 'COMMERCIAL' AND catalog_item_id IS NULL AND part_number = @Code",
                    new { Cat = catalogId.Value, Code = partNumber });

            var payload = new DdpChange
            {
                ProjectId = bom.ProjectId,
                Action = "update",
                ItemId = bom.Id,
                DdpType = "COMMERCIAL",
            };
            _ = _hub.Clients.Group($"project-{bom.ProjectId}").SendAsync("DdpChanged", payload);
            _ = _hub.Clients.Group(ProjectHub.AllGroup).SendAsync("DdpChanged", payload);
        }
        return Ok(response);
    }

    /// <summary>
    /// Cuore dell'associazione articolo Danea → codice ATEC (Extra1 fail-fast, poi specchio
    /// locale). Ritorna anche il codice nuovo (non formattato) per gli usi a valle.
    /// </summary>
    private (ApiResponse<CatalogMappingAssignResult> Response, string? NewCode) AssignCore(
        System.Data.IDbConnection c, int catalogItemId, int codexItemId, bool force)
    {
        string? newCode = c.ExecuteScalar<string?>(
            "SELECT codice_nuovo FROM codex_items WHERE id = @Id", new { Id = codexItemId });
        if (newCode == null)
            return (ApiResponse<CatalogMappingAssignResult>.Fail("Riga Codex non trovata."), null);
        if (string.IsNullOrEmpty(newCode))
            return (ApiResponse<CatalogMappingAssignResult>.Fail(
                "La riga Codex non ha ancora il codice nuovo: in Extra1 vanno SOLO codici nuovi (ricodificala prima)."), null);

        var item = c.QueryFirstOrDefault<(string AtecCode, int? EasyfattId)>(@"
            SELECT COALESCE(atec_code,''), easyfatt_id FROM catalog_items WHERE id = @Id",
            new { Id = catalogItemId });
        if (item == default && c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM catalog_items WHERE id = @Id", new { Id = catalogItemId }) == 0)
            return (ApiResponse<CatalogMappingAssignResult>.Fail("Articolo di catalogo non trovato."), null);

        if (string.Equals(item.AtecCode, newCode, StringComparison.Ordinal))
            return (ApiResponse<CatalogMappingAssignResult>.Ok(
                new CatalogMappingAssignResult { Assigned = true }, "Già associato a questo codice"), newCode);

        // 1 articolo = 1 codice ATEC: la riassegnazione richiede la conferma esplicita (Force).
        if (item.AtecCode.Length > 0 && !force)
            return (ApiResponse<CatalogMappingAssignResult>.Ok(new CatalogMappingAssignResult
            {
                Assigned = false,
                RequiresForce = true,
                CurrentAtecCode = CodexListItem.FormatCodice(item.AtecCode),
            }), null);

        if (_danea.MappingMaster && !item.EasyfattId.HasValue)
            return (ApiResponse<CatalogMappingAssignResult>.Fail(
                "Articolo senza collegamento Easyfatt (easyfatt_id): eseguire una sincronizzazione Danea."), null);

        try
        {
            // Prima Danea (fail-fast), poi lo specchio locale.
            if (item.EasyfattId.HasValue)
                _danea.WriteExtra1(item.EasyfattId.Value, newCode);
        }
        catch (Exception ex)
        {
            return (ApiResponse<CatalogMappingAssignResult>.Fail(
                $"Scrittura su Danea non riuscita (nessuna modifica): {ex.Message}"), null);
        }

        c.Execute(@"UPDATE catalog_items SET atec_code = @Code, codex_item_id = @CodexId WHERE id = @Id",
            new { Code = newCode, CodexId = codexItemId, Id = catalogItemId });

        // #142: se il codice appena associato è il 201 di qualche grezzo, quelle righe erano
        // FERME («grezzo da associare», bordo lampeggiante): un DdpChanged per commessa e le
        // griglie aperte si sbloccano da sole, senza aspettare un ricarico.
        string chiaveGrezzo = c.ExecuteScalar<string?>(
            "SELECT REPLACE(codice, '.', '') FROM codex_items WHERE id = @Id",
            new { Id = codexItemId }) ?? "";
        if (chiaveGrezzo.Length > 0)
        {
            foreach (int projectId in c.Query<int>(
                "SELECT DISTINCT project_id FROM bom_items WHERE raw_codex_code = @Chiave",
                new { Chiave = chiaveGrezzo }))
            {
                var payload = new DdpChange
                    { ProjectId = projectId, Action = "update", ItemId = 0, DdpType = "COMMERCIAL" };
                foreach (string group in new[] { $"project-{projectId}", ProjectHub.AllGroup })
                    _ = _hub.Clients.Group(group).SendAsync("DdpChanged", payload);
            }
        }

        return (ApiResponse<CatalogMappingAssignResult>.Ok(
            new CatalogMappingAssignResult { Assigned = true },
            $"Associato a {CodexListItem.FormatCodice(newCode)}"), newCode);
    }

    [HttpPost("unassign")]
    [Authorize]
    [RequireFeature("action.assign_atec_code")]
    public IActionResult Unassign([FromBody] CatalogMappingUnassignRequest req)
    {
        using var c = _db.Open();
        var item = c.QueryFirstOrDefault<(string AtecCode, int? EasyfattId)>(@"
            SELECT COALESCE(atec_code,''), easyfatt_id FROM catalog_items WHERE id = @Id",
            new { Id = req.CatalogItemId });
        if (item.AtecCode.Length == 0)
            return Ok(ApiResponse<bool>.Ok(true, "Articolo già senza codice ATEC"));

        try
        {
            if (item.EasyfattId.HasValue)
                _danea.WriteExtra1(item.EasyfattId.Value, "");
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<bool>.Fail(
                $"Scrittura su Danea non riuscita (nessuna modifica): {ex.Message}"));
        }

        c.Execute("UPDATE catalog_items SET atec_code = NULL, codex_item_id = NULL WHERE id = @Id",
            new { Id = req.CatalogItemId });
        return Ok(ApiResponse<bool>.Ok(true, "Associazione rimossa"));
    }
}
