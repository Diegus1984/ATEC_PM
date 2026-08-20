using System.Data;
using Dapper;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Fogli di calcolo a righe (blocco 5 del piano V32): il DETTAGLIO con cui è stato ottenuto
/// il totale di una voce di costo. Prima esisteva solo il totale, e il ragionamento che
/// l'aveva prodotto si perdeva alla prima riapertura.
///
/// Il foglio è generico e identificato da <c>calc_key</c>: oggi lo usa «Lavorazioni Officine»
/// a preventivo, domani le 4 calcolatrici della Trasferta (blocco 6) senza altre migrazioni.
///
/// L'importo di riga NON si prende mai dal client: il server ricalcola tutte le righe non
/// congelate a ogni salvataggio, così il totale letto dal conto economico è per costruzione
/// coerente con quello che si vede nella finestra di calcolo.
/// </summary>
public static class ProjectCalcSheets
{
    /// <summary>Ricarico di vendita ammesso (D4): sono MOLTIPLICATORI, non percentuali.</summary>
    public const decimal MinMarkup = 1.000m;
    public const decimal MaxMarkup = 9.999m;

    /// <summary>Testata del foglio, l'unica cosa che serve prima di leggerne le righe.</summary>
    private sealed class SheetHead
    {
        public int Id { get; set; }
        public int RowVersion { get; set; }
    }

    /// <summary>
    /// Foglio della commessa. Se non è mai stato compilato torna un foglio vuoto con
    /// <c>RowVersion = 0</c>: la riga di <c>project_calc_sheets</c> nasce al primo salvataggio,
    /// non alla prima lettura (una GET non deve scrivere).
    /// </summary>
    public static ProjectCalcSheetDto Load(IDbConnection c, int projectId, string calcKey)
    {
        var sheet = new ProjectCalcSheetDto { CalcKey = calcKey };

        SheetHead? head = c.QueryFirstOrDefault<SheetHead>(
            "SELECT id AS Id, row_version AS RowVersion FROM project_calc_sheets WHERE project_id=@Pid AND calc_key=@Key",
            new { Pid = projectId, Key = calcKey });

        if (head == null) return WithTotals(sheet);

        sheet.RowVersion = head.RowVersion;
        sheet.Rows = c.Query<ProjectCalcRowDto>(@"
            SELECT id AS Id, group_key AS GroupKey, description AS Description,
                   quantity AS Quantity, unit_cost AS UnitCost, multiplier AS Multiplier,
                   amount AS Amount,
                   amount_pinned AS AmountPinned, markup_value AS MarkupValue,
                   linked_source AS LinkedSource, sort_order AS SortOrder
            FROM project_calc_rows
            WHERE sheet_id = @Sid
            ORDER BY group_key, sort_order, id",
            new { Sid = head.Id }).ToList();

        return WithTotals(sheet);
    }

    /// <summary>
    /// Riscrive TUTTO il foglio (è la «Conferma» della finestra: una scrittura sola, con i
    /// valori definitivi). Restituisce il foglio ricaricato, oppure un messaggio d'errore.
    ///
    /// Sostituzione integrale invece di un diff per riga: la finestra di calcolo lavora su una
    /// copia locale e non fa commit per campo, quindi non esistono le due trappole del blocco 4
    /// (il refetch che cancella quello che si sta scrivendo, e i salvataggi di fila che si
    /// scartano a vicenda). Gli id delle righe cambiano a ogni conferma: nessuno ci si aggancia.
    /// </summary>
    public static (ProjectCalcSheetDto? Sheet, string? Error) Save(
        IDbConnection c, int projectId, string calcKey, ProjectCalcSheetSaveRequest req, int? userId)
    {
        List<ProjectCalcRowDto> rows = (req.Rows ?? new()).Where(r => !r.IsEmpty).ToList();

        foreach (ProjectCalcRowDto row in rows)
        {
            if (row.MarkupValue < MinMarkup || row.MarkupValue > MaxMarkup)
                return (null, $"Il ricarico deve essere un moltiplicatore tra {MinMarkup:0.000} e {MaxMarkup:0.000} (1,450 = +45%), non una percentuale.");
        }

        using IDbTransaction tx = c.BeginTransaction();

        bool projectExists = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM projects WHERE id=@Pid", new { Pid = projectId }, tx) > 0;
        if (!projectExists)
        {
            tx.Rollback();
            return (null, "Commessa non trovata.");
        }

        SheetHead? head = c.QueryFirstOrDefault<SheetHead>(
            "SELECT id AS Id, row_version AS RowVersion FROM project_calc_sheets WHERE project_id=@Pid AND calc_key=@Key FOR UPDATE",
            new { Pid = projectId, Key = calcKey }, tx);

        int sheetId;
        if (head == null)
        {
            // Prima compilazione. Il RowVersion atteso dal client può solo essere 0 o assente:
            // qualsiasi altro valore vuol dire che nel frattempo qualcuno ha svuotato il foglio.
            if (req.RowVersion is > 0)
            {
                tx.Rollback();
                return (null, "Il calcolo è stato azzerato da un altro utente: riapri la finestra prima di risalvare.");
            }

            sheetId = c.ExecuteScalar<int>(@"
                INSERT INTO project_calc_sheets (project_id, calc_key, row_version, updated_by)
                VALUES (@Pid, @Key, 1, @Uid);
                SELECT LAST_INSERT_ID()",
                new { Pid = projectId, Key = calcKey, Uid = userId }, tx);
        }
        else
        {
            if (req.RowVersion != null && req.RowVersion != head.RowVersion)
            {
                tx.Rollback();
                return (null, "Calcolo modificato da un altro utente: riapri la finestra prima di risalvare.");
            }

            sheetId = head.Id;
            c.Execute("UPDATE project_calc_sheets SET row_version = row_version + 1, updated_by = @Uid WHERE id = @Sid",
                new { Sid = sheetId, Uid = userId }, tx);
            c.Execute("DELETE FROM project_calc_rows WHERE sheet_id = @Sid", new { Sid = sheetId }, tx);
        }

        int sortOrder = 0;
        foreach (ProjectCalcRowDto row in rows)
        {
            // L'importo lo decide il server: quello congelato se c'è il lucchetto, altrimenti
            // il calcolo. Un lucchetto senza importo non congela nulla e torna automatico.
            bool pinned = row.AmountPinned && row.Amount != null;
            decimal? amount = pinned ? row.Amount : row.ComputedAmount;

            c.Execute(@"
                INSERT INTO project_calc_rows
                    (sheet_id, group_key, description, quantity, unit_cost, multiplier, amount,
                     amount_pinned, markup_value, linked_source, sort_order)
                VALUES (@Sid, @GroupKey, @Description, @Quantity, @UnitCost, @Multiplier, @Amount,
                        @Pinned, @Markup, @LinkedSource, @SortOrder)",
                new
                {
                    Sid = sheetId,
                    GroupKey = (row.GroupKey ?? "").Trim(),
                    Description = (row.Description ?? "").Trim(),
                    row.Quantity,
                    row.UnitCost,
                    row.Multiplier,
                    Amount = amount,
                    Pinned = pinned,
                    Markup = row.MarkupValue,
                    LinkedSource = (row.LinkedSource ?? "").Trim(),
                    SortOrder = ++sortOrder,
                }, tx);
        }

        tx.Commit();
        return (Load(c, projectId, calcKey), null);
    }

    /// <summary>
    /// Totali di un foglio: «—» (NULL) solo se NESSUNA riga ha un importo — 0,00 € vorrebbe
    /// dire «vale zero», che è un'altra cosa. Stessa regola del Totale Ordine.
    /// </summary>
    public static ProjectCalcSheetDto WithTotals(ProjectCalcSheetDto sheet)
    {
        sheet.Total = SumOrNull(sheet.Rows, r => r.EffectiveAmount);
        sheet.SaleTotal = SumOrNull(sheet.Rows, r => r.SaleAmount);
        return sheet;
    }

    /// <summary>Totale di una sezione del foglio (es. le sole Officine interne). NULL con la stessa regola.</summary>
    public static decimal? GroupTotal(ProjectCalcSheetDto sheet, string groupKey) =>
        SumOrNull(sheet.Rows.Where(r => r.GroupKey == groupKey), r => r.EffectiveAmount);

    /// <summary>
    /// Rigenera le righe AUTOMATICHE di un foglio senza toccare quelle scritte a mano: si
    /// buttano via solo quelle il cui <c>linked_source</c> comincia per <paramref name="sourcePrefix"/>
    /// e si rimettono quelle nuove. È il pattern di <c>project_cashflow_categories</c>.
    /// Da chiamare FUORI da una transazione: <see cref="Save"/> ne apre una sua.
    /// </summary>
    public static void SyncLinkedRows(
        IDbConnection c, int projectId, string calcKey, string sourcePrefix,
        List<ProjectCalcRowDto> generated, int? userId)
    {
        ProjectCalcSheetDto sheet = Load(c, projectId, calcKey);
        List<ProjectCalcRowDto> manual = sheet.Rows
            .Where(r => !r.LinkedSource.StartsWith(sourcePrefix, StringComparison.Ordinal))
            .ToList();

        // Niente da scrivere e niente da ripulire: non tocchiamo il foglio (né il row_version,
        // che farebbe fallire il salvataggio di chi ha la finestra aperta).
        if (generated.Count == 0 && manual.Count == sheet.Rows.Count) return;

        Save(c, projectId, calcKey, new ProjectCalcSheetSaveRequest
        {
            RowVersion = null,   // rigenerazione di servizio: non è una modifica dell'utente
            Rows = manual.Concat(generated).ToList(),
        }, userId);
    }

    /// <summary>
    /// Cancella dei fogli per chiave. Serve ai fogli con owner polimorfo (le 4 calcolatrici di
    /// una riga di trasferta): la chiave porta l'id della riga, quindi non c'è nessuna FK che
    /// li porti via da soli.
    /// </summary>
    public static void DeleteSheets(IDbConnection c, int projectId, IEnumerable<string> calcKeys)
    {
        List<string> keys = calcKeys.ToList();
        if (keys.Count == 0) return;
        c.Execute("DELETE FROM project_calc_sheets WHERE project_id = @Pid AND calc_key IN @Keys",
            new { Pid = projectId, Keys = keys });
    }

    private static decimal? SumOrNull(
        IEnumerable<ProjectCalcRowDto> rows, Func<ProjectCalcRowDto, decimal?> pick)
    {
        decimal sum = 0;
        bool any = false;
        foreach (ProjectCalcRowDto row in rows)
        {
            decimal? value = pick(row);
            if (value == null) continue;
            sum += value.Value;
            any = true;
        }
        return any ? sum : null;
    }
}
