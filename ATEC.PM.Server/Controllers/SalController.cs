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

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/sal")]
[Authorize]
public class SalController : ControllerBase
{
    private readonly DbService _db;
    private readonly IHubContext<ProjectHub> _hub;

    public SalController(DbService db, IHubContext<ProjectHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    public const string ConflictMessage = "CONFLITTO: record SAL modificato da un altro utente";

    // Periodo del controllo periodico del prospetto (giorni)
    private const int ProspettoCheckPeriodDays = 15;

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

    [HttpPut("header")]
    public IActionResult UpdateHeader([FromQuery] int projectId, [FromBody] SalHeaderSaveRequest req)
    {
        if (projectId <= 0) return Ok(ApiResponse<int>.Fail("projectId obbligatorio"));
        using var c = _db.Open();

        // Po/RifOfferta: null = campo non inviato dal client (pre-Fase 3) → COALESCE preserva il valore corrente;
        // stringa vuota = svuota il campo.
        // Il cliente non si modifica dal foglio SAL: proviene dall'anagrafica commessa.
        int rows = c.Execute(@"
            UPDATE project_sal SET
                valore=@Valore,
                po=COALESCE(@Po, po), rif_offerta=COALESCE(@RifOfferta, rif_offerta),
                row_version = row_version + 1, updated_at = CURRENT_TIMESTAMP
             WHERE project_id=@Pid AND (@RowVersion IS NULL OR row_version=@RowVersion)",
            new
            {
                req.Valore,
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

    [HttpPut("rows/{id}")]
    public IActionResult UpdateRow(int id, [FromBody] SalRowSaveRequest req)
    {
        using var c = _db.Open();
        string? role = User.FindFirst(ClaimTypes.Role)?.Value;

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

        // Lock: riga con fattura pagata modificabile solo da ADMIN (confronto case-insensitive,
        // coerente con la collation MySQL)
        bool wasPagata = string.Equals(currentPagamento, "Pagata", StringComparison.OrdinalIgnoreCase);
        if (wasPagata && role != "ADMIN")
        {
            return Ok(ApiResponse<int>.Fail("Riga bloccata: fattura pagata (solo ADMIN può modificarla)"));
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

    [HttpDelete("rows/{id}")]
    public IActionResult DeleteRow(int id, [FromQuery] int? rowVersion = null)
    {
        using var c = _db.Open();
        string? role = User.FindFirst(ClaimTypes.Role)?.Value;

        // Lock: riga con fattura pagata eliminabile solo da ADMIN
        var current = c.QueryFirstOrDefault<dynamic>(
            "SELECT pagamento, project_id FROM sal_rows WHERE id=@Id", new { Id = id });
        if (current != null)
        {
            // Confronto case-insensitive, coerente con la collation MySQL
            string? currentPagamento = (string?)current.pagamento;
            if (string.Equals(currentPagamento, "Pagata", StringComparison.OrdinalIgnoreCase) && role != "ADMIN")
            {
                return Ok(ApiResponse<bool>.Fail("Riga bloccata: fattura pagata (solo ADMIN può eliminarla)"));
            }
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

    [HttpPost("project/{projectId}/seed-template")]
    public IActionResult SeedTemplate(int projectId)
    {
        using var c = _db.Open();
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM projects WHERE id=@Id", new { Id = projectId }) == 0)
            return Ok(ApiResponse<int>.Fail("Commessa non trovata"));

        int existing = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_rows WHERE project_id=@Pid", new { Pid = projectId });
        if (existing > 0) return Ok(ApiResponse<int>.Fail("La commessa contiene già degli step SAL"));

        var steps = new[]
        {
            new { Step = "1° acconto all'ordine", Perc = 15.0m, Cond = "A Vista" },
            new { Step = "2° acconto ad approvazione disegni", Perc = 15.0m, Cond = "A Vista" },
            new { Step = "3° acconto ad avviso merce pronta", Perc = 10.0m, Cond = "A Vista" },
            new { Step = "4° acconto a consegna/installazione", Perc = 20.0m, Cond = "30 gg. dffm." },
            new { Step = "5° acconto a collaudo", Perc = 20.0m, Cond = "30 gg. dffm." },
            new { Step = "Saldo a 30 gg. fine collaudo", Perc = 20.0m, Cond = "30 gg. dffm." }
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

    [HttpPut("conditions/{id}/toggle-active")]
    public IActionResult ToggleActiveCondition(int id, [FromQuery] bool active)
    {
        using var c = _db.Open();
        int rows = c.Execute("UPDATE sal_conditions SET is_active=@Active WHERE id=@Id", new { Active = active, Id = id });
        if (rows == 0) return Ok(ApiResponse<int>.Fail("Condizione non trovata"));

        NotifyLookupChanged();
        return Ok(ApiResponse<int>.Ok(id, "Condizione aggiornata"));
    }

    [HttpDelete("conditions/{id}")]
    public IActionResult DeleteCondition(int id)
    {
        using var c = _db.Open();
        int rows = c.Execute("DELETE FROM sal_conditions WHERE id=@Id", new { Id = id });
        if (rows == 0) return Ok(ApiResponse<bool>.Fail("Condizione non trovata"));
        NotifyLookupChanged();
        return Ok(ApiResponse<bool>.Ok(true, "Condizione eliminata"));
    }

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

    [HttpPost("sap-causali")]
    public IActionResult CreateSapCausale([FromBody] SalConditionSaveRequest req) => CreateLookupRow(TableSapCausali, req);

    [HttpPut("sap-causali/{id}")]
    public IActionResult UpdateSapCausale(int id, [FromBody] SalConditionSaveRequest req) => UpdateLookupRow(TableSapCausali, id, req);

    [HttpPut("sap-causali/{id}/toggle-active")]
    public IActionResult ToggleActiveSapCausale(int id, [FromQuery] bool active) => ToggleActiveLookupRow(TableSapCausali, id, active);

    [HttpDelete("sap-causali/{id}")]
    public IActionResult DeleteSapCausale(int id) => DeleteLookupRow(TableSapCausali, id);

    [HttpPost("sap-causali/reorder")]
    public IActionResult ReorderSapCausali([FromBody] SalReorderRequest req) => ReorderLookupRows(TableSapCausali, req);

    [HttpPost("sap-causali/reset")]
    public IActionResult ResetSapCausali() =>
        ResetLookupRows(TableSapCausali, new[] { "Acconto", "Ricavo" }, "Causali Conto SAP ripristinate allo standard");

    // --- Stati Pagamento (/api/sal/payment-states) ---

    [HttpGet("payment-states")]
    public IActionResult GetPaymentStates() => GetLookupRows(TablePaymentStates, activeOnly: false);

    [HttpGet("payment-states/active")]
    public IActionResult GetActivePaymentStates() => GetLookupRows(TablePaymentStates, activeOnly: true);

    [HttpPost("payment-states")]
    public IActionResult CreatePaymentState([FromBody] SalConditionSaveRequest req) => CreateLookupRow(TablePaymentStates, req);

    [HttpPut("payment-states/{id}")]
    public IActionResult UpdatePaymentState(int id, [FromBody] SalConditionSaveRequest req) => UpdateLookupRow(TablePaymentStates, id, req);

    [HttpPut("payment-states/{id}/toggle-active")]
    public IActionResult ToggleActivePaymentState(int id, [FromQuery] bool active) => ToggleActiveLookupRow(TablePaymentStates, id, active);

    [HttpDelete("payment-states/{id}")]
    public IActionResult DeletePaymentState(int id) => DeleteLookupRow(TablePaymentStates, id);

    [HttpPost("payment-states/reorder")]
    public IActionResult ReorderPaymentStates([FromBody] SalReorderRequest req) => ReorderLookupRows(TablePaymentStates, req);

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
        var rows = c.Query<SalProspettoRowDto>(@"
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
            WHERE p.status = 'ACTIVE'
            ORDER BY p.code, t.data_fatt ASC").ToList();

        return Ok(ApiResponse<List<SalProspettoRowDto>>.Ok(rows));
    }

    [HttpGet("prospetto/check")]
    public IActionResult GetProspettoCheck()
    {
        using var c = _db.Open();
        return Ok(ApiResponse<SalProspettoCheckDto>.Ok(LoadProspettoCheck(c)));
    }

    [HttpPost("prospetto/check")]
    public IActionResult ConfirmProspettoCheck()
    {
        using var c = _db.Open();
        // Registra la conferma del controllo periodico (storico: chi + quando)
        c.Execute("INSERT INTO sal_prospetto_checks (checked_by) VALUES (@By)",
            new { By = CurrentEmployeeId > 0 ? CurrentEmployeeId : (int?)null });

        // Broadcast globale (stesso meccanismo di NotifyChanged, senza commessa specifica):
        // il banner del controllo periodico degli altri client si aggiorna subito.
        _ = _hub.Clients.All.SendAsync("GlobalSalChanged", new { action = "prospetto_check", projectId = 0 });

        return Ok(ApiResponse<SalProspettoCheckDto>.Ok(LoadProspettoCheck(c), "Controllo prospetto registrato"));
    }

    // Stato del controllo periodico del prospetto: ultima conferma registrata + scadenza a 15 giorni
    private SalProspettoCheckDto LoadProspettoCheck(MySqlConnector.MySqlConnection c)
    {
        var check = c.QueryFirstOrDefault<SalProspettoCheckDto>(@"
            SELECT pc.checked_at AS CheckedAt,
                   COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS CheckedByName,
                   DATEDIFF(CURDATE(), DATE(pc.checked_at)) AS Days
            FROM sal_prospetto_checks pc
            LEFT JOIN employees e ON e.id = pc.checked_by
            ORDER BY pc.id DESC
            LIMIT 1");

        if (check == null)
        {
            // Nessun controllo mai registrato: il controllo è dovuto subito
            return new SalProspettoCheckDto { Due = true };
        }

        check.Due = check.Days.HasValue && check.Days.Value >= ProspettoCheckPeriodDays;
        check.NextDue = check.CheckedAt?.AddDays(ProspettoCheckPeriodDays);
        return check;
    }

    [HttpGet("economics")]
    public IActionResult GetEconomics()
    {
        // Dati economici globali: visibili solo ai ruoli PM/ADMIN → 403 esplicito (pattern TimesheetController)
        string? role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (role != "ADMIN" && role != "PM")
            return StatusCode(403, ApiResponse<SalEconomicsDto>.Fail("Non autorizzato"));

        using var c = _db.Open();

        // Headers: TUTTI i project_sal delle commesse attive, anche senza righe SAL
        // (servono al totale Ordini del Cash Flow — card v10 "Totale Ordini commesse Attive")
        var headers = c.Query<SalEconomicsHeaderDto>(@"
            SELECT ps.project_id AS ProjectId, p.code AS Code,
                   COALESCE(cu.company_name, ps.cliente, '') AS Cliente, ps.valore AS Valore
            FROM project_sal ps
            JOIN projects p ON p.id = ps.project_id
            LEFT JOIN customers cu ON cu.id = p.customer_id
            WHERE p.status = 'ACTIVE'
            ORDER BY p.code").ToList();

        // Rows: dettaglio step delle sole commesse attive (coerenza col Prospetto)
        var rows = c.Query<SalEconomicsRowDto>(@"
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
            WHERE p.status = 'ACTIVE'
            ORDER BY p.code, sr.data_fatt").ToList();

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
        var rows = c.Query<SalSummaryDto>(@"
            SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title,
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
            WHERE p.status = 'ACTIVE'
            GROUP BY p.id, p.code, p.title
            HAVING COUNT(*) > 0
            ORDER BY p.code").ToList();
        return Ok(ApiResponse<List<SalSummaryDto>>.Ok(rows));
    }
}

