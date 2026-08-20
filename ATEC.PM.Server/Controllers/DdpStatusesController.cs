using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Stati (causali) della distinta DDP: etichetta + colori editabili da Conf. DDP.
// La chiave (status_key) è il valore salvato sulle righe: si può CREARE (chiave auto-generata univoca)
// e MODIFICARE (etichetta/colori/ordine), ma la chiave di uno stato esistente NON cambia. Niente delete.
// Stati e matrice DDP: lettura libera (serve ai picker e alle griglie di ogni livello),
// scrittura riservata al livello della feature «nav.ddp_destinazioni».
[ApiController]
[Route("api/ddp-statuses")]
[Authorize]
public class DdpStatusesController : ControllerBase
{
    private readonly DbService _db;
    private readonly AnagraficheCache _cache;
    public DdpStatusesController(DbService db, AnagraficheCache cache)
    {
        _db = db;
        _cache = cache;
    }

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();
        var rows = c.Query<DdpStatusItem>(@"
            SELECT id, status_key AS StatusKey, label, color_bg AS ColorBg, color_fg AS ColorFg,
                   sort_order AS SortOrder, is_active AS IsActive
            FROM ddp_statuses
            ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<DdpStatusItem>>.Ok(rows));
    }

    [HttpGet("active")]
    public IActionResult GetActive()
    {
        using var c = _db.Open();
        var rows = c.Query<DdpStatusItem>(@"
            SELECT id, status_key AS StatusKey, label, color_bg AS ColorBg, color_fg AS ColorFg,
                   sort_order AS SortOrder, is_active AS IsActive
            FROM ddp_statuses
            WHERE is_active = TRUE
            ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<DdpStatusItem>>.Ok(rows));
    }

    [RequireFeature("nav.ddp_destinazioni")]
    [HttpPost]
    public IActionResult Create([FromBody] DdpStatusSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label))
            return Ok(ApiResponse<int>.Fail("Etichetta obbligatoria"));

        using var c = _db.Open();
        string key = GenerateUniqueKey(c, req.Label);
        int sort = req.SortOrder > 0
            ? req.SortOrder
            : c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order), 0) + 1 FROM ddp_statuses");

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO ddp_statuses (status_key, label, color_bg, color_fg, sort_order, is_active)
            VALUES (@Key, @Label, @ColorBg, @ColorFg, @Sort, @IsActive);
            SELECT LAST_INSERT_ID()",
            new { Key = key, req.Label, req.ColorBg, req.ColorFg, Sort = sort, req.IsActive });
        return Ok(ApiResponse<int>.Ok(id, $"Stato creato ({key})"));
    }

    // Genera una chiave stabile dall'etichetta (UPPER, senza accenti, [^A-Z0-9]→_), univoca su ddp_statuses.
    private static string GenerateUniqueKey(MySqlConnector.MySqlConnection c, string label)
    {
        string baseKey = Slugify(label);
        if (string.IsNullOrEmpty(baseKey)) baseKey = "STATO";

        string key = baseKey;
        int n = 1;
        while (c.ExecuteScalar<int>("SELECT COUNT(*) FROM ddp_statuses WHERE status_key = @Key", new { Key = key }) > 0)
            key = $"{baseKey}_{++n}";
        return key;
    }

    private static string Slugify(string text)
    {
        string normalized = (text ?? "").Trim().Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (char ch in normalized)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue; // scarta i segni diacritici (accenti)
            char up = char.ToUpperInvariant(ch);
            sb.Append((up >= 'A' && up <= 'Z') || (up >= '0' && up <= '9') ? up : '_');
        }
        // collassa underscore multipli e rifila quelli ai bordi
        string[] parts = sb.ToString().Split('_', System.StringSplitOptions.RemoveEmptyEntries);
        return string.Join("_", parts);
    }

    // ── Matrice degli avanzamenti di stato (v7, per tipo di distinta) ──
    // Riga = (tipo, stato corrente), ToKeys = stati selezionabili nella finestra opzioni.
    // FromKey 'INIZIO' = finestra di partenza delle righe senza stato.
    // Il sentinella to_key='' (terminale governato senza uscite) diventa ToKeys vuota.
    [HttpGet("transitions")]
    public IActionResult GetTransitions()
    {
        using var c = _db.Open();
        var pairs = c.Query<(string DdpType, string FromKey, string ToKey)>(
            "SELECT ddp_type, from_key, to_key FROM ddp_status_transitions ORDER BY ddp_type, from_key, to_key").ToList();
        var rows = pairs
            .GroupBy(p => (p.DdpType, p.FromKey))
            .Select(g => new DdpStatusTransitionItem
            {
                DdpType = g.Key.DdpType,
                FromKey = g.Key.FromKey,
                ToKeys = g.Select(p => p.ToKey).Where(k => !string.IsNullOrEmpty(k)).ToList(),
            })
            .ToList();
        return Ok(ApiResponse<List<DdpStatusTransitionItem>>.Ok(rows));
    }

    // Salvataggio integrale della matrice (editor in Conf. DDP): sostituisce tutte le righe
    // di entrambi i tipi. Una coppia (tipo, stato) con ToKeys vuota resta governata
    // (sentinella ''); una coppia NON presente nel payload esce dalla matrice → finestra completa.
    [RequireFeature("nav.ddp_destinazioni")]
    [HttpPut("transitions")]
    public IActionResult SaveTransitions([FromBody] List<DdpStatusTransitionItem> rows)
    {
        using var c = _db.Open();
        var validKeys = c.Query<string>("SELECT status_key FROM ddp_statuses")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            DdpTransitionService.TypeCommercial,
            DdpTransitionService.TypeOfficina,
        };

        using var tx = c.BeginTransaction();
        c.Execute("DELETE FROM ddp_status_transitions", transaction: tx);
        foreach (DdpStatusTransitionItem row in rows ?? new List<DdpStatusTransitionItem>())
        {
            string type = (row.DdpType ?? "").Trim().ToUpperInvariant();
            string from = (row.FromKey ?? "").Trim().ToUpperInvariant();
            bool validFrom = from == "INIZIO" || validKeys.Contains(from);
            if (!validTypes.Contains(type) || from.Length == 0 || !validFrom) continue;

            var tos = (row.ToKeys ?? new List<string>())
                .Select(k => (k ?? "").Trim().ToUpperInvariant())
                .Where(k => k.Length > 0 && k != from && validKeys.Contains(k))
                .Distinct()
                .ToList();

            if (tos.Count == 0)
            {
                c.Execute("INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key) VALUES (@Type, @From, '')",
                    new { Type = type, From = from }, tx);
                continue;
            }
            foreach (string to in tos)
                c.Execute("INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key) VALUES (@Type, @From, @To)",
                    new { Type = type, From = from, To = to }, tx);
        }
        tx.Commit();

        // DOPO il commit, mai prima (vedi AnagraficheCache): la matrice la legge anche il
        // controllo di transizione riga per riga, e una voce ripopolata a metà transazione
        // resterebbe sbagliata finché non si riavvia il server.
        _cache.Invalida(Anagrafica.TransizioniDdp);

        return Ok(ApiResponse<bool>.Ok(true, "Matrice transizioni salvata"));
    }

    [RequireFeature("nav.ddp_destinazioni")]
    [HttpPut("{id}")]
    public IActionResult Update(int id, [FromBody] DdpStatusSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label))
            return Ok(ApiResponse<int>.Fail("Etichetta obbligatoria"));

        using var c = _db.Open();
        int rows = c.Execute(@"
            UPDATE ddp_statuses
               SET label = @Label, color_bg = @ColorBg, color_fg = @ColorFg,
                   sort_order = @SortOrder, is_active = @IsActive
             WHERE id = @Id",
            new { req.Label, req.ColorBg, req.ColorFg, req.SortOrder, req.IsActive, Id = id });
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Stato non trovato"));
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }
}
