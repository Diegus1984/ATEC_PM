using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

// Gestore DDP (sezione PM): riepilogo delle DDP Commerciali aggregato per commessa.
[ApiController]
[Route("api/ddp-manager")]
[Authorize]
// Gestore DDP: stessa chiave della voce di menu.
[RequireFeature("nav.gestore_ddp")]
public class DdpManagerController : ControllerBase
{
    private readonly DbService _db;
    private readonly AnagraficheCache _cache;
    private readonly ProjectWriteGuard _guard;

    public DdpManagerController(DbService db, AnagraficheCache cache, ProjectWriteGuard guard)
    {
        _db = db;
        _cache = cache;
        _guard = guard;
    }

    // Stati "consegnato/gestito" di default (fallback se l'aggregazione A2 non è configurata).
    // Dalla v75 (segnalazione #54) la chiusura positiva non è più solo DISP: in officina sono
    // CON (comprato fuori) e COS (costruito in casa). DISP resta per il commerciale e per lo
    // storico. SPED e MOD non esistono più (assorbiti da DISP e RAM con la v39).
    private static readonly string[] DefaultDelivered = { "DISP", "CON", "COS", "ASS" };

    // Set "Materiale Consegnato" = appartenenze dell'aggregazione A2 (configurabile da "Aggregazioni DDP").
    private static string[] LoadDelivered(System.Data.IDbConnection c)
    {
        string[] keys = c.Query<string>(@"
            SELECT s.status_key FROM ddp_aggregation_states s
            JOIN ddp_aggregations a ON a.id = s.aggregation_id
            WHERE a.code = 'A2'").ToArray();
        return keys.Length > 0 ? keys : DefaultDelivered;
    }

    [HttpGet("summary")]
    public IActionResult GetSummary()
    {
        try
        {
            using var c = _db.Open();
            string[] delivered = LoadDelivered(c);
            // Stati «esclusi da totale/conteggi» (A9). Il totale € li esclude; i KPI "da consegnare/
            // in ritardo" non contano né i consegnati (A2) né gli esclusi (A9) → unione passata a @Delivered.
            string[] excluded = DdpAggregationSet.Load(c, "A9", _cache);
            string[] deliveredOrExcluded = delivered.Concat(excluded).Distinct().ToArray();

            // Una entry per (commessa, tipo distinta): card COMMERCIALE e OFFICINA affiancate nella pagina.
            List<DdpProjectSummary> summaries = c.Query<DdpProjectSummary>($@"
                SELECT b.project_id AS ProjectId, p.code AS Code,
                       COALESCE(cu.company_name, '') AS CustomerName,
                       'COMMERCIAL' AS DdpType,
                       COUNT(*) AS TotalRows,
                       COALESCE(SUM(CASE WHEN COALESCE(b.item_status,'') NOT IN @Excluded THEN b.quantity * b.unit_cost ELSE 0 END), 0) AS TotalValue,
                       SUM(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN 1 ELSE 0 END) AS DatedCount,
                       SUM(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                AND b.date_needed < CURDATE() THEN 1 ELSE 0 END) AS OverdueCount,
                       MIN(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN b.date_needed END) AS DeliveryStart,
                       MAX(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN b.date_needed END) AS DeliveryEnd,
                       MAX(b.created_at) AS LastInsertedAt
                FROM bom_items b
                JOIN projects p ON p.id = b.project_id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE b.ddp_type = 'COMMERCIAL'{_guard.FiltroBozzeSql(User)}
                GROUP BY b.project_id, p.code, cu.company_name
                UNION ALL
                SELECT o.project_id, p.code,
                       COALESCE(cu.company_name, ''),
                       'OFFICINA',
                       COUNT(*),
                       COALESCE(SUM(CASE WHEN COALESCE(o.item_status,'') NOT IN @Excluded THEN o.quantity * o.unit_cost ELSE 0 END), 0),
                       SUM(CASE WHEN o.date_needed IS NOT NULL AND o.item_status NOT IN @Delivered
                                THEN 1 ELSE 0 END),
                       SUM(CASE WHEN o.date_needed IS NOT NULL AND o.item_status NOT IN @Delivered
                                AND o.date_needed < CURDATE() THEN 1 ELSE 0 END),
                       MIN(CASE WHEN o.date_needed IS NOT NULL AND o.item_status NOT IN @Delivered
                                THEN o.date_needed END),
                       MAX(CASE WHEN o.date_needed IS NOT NULL AND o.item_status NOT IN @Delivered
                                THEN o.date_needed END),
                       MAX(o.created_at)
                FROM ddp_officina_items o
                JOIN projects p ON p.id = o.project_id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE o.id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL)
                  {_guard.FiltroBozzeSql(User)}
                GROUP BY o.project_id, p.code, cu.company_name
                ORDER BY Code DESC, DdpType", new { Delivered = deliveredOrExcluded, Excluded = excluded }).ToList();

            var statusRows = c.Query<(int ProjectId, string DdpType, string StatusKey, int Count)>(@"
                SELECT b.project_id AS ProjectId, 'COMMERCIAL' AS DdpType,
                       b.item_status AS StatusKey, COUNT(*) AS Count
                FROM bom_items b
                WHERE b.ddp_type = 'COMMERCIAL'
                GROUP BY b.project_id, b.item_status
                UNION ALL
                SELECT o.project_id, 'OFFICINA', o.item_status, COUNT(*)
                FROM ddp_officina_items o
                WHERE o.id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL)
                GROUP BY o.project_id, o.item_status").ToList();

            var statusByKey = statusRows
                .GroupBy(row => (row.ProjectId, row.DdpType))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(row => new DdpStatusCount
                        {
                            StatusKey = row.StatusKey,
                            Count = row.Count,
                        })
                        .OrderByDescending(sc => sc.Count)
                        .ThenBy(sc => sc.StatusKey)
                        .ToList());

            foreach (DdpProjectSummary summary in summaries)
            {
                if (statusByKey.TryGetValue((summary.ProjectId, summary.DdpType), out List<DdpStatusCount>? counts))
                    summary.StatusCounts = counts;
            }

            return Ok(ApiResponse<List<DdpProjectSummary>>.Ok(summaries));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpProjectSummary>>.Fail($"Errore: {ex.Message}"));
        }
    }

    /// <summary>
    /// Elenco per la card «DDP Commesse» della sezione Gestione Controlli (#113, #114):
    /// le distinte — commerciali e d'officina — toccate negli ultimi N giorni (default 7)
    /// <b>da qualcun altro</b> e <b>non ancora aperte</b> da chi sta guardando.
    ///
    /// <para>Una voce per (commessa, tipo distinta): è la granularità con cui si apre la DDP
    /// dal Gestore (<c>/gestore-ddp/{projectId}?type=</c>), quindi anche quella con cui la
    /// presa visione ha senso. Contano come aggiornamento l'inserimento e la modifica di una
    /// riga e i cambi di stato in cronistoria.</para>
    ///
    /// <para><b>«Da colleghi»</b>: si esclude chi ha in mano la sessione, guardando l'ultima
    /// firma sulla riga (<c>updated_by</c>, o <c>created_by</c> se non è mai stata modificata)
    /// e l'autore dell'evento di stato. Una modifica <i>senza</i> firma — riga storica, o
    /// passaggio fatto dal programma — resta nell'elenco: nessuno l'ha rivendicata, quindi
    /// va vista.</para>
    /// </summary>
    [HttpGet("/api/ddp-manager/updated-list")]
    public IActionResult GetUpdatedList([FromQuery] int days = 7)
    {
        using var c = _db.Open();
        return Ok(ApiResponse<List<DdpUpdatedItem>>.Ok(LoadUpdated(c, days)));
    }

    /// <summary>
    /// Conteggio della stessa lista, per chi vuole solo il numero. Calcolato dalla lista
    /// apposta: due query separate sullo stesso concetto tornano a divergere.
    /// </summary>
    [HttpGet("/api/ddp-manager/updated-count")]
    public IActionResult GetUpdatedCount([FromQuery] int days = 7)
    {
        using var c = _db.Open();
        return Ok(ApiResponse<int>.Ok(LoadUpdated(c, days).Count));
    }

    /// <summary>
    /// Presa visione di una DDP (#114): chi apre la distinta di una commessa se la toglie
    /// dall'elenco della Dashboard. È <b>personale</b> — non una filigrana condivisa come la
    /// verifica dello scarico ore — e non chiude niente: se un collega tocca ancora quella
    /// distinta, la voce ricompare, perché l'elenco confronta l'ultimo aggiornamento con
    /// questa data.
    /// </summary>
    [RequireProjectVisible]
    [HttpPost("{projectId:int}/seen")]
    public IActionResult MarkSeen(int projectId, [FromQuery] string type = "COMMERCIAL")
    {
        int me = CurrentEmployeeId;
        if (me <= 0) return Ok(ApiResponse<bool>.Ok(false));   // token senza dipendente: niente da segnare

        using var c = _db.Open();
        // INSERT … SELECT invece di VALUES: con un id di commessa che non esiste la foreign
        // key farebbe saltare l'inserimento, e quell'eccezione tornava al client come 500.
        // Qui la riga semplicemente non si scrive e si risponde «niente da segnare»: è una
        // presa visione, non un'operazione che deve fallire rumorosamente.
        int scritte = c.Execute(@"
            INSERT INTO ddp_review_acks (employee_id, project_id, ddp_type, seen_at)
            SELECT @Me, p.id, @Type, NOW() FROM projects p WHERE p.id = @ProjectId
            ON DUPLICATE KEY UPDATE seen_at = NOW()",
            new { Me = me, ProjectId = projectId, Type = NormalizeDdpType(type) });

        return Ok(ApiResponse<bool>.Ok(scritte > 0));
    }

    // Id dipendente dal token (claim NameIdentifier): l'elenco delle DDP da verificare è personale.
    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out int id)
            ? id
            : 0;

    private static string NormalizeDdpType(string? type) =>
        string.Equals(type, "OFFICINA", StringComparison.OrdinalIgnoreCase) ? "OFFICINA" : "COMMERCIAL";

    /// <summary>
    /// Le tre sorgenti di «questa DDP è stata toccata» in un elenco solo: righe commerciali,
    /// righe d'officina e cronistoria degli stati. Il nome che si mostra è quello dell'ultimo
    /// intervento (primo elemento del GROUP_CONCAT ordinato per data: un eventuale troncamento
    /// del concat riguarda la coda, non quello che serve).
    ///
    /// <para>Sta in una costante <c>public</c>, con <c>{0}</c> al posto del filtro bozze, perché
    /// i test la eseguono <b>così com'è</b> invece di ricopiarla: una regola riscritta a mano nel
    /// test è una regola che smette di sorvegliare quella vera appena una delle due cambia.</para>
    ///
    /// <para>Parametri: <c>@Me</c> (dipendente che guarda, 0 = nessuno) e <c>@Days</c> (finestra).</para>
    /// </summary>
    public const string AggiornamentiDaVerificareSql = @"
            SELECT p.id AS ProjectId, p.code AS Code, p.title AS Title,
                   COALESCE(cu.company_name, '') AS CustomerName,
                   u.DdpType AS DdpType,
                   MAX(u.Quando) AS UpdatedAt,
                   -- Nome dell'ultimo intervento: 0x1F (unit separator) come separatore
                   -- perché un nome può contenere qualunque punteggiatura, quello no.
                   SUBSTRING_INDEX(
                       GROUP_CONCAT(u.Chi ORDER BY u.Quando DESC SEPARATOR 0x1F),
                       0x1F, 1) AS UpdatedBy
            FROM (
                SELECT b.project_id AS ProjectId, 'COMMERCIAL' AS DdpType,
                       GREATEST(COALESCE(b.updated_at, b.created_at),
                                COALESCE(b.created_at, b.updated_at)) AS Quando,
                       COALESCE(CONCAT(e.first_name, ' ', e.last_name), '') AS Chi
                FROM bom_items b
                LEFT JOIN employees e ON e.id = COALESCE(b.updated_by, b.created_by)
                WHERE COALESCE(b.ddp_type, 'COMMERCIAL') = 'COMMERCIAL'
                  AND COALESCE(b.updated_by, b.created_by, 0) <> @Me
                  AND GREATEST(COALESCE(b.updated_at, b.created_at),
                               COALESCE(b.created_at, b.updated_at)) >= DATE_SUB(NOW(), INTERVAL @Days DAY)

                UNION ALL

                SELECT o.project_id, 'OFFICINA',
                       GREATEST(COALESCE(o.updated_at, o.created_at),
                                COALESCE(o.created_at, o.updated_at)),
                       COALESCE(CONCAT(e2.first_name, ' ', e2.last_name), '')
                FROM ddp_officina_items o
                LEFT JOIN employees e2 ON e2.id = COALESCE(o.updated_by, o.created_by)
                WHERE COALESCE(o.updated_by, o.created_by, 0) <> @Me
                  AND GREATEST(COALESCE(o.updated_at, o.created_at),
                               COALESCE(o.created_at, o.updated_at)) >= DATE_SUB(NOW(), INTERVAL @Days DAY)

                UNION ALL

                SELECT ev.project_id,
                       CASE WHEN ev.item_type = 'OFFICINA' THEN 'OFFICINA' ELSE 'COMMERCIAL' END,
                       ev.changed_at,
                       COALESCE(ev.changed_by_name, '')
                FROM ddp_item_events ev
                WHERE COALESCE(ev.changed_by_id, 0) <> @Me
                  AND ev.changed_at >= DATE_SUB(NOW(), INTERVAL @Days DAY)
            ) u
            JOIN projects p ON p.id = u.ProjectId
            LEFT JOIN customers cu ON cu.id = p.customer_id
            LEFT JOIN ddp_review_acks a
                   ON a.project_id = u.ProjectId AND a.ddp_type = u.DdpType AND a.employee_id = @Me
            WHERE p.status NOT IN ('CANCELLED')
              {0}
            GROUP BY p.id, p.code, p.title, cu.company_name, u.DdpType, a.seen_at
            HAVING a.seen_at IS NULL OR MAX(u.Quando) > a.seen_at
            ORDER BY MAX(u.Quando) DESC";

    private List<DdpUpdatedItem> LoadUpdated(System.Data.IDbConnection c, int days)
    {
        if (days <= 0) days = 7;
        // Token senza dipendente → -1, non 0: 0 è il valore con cui la query rappresenta
        // «riga senza firma», e coinciderebbe, nascondendo proprio quelle da guardare.
        int me = CurrentEmployeeId > 0 ? CurrentEmployeeId : -1;
        return c.Query<DdpUpdatedItem>(
            string.Format(AggiornamentiDaVerificareSql, _guard.FiltroBozzeSql(User)),
            new { Me = me, Days = days }).ToList();
    }

    // Sintesi di una singola commessa: KPI + ripartizione per stato.
    // type = COMMERCIAL (bom_items) | OFFICINA (ddp_officina_items): stesso shape di risposta.
    [RequireProjectVisible]
    [HttpGet("{projectId:int}")]
    public IActionResult GetDetail(int projectId, [FromQuery] string type = "COMMERCIAL")
    {
        try
        {
            using var c = _db.Open();
            string[] delivered = LoadDelivered(c);
            string[] excluded = DdpAggregationSet.Load(c, "A9", _cache);
            string[] deliveredOrExcluded = delivered.Concat(excluded).Distinct().ToArray();

            bool officina = string.Equals(type, "OFFICINA", StringComparison.OrdinalIgnoreCase);
            string table = officina ? "ddp_officina_items" : "bom_items";
            string typeFilter = officina
                ? "b.id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL) AND "
                : "b.ddp_type = 'COMMERCIAL' AND ";

            DdpProjectDetail? head = c.QueryFirstOrDefault<DdpProjectDetail>($@"
                SELECT b.project_id AS ProjectId, p.code AS Code,
                       COALESCE(cu.company_name, '') AS CustomerName,
                       COUNT(*) AS TotalRows,
                       COALESCE(SUM(CASE WHEN COALESCE(b.item_status,'') NOT IN @Excluded THEN b.quantity * b.unit_cost ELSE 0 END), 0) AS TotalValue,
                       SUM(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN 1 ELSE 0 END) AS DatedCount,
                       SUM(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                AND b.date_needed < CURDATE() THEN 1 ELSE 0 END) AS OverdueCount,
                       MIN(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN b.date_needed END) AS DeliveryStart,
                       MAX(CASE WHEN b.date_needed IS NOT NULL AND b.item_status NOT IN @Delivered
                                THEN b.date_needed END) AS DeliveryEnd
                FROM {table} b
                JOIN projects p ON p.id = b.project_id
                LEFT JOIN customers cu ON cu.id = p.customer_id
                WHERE {typeFilter}b.project_id = @pid
                GROUP BY b.project_id, p.code, cu.company_name", new { pid = projectId, Delivered = deliveredOrExcluded, Excluded = excluded });

            if (head == null)
                return Ok(ApiResponse<DdpProjectDetail>.Fail(officina
                    ? "Nessuna DDP officina per questa commessa"
                    : "Nessuna DDP commerciale per questa commessa"));

            head.StatusCounts = c.Query<DdpStatusCount>($@"
                SELECT b.item_status AS StatusKey, COUNT(*) AS Count
                FROM {table} b
                WHERE {typeFilter}b.project_id = @pid
                GROUP BY b.item_status
                ORDER BY Count DESC", new { pid = projectId }).ToList();

            return Ok(ApiResponse<DdpProjectDetail>.Ok(head));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<DdpProjectDetail>.Fail($"Errore: {ex.Message}"));
        }
    }

    // ── Report di Controllo cross-commessa (segnalazione #62) ───────────────
    // Tutti gli stati previsti in Sintesi/Avanzamento, con filtro C/O lato client.
    // rit = data prevista scaduta e stato ancora «in transito»; gli altri filtrano
    // per causale (o insieme A2/A9). dc/mit/ass sono tipici Officina.

    private static readonly string[] ControlReports =
    {
        "rit", "ver", "chek", "ro", "do", "dc", "io", "par", "mit", "del", "ass", "stop",
    };

    // Condizione SQL del report sul set righe già proiettato (alias t).
    // @NotInTransit / @Delivered / @Stop sono array Dapper (mai vuoti: fallback).
    private static string ControlCondition(string report) => report switch
    {
        "rit"  => "t.DateNeeded IS NOT NULL AND t.DateNeeded < CURDATE() AND t.ItemStatus NOT IN @NotInTransit",
        "ver"  => "t.ItemStatus = 'VER'",
        "chek" => "t.ItemStatus = 'CHEK'",
        "ro"   => "t.ItemStatus = 'RO'",
        "do"   => "t.ItemStatus = 'DO'",
        "dc"   => "t.ItemStatus = 'DC'",
        "io"   => "t.ItemStatus = 'IO'",
        "par"  => "t.ItemStatus = 'PAR'",
        "mit"  => "t.ItemStatus = 'MIT'",
        // Magazzino = A2 senza ASS (ASS ha il report dedicato).
        "del"  => "t.ItemStatus IN @Delivered AND t.ItemStatus <> 'ASS'",
        "ass"  => "t.ItemStatus = 'ASS'",
        "stop" => "t.ItemStatus IN @Stop",
        _ => "1=0",
    };

    private string[] LoadNotInTransit(System.Data.IDbConnection c)
    {
        string[] delivered = LoadDelivered(c);
        string[] excluded = DdpAggregationSet.Load(c, "A9", _cache);
        return delivered.Concat(excluded).Distinct().ToArray();
    }

    private string[] LoadStop(System.Data.IDbConnection c)
    {
        string[] keys = DdpAggregationSet.Load(c, "A9", _cache);
        return keys.Length > 0 ? keys : new[] { "ANN", "SOSP", "SOST", "RAM" };
    }

    [HttpGet("control-summary")]
    public IActionResult GetControlSummary()
    {
        try
        {
            using var c = _db.Open();
            string[] notInTransit = LoadNotInTransit(c);
            string[] delivered = LoadDelivered(c);
            string[] stop = LoadStop(c);

            // Un solo passaggio per tabella: contatori in SUM(CASE …).
            const string counts = @"
                COALESCE(SUM(CASE WHEN b.date_needed IS NOT NULL AND b.date_needed < CURDATE()
                         AND COALESCE(b.item_status,'') NOT IN @NotInTransit THEN 1 ELSE 0 END), 0) AS RitN,
                COALESCE(SUM(CASE WHEN b.item_status = 'VER'  THEN 1 ELSE 0 END), 0) AS VerN,
                COALESCE(SUM(CASE WHEN b.item_status = 'CHEK' THEN 1 ELSE 0 END), 0) AS ChekN,
                COALESCE(SUM(CASE WHEN b.item_status = 'RO'   THEN 1 ELSE 0 END), 0) AS RoN,
                COALESCE(SUM(CASE WHEN b.item_status = 'DO'   THEN 1 ELSE 0 END), 0) AS DoN,
                COALESCE(SUM(CASE WHEN b.item_status = 'DC'   THEN 1 ELSE 0 END), 0) AS DcN,
                COALESCE(SUM(CASE WHEN b.item_status = 'IO'   THEN 1 ELSE 0 END), 0) AS IoN,
                COALESCE(SUM(CASE WHEN b.item_status = 'PAR'  THEN 1 ELSE 0 END), 0) AS ParN,
                COALESCE(SUM(CASE WHEN b.item_status = 'MIT'  THEN 1 ELSE 0 END), 0) AS MitN,
                COALESCE(SUM(CASE WHEN COALESCE(b.item_status,'') IN @Delivered
                         AND COALESCE(b.item_status,'') <> 'ASS' THEN 1 ELSE 0 END), 0) AS DelN,
                COALESCE(SUM(CASE WHEN b.item_status = 'ASS'  THEN 1 ELSE 0 END), 0) AS AssN,
                COALESCE(SUM(CASE WHEN COALESCE(b.item_status,'') IN @Stop THEN 1 ELSE 0 END), 0) AS StopN";

            var args = new { NotInTransit = notInTransit, Delivered = delivered, Stop = stop };

            var com = c.QuerySingleOrDefault<ControlCounts>(
                $"SELECT {counts} FROM bom_items b WHERE b.ddp_type = 'COMMERCIAL'", args)
                ?? new ControlCounts();
            var off = c.QuerySingleOrDefault<ControlCounts>(
                $@"SELECT {counts} FROM ddp_officina_items b
                   WHERE b.id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL)",
                args) ?? new ControlCounts();

            List<DdpControlSummaryEntry> entries = new()
            {
                new() { Report = "rit",  CommercialCount = com.RitN,  OfficinaCount = off.RitN },
                new() { Report = "ver",  CommercialCount = com.VerN,  OfficinaCount = off.VerN },
                new() { Report = "chek", CommercialCount = com.ChekN, OfficinaCount = off.ChekN },
                new() { Report = "ro",   CommercialCount = com.RoN,   OfficinaCount = off.RoN },
                new() { Report = "do",   CommercialCount = com.DoN,   OfficinaCount = off.DoN },
                // Da costruire / trattamento / assegnato: report riservati alle Officine.
                new() { Report = "dc",   CommercialCount = 0,         OfficinaCount = off.DcN },
                new() { Report = "io",   CommercialCount = com.IoN,   OfficinaCount = off.IoN },
                new() { Report = "par",  CommercialCount = com.ParN,  OfficinaCount = off.ParN },
                new() { Report = "mit",  CommercialCount = 0,         OfficinaCount = off.MitN },
                new() { Report = "del",  CommercialCount = com.DelN,  OfficinaCount = off.DelN },
                new() { Report = "ass",  CommercialCount = 0,         OfficinaCount = off.AssN },
                new() { Report = "stop", CommercialCount = com.StopN, OfficinaCount = off.StopN },
            };
            return Ok(ApiResponse<List<DdpControlSummaryEntry>>.Ok(entries));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpControlSummaryEntry>>.Fail($"Errore: {ex.Message}"));
        }
    }

    private sealed class ControlCounts
    {
        public int RitN { get; set; }
        public int VerN { get; set; }
        public int ChekN { get; set; }
        public int RoN { get; set; }
        public int DoN { get; set; }
        public int DcN { get; set; }
        public int IoN { get; set; }
        public int ParN { get; set; }
        public int MitN { get; set; }
        public int DelN { get; set; }
        public int AssN { get; set; }
        public int StopN { get; set; }
    }

    [HttpGet("control-report")]
    public IActionResult GetControlReport([FromQuery] string report = "rit", [FromQuery] string type = "COMMERCIAL")
    {
        try
        {
            report = report.ToLowerInvariant();
            if (!ControlReports.Contains(report))
                return Ok(ApiResponse<List<DdpControlReportRow>>.Fail($"Report sconosciuto: {report}"));

            bool officina = string.Equals(type, "OFFICINA", StringComparison.OrdinalIgnoreCase);
            // Report tipici Officina: lato commerciale restano vuoti (separazione C/O, #62).
            if ((report is "dc" or "mit" or "ass") && !officina)
                return Ok(ApiResponse<List<DdpControlReportRow>>.Ok(new List<DdpControlReportRow>()));

            using var c = _db.Open();
            string[] notInTransit = LoadNotInTransit(c);
            string[] delivered = LoadDelivered(c);
            string[] stop = LoadStop(c);

            // RowNumber calcolato sull'INTERA distinta (partizione per commessa, ordine id),
            // prima del filtro: è il numero riga "vero" che l'utente vede nel Gestore DDP.
            string inner = officina
                ? @"SELECT o.project_id AS ProjectId, p.code AS ProjectCode,
                           COALESCE(cu.company_name, '') AS CustomerName, 'OFFICINA' AS DdpType,
                           o.id AS Id,
                           ROW_NUMBER() OVER (PARTITION BY o.project_id ORDER BY o.id) AS RowNumber,
                           o.part_number AS PartNumber, o.description AS Description,
                           '' AS Unit, o.quantity AS Quantity, o.unit_cost AS UnitCost,
                           o.supplier_name AS SupplierName, '' AS Manufacturer,
                           o.material AS Material, o.treatment AS Treatment,
                           COALESCE(o.item_status,'') AS ItemStatus, o.requested_by AS RequestedBy,
                           o.danea_ref AS DaneaRef, o.date_needed AS DateNeeded,
                           o.created_by AS CreatedById,
                           COALESCE(CONCAT(eo.first_name, ' ', eo.last_name), '') AS CreatedByName,
                           o.created_at AS CreatedAt,
                           o.destination AS Destination, o.destination_spec AS DestinationSpec,
                           COALESCE(o.notes,'') AS Notes,
                           o.parent_officina_item_id AS ParentOfficinaItemId, o.composition_qty AS CompositionQty
                    FROM ddp_officina_items o
                    JOIN projects p ON p.id = o.project_id
                    LEFT JOIN customers cu ON cu.id = p.customer_id
                    LEFT JOIN employees eo ON eo.id = o.created_by
                    WHERE o.id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL)"
                : @"SELECT b.project_id AS ProjectId, p.code AS ProjectCode,
                           COALESCE(cu.company_name, '') AS CustomerName, 'COMMERCIAL' AS DdpType,
                           b.id AS Id,
                           ROW_NUMBER() OVER (PARTITION BY b.project_id ORDER BY b.id) AS RowNumber,
                           b.part_number AS PartNumber, b.description AS Description,
                           b.unit AS Unit, b.quantity AS Quantity, b.unit_cost AS UnitCost,
                           COALESCE(s.company_name, '') AS SupplierName, b.manufacturer AS Manufacturer,
                           '' AS Material, '' AS Treatment,
                           COALESCE(b.item_status,'') AS ItemStatus, b.requested_by AS RequestedBy,
                           b.danea_ref AS DaneaRef, b.date_needed AS DateNeeded,
                           b.created_by AS CreatedById,
                           COALESCE(CONCAT(eb.first_name, ' ', eb.last_name), '') AS CreatedByName,
                           b.created_at AS CreatedAt,
                           b.destination AS Destination, b.destination_spec AS DestinationSpec,
                           COALESCE(b.notes,'') AS Notes,
                           NULL AS ParentOfficinaItemId, NULL AS CompositionQty
                    FROM bom_items b
                    JOIN projects p ON p.id = b.project_id
                    LEFT JOIN customers cu ON cu.id = p.customer_id
                    LEFT JOIN suppliers s ON s.id = b.supplier_id
                    LEFT JOIN employees eb ON eb.id = b.created_by
                    WHERE b.ddp_type = 'COMMERCIAL'";

            // #88: il report cross-commessa non deve elencare le righe delle bozze
            // a chi le bozze non le vede.
            inner += _guard.FiltroBozzeSql(User);

            // rit/io ordinati per data di consegna (poi commessa); gli altri per commessa e riga.
            string orderBy = report is "rit" or "io"
                ? "t.DateNeeded, t.ProjectCode, t.RowNumber"
                : "t.ProjectCode, t.RowNumber";

            List<DdpControlReportRow> rows = c.Query<DdpControlReportRow>(
                $"SELECT t.* FROM ({inner}) t WHERE {ControlCondition(report)} ORDER BY {orderBy}",
                new { NotInTransit = notInTransit, Delivered = delivered, Stop = stop }).ToList();

            return Ok(ApiResponse<List<DdpControlReportRow>>.Ok(rows));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpControlReportRow>>.Fail($"Errore: {ex.Message}"));
        }
    }

    // Consegne previste per giorno su tutte le commesse (grafico "Analisi Consegne"):
    // righe con data prevista e stato in transito, valore = quantità × costo unitario.
    [HttpGet("deliveries-by-day")]
    public IActionResult GetDeliveriesByDay()
    {
        try
        {
            using var c = _db.Open();
            string[] notInTransit = LoadNotInTransit(c);

            var rows = c.Query<(DateTime Day, string DdpType, int N, decimal Amount)>(@"
                SELECT b.date_needed AS Day, 'COMMERCIAL' AS DdpType,
                       COUNT(*) AS N, COALESCE(SUM(b.quantity * b.unit_cost), 0) AS Amount
                FROM bom_items b
                WHERE b.ddp_type = 'COMMERCIAL' AND b.date_needed IS NOT NULL
                  AND COALESCE(b.item_status,'') NOT IN @NotInTransit
                GROUP BY b.date_needed
                UNION ALL
                SELECT o.date_needed, 'OFFICINA', COUNT(*), COALESCE(SUM(o.quantity * o.unit_cost), 0)
                FROM ddp_officina_items o
                WHERE o.date_needed IS NOT NULL
                  AND COALESCE(o.item_status,'') NOT IN @NotInTransit
                  AND o.id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL)
                GROUP BY o.date_needed", new { NotInTransit = notInTransit }).ToList();

            Dictionary<DateTime, DdpDeliveriesDay> byDay = new();
            foreach ((DateTime day, string ddpType, int n, decimal amount) in rows)
            {
                if (!byDay.TryGetValue(day.Date, out DdpDeliveriesDay? entry))
                    byDay[day.Date] = entry = new DdpDeliveriesDay { Day = day.Date };
                if (ddpType == "OFFICINA") { entry.OfficinaCount += n; entry.OfficinaValue += amount; }
                else { entry.CommercialCount += n; entry.CommercialValue += amount; }
            }

            List<DdpDeliveriesDay> days = byDay.Values.OrderBy(d => d.Day).ToList();
            return Ok(ApiResponse<List<DdpDeliveriesDay>>.Ok(days));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpDeliveriesDay>>.Fail($"Errore: {ex.Message}"));
        }
    }
}
