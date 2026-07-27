using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Server.Services;
using ATEC.PM.Server.Data;
using System.Collections.Generic;
using System.Linq;
using System;

namespace ATEC.PM.Server.Controllers;

[ApiController]
[Route("api/deadlines")]
[Authorize]
public class DeadlinesController : ControllerBase
{
    private readonly DbService _db;
    public DeadlinesController(DbService db) => _db = db;

    [HttpGet]
    public IActionResult GetAll()
    {
        using var c = _db.Open();

        // Carica stati esclusi (aggregazione A9) per DDP
        string[] excluded = DdpAggregationSet.Load(c, "A9");

        string sql = @"
            SELECT u.*, DATEDIFF(u.DueDate, CURDATE()) AS Days
            FROM (
                -- 1. SAL (fatturazione: alert finché non emessa — stato '' o 'daEmettere')
                SELECT
                    'SAL' AS Type,
                    'SAL_ROW' AS RefType,
                    sr.id AS RefId,
                    sr.project_id AS ProjectId,
                    p.code AS Code,
                    p.title AS Title,
                    sr.step AS Description,
                    sr.data_fatt AS DueDate
                FROM sal_rows sr
                JOIN projects p ON p.id = sr.project_id
                WHERE sr.data_fatt IS NOT NULL AND sr.stato <> 'emessa'

                UNION ALL

                -- 2. PROJECT
                SELECT 
                    'PROJECT' AS Type,
                    'PROJECT' AS RefType,
                    p.id AS RefId,
                    p.id AS ProjectId,
                    p.code AS Code,
                    p.title AS Title,
                    p.title AS Description,
                    p.end_date_planned AS DueDate
                FROM projects p
                WHERE p.end_date_planned IS NOT NULL AND p.end_date_actual IS NULL AND p.status = 'ACTIVE'

                UNION ALL

                -- 3. CHECKLIST
                SELECT 
                    'CHECKLIST' AS Type,
                    'CHECKLIST' AS RefType,
                    i.id AS RefId,
                    i.project_id AS ProjectId,
                    COALESCE(p.code, '') AS Code,
                    COALESCE(p.title, '') AS Title,
                    i.description AS Description,
                    i.due_date AS DueDate
                FROM checklist_items i
                LEFT JOIN projects p ON p.id = i.project_id
                WHERE i.due_date IS NOT NULL AND i.status <> 'CLOSED'

                UNION ALL

                -- 4. MOM
                SELECT 
                    'MOM' AS Type,
                    'MOM_ACTION' AS RefType,
                    a.id AS RefId,
                    m.project_id AS ProjectId,
                    COALESCE(p.code, '') AS Code,
                    COALESCE(p.title, '') AS Title,
                    a.attivita AS Description,
                    a.data_check AS DueDate
                FROM mom_action_items a
                JOIN mom_records m ON m.id = a.mom_id
                LEFT JOIN projects p ON p.id = m.project_id
                WHERE a.data_check IS NOT NULL AND a.status <> 'CLOSED'

                UNION ALL

                -- 5. DDP
                SELECT 
                    'DDP' AS Type,
                    'BOM' AS RefType,
                    b.id AS RefId,
                    b.project_id AS ProjectId,
                    p.code AS Code,
                    p.title AS Title,
                    CONCAT(b.part_number, ' - ', b.description) AS Description,
                    b.date_needed AS DueDate
                FROM bom_items b
                JOIN projects p ON p.id = b.project_id
                WHERE b.date_needed IS NOT NULL
                  AND b.item_status NOT IN @Delivered
                  AND COALESCE(b.item_status, '') NOT IN @Excluded

                UNION ALL

                -- 6. SAL INCASSO (saldo fattura previsto = data_fatt + gg_saldo; TUTTI gli
                --    incassi aperti, anche futuri: il cruscotto mostra tutto e i badge
                --    colorano quelli scaduti)
                SELECT
                    'SAL_INCASSO' AS Type,
                    'SAL_ROW' AS RefType,
                    sr.id AS RefId,
                    sr.project_id AS ProjectId,
                    p.code AS Code,
                    p.title AS Title,
                    sr.step AS Description,
                    DATE_ADD(sr.data_fatt, INTERVAL sr.gg_saldo DAY) AS DueDate
                FROM sal_rows sr
                JOIN projects p ON p.id = sr.project_id
                WHERE sr.data_fatt IS NOT NULL
                  AND sr.gg_saldo IS NOT NULL
                  -- Guardia: un gg_saldo abnorme manda DATE_ADD fuori range → NULL,
                  -- che Dapper non mappa su DueDate non-nullable (500 su tutto l'endpoint)
                  AND DATE_ADD(sr.data_fatt, INTERVAL sr.gg_saldo DAY) IS NOT NULL
                  AND sr.pagamento <> 'Pagata'
            ) u
            ORDER BY u.DueDate ASC";

        var rows = c.Query<DeadlineDto>(sql, new
        {
            Delivered = DdpDeliveredStatuses.Keys,
            Excluded = excluded
        }).ToList();

        return Ok(ApiResponse<List<DeadlineDto>>.Ok(rows));
    }
}
