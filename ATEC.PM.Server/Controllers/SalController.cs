using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Hubs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/sal")]
[Authorize]
// SAL / Fatturazione: dati economici, stessa chiave della voce di menu.
// OR albero/menu (split passo 3, §12.8.4): grant fotografati da M103, nessuno cambia.
[RequireFeature("project.sal", "nav.sal")]
public class SalController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    private readonly FeatureAccessService _access;
    private readonly ProjectWriteGuard _guard;

    public SalController(
        DbService db, IHubContext<ProjectHub> hub, FeatureAccessService access, ProjectWriteGuard guard)
    {
        _db = db;
        _hub = hub;
        _access = access;
        _guard = guard;
    }

    /// <summary>Chiave della funzione «dati economici del SAL» (pagina «Permessi»).</summary>
    public const string EconomicsFeature = "sal.economics";

    /// <summary>
    /// Il chiamante può vedere gli importi? È una FUNZIONE, non un livello: l'ufficio
    /// amministrazione deve vedere il fatturato senza essere promosso a Project Manager.
    /// Si valuta sulla PERSONA (<c>CanAccessUser</c>): la variante per solo RUOLO
    /// (<c>CanAccess</c>) non guarda <c>employee_feature_access</c>, quindi qui i permessi
    /// individuali venivano ignorati e a decidere restava il vecchio <c>min_level</c>.
    /// </summary>
    private bool CanSeeEconomics() =>
        _access.CanAccessUser(CurrentEmployeeId, User.FindFirst(ClaimTypes.Role)?.Value, EconomicsFeature);

    /// <summary>
    /// Il chiamante può mettere mano al foglio SAL di una commessa CHIUSA? Prima era il
    /// livello ADMIN, ora è la chiave «Modifica SAL di commessa chiusa» sulla persona:
    /// è una scrittura, quindi <c>CanWriteUser</c> (una concessione in sola lettura non basta).
    /// <para>⚠️ Vale <b>anche</b> il permesso generale della #88 («Opera su commesse sospese o
    /// chiuse»): senza questo OR un PM si troverebbe respinto proprio dove la segnalazione gli
    /// promette di «operare come se la commessa fosse attiva» — passerebbe il cancello nuovo e
    /// verrebbe fermato da questo, che è più vecchio e più stretto.</para>
    /// </summary>
    private bool CanEditClosedSal() =>
        _access.CanWriteUser(CurrentEmployeeId, User.FindFirst(ClaimTypes.Role)?.Value, "action.sal_edit_closed")
        || _guard.PuoScavalcare(User);

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>
    /// Commessa chiusa (COMPLETED/CANCELLED) = foglio SAL in sola lettura per chi non ha la
    /// chiave <c>action.sal_edit_closed</c>. È l'unico lock del SAL: lo stato «Pagata» non
    /// blocca più nulla, altrimenti un incasso segnato per sbaglio resterebbe lì per sempre.
    /// </summary>
    private static bool IsProjectClosed(MySqlConnector.MySqlConnection c, int projectId) =>
        ATEC.PM.Shared.ProjectStatuses.IsClosed(c.ExecuteScalar<string>(
            "SELECT status FROM projects WHERE id=@Id", new { Id = projectId }));

    /// <summary>
    /// Perimetro delle viste SAL aggregate (Prospetto, Cash Flow, Analisi, riepilogo).
    /// <para>
    /// Prima era il solo <c>p.status = 'ACTIVE'</c>: una commessa che passava a COMPLETED o
    /// ON_HOLD spariva da tutto, comprese le fatture ancora da emettere o da incassare —
    /// cioè proprio i soldi che restano da vedere. Ora entra anche la commessa chiusa che ha
    /// ancora almeno una riga aperta (non emessa, oppure emessa e non ancora «Pagata»).
    /// <list type="bullet">
    ///   <item><description><b>CANCELLED</b> resta fuori: lì il lavoro è stato annullato, non sospeso.</description></item>
    ///   <item><description><b>DRAFT</b> resta fuori: è una commessa non ancora avviata, un piano di
    ///   fatturazione abbozzato non deve finire negli allarmi di fatturazione e incasso.</description></item>
    ///   <item><description>Le <b>Altre Attività</b> (codice libero: INTERNA, SERVICE _ SANGRATO…)
    ///   restano fuori dal SAL: «non entrano nella gestione Sal Fatturazione» (segnalazione #85).</description></item>
    /// </list>
    /// (Decisione dell'utente del 04/08/2026; l'esclusione delle bozze è arrivata dopo la prova
    /// a runtime, dove si è visto che con il solo filtro su CANCELLED entravano anche le DRAFT.)
    /// </para>
    /// </summary>
    private static readonly string ProjectScope = $@"(
        {ProjectSorting.IsCommessa("p")}
        AND (
            p.status = 'ACTIVE'
            OR (p.status NOT IN ('CANCELLED', 'DRAFT') AND EXISTS (
                SELECT 1 FROM sal_rows sr_open
                WHERE sr_open.project_id = p.id
                  AND (sr_open.stato <> 'emessa' OR COALESCE(sr_open.pagamento, '') <> 'Pagata')
            ))
        )
    )";

    public const string ConflictMessage = "CONFLITTO: record SAL modificato da un altro utente";

    // N° fattura: solo cifre (stringa per preservare gli zeri iniziali)
    private static string SanitizeNFatt(string? nFatt) => Regex.Replace(nFatt ?? "", @"\D", "");

    // Colore HEX ammesso per gli stati pagamento: #RRGGBB o #RRGGBBAA (colonne VARCHAR(9))
    private static readonly Regex HexColorRegex = new(@"^#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?$", RegexOptions.Compiled);

    // Normalizza un colore in input: vuoto/spazi → null (= stato neutro senza tinta);
    // altrimenti deve essere un HEX valido. Ritorna false se il valore non è accettabile.
    private static bool TryNormalizeColor(string? raw, out string? color)
    {
        color = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        return color == null || HexColorRegex.IsMatch(color);
    }

    // Troncamento server-side ai limiti colonna: evita MySqlException (500) su input troppo lunghi
    private static string Trunc(string? s, int max)
    {
        string v = (s ?? "").Trim();
        return v.Length <= max ? v : v.Substring(0, max);
    }

    // Clamp difensivo dei numerici: previene overflow DATE_ADD (gg_saldo abnorme) e valori assurdi
    private static int? Clamp(int? v, int min, int max) =>
        v == null ? null : Math.Clamp(v.Value, min, max);

    private void NotifyChanged(string action, int projectId)
    {
        _ = _hub.Clients.Group(ProjectHub.ProjectGroup(projectId))
            .SendAsync("SalChanged", new { action, projectId });
        _ = _hub.Clients.All.SendAsync("GlobalSalChanged", new { action, projectId });
    }

    // Broadcast per i cambi alle ANAGRAFICHE SAL (condizioni, causali SAP, stati pagamento):
    // cataloghi globali senza commessa specifica → solo evento globale con projectId=0,
    // fire-and-forget come NotifyChanged. I client invalidano le query dei cataloghi.
    private void NotifyLookupChanged()
    {
        _ = _hub.Clients.All.SendAsync("GlobalSalChanged", new { action = "lookup", projectId = 0 });
    }

    [RequireProjectVisible]
    [HttpGet]
    public IActionResult GetBundle([FromQuery] int projectId)
    {
        if (projectId <= 0) return Ok(ApiResponse<SalBundleDto>.Fail("projectId obbligatorio"));
        using var c = _db.Open();

        // 1. Carica o crea l'header SAL al volo; il nome cliente è dalla commessa (sola lettura).
        var header = c.QueryFirstOrDefault<SalHeaderDto>(@"
            SELECT p.id AS ProjectId, ps.cliente AS Cliente, ps.valore AS Valore,
                   COALESCE(ps.row_version, 0) AS RowVersion, COALESCE(ps.po, '') AS Po,
                   COALESCE(ps.rif_offerta, '') AS RifOfferta,
                   COALESCE(cu.company_name, '') AS CustomerName
            FROM projects p
            LEFT JOIN project_sal ps ON ps.project_id = p.id
            LEFT JOIN customers cu ON cu.id = p.customer_id
            WHERE p.id = @Pid", new { Pid = projectId });

        if (header == null)
            return Ok(ApiResponse<SalBundleDto>.Fail("Commessa non trovata"));

        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM project_sal WHERE project_id=@Pid", new { Pid = projectId }) == 0)
        {
            c.Execute("INSERT IGNORE INTO project_sal (project_id, cliente, valore) VALUES (@Pid, '', NULL)", new { Pid = projectId });
            header = c.QueryFirstOrDefault<SalHeaderDto>(@"
                SELECT p.id AS ProjectId, ps.cliente AS Cliente, ps.valore AS Valore,
                       ps.row_version AS RowVersion, ps.po AS Po, ps.rif_offerta AS RifOfferta,
                       COALESCE(cu.company_name, '') AS CustomerName
                FROM projects p
                JOIN project_sal ps ON ps.project_id = p.id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE p.id = @Pid", new { Pid = projectId })
                ?? header;
        }

        // Commessa chiusa → foglio in sola lettura (l'unico lock del SAL, vedi IsProjectClosed).
        header.IsProjectClosed = IsProjectClosed(c, projectId);

        // #91: l'Importo Ordine è un dato economico — a chi non ha `sal.economics` non esce
        // nemmeno da qui (il client nasconde il campo, ma nel tab Network si leggerebbe).
        // Il salvataggio è protetto in UpdateHeader: il null rispedito non azzera niente.
        if (!CanSeeEconomics())
            header.Valore = null;

        // 2. Carica le righe SAL ordinate per sort_order, id
        var rows = c.Query<SalRowDto>(@"
            SELECT id AS Id, project_id AS ProjectId, step AS Step, perc AS Perc,
                   condizione AS Condizione, data_fatt AS DataFatt, stato AS Stato,
                   sort_order AS SortOrder, row_version AS RowVersion,
                   paid_by AS PaidBy, paid_at AS PaidAt,
                   iva_perc AS IvaPerc, gg_saldo AS GgSaldo, n_fatt AS NFatt,
                   conto_sap AS ContoSap, pagamento AS Pagamento,
                   data_pagamento AS DataPagamento, note AS Note
            FROM sal_rows WHERE project_id=@Pid ORDER BY sort_order, id", new { Pid = projectId }).ToList();

        var bundle = new SalBundleDto
        {
            Header = header,
            Rows = rows
        };

        return Ok(ApiResponse<SalBundleDto>.Ok(bundle));
    }

    [RequireProjectWritable]
    [HttpPut("header")]
    public IActionResult UpdateHeader([FromQuery] int projectId, [FromBody] SalHeaderSaveRequest req)
    {
        if (projectId <= 0) return Ok(ApiResponse<int>.Fail("projectId obbligatorio"));
        using var c = _db.Open();

        // Po/RifOfferta: null = campo non inviato dal client (pre-Fase 3) → COALESCE preserva il valore corrente;
        // stringa vuota = svuota il campo.
        // Il cliente non si modifica dal foglio SAL: proviene dall'anagrafica commessa.
        // Valore (#91): chi non ha `sal.economics` riceve dal bundle Valore=null e lo
        // rispedirebbe tale e quale — scriverlo azzererebbe l'Importo Ordine a ogni suo
        // salvataggio di PO/Rif. Offerta. Quindi il campo lo scrive solo chi lo vede.
        int rows = c.Execute(@"
            UPDATE project_sal SET
                valore = IF(@Economics, @Valore, valore),
                po=COALESCE(@Po, po), rif_offerta=COALESCE(@RifOfferta, rif_offerta),
                row_version = row_version + 1, updated_at = CURRENT_TIMESTAMP
             WHERE project_id=@Pid AND (@RowVersion IS NULL OR row_version=@RowVersion)",
            new
            {
                req.Valore,
                Economics = CanSeeEconomics(),
                Po = req.Po == null ? null : Trunc(req.Po, 150),
                RifOfferta = req.RifOfferta == null ? null : Trunc(req.RifOfferta, 200),
                Pid = projectId,
                req.RowVersion
            });

        if (rows == 0)
        {
            int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM project_sal WHERE project_id=@Pid", new { Pid = projectId });
            return Ok(ApiResponse<int>.Fail(exists > 0 ? ConflictMessage : "Header SAL non trovato"));
        }

        NotifyChanged("header", projectId);
        return Ok(ApiResponse<int>.Ok(projectId, "Header SAL aggiornato"));
    }

    [RequireProjectWritable]
    [HttpPost("rows")]
    public IActionResult CreateRow([FromQuery] int projectId, [FromBody] SalRowSaveRequest req)
    {
        if (projectId <= 0) return Ok(ApiResponse<int>.Fail("projectId obbligatorio"));
        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<int>.Fail("Commessa non trovata"));

        // Riga nuova: niente da preservare → i campi testo null diventano stringa vuota
        string effPagamento = Trunc(req.Pagamento, 100);

        // Compatibilità legacy: un client vecchio può ancora mandare stato='pagata' → emessa + Pagata
        string stato = req.Stato ?? "";
        if (stato == "pagata")
        {
            stato = "emessa";
            effPagamento = "Pagata";
        }

        // Validazione stato fatturazione: valori ammessi '' | 'daEmettere' | 'emessa'
        if (stato != "" && stato != "daEmettere" && stato != "emessa")
            return Ok(ApiResponse<int>.Fail("Stato fatturazione non valido"));

        // Riga che nasce già pagata: registra subito l'audit chi/quando
        int? paidBy = null;
        DateTime? paidAt = null;
        if (string.Equals(effPagamento, "Pagata", StringComparison.OrdinalIgnoreCase))
        {
            paidBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null;
            paidAt = DateTime.Now;
        }

        int sortOrder = c.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM sal_rows WHERE project_id=@Pid",
            new { Pid = projectId });

        // Clamp difensivo: gg_saldo in [0, 3650] e iva_perc in [0, 100] quando non null
        // (previene overflow DATE_ADD e valori assurdi)
        int? ggSaldo = Clamp(req.GgSaldo, 0, 3650) ?? 0;
        // Regola di business: una riga NUOVA nasce sempre con IVA 22% (se diversa si
        // corregge a mano). Il default vale solo alla creazione: in update il valore
        // resta quello inviato dal client (anche svuotato).
        int? ivaPerc = Clamp(req.IvaPerc, 0, 100) ?? 22;
        int id = c.ExecuteScalar<int>(@"
            INSERT INTO sal_rows
                (project_id, step, perc, condizione, data_fatt, stato, sort_order, created_by,
                 iva_perc, gg_saldo, n_fatt, conto_sap, pagamento, data_pagamento, note, paid_by, paid_at)
            VALUES (@Pid, @Step, @Perc, @Condizione, @DataFatt, @Stato, @SortOrder, @CreatedBy,
                    @IvaPerc, @GgSaldo, @NFatt, @ContoSap, @Pagamento, @DataPagamento, @Note, @PaidBy, @PaidAt);
            SELECT LAST_INSERT_ID()",
            new
            {
                Pid = projectId,
                Step = Trunc(req.Step, 1000),
                req.Perc,
                Condizione = Trunc(req.Condizione, 200),
                req.DataFatt,
                Stato = stato,
                SortOrder = sortOrder,
                CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null,
                IvaPerc = ivaPerc,
                GgSaldo = ggSaldo,
                NFatt = Trunc(SanitizeNFatt(req.NFatt), 50),
                ContoSap = Trunc(req.ContoSap, 200),
                Pagamento = effPagamento,
                req.DataPagamento,
                Note = Trunc(req.Note, 2000),
                PaidBy = paidBy,
                PaidAt = paidAt
            });

        NotifyChanged("create_row", projectId);
        return Ok(ApiResponse<int>.Ok(id, "Step SAL aggiunto"));
    }

    [RequireProjectWritable(Tabella = "sal_rows")]
    [HttpPut("rows/{id}")]
    public IActionResult UpdateRow(int id, [FromBody] SalRowSaveRequest req)
    {
        using var c = _db.Open();

        // Recupera la riga attuale: lock su pagamento='Pagata', audit paid_by/paid_at
        // e valori correnti dei campi testo (da preservare se il client non li invia)
        var current = c.QueryFirstOrDefault<dynamic>(
            "SELECT stato, pagamento, project_id, paid_by, paid_at, n_fatt, conto_sap, note FROM sal_rows WHERE id=@Id", new { Id = id });
        string? currentPagamento = current != null ? (string?)current.pagamento : null;
        string? currentNFatt = current != null ? (string?)current.n_fatt : null;
        string? currentContoSap = current != null ? (string?)current.conto_sap : null;
        string? currentNote = current != null ? (string?)current.note : null;

        // Valori effettivi: null = campo non inviato dal client (pre-Fase 3) → preservare il valore corrente;
        // stringa vuota = svuota il campo.
        string effPagamento = req.Pagamento != null ? Trunc(req.Pagamento, 100) : (currentPagamento ?? "");
        string effNFatt = req.NFatt != null ? Trunc(SanitizeNFatt(req.NFatt), 50) : (currentNFatt ?? "");
        string effContoSap = req.ContoSap != null ? Trunc(req.ContoSap, 200) : (currentContoSap ?? "");
        string effNote = req.Note != null ? Trunc(req.Note, 2000) : (currentNote ?? "");

        // Compatibilità legacy: un client vecchio può ancora mandare stato='pagata' → emessa + Pagata
        string stato = req.Stato ?? "";
        if (stato == "pagata")
        {
            stato = "emessa";
            effPagamento = "Pagata";
        }

        // Validazione stato fatturazione: valori ammessi '' | 'daEmettere' | 'emessa'
        if (stato != "" && stato != "daEmettere" && stato != "emessa")
            return Ok(ApiResponse<int>.Fail("Stato fatturazione non valido"));

        // «Pagata» NON blocca più la riga: da uno stato di pagamento si deve poter tornare
        // indietro (un incasso segnato per errore va corretto). L'unico lock è la commessa
        // CHIUSA: lì il SAL è storia, e ci mette mano solo chi ha `action.sal_edit_closed`.
        bool wasPagata = string.Equals(currentPagamento, "Pagata", StringComparison.OrdinalIgnoreCase);
        if (current != null && !CanEditClosedSal()
            && IsProjectClosed(c, (int)current.project_id))
        {
            return Ok(ApiResponse<int>.Fail(
                "Commessa chiusa: il foglio SAL è in sola lettura (serve il permesso «Modifica SAL di commessa chiusa»)"));
        }

        // Transizioni paid_by/paid_at pilotate dal campo Pagamento effettivo (non più dallo stato)
        int? paidBy = null;
        DateTime? paidAt = null;

        if (string.Equals(effPagamento, "Pagata", StringComparison.OrdinalIgnoreCase))
        {
            if (wasPagata)
            {
                // Era già pagata: preserva l'audit esistente
                paidBy = (int?)current?.paid_by;
                paidAt = (DateTime?)current?.paid_at;
            }
            else
            {
                // Transizione a Pagata: registra chi e quando
                paidBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null;
                paidAt = DateTime.Now;
            }
        }

        // Clamp difensivo: gg_saldo in [0, 3650] e iva_perc in [0, 100] quando non null
        // (previene overflow DATE_ADD e valori assurdi)
        int? ggSaldo = Clamp(req.GgSaldo, 0, 3650) ?? 0;
        int? ivaPerc = Clamp(req.IvaPerc, 0, 100);

        int rows = c.Execute(@"
            UPDATE sal_rows SET
                step=@Step, perc=@Perc, condizione=@Condizione, data_fatt=@DataFatt, stato=@Stato,
                iva_perc=@IvaPerc, gg_saldo=@GgSaldo, n_fatt=@NFatt, conto_sap=@ContoSap,
                pagamento=@Pagamento, data_pagamento=@DataPagamento, note=@Note,
                paid_by=@PaidBy, paid_at=@PaidAt,
                row_version = row_version + 1, updated_at = CURRENT_TIMESTAMP
             WHERE id=@Id AND (@RowVersion IS NULL OR row_version=@RowVersion)",
            new
            {
                Step = Trunc(req.Step, 1000),
                req.Perc,
                Condizione = Trunc(req.Condizione, 200),
                req.DataFatt,
                Stato = stato,
                IvaPerc = ivaPerc,
                GgSaldo = ggSaldo,
                NFatt = effNFatt,
                ContoSap = effContoSap,
                Pagamento = effPagamento,
                req.DataPagamento,
                Note = effNote,
                PaidBy = paidBy,
                PaidAt = paidAt,
                Id = id,
                req.RowVersion
            });

        if (rows == 0)
        {
            int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_rows WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<int>.Fail(exists > 0 ? ConflictMessage : "Step SAL non trovato"));
        }

        int projectId = current != null ? (int)current.project_id : 0;
        NotifyChanged("update_row", projectId);
        return Ok(ApiResponse<int>.Ok(id, "Step SAL aggiornato"));
    }

    [RequireProjectWritable(Tabella = "sal_rows")]
    [HttpDelete("rows/{id}")]
    public IActionResult DeleteRow(int id, [FromQuery] int? rowVersion = null)
    {
        using var c = _db.Open();

        // Lock unico: commessa chiusa (una riga pagata resta eliminabile — vedi UpdateRow)
        var current = c.QueryFirstOrDefault<dynamic>(
            "SELECT pagamento, project_id FROM sal_rows WHERE id=@Id", new { Id = id });
        if (current != null && !CanEditClosedSal()
            && IsProjectClosed(c, (int)current.project_id))
        {
            return Ok(ApiResponse<bool>.Fail(
                "Commessa chiusa: il foglio SAL è in sola lettura (serve il permesso «Modifica SAL di commessa chiusa»)"));
        }

        int projectId = current != null ? (int)current.project_id : 0;
        // Optimistic lock opzionale: se rowVersion è valorizzato deve corrispondere
        int rows = c.Execute("DELETE FROM sal_rows WHERE id=@Id AND (@Rv IS NULL OR row_version=@Rv)",
            new { Id = id, Rv = rowVersion });
        if (rows == 0)
        {
            int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_rows WHERE id=@Id", new { Id = id });
            return Ok(ApiResponse<bool>.Fail(exists > 0 ? ConflictMessage : "Step SAL non trovato"));
        }

        NotifyChanged("delete_row", projectId);
        return Ok(ApiResponse<bool>.Ok(true, "Step SAL eliminato"));
    }

    [RequireProjectWritable]
    [HttpPost("rows/reorder")]
    public IActionResult Reorder([FromQuery] int projectId, [FromBody] SalReorderRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0) return Ok(ApiResponse<bool>.Ok(true));
        using var c = _db.Open();
        int order = 0;
        foreach (int id in req.Ids)
        {
            c.Execute("UPDATE sal_rows SET sort_order=@Sort WHERE id=@Id AND project_id=@Pid",
                new { Sort = order++, Id = id, Pid = projectId });
        }
        NotifyChanged("reorder_rows", projectId);
        return Ok(ApiResponse<bool>.Ok(true, "Ordine step aggiornato"));
    }

    [RequireProjectWritable]
    [HttpPost("project/{projectId}/seed-template")]
    public IActionResult SeedTemplate(int projectId)
    {
        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<int>.Fail("Commessa non trovata"));

        int existing = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_rows WHERE project_id=@Pid", new { Pid = projectId });
        if (existing > 0) return Ok(ApiResponse<int>.Fail("La commessa contiene già degli step SAL"));

        // Modello a 6 step allineato al prototipo Gestione_Commesse_V32 (04/08/2026):
        // le percentuali erano già le stesse (15/15/10/20/20/20), cambiano i testi e tre
        // condizioni di pagamento (step 2 e 3 passano da «A Vista» a «30 gg. dffm.»).
        // Vale solo per i SAL creati da qui in avanti: le commesse già compilate non si toccano.
        var steps = new[]
        {
            new { Step = "1° acconto all'ordine per inizio progettazione", Perc = 15.0m, Cond = "A Vista" },
            new { Step = "2° acconto dall'ordine", Perc = 15.0m, Cond = "30 gg. dffm." },
            new { Step = "Alla consegna ed accettazione del progetto e benestare per ordini materiali presso i fornitori", Perc = 10.0m, Cond = "30 gg. dffm." },
            new { Step = "Al sito pilota in ATEC – collaudo in bianco AT", Perc = 20.0m, Cond = "30 gg. dffm." },
            new { Step = "Alla consegna materiali", Perc = 20.0m, Cond = "30 gg. dffm." },
            new { Step = "Al collaudo presso sede Cliente", Perc = 20.0m, Cond = "30 gg. dffm." }
        };

        int sortOrder = 0;
        foreach (var s in steps)
        {
            // Le righe seed nascono con %IVA 22 e GG saldo 0 (default v10)
            c.Execute(@"
                INSERT INTO sal_rows (project_id, step, perc, condizione, iva_perc, gg_saldo, sort_order, created_by)
                VALUES (@Pid, @Step, @Perc, @Condizione, @IvaPerc, 0, @Sort, @CreatedBy)",
                new
                {
                    Pid = projectId,
                    s.Step,
                    s.Perc,
                    Condizione = s.Cond,
                    IvaPerc = 22,
                    Sort = sortOrder++,
                    CreatedBy = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null
                });
        }

        NotifyChanged("seed_template", projectId);
        return Ok(ApiResponse<int>.Ok(steps.Length, $"{steps.Length} step SAL inseriti"));
    }

    [HttpGet("conditions")]
    public IActionResult GetConditions()
    {
        using var c = _db.Open();
        var rows = c.Query<SalConditionDto>(@"
            SELECT id AS Id, label AS Label, sort_order AS SortOrder, is_active AS IsActive
            FROM sal_conditions ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<SalConditionDto>>.Ok(rows));
    }

    [HttpGet("conditions/active")]
    public IActionResult GetActiveConditions()
    {
        using var c = _db.Open();
        var rows = c.Query<SalConditionDto>(@"
            SELECT id AS Id, label AS Label, sort_order AS SortOrder, is_active AS IsActive
            FROM sal_conditions WHERE is_active=TRUE ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<SalConditionDto>>.Ok(rows));
    }

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("conditions")]
    public IActionResult CreateCondition([FromBody] SalConditionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label)) return Ok(ApiResponse<int>.Fail("Etichetta obbligatoria"));
        using var c = _db.Open();

        string label = Trunc(req.Label, 200);
        int exists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_conditions WHERE LOWER(label)=LOWER(@Lbl)", new { Lbl = label });
        if (exists > 0) return Ok(ApiResponse<int>.Fail("Condizione già esistente"));

        int sortOrder = c.ExecuteScalar<int>("SELECT COALESCE(MAX(sort_order), -1) + 1 FROM sal_conditions");

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO sal_conditions (label, sort_order, is_active)
            VALUES (@Label, @Sort, TRUE);
            SELECT LAST_INSERT_ID()",
            new { Label = label, Sort = sortOrder });

        NotifyLookupChanged();
        return Ok(ApiResponse<int>.Ok(id, "Condizione creata"));
    }

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPut("conditions/{id}")]
    public IActionResult UpdateCondition(int id, [FromBody] SalConditionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label)) return Ok(ApiResponse<int>.Fail("Etichetta obbligatoria"));
        using var c = _db.Open();

        int rows = c.Execute("UPDATE sal_conditions SET label=@Label WHERE id=@Id", new { Label = Trunc(req.Label, 200), Id = id });
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Condizione non trovata"));

        NotifyLookupChanged();
        return Ok(ApiResponse<int>.Ok(id, "Condizione aggiornata"));
    }

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPut("conditions/{id}/toggle-active")]
    public IActionResult ToggleActiveCondition(int id, [FromQuery] bool active)
    {
        using var c = _db.Open();
        int rows = c.Execute("UPDATE sal_conditions SET is_active=@Active WHERE id=@Id", new { Active = active, Id = id });
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Condizione non trovata"));

        NotifyLookupChanged();
        return Ok(ApiResponse<int>.Ok(id, "Condizione aggiornata"));
    }

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpDelete("conditions/{id}")]
    public IActionResult DeleteCondition(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM sal_conditions WHERE id=@Id", new { Id = id });
        if (rows == 0) return Ok(ApiResponse<bool>.Fail("Condizione non trovata"));
        NotifyLookupChanged();
        return Ok(ApiResponse<bool>.Ok(true, "Condizione eliminata"));
    }

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("conditions/reorder")]
    public IActionResult ReorderConditions([FromBody] SalReorderRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0) return Ok(ApiResponse<bool>.Ok(true));
        using var c = _db.Open();
        int order = 0;
        foreach (int id in req.Ids)
        {
            c.Execute("UPDATE sal_conditions SET sort_order=@Sort WHERE id=@Id",
                new { Sort = order++, Id = id });
        }
        NotifyLookupChanged();
        return Ok(ApiResponse<bool>.Ok(true, "Ordine condizioni aggiornato"));
    }

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("conditions/reset")]
    public IActionResult ResetConditions()
    {
        using var c = _db.Open();
        c.Execute("DELETE FROM sal_conditions");
        string[] standardConditions = new[] { "A Vista", "30 gg. dffm.", "60 gg. dffm.", "90 gg. dffm." };
        int order = 1;
        foreach (string cond in standardConditions)
        {
            c.Execute("INSERT INTO sal_conditions (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                new { Label = cond, Sort = order++ });
        }
        NotifyLookupChanged();
        return Ok(ApiResponse<bool>.Ok(true, "Condizioni di pagamento ripristinate allo standard"));
    }

    // ------------------------------------------------------------------
    // Anagrafiche SAL aggiuntive (causali Conto SAP, stati pagamento):
    // stesso CRUD delle condizioni, helper privati parametrizzati sul nome
    // tabella. Il nome tabella NON può essere un parametro Dapper: whitelist
    // hardcoded di costanti, mai input utente nella SQL.
    // ------------------------------------------------------------------
    private const string TableSapCausali = "sal_sap_causali";
    private const string TablePaymentStates = "sal_payment_states";

    private static string LookupTable(string table)
    {
        // Guardia whitelist: accetta solo le tabelle anagrafica note
        if (table != TableSapCausali && table != TablePaymentStates)
            throw new ArgumentException($"Tabella anagrafica SAL non ammessa: {table}");
        return table;
    }

    // Voci di sistema di sal_payment_states: 'Pagata' e 'Parzialmente Pagata' hanno semantica
    // cablata nel codice (lock righe, audit, bucket Cash Flow) → non rinominabili né eliminabili.
    // I loro COLORI restano però modificabili (pura estetica, nessuna semantica).
    private static bool IsSystemPaymentLabel(string? label) =>
        string.Equals(label, "Pagata", StringComparison.OrdinalIgnoreCase)
        || string.Equals(label, "Parzialmente Pagata", StringComparison.OrdinalIgnoreCase);

    private static bool IsSystemPaymentState(MySqlConnector.MySqlConnection c, string table, int id)
    {
        if (table != TablePaymentStates) return false;
        string? label = c.ExecuteScalar<string?>(
            "SELECT label FROM sal_payment_states WHERE id=@Id", new { Id = id });
        return IsSystemPaymentLabel(label);
    }

    private IActionResult GetLookupRows(string table, bool activeOnly)
    {
        string t = LookupTable(table);
        // Solo sal_payment_states ha le colonne colore configurabili: per le altre anagrafiche
        // i campi ColorBg/ColorFg del DTO restano null (nessuna colonna selezionata).
        string colorColumns = t == TablePaymentStates ? ", color_bg AS ColorBg, color_fg AS ColorFg" : "";
        using var c = _db.Open();
        var rows = c.Query<SalConditionDto>($@"
            SELECT id AS Id, label AS Label, sort_order AS SortOrder, is_active AS IsActive{colorColumns}
            FROM {t}{(activeOnly ? " WHERE is_active=TRUE" : "")} ORDER BY sort_order, label").ToList();
        return Ok(ApiResponse<List<SalConditionDto>>.Ok(rows));
    }

    private IActionResult CreateLookupRow(string table, SalConditionSaveRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Label)) return Ok(ApiResponse<int>.Fail("Etichetta obbligatoria"));
        string t = LookupTable(table);

        // Colori opzionali: considerati SOLO per gli stati pagamento (le altre anagrafiche li ignorano)
        string? colorBg = null;
        string? colorFg = null;
        if (t == TablePaymentStates)
        {
            if (!TryNormalizeColor(req.ColorBg, out colorBg) || !TryNormalizeColor(req.ColorFg, out colorFg))
                return Ok(ApiResponse<int>.Fail("Colore non valido: formato ammesso #RRGGBB o #RRGGBBAA"));
        }

        using var c = _db.Open();

        string label = Trunc(req.Label, 200);
        int exists = c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {t} WHERE LOWER(label)=LOWER(@Lbl)", new { Lbl = label });
        if (exists > 0) return Ok(ApiResponse<int>.Fail("Voce già esistente"));

        int sortOrder = c.ExecuteScalar<int>($"SELECT COALESCE(MAX(sort_order), -1) + 1 FROM {t}");

        int id;
        if (t == TablePaymentStates)
        {
            id = c.ExecuteScalar<int>(@"
                INSERT INTO sal_payment_states (label, sort_order, is_active, color_bg, color_fg)
                VALUES (@Label, @Sort, TRUE, @ColorBg, @ColorFg);
                SELECT LAST_INSERT_ID()",
                new { Label = label, Sort = sortOrder, ColorBg = colorBg, ColorFg = colorFg });
        }
        else
        {
            id = c.ExecuteScalar<int>($@"
                INSERT INTO {t} (label, sort_order, is_active)
                VALUES (@Label, @Sort, TRUE);
                SELECT LAST_INSERT_ID()",
                new { Label = label, Sort = sortOrder });
        }

        NotifyLookupChanged();
        return Ok(ApiResponse<int>.Ok(id, "Voce creata"));
    }

    private IActionResult UpdateLookupRow(string table, int id, SalConditionSaveRequest req)
    {
        string t = LookupTable(table);

        // Colori opzionali: considerati SOLO per gli stati pagamento (le altre anagrafiche li ignorano)
        string? colorBg = null;
        string? colorFg = null;
        if (t == TablePaymentStates)
        {
            if (!TryNormalizeColor(req.ColorBg, out colorBg) || !TryNormalizeColor(req.ColorFg, out colorFg))
                return Ok(ApiResponse<int>.Fail("Colore non valido: formato ammesso #RRGGBB o #RRGGBBAA"));
        }

        using var c = _db.Open();

        string label = Trunc(req.Label, 200);

        if (t == TablePaymentStates)
        {
            string? currentLabel = c.ExecuteScalar<string?>(
                "SELECT label FROM sal_payment_states WHERE id=@Id", new { Id = id });
            if (currentLabel == null) return Ok(ApiResponse<int>.Fail("Voce non trovata"));

            // Etichetta vuota = aggiorna SOLO i colori, per TUTTE le voci (sistema e custom):
            // è il contratto usato dall'editor colori del client, che così non ritrasmette
            // l'etichetta in cache e non sovrascrive un rename concorrente di un altro utente.
            bool colorsOnly = string.IsNullOrWhiteSpace(label);

            // Voce di sistema: il rename resta bloccato (semantica cablata sull'etichetta),
            // MA i colori sono modificabili → etichetta identica all'attuale = solo colori.
            if (IsSystemPaymentLabel(currentLabel))
            {
                if (!colorsOnly && !string.Equals(label, currentLabel, StringComparison.OrdinalIgnoreCase))
                    return Ok(ApiResponse<int>.Fail("Voce di sistema: non può essere rinominata o eliminata"));
                colorsOnly = true;
            }

            if (colorsOnly)
            {
                c.Execute("UPDATE sal_payment_states SET color_bg=@ColorBg, color_fg=@ColorFg WHERE id=@Id",
                    new { ColorBg = colorBg, ColorFg = colorFg, Id = id });
                NotifyLookupChanged();
                return Ok(ApiResponse<int>.Ok(id, "Voce aggiornata"));
            }
        }

        if (string.IsNullOrWhiteSpace(label)) return Ok(ApiResponse<int>.Fail("Etichetta obbligatoria"));

        // Check duplicati case-insensitive (come in CreateLookupRow), escludendo la voce corrente
        int exists = c.ExecuteScalar<int>($"SELECT COUNT(*) FROM {t} WHERE LOWER(label)=LOWER(@Lbl) AND id<>@Id", new { Lbl = label, Id = id });
        if (exists > 0) return Ok(ApiResponse<int>.Fail("Voce già esistente"));

        int rows;
        if (t == TablePaymentStates)
        {
            // Stato pagamento non di sistema: label e colori aggiornati insieme
            rows = c.Execute("UPDATE sal_payment_states SET label=@Label, color_bg=@ColorBg, color_fg=@ColorFg WHERE id=@Id",
                new { Label = label, ColorBg = colorBg, ColorFg = colorFg, Id = id });
        }
        else
        {
            rows = c.Execute($"UPDATE {t} SET label=@Label WHERE id=@Id", new { Label = label, Id = id });
        }
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Voce non trovata"));

        NotifyLookupChanged();
        return Ok(ApiResponse<int>.Ok(id, "Voce aggiornata"));
    }

    private IActionResult ToggleActiveLookupRow(string table, int id, bool active)
    {
        string t = LookupTable(table);
        using var c = _db.Open();
        int rows = c.Execute($"UPDATE {t} SET is_active=@Active WHERE id=@Id", new { Active = active, Id = id });
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Voce non trovata"));

        NotifyLookupChanged();
        return Ok(ApiResponse<int>.Ok(id, "Voce aggiornata"));
    }

    private IActionResult DeleteLookupRow(string table, int id)
    {
        string t = LookupTable(table);
        using var c = _db.Open();

        // Le voci di sistema degli stati pagamento non si eliminano (semantica cablata nel codice)
        if (IsSystemPaymentState(c, t, id))
            return Ok(ApiResponse<bool>.Fail("Voce di sistema: non può essere rinominata o eliminata"));

        int rows = c.Execute($"DELETE FROM {t} WHERE id=@Id", new { Id = id });
        if (rows == 0) return Ok(ApiResponse<bool>.Fail("Voce non trovata"));
        NotifyLookupChanged();
        return Ok(ApiResponse<bool>.Ok(true, "Voce eliminata"));
    }

    private IActionResult ReorderLookupRows(string table, SalReorderRequest req)
    {
        if (req?.Ids == null || req.Ids.Count == 0) return Ok(ApiResponse<bool>.Ok(true));
        string t = LookupTable(table);
        using var c = _db.Open();
        int order = 0;
        foreach (int id in req.Ids)
        {
            c.Execute($"UPDATE {t} SET sort_order=@Sort WHERE id=@Id",
                new { Sort = order++, Id = id });
        }
        NotifyLookupChanged();
        return Ok(ApiResponse<bool>.Ok(true, "Ordine aggiornato"));
    }

    private IActionResult ResetLookupRows(string table, string[] defaults, string message)
    {
        string t = LookupTable(table);
        using var c = _db.Open();
        c.Execute($"DELETE FROM {t}");
        int order = 1;
        foreach (string label in defaults)
        {
            c.Execute($"INSERT INTO {t} (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                new { Label = label, Sort = order++ });
        }
        NotifyLookupChanged();
        return Ok(ApiResponse<bool>.Ok(true, message));
    }

    // --- Causali Conto SAP (/api/sal/sap-causali) ---

    [HttpGet("sap-causali")]
    public IActionResult GetSapCausali() => GetLookupRows(TableSapCausali, activeOnly: false);

    [HttpGet("sap-causali/active")]
    public IActionResult GetActiveSapCausali() => GetLookupRows(TableSapCausali, activeOnly: true);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("sap-causali")]
    public IActionResult CreateSapCausale([FromBody] SalConditionSaveRequest req) => CreateLookupRow(TableSapCausali, req);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPut("sap-causali/{id}")]
    public IActionResult UpdateSapCausale(int id, [FromBody] SalConditionSaveRequest req) => UpdateLookupRow(TableSapCausali, id, req);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPut("sap-causali/{id}/toggle-active")]
    public IActionResult ToggleActiveSapCausale(int id, [FromQuery] bool active) => ToggleActiveLookupRow(TableSapCausali, id, active);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpDelete("sap-causali/{id}")]
    public IActionResult DeleteSapCausale(int id) => DeleteLookupRow(TableSapCausali, id);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("sap-causali/reorder")]
    public IActionResult ReorderSapCausali([FromBody] SalReorderRequest req) => ReorderLookupRows(TableSapCausali, req);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("sap-causali/reset")]
    public IActionResult ResetSapCausali() =>
        ResetLookupRows(TableSapCausali, new[] { "Acconto", "Ricavo" }, "Causali Conto SAP ripristinate allo standard");

    // --- Stati Pagamento (/api/sal/payment-states) ---

    [HttpGet("payment-states")]
    public IActionResult GetPaymentStates() => GetLookupRows(TablePaymentStates, activeOnly: false);

    [HttpGet("payment-states/active")]
    public IActionResult GetActivePaymentStates() => GetLookupRows(TablePaymentStates, activeOnly: true);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("payment-states")]
    public IActionResult CreatePaymentState([FromBody] SalConditionSaveRequest req) => CreateLookupRow(TablePaymentStates, req);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPut("payment-states/{id}")]
    public IActionResult UpdatePaymentState(int id, [FromBody] SalConditionSaveRequest req) => UpdateLookupRow(TablePaymentStates, id, req);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPut("payment-states/{id}/toggle-active")]
    public IActionResult ToggleActivePaymentState(int id, [FromQuery] bool active) => ToggleActiveLookupRow(TablePaymentStates, id, active);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpDelete("payment-states/{id}")]
    public IActionResult DeletePaymentState(int id) => DeleteLookupRow(TablePaymentStates, id);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("payment-states/reorder")]
    public IActionResult ReorderPaymentStates([FromBody] SalReorderRequest req) => ReorderLookupRows(TablePaymentStates, req);

    [ScritturaNonDiCommessa("Anagrafica del SAL condivisa da tutte le commesse, non dati di una commessa")]
    [HttpPost("payment-states/reset")]
    public IActionResult ResetPaymentStates()
    {
        // Reset dedicato (non passa da ResetLookupRows): ripristina anche i COLORI di default
        // delle voci di sistema — verde pastello per Pagata, rosso pastello per Parzialmente Pagata
        // (stessi valori del seed v23 in DbService/SalDbService).
        using var c = _db.Open();
        c.Execute("DELETE FROM sal_payment_states");

        (string Label, string ColorBg, string ColorFg)[] defaults = new (string, string, string)[]
        {
            ("Pagata", "#D1FAE5", "#065F46"),
            ("Parzialmente Pagata", "#FEE2E2", "#991B1B")
        };
        int order = 1;
        foreach ((string Label, string ColorBg, string ColorFg) state in defaults)
        {
            c.Execute(@"INSERT INTO sal_payment_states (label, sort_order, is_active, color_bg, color_fg)
                VALUES (@Label, @Sort, TRUE, @ColorBg, @ColorFg)",
                new { state.Label, Sort = order++, state.ColorBg, state.ColorFg });
        }
        NotifyLookupChanged();
        return Ok(ApiResponse<bool>.Ok(true, "Stati pagamento ripristinati allo standard"));
    }


    [HttpGet("prospetto")]
    public IActionResult GetProspetto()
    {
        using var c = _db.Open();
        // Regola inclusione v10: ipotesi non ancora emesse + fatture emesse in attesa di incasso
        // (tutte le righe, non più le prime 2 per commessa). Data prevista saldo derivata, mai persistita.
        var rows = c.Query<SalProspettoRowDto>($@"
            SELECT t.project_id AS ProjectId, p.code AS Code,
                   COALESCE(cu.company_name, ps.cliente, '') AS Cliente,
                   t.step AS Step, t.perc AS Perc, t.condizione AS Condizione, t.data_fatt AS DataFatt,
                   (ps.valore * t.perc / 100) AS Importo,
                   t.row_num AS Ord,
                   t.gg_saldo AS GgSaldo, t.data_saldo AS DataSaldo,
                   t.stato AS Stato, t.pagamento AS Pagamento,
                   CASE
                       WHEN t.pagamento <> 'Pagata' AND t.data_saldo IS NOT NULL AND t.data_saldo < CURDATE() THEN 'incasso'
                       WHEN t.stato <> 'emessa' AND t.data_fatt <= CURDATE() THEN 'warn'
                       WHEN t.stato <> 'emessa' AND CURDATE() >= DATE_SUB(DATE_SUB(t.data_fatt, INTERVAL WEEKDAY(t.data_fatt) DAY), INTERVAL 7 DAY) THEN 'pre'
                       WHEN t.stato = 'emessa' THEN 'attesa'
                       ELSE ''
                   END AS Alert
            FROM (
                SELECT id, project_id, step, perc, condizione, data_fatt, stato, pagamento, gg_saldo,
                       DATE_ADD(data_fatt, INTERVAL gg_saldo DAY) AS data_saldo,
                       ROW_NUMBER() OVER (PARTITION BY project_id ORDER BY data_fatt ASC, id ASC) AS row_num
                FROM sal_rows
                WHERE data_fatt IS NOT NULL
                  AND (stato <> 'emessa' OR (pagamento <> 'Pagata' AND gg_saldo IS NOT NULL))
            ) t
            JOIN projects p ON p.id = t.project_id
            LEFT JOIN project_sal ps ON ps.project_id = t.project_id
            LEFT JOIN customers cu ON cu.id = p.customer_id
            WHERE {ProjectScope}
            ORDER BY {ProjectSorting.OrderBy("p")}, t.data_fatt ASC").ToList();

        // #91: gli importi (ps.valore × perc) sono dati economici — azzerati QUI per chi
        // non ha `sal.economics`, come nel summary: il client nasconde le colonne, ma la
        // risposta di rete deve essere pulita comunque.
        if (!CanSeeEconomics())
            foreach (SalProspettoRowDto r in rows)
                r.Importo = null;

        return Ok(ApiResponse<List<SalProspettoRowDto>>.Ok(rows));
    }

    [HttpGet("economics")]
    public IActionResult GetEconomics()
    {
        // Dati economici globali: serve la funzione `sal.economics` → 403 esplicito
        if (!CanSeeEconomics())
            return StatusCode(403, ApiResponse<SalEconomicsDto>.Fail("Non autorizzato"));

        using var c = _db.Open();

        // Headers: TUTTI i project_sal delle commesse attive, anche senza righe SAL
        // (servono al totale Ordini del Cash Flow — card v10 "Totale Ordini commesse Attive")
        var headers = c.Query<SalEconomicsHeaderDto>($@"
            SELECT ps.project_id AS ProjectId, p.code AS Code,
                   COALESCE(cu.company_name, ps.cliente, '') AS Cliente, ps.valore AS Valore
            FROM project_sal ps
            JOIN projects p ON p.id = ps.project_id
            LEFT JOIN customers cu ON cu.id = p.customer_id
            WHERE {ProjectScope}
            ORDER BY {ProjectSorting.OrderBy("p")}").ToList();

        // Rows: dettaglio step delle sole commesse attive (coerenza col Prospetto)
        var rows = c.Query<SalEconomicsRowDto>($@"
            SELECT sr.project_id AS ProjectId, p.code AS Code,
                   COALESCE(cu.company_name, ps.cliente, '') AS Cliente,
                   ps.valore AS Valore, sr.step AS Step, sr.perc AS Perc,
                   (ps.valore * sr.perc / 100) AS Importo,
                   sr.iva_perc AS IvaPerc,
                   (ps.valore * sr.perc / 100 * COALESCE(sr.iva_perc, 0) / 100) AS Iva,
                   (ps.valore * sr.perc / 100 * (1 + COALESCE(sr.iva_perc, 0) / 100)) AS TotIva,
                   sr.condizione AS Condizione, sr.data_fatt AS DataFatt, sr.gg_saldo AS GgSaldo,
                   DATE_ADD(sr.data_fatt, INTERVAL sr.gg_saldo DAY) AS DataSaldo,
                   sr.stato AS Stato, sr.pagamento AS Pagamento
            FROM sal_rows sr
            JOIN projects p ON p.id = sr.project_id
            LEFT JOIN project_sal ps ON ps.project_id = sr.project_id
            LEFT JOIN customers cu ON cu.id = p.customer_id
            WHERE {ProjectScope}
            ORDER BY {ProjectSorting.OrderBy("p")}, sr.data_fatt").ToList();

        return Ok(ApiResponse<SalEconomicsDto>.Ok(new SalEconomicsDto { Headers = headers, Rows = rows }));
    }

    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        using var c = _db.Open();
        // Aperte = non ancora emesse (include il futuro 'daEmettere').
        // Warn/Pre/Incasso: classificazione per riga MUTUAMENTE ESCLUSIVA con la stessa
        // priorità del prospetto (incasso > warn > pre) via CASE unico — una riga con
        // saldo scaduto conta SOLO come incasso. Solo commesse ACTIVE (coerenza /prospetto).
        var rows = c.Query<SalSummaryDto>($@"
            SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title,
                   COALESCE(ps.po, '') AS Po,
                   COALESCE(ps.rif_offerta, '') AS RifOfferta,
                   ps.valore AS Valore,
                   COUNT(*) AS Total,
                   COALESCE(SUM(t.data_fatt IS NOT NULL AND t.stato <> 'emessa'), 0) AS Open,
                   COALESCE(SUM(t.cls = 'warn'), 0) AS Warn,
                   COALESCE(SUM(t.cls = 'pre'), 0) AS Pre,
                   COALESCE(SUM(t.cls = 'incasso'), 0) AS Incasso,
                   COALESCE(SUM(t.perc), 0) AS PercTotal,
                   COALESCE(SUM(CASE WHEN t.pagamento = 'Pagata' THEN t.perc ELSE 0 END), 0) AS PercPaid
            FROM (
                SELECT project_id, stato, data_fatt, perc, pagamento,
                       CASE
                           WHEN pagamento <> 'Pagata' AND data_fatt IS NOT NULL AND gg_saldo IS NOT NULL
                                AND DATE_ADD(data_fatt, INTERVAL gg_saldo DAY) < CURDATE() THEN 'incasso'
                           WHEN stato <> 'emessa' AND data_fatt IS NOT NULL
                                AND data_fatt <= CURDATE() THEN 'warn'
                           WHEN stato <> 'emessa' AND data_fatt IS NOT NULL
                                AND CURDATE() >= DATE_SUB(DATE_SUB(data_fatt,
                                     INTERVAL WEEKDAY(data_fatt) DAY), INTERVAL 7 DAY) THEN 'pre'
                           ELSE ''
                       END AS cls
                FROM sal_rows
            ) t
            JOIN projects p ON p.id = t.project_id
            LEFT JOIN project_sal ps ON ps.project_id = p.id
            WHERE {ProjectScope}
            GROUP BY p.id, p.code, p.title, ps.po, ps.rif_offerta, ps.valore
            HAVING COUNT(*) > 0
            ORDER BY {ProjectSorting.OrderBy("p")}").ToList();

        // L'Importo Ordine è un dato economico: chi non ha `sal.economics` riceve Valore
        // null (azzerato QUI, mai lasciato al client). Contatori e percentuali restano
        // visibili a chiunque abbia nav.sal, come prima della #91.
        if (!CanSeeEconomics())
            foreach (var r in rows)
                r.Valore = null;

        return Ok(ApiResponse<List<SalSummaryDto>>.Ok(rows));
    }
}

