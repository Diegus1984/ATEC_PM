using System.Data;
using Dapper;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Pezzi condivisi del Bilancio commessa (blocco 4 del piano V32): righe dell'ordine
/// cliente e scomposizione dei costi a consuntivo. Stanno qui e non nei controller
/// perché li usano sia il conto economico della singola commessa
/// (<c>BudgetVsActualController</c>) sia la pagina cross-commessa (<c>BilancioController</c>),
/// e i numeri delle due schermate devono coincidere.
/// </summary>
public static class ProjectEconomics
{
    /// <summary>
    /// Costo di una riga DDP: quantità × costo unitario. Filtri obbligatori per QUALSIASI
    /// query economica sull'officina, altrimenti i totali non tornano con la dashboard:
    ///  (a) esclusione degli stati dell'aggregazione A9 («escluso da totale»), configurabile
    ///      dall'admin — array vuoto = nessuna esclusione, semantica voluta;
    ///  (b) dedup dei padri: una riga che è padre di almeno un'altra è l'assieme di righe
    ///      già esplose dalla composizione Codex e conterebbe due volte.
    /// </summary>
    public const string OfficinaParentDedup =
        "id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL)";

    /// <summary>
    /// Gemella di <see cref="OfficinaParentDedup"/> per la DDP Commerciale, obbligatoria da
    /// quando i componenti di una composizione si dividono fra le due distinte (#119).
    ///
    /// <para>🪤 Attenzione a come funziona, perché è il contrario della griglia: <b>si buttano
    /// via i padri e si contano i figli</b>. In griglia il costo del gruppo si arrotola
    /// nell'intestazione e i componenti non sommano; qui i componenti sono righe di costo
    /// vere e l'intestazione è solo un'etichetta. Senza questa esclusione l'intestazione
    /// creata in <c>bom_items</c> conterebbe una seconda volta il costo dei suoi figli — che
    /// è esattamente l'errore che la dedup dell'officina evita dal blocco 4.</para>
    /// </summary>
    public const string CommercialeParentDedup =
        "id NOT IN (SELECT DISTINCT parent_bom_item_id FROM bom_items WHERE parent_bom_item_id IS NOT NULL)";

    /// <summary>
    /// Natura della lavorazione officina in SQL, con ripiego a cascata (alias di tabella `o`,
    /// LEFT JOIN opzionale `wr` su project_work_requests):
    ///  1. <c>o.work_type</c> — classificazione congelata sulla riga (migrazione v61 +
    ///     <see cref="OfficinaRowSync.FreezeOfficinaWorkType"/>, e dalla v92 il backfill di
    ///     tutto il pregresso): sopravvive alla chiusura ed è ormai il ramo che risponde
    ///     quasi sempre;
    ///  2. <c>wr.type</c> — la lavorazione collegata 1:1. Dalla v92 esistono solo le righe
    ///     storiche: le bozze non si creano più, quindi questo ramo si svuota nel tempo;
    ///  3. lo stato corrente della riga (stessa regola di <c>TypeFromOfficinaStatus</c>), che
    ///     però dice «interna/esterna» solo finché la riga è in corso.
    /// Stringa vuota = non classificabile: il costo c'è ma non si sa di che natura sia, e
    /// finisce nella terza voce «non classificate» invece di essere attribuito a caso.
    /// </summary>
    public const string OfficinaWorkTypeExpr = @"
        COALESCE(
            NULLIF(TRIM(COALESCE(o.work_type, '')), ''),
            NULLIF(TRIM(COALESCE(wr.type, '')), ''),
            CASE COALESCE(o.item_status, '')
                WHEN 'DC' THEN 'Internal'
                WHEN 'COS' THEN 'Internal'
                WHEN 'DO' THEN 'External'
                WHEN 'RO' THEN 'External'
                WHEN 'IO' THEN 'External'
                WHEN 'CON' THEN 'External'
                ELSE ''
            END
        )";

    /// <summary>Costo officina per natura: interne / esterne / non classificate.</summary>
    public sealed class WorkshopCost
    {
        public decimal Internal { get; set; }
        public decimal External { get; set; }
        public decimal Unclassified { get; set; }
        public decimal Total => Internal + External + Unclassified;
    }

    /// <summary>
    /// Voce «Lavorazioni Officine» a consuntivo, separata interne/esterne.
    /// Somma invariata rispetto al vecchio calcolo unico: è una ripartizione, non un nuovo costo.
    /// </summary>
    public static WorkshopCost GetWorkshopCost(IDbConnection c, int projectId, string[] excludedStates)
    {
        var rows = c.Query<(string WorkType, decimal Cost)>($@"
            SELECT {OfficinaWorkTypeExpr} AS WorkType,
                   COALESCE(SUM(o.quantity * o.unit_cost), 0) AS Cost
            FROM ddp_officina_items o
            LEFT JOIN project_work_requests wr ON wr.ddp_officina_item_id = o.id
            WHERE o.project_id = @Pid
              AND COALESCE(o.item_status,'') NOT IN @Excluded
              AND o.{OfficinaParentDedup}
            GROUP BY WorkType",
            new { Pid = projectId, Excluded = excludedStates });

        WorkshopCost result = new();
        foreach ((string workType, decimal cost) in rows)
        {
            // La Stampa 3D (#87) è lavoro fatto in casa come le interne: nel conto economico
            // sta con loro. Ha un tipo suo solo perché ha una tariffa oraria sua.
            if (OfficinaWorkTypes.IsInHouse(workType))
                result.Internal += cost;
            else if (string.Equals(workType, OfficinaWorkTypes.External, StringComparison.OrdinalIgnoreCase))
                result.External += cost;
            else
                result.Unclassified += cost;
        }
        return result;
    }

    /// <summary>
    /// «Extra Lavoro» (segnalazione #39) per chi legge <c>timesheet_entries</c> in diretta,
    /// senza passare da <c>v_timesheet_with_section</c> (che espone già `counts_in_project`).
    ///
    /// Si usa in coppia: <see cref="ExtraWorkJoin"/> nella FROM e questa nella WHERE.
    /// L'alias della riga di timesheet è sempre <c>te</c>.
    ///
    /// Serve perché la condizione va ripetuta in OGNI conteggio di ore o costi della commessa:
    /// se una pagina la dimentica, mostra ore che il Bilancio non conta più — ed è esattamente
    /// quello che succedeva alla Dashboard commessa, che scriveva «Ore totali 56,0» accanto a
    /// un «Consuntivo Costi» calcolato su 48. Gli elenchi cronologici (ultime attività) non la
    /// applicano: lì si legge cosa ha scritto la gente, non cosa costa.
    /// </summary>
    public const string ExtraWorkJoin =
        "LEFT JOIN timesheet_extra_work ew ON ew.entry_id = te.id";

    /// <inheritdoc cref="ExtraWorkJoin"/>
    public const string ExtraWorkCounts =
        "COALESCE(ew.counts_in_project, 1) = 1";

    /// <summary>
    /// Costo dei soli materiali commerciali (bom_items), stesse esclusioni A9 e dedup dei
    /// padri di composizione (#119, vedi <see cref="CommercialeParentDedup"/>).
    /// </summary>
    public static decimal GetCommercialMaterialCost(IDbConnection c, int projectId, string[] excludedStates) =>
        c.ExecuteScalar<decimal?>($@"
            SELECT COALESCE(SUM(quantity * unit_cost), 0)
            FROM bom_items
            WHERE project_id = @Pid AND COALESCE(item_status,'') NOT IN @Excluded
              AND {CommercialeParentDedup}",
            new { Pid = projectId, Excluded = excludedStates }) ?? 0;

    // ═══════════════════════════════════════════════════════════════
    // ORDINE COMMESSA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Garantisce il «minimo 1 riga» della tabella Ordine Commessa. Alla prima apertura del
    /// Bilancio la commessa riceve una riga sola, con l'importo preso dal <c>revenue</c> già
    /// esistente: da lì in poi l'ordine è la somma delle righe e i numeri non cambiano.
    /// Le commesse mai aperte restano senza righe e si comportano esattamente come prima.
    ///
    /// La creazione è serializzata per commessa da un <c>SELECT ... FOR UPDATE</c> sulla riga
    /// di <c>projects</c>: viene chiamata da una GET, e due letture simultanee della stessa
    /// commessa (il tab della commessa e la card su /bilancio) creerebbero altrimenti DUE
    /// righe seed identiche — con il ricavo raddoppiato alla prima scrittura, e da lì un
    /// numero sbagliato in SAL, dashboard e cash flow.
    ///
    /// Se la commessa non esiste non fa nulla: senza questo controllo l'INSERT violerebbe la
    /// foreign key e una GET con un id sbagliato risponderebbe 500 invece del payload vuoto.
    /// </summary>
    public static void EnsureOrderLines(IDbConnection c, int projectId)
    {
        // Via rapida: dopo la prima volta la riga c'è per sempre, niente transazione.
        int count = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_order_lines WHERE project_id = @Pid", new { Pid = projectId });
        if (count > 0) return;

        using IDbTransaction tx = c.BeginTransaction();

        // COALESCE separato dal test di esistenza: `revenue` è NULL-able, quindi un valore
        // nullo non deve essere confuso con «commessa inesistente».
        List<decimal> found = c.Query<decimal>(
            "SELECT COALESCE(revenue, 0) FROM projects WHERE id = @Pid FOR UPDATE",
            new { Pid = projectId }, tx).ToList();

        if (found.Count == 0)
        {
            tx.Rollback();
            return;
        }

        // Ricontrollo dentro il lock: chi è arrivato prima l'ha già creata.
        int again = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM project_order_lines WHERE project_id = @Pid",
            new { Pid = projectId }, tx);

        if (again == 0)
        {
            decimal revenue = found[0];
            c.Execute(@"
                INSERT INTO project_order_lines (project_id, order_ref, order_position, amount, sort_order)
                VALUES (@Pid, '', '', @Amount, 1)",
                new { Pid = projectId, Amount = revenue > 0 ? revenue : (decimal?)null }, tx);
        }

        tx.Commit();
    }

    /// <summary>
    /// Riallinea <c>projects.revenue</c> alla somma delle righe ordine e restituisce il totale.
    /// Va chiamata dopo OGNI scrittura sulle righe: SAL, dashboard, cash flow e conto economico
    /// continuano a leggere <c>revenue</c> e non si accorgono del cambio di struttura.
    /// Nessuna riga con importo = totale 0 (come una commessa senza ordine), non NULL.
    /// </summary>
    public static decimal SyncRevenueFromOrderLines(IDbConnection c, int projectId)
    {
        decimal total = c.ExecuteScalar<decimal?>(
            "SELECT COALESCE(SUM(amount), 0) FROM project_order_lines WHERE project_id = @Pid",
            new { Pid = projectId }) ?? 0;

        c.Execute("UPDATE projects SET revenue = @Total WHERE id = @Pid",
            new { Pid = projectId, Total = total });

        return total;
    }

    /// <summary>Righe ordine della commessa, in ordine di visualizzazione.</summary>
    public static List<ProjectOrderLineDto> GetOrderLines(IDbConnection c, int projectId) =>
        c.Query<ProjectOrderLineDto>(@"
            SELECT id AS Id, order_ref AS OrderRef, order_position AS OrderPosition,
                   amount AS Amount, sort_order AS SortOrder, row_version AS RowVersion
            FROM project_order_lines
            WHERE project_id = @Pid
            ORDER BY sort_order, id", new { Pid = projectId }).ToList();

    /// <summary>
    /// Totale Ordine «da mostrare»: NULL se nessuna riga ha un importo, così la UI scrive «—»
    /// invece di un fuorviante 0,00 €. Il totale usato nei calcoli resta <c>revenue</c>.
    /// </summary>
    public static decimal? DisplayOrderTotal(IEnumerable<ProjectOrderLineDto> lines)
    {
        decimal sum = 0;
        bool any = false;
        foreach (ProjectOrderLineDto line in lines)
        {
            if (line.Amount == null) continue;
            sum += line.Amount.Value;
            any = true;
        }
        return any ? sum : null;
    }
}
