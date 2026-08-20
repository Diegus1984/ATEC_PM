using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Hubs;
using System.Security.Claims;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/codex")]
[Authorize]
public class CodexController : ControllerBase
{
    private readonly DbService _db;
    private readonly CodexSyncService _sync;
    private readonly CodexGeneratorService _generator;
    private readonly IHubContext<CodexHub> _codexHub;

    public CodexController(DbService db, CodexSyncService sync, CodexGeneratorService generator,
        IHubContext<CodexHub> codexHub)
    {
        _db = db;
        _sync = sync;
        _generator = generator;
        _codexHub = codexHub;
    }

    // Notifica real-time ai client che guardano la Composizione (hub /hubs/codex),
    // escludendo chi ha fatto la modifica (conn) per non auto-ricaricarsi.
    private void NotifyCompositionChange(string? conn, int parentCodexId, string action, int compositionId)
    {
        var payload = new CompositionChange
        {
            ParentCodexId = parentCodexId,
            Action = action,
            CompositionId = compositionId
        };
        IClientProxy target = string.IsNullOrEmpty(conn)
            ? _codexHub.Clients.All
            : _codexHub.Clients.AllExcept(conn);
        _ = target.SendAsync("CompositionChanged", payload);
    }

    /// <summary>Colonne ordinabili (chiave client → colonna SQL).</summary>
    private static readonly Dictionary<string, string> CodexSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["codice"] = "codice",
        ["codiceNuovo"] = "codice_nuovo",
        ["descr"] = "descr",
        ["codeForn"] = "code_forn",
        ["fornitore"] = "fornitore",
        ["prezzoForn"] = "prezzo_forn",
        ["iva"] = "iva",
        ["produttore"] = "produttore",
        ["data"] = "data",
        ["categoria"] = "categoria",
        ["barcode"] = "barcode",
        ["tipologia"] = "tipologia",
        ["extra1"] = "extra1",
        ["extra2"] = "extra2",
        ["extra3"] = "extra3",
        ["codeProd"] = "code_prod",
        ["spec"] = "spec",
        ["oper"] = "oper",
        ["um"] = "um",
        ["ubicazione"] = "ubicazione",
        ["codexforn"] = "codexforn",
        ["note"] = "note",
    };

    [HttpGet]
    public IActionResult GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? codice = null,
        [FromQuery] string? descr = null,
        [FromQuery] string? codeForn = null,
        [FromQuery] string? fornitore = null,
        [FromQuery] string? produttore = null,
        [FromQuery] string? categoria = null,
        [FromQuery] string? barcode = null,
        [FromQuery] string? tipologia = null,
        [FromQuery] string? codeProd = null,
        [FromQuery] string? note = null,
        [FromQuery] string? prezzoForn = null,
        [FromQuery] string? iva = null,
        [FromQuery] string? data = null,
        [FromQuery] string? extra1 = null,
        [FromQuery] string? extra2 = null,
        [FromQuery] string? extra3 = null,
        [FromQuery] string? spec = null,
        [FromQuery] string? oper = null,
        [FromQuery] string? um = null,
        [FromQuery] string? ubicazione = null,
        [FromQuery] string? codexforn = null,
        [FromQuery] string? codiceNuovo = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] string? codicePrefixes = null,
        [FromQuery] string? newCodeState = null)
    {
        try
        {
            (page, pageSize, int offset) = PagedQueryHelper.Normalize(page, pageSize);
            var clauses = new List<string>();
            var dp = new Dapper.DynamicParameters();

            void AddLike(string column, string? filter, string param)
            {
                string? pat = PagedQueryHelper.ToLikePattern(filter);
                if (pat == null) return;
                clauses.Add($"{column} LIKE @{param}");
                dp.Add(param, pat);
            }

            // Onora le regole jolly (abc, abc*, *abc, *abc*) anche sulla ricerca globale,
            // come per i filtri per-colonna (stesso helper).
            string? searchPat = PagedQueryHelper.ToLikePattern(search);
            if (searchPat != null)
            {
                string searchCodicePat = searchPat.Replace(".", "");
                clauses.Add(@"(codice LIKE @SearchCodice OR codice_nuovo LIKE @SearchCodice
                    OR descr LIKE @Search OR code_forn LIKE @Search
                    OR fornitore LIKE @Search OR produttore LIKE @Search OR categoria LIKE @Search
                    OR barcode LIKE @Search OR note LIKE @Search OR code_prod LIKE @Search)");
                dp.Add("Search", searchPat);
                dp.Add("SearchCodice", searchCodicePat);
            }

            if (codice != null)
            {
                codice = codice.Replace(".", "");
            }
            if (codiceNuovo != null)
            {
                codiceNuovo = codiceNuovo.Replace(".", "");
            }
            AddLike("codice", codice, "Codice");
            AddLike("codice_nuovo", codiceNuovo, "CodiceNuovo");
            AddLike("descr", descr, "Descr");
            AddLike("code_forn", codeForn, "CodeForn");
            AddLike("fornitore", fornitore, "Fornitore");
            AddLike("produttore", produttore, "Produttore");
            AddLike("categoria", categoria, "Categoria");
            AddLike("barcode", barcode, "Barcode");
            AddLike("tipologia", tipologia, "Tipologia");
            AddLike("code_prod", codeProd, "CodeProd");
            AddLike("note", note, "Note");
            AddLike("CAST(prezzo_forn AS CHAR)", prezzoForn, "PrezzoForn");
            AddLike("iva", iva, "Iva");
            AddLike("CAST(data AS CHAR)", data, "Data");
            AddLike("extra1", extra1, "Extra1");
            AddLike("extra2", extra2, "Extra2");
            AddLike("extra3", extra3, "Extra3");
            AddLike("spec", spec, "Spec");
            AddLike("oper", oper, "Oper");
            AddLike("um", um, "Um");
            AddLike("ubicazione", ubicazione, "Ubicazione");
            AddLike("codexforn", codexforn, "Codexforn");

            // Stato ricodifica (pagina Ricodifica Codex): missing = senza codice nuovo, done = con.
            if (string.Equals(newCodeState, "missing", StringComparison.OrdinalIgnoreCase))
                clauses.Add("(codice_nuovo IS NULL OR codice_nuovo = '')");
            else if (string.Equals(newCodeState, "done", StringComparison.OrdinalIgnoreCase))
                clauses.Add("(codice_nuovo IS NOT NULL AND codice_nuovo <> '')");

            // Filtro per prefisso del codice (OR di più prefissi), usato dal picker
            // della composizione (es. 5xx → figli 1xx-4xx). Valori sanificati.
            if (!string.IsNullOrWhiteSpace(codicePrefixes))
            {
                var parts = codicePrefixes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(p => new string(p.Where(char.IsLetterOrDigit).ToArray()))
                    .Where(p => p.Length > 0)
                    .Distinct()
                    .ToList();
                if (parts.Count > 0)
                {
                    var ors = new List<string>();
                    for (int i = 0; i < parts.Count; i++)
                    {
                        string pn = $"Pref{i}";
                        ors.Add($"codice LIKE @{pn}");
                        dp.Add(pn, parts[i] + "%");
                    }
                    clauses.Add("(" + string.Join(" OR ", ors) + ")");
                }
            }

            string where = clauses.Count > 0 ? "WHERE " + string.Join(" AND ", clauses) : "";
            string orderBy = PagedQueryHelper.OrderBy(sortBy, sortDir, CodexSortColumns, "ORDER BY codice");
            using var c = _db.Open();
            int total = c.ExecuteScalar<int>($"SELECT COUNT(*) FROM codex_items {where}", dp);
            dp.Add("Limit", pageSize);
            dp.Add("Offset", offset);

            var rows = c.Query<CodexListItem>($@"
            SELECT id, codice AS Codice, COALESCE(codice_nuovo,'') AS CodiceNuovo,
                   code_forn AS CodeForn, fornitore AS Fornitore,
                   prezzo_forn AS PrezzoForn, iva AS Iva, produttore AS Produttore,
                   data AS Data, descr AS Descr, note AS Note, categoria AS Categoria,
                   barcode AS Barcode, tipologia AS Tipologia,
                   extra1 AS Extra1, extra2 AS Extra2, extra3 AS Extra3,
                   code_prod AS CodeProd, spec AS Spec, oper AS Oper, um AS Um,
                   ubicazione AS Ubicazione, codexforn AS Codexforn
            FROM codex_items {where}
            {orderBy}
            LIMIT @Limit OFFSET @Offset", dp).ToList();

            int loaded = offset + rows.Count;
            return Ok(ApiResponse<PagedResult<CodexListItem>>.Ok(new PagedResult<CodexListItem>
            {
                Items = rows,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                HasMore = loaded < total
            }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResult<CodexListItem>>.Fail(ex.Message));
        }
    }

    [HttpGet("{id}")]
    public IActionResult GetById(int id)
    {
        using var c = _db.Open();
        var item = c.QueryFirstOrDefault<CodexListItem>(@"
            SELECT id, codice AS Codice, COALESCE(codice_nuovo,'') AS CodiceNuovo,
                   code_forn AS CodeForn, fornitore AS Fornitore,
                   prezzo_forn AS PrezzoForn, iva AS Iva, produttore AS Produttore,
                   data AS Data, descr AS Descr, note AS Note, categoria AS Categoria,
                   barcode AS Barcode, tipologia AS Tipologia,
                   extra1 AS Extra1, extra2 AS Extra2, extra3 AS Extra3,
                   code_prod AS CodeProd, spec AS Spec, oper AS Oper, um AS Um,
                   ubicazione AS Ubicazione, codexforn AS Codexforn
            FROM codex_items WHERE id=@Id", new { Id = id });
        if (item == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<CodexListItem>.Ok(item));
    }

    // ── NUOVA CODIFICA (ricodifica manuale, ampliamento Codex 21/07/2026) ──
    // Colonna codice_nuovo: di proprietà di ATEC PM (il sync remoto non la tocca),
    // compilata SOLO a mano. Formato = solo cifre come il codice attuale (il punto di
    // display viene rimosso), unicità contro ENTRAMBE le colonne (mai un nuovo uguale
    // a un vecchio esistente). Chi può ricodificare: chiave «action.recode_codex».

    [HttpPut("{id}/new-code")]
    [Authorize]
    [RequireFeature("action.recode_codex")]
    public IActionResult SetNewCode(int id, [FromBody] CodexNewCodeSaveRequest req)
    {
        string raw = (req.NewCode ?? "").Replace(".", "").Trim();

        using var c = _db.Open();
        string? oldCode = c.ExecuteScalar<string?>(
            "SELECT codice FROM codex_items WHERE id=@Id", new { Id = id });
        if (oldCode == null) return NotFound(ApiResponse<string>.Fail("Articolo non trovato"));

        if (raw.Length == 0)
        {
            // Rimozione del codice nuovo (torna "non ricodificato").
            c.Execute("UPDATE codex_items SET codice_nuovo = NULL WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<string>.Ok("", "Codice nuovo rimosso"));
        }

        // REGOLA (21/07/2026): il codice NON si digita a mano. Deve arrivare da una
        // prenotazione del sistema (POST new-code/reserve): l'operatore lo accetta e basta.
        if (!req.ReservationId.HasValue)
            return Ok(ApiResponse<string>.Fail(
                "Il codice nuovo va generato dal sistema: usa i pulsanti famiglia per prenotarlo."));

        string? reservedCode = c.ExecuteScalar<string?>(@"
            SELECT reserved_code FROM codex_reservations
            WHERE id = @ResId AND status = 'RESERVED' AND expires_at > NOW()",
            new { ResId = req.ReservationId.Value });
        if (reservedCode == null)
            return Ok(ApiResponse<string>.Fail(
                "Prenotazione scaduta o non valida: genera di nuovo il codice dai pulsanti famiglia."));
        if (!string.Equals(reservedCode, raw, StringComparison.Ordinal))
            return Ok(ApiResponse<string>.Fail(
                "Il codice non corrisponde alla prenotazione: genera di nuovo il codice dai pulsanti famiglia."));

        // Reti di sicurezza (il codice prenotato nasce già libero, ma il vincolo resta):
        // mai uguale a un codice vecchio né a un codice nuovo di un'altra riga.
        int clash = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM codex_items
            WHERE codice = @Code OR (codice_nuovo = @Code AND id <> @Id)",
            new { Code = raw, Id = id });
        if (clash > 0)
            return Ok(ApiResponse<string>.Fail(
                $"Il codice {CodexListItem.FormatCodice(raw)} risulta già usato."));

        c.Execute("UPDATE codex_items SET codice_nuovo = @Code WHERE id=@Id",
            new { Code = raw, Id = id });

        // Il codice ora è custodito dalla colonna (UNIQUE): la prenotazione ha finito il suo compito.
        c.Execute("DELETE FROM codex_reservations WHERE id = @Id", new { Id = req.ReservationId.Value });

        return Ok(ApiResponse<string>.Ok(CodexListItem.FormatCodice(raw), "Codice nuovo assegnato"));
    }

    // PRENOTA il prossimo codice della famiglia secondo la REGOLA CODEX —
    // prefisso famiglia (3) + data odierna ddMMyy (6) + progressivo del giorno (3),
    // stessa meccanica del generatore (GET_LOCK + codex_reservations): più operatori
    // che ricodificano in parallelo NON ricevono mai lo stesso codice. La prenotazione
    // (TTL 10 min) si libera al salvataggio o con /api/codex/release/{id}.
    [HttpPost("new-code/reserve")]
    [Authorize]
    [RequireFeature("action.recode_codex")]
    public IActionResult ReserveNewCode([FromBody] CodexNewCodeReserveRequest req)
    {
        string fam = new string((req.Family ?? "").Where(char.IsDigit).ToArray());
        if (fam.Length != 3)
            return Ok(ApiResponse<CodexReservationResult>.Fail("Famiglia non valida (3 cifre, es. 201)."));

        try
        {
            string userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var (code, reservationId) = _generator.ReserveNextNewCode(fam, userName);
            return Ok(ApiResponse<CodexReservationResult>.Ok(new CodexReservationResult
            {
                Codice = CodexGeneratorService.FormatCodeForDisplay(code),
                ReservationId = reservationId,
            }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<CodexReservationResult>.Fail(ex.Message));
        }
    }

    // Assegnazione MASSIVA, fase 1: prenota N codici della famiglia per le righe
    // selezionate senza codice nuovo e ritorna l'anteprima vecchio→nuovo+descrizione
    // che l'operatore conferma nel form (bulk-commit) o annulla (bulk-release).
    [HttpPost("new-code/bulk-reserve")]
    [Authorize]
    [RequireFeature("action.recode_codex")]
    public IActionResult BulkReserveNewCodes([FromBody] CodexBulkAssignRequest req)
    {
        string fam = new string((req.Family ?? "").Where(char.IsDigit).ToArray());
        if (fam.Length != 3)
            return Ok(ApiResponse<CodexBulkReserveResult>.Fail("Famiglia non valida (3 cifre, es. 201)."));
        var ids = (req.Ids ?? new List<int>()).Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Ok(ApiResponse<CodexBulkReserveResult>.Fail("Nessuna riga selezionata."));

        try
        {
            string userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var (items, skipped) = _generator.ReserveNextNewCodesBulk(fam, ids, userName);
            return Ok(ApiResponse<CodexBulkReserveResult>.Ok(new CodexBulkReserveResult
            {
                Items = items.Select(i => new CodexBulkReserveItem
                {
                    Id = i.ItemId,
                    Codice = CodexListItem.FormatCodice(i.OldCode),
                    Descr = i.Descr,
                    NewCode = CodexListItem.FormatCodice(i.NewCode),
                    ReservationId = i.ReservationId,
                }).ToList(),
                Skipped = skipped,
            }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<CodexBulkReserveResult>.Fail(ex.Message));
        }
    }

    // Assegnazione MASSIVA, fase 2: il pulsante «Assegna» del form scrive i codici
    // prenotati sulle righe. Ogni voce viene verificata contro la sua prenotazione;
    // righe nel frattempo ricodificate da altri vengono saltate.
    [HttpPost("new-code/bulk-commit")]
    [Authorize]
    [RequireFeature("action.recode_codex")]
    public IActionResult BulkCommitNewCodes([FromBody] CodexBulkCommitRequest req)
    {
        var items = req.Items ?? new List<CodexBulkReserveItem>();
        if (items.Count == 0)
            return Ok(ApiResponse<CodexBulkAssignResult>.Fail("Nessuna riga da assegnare."));

        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        int assigned = 0, skipped = 0;
        foreach (CodexBulkReserveItem item in items)
        {
            string raw = (item.NewCode ?? "").Replace(".", "").Trim();

            // La prenotazione deve esistere, essere attiva e portare esattamente questo codice.
            string? reserved = c.ExecuteScalar<string?>(@"
                SELECT reserved_code FROM codex_reservations
                WHERE id = @ResId AND status = 'RESERVED' AND expires_at > NOW()",
                new { ResId = item.ReservationId }, tx);
            if (reserved == null || !string.Equals(reserved, raw, StringComparison.Ordinal))
            {
                skipped++;
                continue;
            }

            // Riga nel frattempo ricodificata da un altro operatore → saltata, prenotazione liberata.
            int updated = c.Execute(@"
                UPDATE codex_items SET codice_nuovo = @Code
                WHERE id = @Id AND (codice_nuovo IS NULL OR codice_nuovo = '')",
                new { Code = raw, Id = item.Id }, tx);
            if (updated > 0) assigned++;
            else skipped++;

            c.Execute("DELETE FROM codex_reservations WHERE id = @ResId",
                new { ResId = item.ReservationId }, tx);
        }
        tx.Commit();

        string msg = skipped > 0
            ? $"{assigned} codici assegnati, {skipped} righe saltate"
            : $"{assigned} codici assegnati";
        return Ok(ApiResponse<CodexBulkAssignResult>.Ok(
            new CodexBulkAssignResult { Assigned = assigned, Skipped = skipped }, msg));
    }

    // Annulla del form: libera in blocco le prenotazioni dell'anteprima.
    [HttpPost("new-code/bulk-release")]
    [Authorize]
    [RequireFeature("action.recode_codex")]
    public IActionResult BulkReleaseNewCodes([FromBody] CodexBulkReleaseRequest req)
    {
        var ids = (req.ReservationIds ?? new List<int>()).Where(i => i > 0).Distinct().ToList();
        if (ids.Count > 0)
        {
            using var c = _db.Open();
            c.Execute("DELETE FROM codex_reservations WHERE id IN @Ids AND status = 'RESERVED'",
                new { Ids = ids });
        }
        return Ok(ApiResponse<bool>.Ok(true, "Prenotazioni rilasciate"));
    }

    // Reset MASSIVO del codice nuovo (l'operatore ha sbagliato la qualifica):
    // le righe selezionate tornano "non ricodificate".
    [HttpPost("new-code/bulk-remove")]
    [Authorize]
    [RequireFeature("action.recode_codex")]
    public IActionResult BulkRemoveNewCodes([FromBody] CodexBulkRemoveRequest req)
    {
        var ids = (req.Ids ?? new List<int>()).Where(i => i > 0).Distinct().ToList();
        if (ids.Count == 0)
            return Ok(ApiResponse<int>.Fail("Nessuna riga selezionata."));

        using var c = _db.Open();
        int removed = c.Execute(@"
            UPDATE codex_items SET codice_nuovo = NULL
            WHERE id IN @Ids AND codice_nuovo IS NOT NULL AND codice_nuovo <> ''",
            new { Ids = ids });
        return Ok(ApiResponse<int>.Ok(removed, $"{removed} codici nuovi rimossi"));
    }

    // Avanzamento ricodifica per la pagina "a tappeto" (default famiglia vecchia 201xxx).
    [HttpGet("recode-stats")]
    public IActionResult RecodeStats([FromQuery] string? prefix = "201")
    {
        string pref = new string((prefix ?? "201").Where(char.IsLetterOrDigit).ToArray());
        if (pref.Length == 0) pref = "201";

        using var c = _db.Open();
        var stats = c.QueryFirst<CodexRecodeStats>(@"
            SELECT COUNT(*) AS Total,
                   CAST(COALESCE(SUM(CASE WHEN codice_nuovo IS NOT NULL AND codice_nuovo <> '' THEN 1 ELSE 0 END), 0) AS SIGNED) AS Done
            FROM codex_items WHERE codice LIKE @Pref", new { Pref = pref + "%" });
        return Ok(ApiResponse<CodexRecodeStats>.Ok(stats));
    }

    // ── SYNC ────────────────────────────────────────────────

    // Fino alla Fase E il divieto stava SOLO nel client (pagina Codex, `isAdminLevel`):
    // l'API era aperta a ogni autenticato. Togliere il controllo dalla pagina senza dare
    // la chiave qui non lo avrebbe sostituito, lo avrebbe cancellato.
    [HttpPost("sync")]
    [RequireFeature("action.manage_codex")]
    public IActionResult StartSync()
    {
        if (CodexSyncService.IsSyncing)
            return Ok(ApiResponse<string>.Fail("Sincronizzazione già in corso"));

        _ = Task.Run(() => _sync.RunSync());
        return Ok(ApiResponse<string>.Ok("Sincronizzazione avviata"));
    }

    [HttpGet("sync-status")]
    public IActionResult GetSyncStatus()
    {
        var status = new CodexSyncStatus
        {
            IsSyncing = CodexSyncService.IsSyncing,
            LastSync = CodexSyncService.LastSync,
            TotalRows = CodexSyncService.TotalRows,
            LastError = CodexSyncService.LastError
        };
        return Ok(ApiResponse<CodexSyncStatus>.Ok(status));
    }

    // ── GENERAZIONE CON PRENOTAZIONE ────────────────────────

    [HttpGet("prefixes")]
    public IActionResult GetPrefixes()
    {
        var prefixes = _generator.GetAvailablePrefixes()
            .Select(p => new CodexPrefix { Codice = p.Code, Descrizione = p.Description })
            .ToList();
        return Ok(ApiResponse<List<CodexPrefix>>.Ok(prefixes));
    }

    // Prenota / rilascia / conferma NON sono roba della sola pagina Codex: lo stesso
    // pannello «Nuovo codice Codex» si apre anche dal dialog Codice ATEC (Catalogo,
    // Inbox Acquisti, DDP Commerciale) e dal picker della distinta officina. Chiudere
    // qui con la sola `action.manage_codex` toglierebbe a quei due il pulsante che oggi
    // funziona: le chiavi vanno quindi in OR (via d'uscita), una per porta d'ingresso.
    [HttpPost("reserve")]
    [RequireFeature("action.manage_codex", "action.assign_atec_code", "project.ddp_officina")]
    public IActionResult ReserveCode([FromBody] CodexReserveRequest req)
    {
        try
        {
            string userName = User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
            var (code, reservationId) = _generator.ReserveNextCode(req.Prefisso, userName);
            var result = new CodexReservationResult
            {
                Codice = CodexGeneratorService.FormatCodeForDisplay(code),
                ReservationId = reservationId
            };
            return Ok(ApiResponse<CodexReservationResult>.Ok(result));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }

    // Il rilascio ha una porta in più delle altre due: lo chiama anche il dialog della
    // RICODIFICA quando l'operatore annulla, e quello è governato da `action.recode_codex`.
    // Senza la sua chiave qui la prenotazione resterebbe appesa fino alla scadenza (10 min),
    // bruciando un progressivo del giorno per ogni annullamento.
    [HttpPost("release/{reservationId}")]
    [RequireFeature("action.manage_codex", "action.assign_atec_code", "project.ddp_officina", "action.recode_codex")]
    public IActionResult ReleaseReservation(int reservationId)
    {
        try
        {
            _generator.ReleaseReservation(reservationId);
            return Ok(ApiResponse<string>.Ok("Prenotazione rilasciata"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }

    [HttpPost("confirm")]
    [RequireFeature("action.manage_codex", "action.assign_atec_code", "project.ddp_officina")]
    public IActionResult ConfirmReservation([FromBody] CodexConfirmRequest req)
    {
        try
        {
            var (code, itemId) = _generator.ConfirmReservation(req.ReservationId, req.Descrizione);
            var result = new CodexGeneratedCode { Codice = CodexGeneratorService.FormatCodeForDisplay(code), Id = itemId };
            return Ok(ApiResponse<CodexGeneratedCode>.Ok(result, "Codice generato"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }

    // ── MODIFICA / ELIMINA ────────────────────────────────────
    // Una sola porta: la pagina Codex, dove la riga si apre in modifica e si elimina.
    // Prima della Fase E il freno era il solo `isAdminLevel()` del client.

    [HttpPut("{id}")]
    [RequireFeature("action.manage_codex")]
    public IActionResult Update(int id, [FromBody] CodexUpdateRequest req)
    {
        using var c = _db.Open();
        int rows = c.Execute("UPDATE codex_items SET descr=@Descrizione WHERE id=@Id",
            new { req.Descrizione, Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Articolo non trovato"));
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("{id}")]
    [RequireFeature("action.manage_codex")]
    public IActionResult Delete(int id)
    {
        using var c = _db.Open();

        // Blocca cancellazione se usato in una composizione
        int usedInComp = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM codex_compositions WHERE child_codex_id=@Id",
            new { Id = id });
        if (usedInComp > 0)
            return BadRequest(ApiResponse<string>.Fail(
                "Impossibile eliminare: questo articolo è utilizzato in una composizione"));

        int rows = c.Execute("DELETE FROM codex_items WHERE id=@Id", new { Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Articolo non trovato"));
        return Ok(ApiResponse<bool>.Ok(true, "Eliminato"));
    }

    // ── COMPOSIZIONE ────────────────────────────────────────
    // Le SCRITTURE hanno una porta sola, la pagina «Composizione Codex», dove fino alla
    // Fase E il freno era il solo `isAdminLevel()` del client: senza la chiave qui,
    // toglierlo di là avrebbe cancellato il controllo invece di spostarlo. Le GET restano
    // aperte di proposito — l'albero lo legge anche il picker della distinta officina.

    private const string CompositionSelectSql = @"
        SELECT cc.id AS Id, cc.parent_codex_id AS ParentCodexId,
               cc.child_codex_id AS ChildCodexId,
               cc.child_catalog_id AS ChildCatalogId,
               COALESCE(ci.codice, cat.code) AS ChildCodice,
               COALESCE(ci.descr, cat.description) AS ChildDescr,
               cc.sort_order AS SortOrder,
               cc.quantity AS Quantity,
               CASE WHEN cc.child_catalog_id IS NOT NULL THEN 'catalog' ELSE 'codex' END AS Source
        FROM codex_compositions cc
        LEFT JOIN codex_items ci ON ci.id = cc.child_codex_id
        LEFT JOIN catalog_items cat ON cat.id = cc.child_catalog_id";

    [HttpGet("compositions/{parentId}")]
    public IActionResult GetCompositionChildren(int parentId)
    {
        using var c = _db.Open();
        var rows = c.Query<CompositionChildDto>(
            CompositionSelectSql + @"
            WHERE cc.parent_codex_id = @ParentId
            ORDER BY cc.sort_order, COALESCE(ci.codice, cat.code)",
            new { ParentId = parentId }).ToList();
        return Ok(ApiResponse<List<CompositionChildDto>>.Ok(rows));
    }

    [HttpGet("compositions/tree/{codexId}")]
    public IActionResult GetCompositionTree(int codexId)
    {
        using var c = _db.Open();

        var root = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice, descr AS Descr FROM codex_items WHERE id=@Id",
            new { Id = codexId });
        if (root == null) return NotFound(ApiResponse<string>.Fail("Non trovato"));

        var allComps = c.Query<CompositionChildDto>(
            CompositionSelectSql + " ORDER BY cc.sort_order, COALESCE(ci.codice, cat.code)").ToList();

        var lookup = allComps.GroupBy(x => x.ParentCodexId)
                             .ToDictionary(g => g.Key, g => g.ToList());

        var tree = new CompositionTreeNode
        {
            CodexId = root.Id,
            Codice = root.Codice,
            Descr = root.Descr
        };
        BuildTree(tree, lookup);
        return Ok(ApiResponse<CompositionTreeNode>.Ok(tree));
    }

    private void BuildTree(CompositionTreeNode node, Dictionary<int, List<CompositionChildDto>> lookup)
    {
        if (!lookup.ContainsKey(node.CodexId)) return;
        foreach (var child in lookup[node.CodexId])
        {
            var childNode = new CompositionTreeNode
            {
                CodexId = child.ChildCodexId ?? 0,
                CatalogId = child.ChildCatalogId,
                Codice = child.ChildCodice,
                Descr = child.ChildDescr,
                CompositionId = child.Id,
                Source = child.Source,
                Quantity = child.Quantity
            };
            // Solo nodi codex possono avere sotto-figli
            if (child.ChildCodexId.HasValue)
                BuildTree(childNode, lookup);
            node.Children.Add(childNode);
        }
    }

    [HttpPost("compositions")]
    [RequireFeature("action.edit_codex_composition")]
    public IActionResult AddComposition([FromBody] AddCompositionRequest req, [FromQuery] string? conn = null)
    {
        using var c = _db.Open();

        if (req.ChildCodexId == null && req.ChildCatalogId == null)
            return BadRequest(ApiResponse<string>.Fail("Specificare ChildCodexId o ChildCatalogId"));

        var parent = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id",
            new { Id = req.ParentCodexId });
        if (parent == null)
            return BadRequest(ApiResponse<string>.Fail("Articolo parent non trovato"));

        string parentPrefix = parent.Codice.Substring(0, 1);

        // Validazione gerarchia solo per figli codex
        if (req.ChildCodexId.HasValue)
        {
            var child = c.QueryFirstOrDefault<CodexListItem>(
                "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id",
                new { Id = req.ChildCodexId.Value });
            if (child == null)
                return BadRequest(ApiResponse<string>.Fail("Articolo figlio non trovato"));

            string childPrefix = child.Codice.Substring(0, 1);
            string? error = ValidateHierarchy(parentPrefix, childPrefix);
            if (error != null) return BadRequest(ApiResponse<string>.Fail(error));
        }
        else if (req.ChildCatalogId.HasValue)
        {
            // Verifica che l'articolo catalogo esista
            int exists = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM catalog_items WHERE id=@Id",
                new { Id = req.ChildCatalogId.Value });
            if (exists == 0)
                return BadRequest(ApiResponse<string>.Fail("Articolo catalogo non trovato"));
        }

        int qty = Math.Max(1, req.Quantity);

        // Una riga per componente: se il figlio è già in composizione si incrementa
        // la quantità invece di duplicare la riga (<=> = uguaglianza NULL-safe).
        int existingId = c.ExecuteScalar<int?>(@"
            SELECT id FROM codex_compositions
            WHERE parent_codex_id = @ParentCodexId
              AND child_codex_id <=> @ChildCodexId
              AND child_catalog_id <=> @ChildCatalogId",
            new { req.ParentCodexId, req.ChildCodexId, req.ChildCatalogId }) ?? 0;

        int lastId;
        if (existingId > 0)
        {
            c.Execute("UPDATE codex_compositions SET quantity = quantity + @Qty WHERE id=@Id",
                new { Qty = qty, Id = existingId });
            lastId = existingId;
        }
        else
        {
            int maxSort = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(sort_order),0) FROM codex_compositions WHERE parent_codex_id=@P",
                new { P = req.ParentCodexId });

            lastId = c.ExecuteScalar<int>(@"
                INSERT INTO codex_compositions (parent_codex_id, child_codex_id, child_catalog_id, sort_order, quantity)
                VALUES (@ParentCodexId, @ChildCodexId, @ChildCatalogId, @Sort, @Qty);
                SELECT LAST_INSERT_ID()",
                new { req.ParentCodexId, req.ChildCodexId, req.ChildCatalogId, Sort = maxSort + 1, Qty = qty });
        }

        NotifyCompositionChange(conn, req.ParentCodexId, existingId > 0 ? "update" : "create", lastId);

        string msg = existingId > 0
            ? $"Quantità incrementata (+{qty})"
            : qty > 1 ? $"Componente aggiunto ×{qty}" : "Composizione aggiunta";
        return Ok(ApiResponse<int>.Ok(lastId, msg));
    }

    /// <summary>
    /// Aggiorna quantità e/o ordinamento di una riga di composizione. Semantica
    /// "0 = non toccare": i client inviano solo il campo che vogliono cambiare.
    /// </summary>
    [HttpPut("compositions/{id}")]
    [RequireFeature("action.edit_codex_composition")]
    public IActionResult UpdateComposition(int id, [FromBody] UpdateCompositionRequest req, [FromQuery] string? conn = null)
    {
        using var c = _db.Open();
        int parentId = c.ExecuteScalar<int>(
            "SELECT COALESCE(parent_codex_id, 0) FROM codex_compositions WHERE id=@Id", new { Id = id });
        int rows = c.Execute(@"
            UPDATE codex_compositions
            SET sort_order = CASE WHEN @SortOrder >= 1 THEN @SortOrder ELSE sort_order END,
                quantity   = CASE WHEN @Quantity  >= 1 THEN @Quantity  ELSE quantity   END
            WHERE id=@Id",
            new { req.SortOrder, req.Quantity, Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        NotifyCompositionChange(conn, parentId, "update", id);
        return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
    }

    [HttpDelete("compositions/{id}")]
    [RequireFeature("action.edit_codex_composition")]
    public IActionResult DeleteComposition(int id, [FromQuery] string? conn = null)
    {
        using var c = _db.Open();
        // Recupera il parent prima della delete per poterlo includere nella notifica real-time.
        int parentId = c.ExecuteScalar<int>(
            "SELECT COALESCE(parent_codex_id, 0) FROM codex_compositions WHERE id=@Id", new { Id = id });
        int rows = c.Execute("DELETE FROM codex_compositions WHERE id=@Id", new { Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        NotifyCompositionChange(conn, parentId, "delete", id);
        return Ok(ApiResponse<bool>.Ok(true, "Rimosso"));
    }

    [HttpPost("compositions/import-preview/{parentId}")]
    public IActionResult ImportPreview(int parentId, [FromBody] List<CodexImportItem> items)
    {
        using var c = _db.Open();
        var parent = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id",
            new { Id = parentId });

        if (parent == null)
            return BadRequest(ApiResponse<string>.Fail("Articolo parent non trovato"));

        string parentPrefix = parent.Codice.Substring(0, 1);
        var results = new List<CodexImportPreviewResult>();

        foreach (var item in items)
        {
            var cleanCode = (item.Code ?? "").Replace(".", "").Trim();
            if (string.IsNullOrEmpty(cleanCode)) continue;

            // Cerca nel Codex
            var codexItem = c.QueryFirstOrDefault<CodexListItem>(
                "SELECT id, codice AS Codice, descr AS Descr FROM codex_items WHERE codice = @Code",
                new { Code = cleanCode });

            if (codexItem != null)
            {
                string childPrefix = codexItem.Codice.Substring(0, 1);
                string? hierarchyError = ValidateHierarchy(parentPrefix, childPrefix);
                results.Add(new CodexImportPreviewResult
                {
                    Code = item.Code,
                    Quantity = item.Quantity,
                    Descr = codexItem.Descr,
                    Id = codexItem.Id,
                    Source = "codex",
                    IsValid = hierarchyError == null,
                    Error = hierarchyError
                });
            }
            else
            {
                // Cerca a catalogo
                var catalogItem = c.QueryFirstOrDefault<(int Id, string Description)>(
                    "SELECT id, description FROM catalog_items WHERE code = @Code",
                    new { Code = cleanCode });

                if (catalogItem.Id > 0)
                {
                    results.Add(new CodexImportPreviewResult
                    {
                        Code = item.Code,
                        Quantity = item.Quantity,
                        Descr = catalogItem.Description,
                        Id = catalogItem.Id,
                        Source = "catalog",
                        IsValid = true, // Articoli catalogo sempre ammessi
                        Error = null
                    });
                }
                else
                {
                    results.Add(new CodexImportPreviewResult
                    {
                        Code = item.Code,
                        Quantity = item.Quantity,
                        Descr = null,
                        Id = null,
                        Source = null,
                        IsValid = false,
                        Error = "Articolo non trovato"
                    });
                }
            }
        }

        return Ok(ApiResponse<List<CodexImportPreviewResult>>.Ok(results));
    }

    [HttpPost("compositions/import-commit")]
    public IActionResult ImportCommit([FromBody] CodexImportCommitRequest req, [FromQuery] string? conn = null)
    {
        using var c = _db.Open();
        using var tx = c.BeginTransaction();
        try
        {
            var parent = c.QueryFirstOrDefault<CodexListItem>(
                "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id",
                new { Id = req.ParentId }, tx);
            if (parent == null)
                return BadRequest(ApiResponse<string>.Fail("Articolo parent non trovato"));

            string parentPrefix = parent.Codice.Substring(0, 1);

            if (req.ReplaceExisting)
            {
                c.Execute("DELETE FROM codex_compositions WHERE parent_codex_id = @ParentId", new { req.ParentId }, tx);
            }

            int maxSort = c.ExecuteScalar<int>(
                "SELECT COALESCE(MAX(sort_order),0) FROM codex_compositions WHERE parent_codex_id=@P",
                new { P = req.ParentId }, tx);

            int currentSort = maxSort + 1;

            foreach (var item in req.Items)
            {
                var cleanCode = (item.Code ?? "").Replace(".", "").Trim();
                if (string.IsNullOrEmpty(cleanCode)) continue;

                // Trova l'id
                var codexItem = c.QueryFirstOrDefault<CodexListItem>(
                    "SELECT id, codice AS Codice FROM codex_items WHERE codice = @Code",
                    new { Code = cleanCode }, tx);

                int? childCodexId = null;
                int? childCatalogId = null;

                if (codexItem != null)
                {
                    childCodexId = codexItem.Id;
                    string childPrefix = codexItem.Codice.Substring(0, 1);
                    string? hierarchyError = ValidateHierarchy(parentPrefix, childPrefix);
                    if (hierarchyError != null)
                        throw new InvalidOperationException($"L'articolo {item.Code} viola le regole di struttura: {hierarchyError}");
                }
                else
                {
                    var catalogItem = c.QueryFirstOrDefault<(int Id, string Description)>(
                        "SELECT id, description FROM catalog_items WHERE code = @Code",
                        new { Code = cleanCode }, tx);

                    if (catalogItem.Id > 0)
                    {
                        childCatalogId = catalogItem.Id;
                    }
                    else
                    {
                        throw new InvalidOperationException($"Articolo {item.Code} non trovato a sistema");
                    }
                }

                int qty = Math.Max(1, item.Quantity);

                // Una riga per componente: se il figlio è già presente (import in
                // aggiunta, o codice ripetuto nella lista) si somma la quantità.
                int existingId = c.ExecuteScalar<int?>(@"
                    SELECT id FROM codex_compositions
                    WHERE parent_codex_id = @ParentId
                      AND child_codex_id <=> @ChildCodexId
                      AND child_catalog_id <=> @ChildCatalogId",
                    new { ParentId = req.ParentId, ChildCodexId = childCodexId, ChildCatalogId = childCatalogId }, tx) ?? 0;

                if (existingId > 0)
                {
                    c.Execute("UPDATE codex_compositions SET quantity = quantity + @Qty WHERE id=@Id",
                        new { Qty = qty, Id = existingId }, tx);
                }
                else
                {
                    c.Execute(@"
                        INSERT INTO codex_compositions (parent_codex_id, child_codex_id, child_catalog_id, sort_order, quantity)
                        VALUES (@ParentId, @ChildCodexId, @ChildCatalogId, @Sort, @Qty)",
                        new { ParentId = req.ParentId, ChildCodexId = childCodexId, ChildCatalogId = childCatalogId, Sort = currentSort, Qty = qty }, tx);
                    currentSort++;
                }
            }

            tx.Commit();

            NotifyCompositionChange(conn, req.ParentId, "create", 0);
            return Ok(ApiResponse<bool>.Ok(true, "Composizione importata con successo"));
        }
        catch (Exception ex)
        {
            tx.Rollback();
            return BadRequest(ApiResponse<string>.Fail(ex.Message));
        }
    }

    private static string? ValidateHierarchy(string parentPrefix, string childPrefix)
    {
        return parentPrefix switch
        {
            "5" => childPrefix is "1" or "2" or "3" or "4"
                ? null : "Gruppo meccanico (5xx) può contenere solo articoli 1xx-4xx",
            "6" => childPrefix is "5"
                ? null : "Assieme meccanico (6xx) può contenere solo gruppi 5xx",
            "7" => childPrefix is "6"
                ? null : "Layout meccanico (7xx) può contenere solo assiemi 6xx",
            _ => "Solo articoli 5xx, 6xx, 7xx possono avere composizioni"
        };
    }

    // ── RIFERIMENTI 101 → 201/401 ──────────────────────────────

    [HttpGet("references/{sourceId}")]
    public IActionResult GetReferences(int sourceId)
    {
        using var c = _db.Open();
        var refs = c.Query<CodexItemReference>(@"
            SELECT r.id AS Id, r.source_codex_id AS SourceCodexId,
                   r.ref_codex_id AS RefCodexId, r.ref_type AS RefType,
                   ci.codice AS RefCodice, ci.descr AS RefDescr
            FROM codex_item_references r
            JOIN codex_items ci ON ci.id = r.ref_codex_id
            WHERE r.source_codex_id = @Id
            ORDER BY r.ref_type", new { Id = sourceId }).ToList();
        return Ok(ApiResponse<List<CodexItemReference>>.Ok(refs));
    }

    [HttpPost("references")]
    public IActionResult AddReference([FromBody] AddCodexReferenceRequest req)
    {
        using var c = _db.Open();

        // Verifica che il source sia un 101
        var source = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id", new { Id = req.SourceCodexId });
        if (source == null) return BadRequest(ApiResponse<string>.Fail("Articolo sorgente non trovato"));
        if (!source.Codice.StartsWith("1"))
            return BadRequest(ApiResponse<string>.Fail("Solo i codici 1xx possono avere riferimenti 201/401"));

        // Verifica che il ref sia del tipo giusto
        var refItem = c.QueryFirstOrDefault<CodexListItem>(
            "SELECT id, codice AS Codice FROM codex_items WHERE id=@Id", new { Id = req.RefCodexId });
        if (refItem == null) return BadRequest(ApiResponse<string>.Fail("Articolo riferimento non trovato"));

        string refPrefix = refItem.Codice.Substring(0, 1);
        if (req.RefType == "201" && refPrefix != "2")
            return BadRequest(ApiResponse<string>.Fail("Riferimento 201 deve essere un codice 2xx"));
        if (req.RefType == "401" && refPrefix != "4")
            return BadRequest(ApiResponse<string>.Fail("Riferimento 401 deve essere un codice 4xx"));

        try
        {
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO codex_item_references (source_codex_id, ref_codex_id, ref_type)
                VALUES (@SourceCodexId, @RefCodexId, @RefType)
                ON DUPLICATE KEY UPDATE ref_codex_id = @RefCodexId;
                SELECT LAST_INSERT_ID()", req);
            return Ok(ApiResponse<int>.Ok(id, "Riferimento salvato"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Fail($"Errore: {ex.Message}"));
        }
    }

    [HttpDelete("references/{id}")]
    public IActionResult DeleteReference(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM codex_item_references WHERE id=@Id", new { Id = id });
        if (rows == 0) return NotFound(ApiResponse<string>.Fail("Non trovato"));
        return Ok(ApiResponse<bool>.Ok(true, "Riferimento rimosso"));
    }
}