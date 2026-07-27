using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

// Gestore DDP (sezione PM): riepilogo delle DDP Commerciali aggregato per commessa.
[ApiController]
[Route("api/ddp-manager")]
[Authorize]
public class DdpManagerController : ControllerBase
{
    private readonly DbService _db;
    public DdpManagerController(DbService db) => _db = db;

    // Stati "consegnato/gestito" di default (fallback se l'aggregazione A2 non è configurata).
    // Matrice stati v7: DISP assorbe CON/COS/SPED; il vecchio MOD è confluito in RAM.
    private static readonly string[] DefaultDelivered = { "DISP", "ASS" };

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
            string[] excluded = DdpAggregationSet.Load(c, "A9");
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
                WHERE b.ddp_type = 'COMMERCIAL'
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

    // Sintesi di una singola commessa: KPI + ripartizione per stato.
    // type = COMMERCIAL (bom_items) | OFFICINA (ddp_officina_items): stesso shape di risposta.
    [HttpGet("{projectId:int}")]
    public IActionResult GetDetail(int projectId, [FromQuery] string type = "COMMERCIAL")
    {
        try
        {
            using var c = _db.Open();
            string[] delivered = LoadDelivered(c);
            string[] excluded = DdpAggregationSet.Load(c, "A9");
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

    // ── Report di Controllo cross-commessa ──────────────────────────────────
    // I 6 report scandagliano TUTTE le distinte: rit = righe con data prevista scaduta
    // e stato ancora "in transito" (né A2 consegnato né A9 escluso); gli altri filtrano
    // per singola causale. "dc" (Da costruire) ha senso solo per le DDP Officina.

    private static readonly string[] ControlReports = { "rit", "ver", "ro", "do", "io", "dc" };

    // Condizione SQL del report sul set righe già proiettato (alias t). La colonna
    // NOT IN @NotInTransit è parametrizzata Dapper (array mai vuoto: A2 ha fallback).
    private static string ControlCondition(string report) => report switch
    {
        "rit" => "t.DateNeeded IS NOT NULL AND t.DateNeeded < CURDATE() AND t.ItemStatus NOT IN @NotInTransit",
        "ver" => "t.ItemStatus = 'VER'",
        "ro"  => "t.ItemStatus = 'RO'",
        "do"  => "t.ItemStatus = 'DO'",
        "io"  => "t.ItemStatus = 'IO'",
        "dc"  => "t.ItemStatus = 'DC'",
        _ => "1=0",
    };

    private string[] LoadNotInTransit(System.Data.IDbConnection c)
    {
        string[] delivered = LoadDelivered(c);
        string[] excluded = DdpAggregationSet.Load(c, "A9");
        return delivered.Concat(excluded).Distinct().ToArray();
    }

    [HttpGet("control-summary")]
    public IActionResult GetControlSummary()
    {
        try
        {
            using var c = _db.Open();
            string[] notInTransit = LoadNotInTransit(c);

            // Un solo passaggio per tabella: 6 contatori in SUM(CASE …).
            const string counts = @"
                COALESCE(SUM(CASE WHEN b.date_needed IS NOT NULL AND b.date_needed < CURDATE()
                         AND COALESCE(b.item_status,'') NOT IN @NotInTransit THEN 1 ELSE 0 END), 0) AS RitN,
                COALESCE(SUM(CASE WHEN b.item_status = 'VER' THEN 1 ELSE 0 END), 0) AS VerN,
                COALESCE(SUM(CASE WHEN b.item_status = 'RO'  THEN 1 ELSE 0 END), 0) AS RoN,
                COALESCE(SUM(CASE WHEN b.item_status = 'DO'  THEN 1 ELSE 0 END), 0) AS DoN,
                COALESCE(SUM(CASE WHEN b.item_status = 'IO'  THEN 1 ELSE 0 END), 0) AS IoN,
                COALESCE(SUM(CASE WHEN b.item_status = 'DC'  THEN 1 ELSE 0 END), 0) AS DcN";

            (int RitN, int VerN, int RoN, int DoN, int IoN, int DcN) com = c.QuerySingleOrDefault<(int, int, int, int, int, int)>(
                $"SELECT {counts} FROM bom_items b WHERE b.ddp_type = 'COMMERCIAL'", new { NotInTransit = notInTransit });
            (int RitN, int VerN, int RoN, int DoN, int IoN, int DcN) off = c.QuerySingleOrDefault<(int, int, int, int, int, int)>(
                $"SELECT {counts} FROM ddp_officina_items b WHERE b.id NOT IN (SELECT DISTINCT parent_officina_item_id FROM ddp_officina_items WHERE parent_officina_item_id IS NOT NULL)", new { NotInTransit = notInTransit });

            List<DdpControlSummaryEntry> entries = new()
            {
                new() { Report = "rit", CommercialCount = com.RitN, OfficinaCount = off.RitN },
                new() { Report = "ver", CommercialCount = com.VerN, OfficinaCount = off.VerN },
                new() { Report = "ro",  CommercialCount = com.RoN,  OfficinaCount = off.RoN },
                new() { Report = "do",  CommercialCount = com.DoN,  OfficinaCount = off.DoN },
                new() { Report = "io",  CommercialCount = com.IoN,  OfficinaCount = off.IoN },
                // Da costruire: report riservato alle Officine, il conteggio commerciale non si mostra.
                new() { Report = "dc",  CommercialCount = 0,        OfficinaCount = off.DcN },
            };
            return Ok(ApiResponse<List<DdpControlSummaryEntry>>.Ok(entries));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<List<DdpControlSummaryEntry>>.Fail($"Errore: {ex.Message}"));
        }
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
            if (report == "dc" && !officina)
                return Ok(ApiResponse<List<DdpControlReportRow>>.Ok(new List<DdpControlReportRow>()));

            using var c = _db.Open();
            string[] notInTransit = LoadNotInTransit(c);

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
                           o.danea_ref AS DaneaRef, o.date_needed AS DateNeeded, o.created_at AS CreatedAt,
                           o.destination AS Destination, o.destination_spec AS DestinationSpec,
                           COALESCE(o.notes,'') AS Notes,
                           o.parent_officina_item_id AS ParentOfficinaItemId, o.composition_qty AS CompositionQty
                    FROM ddp_officina_items o
                    JOIN projects p ON p.id = o.project_id
                    LEFT JOIN customers cu ON cu.id = p.customer_id
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
                           b.danea_ref AS DaneaRef, b.date_needed AS DateNeeded, b.created_at AS CreatedAt,
                           b.destination AS Destination, b.destination_spec AS DestinationSpec,
                           COALESCE(b.notes,'') AS Notes,
                           NULL AS ParentOfficinaItemId, NULL AS CompositionQty
                    FROM bom_items b
                    JOIN projects p ON p.id = b.project_id
                    LEFT JOIN customers cu ON cu.id = p.customer_id
                    LEFT JOIN suppliers s ON s.id = b.supplier_id
                    WHERE b.ddp_type = 'COMMERCIAL'";

            // rit/io ordinati per data di consegna (poi commessa); gli altri per commessa e riga.
            string orderBy = report is "rit" or "io"
                ? "t.DateNeeded, t.ProjectCode, t.RowNumber"
                : "t.ProjectCode, t.RowNumber";

            List<DdpControlReportRow> rows = c.Query<DdpControlReportRow>(
                $"SELECT t.* FROM ({inner}) t WHERE {ControlCondition(report)} ORDER BY {orderBy}",
                new { NotInTransit = notInTransit }).ToList();

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
