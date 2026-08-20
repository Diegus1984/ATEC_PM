using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// Cronistoria di una riga di distinta: tutti i passaggi di stato, dal più recente.
/// </summary>
[ApiController]
[Route("api/ddp-events")]
[Authorize]
public class DdpItemEventsController : ControllerBase
{
    private readonly DbService _db;
    public DdpItemEventsController(DbService db) => _db = db;

    /// <param name="tipo">COMMERCIAL o OFFICINA</param>
    [HttpGet("{tipo}/{itemId:int}")]
    public IActionResult GetStoria(string tipo, int itemId)
    {
        string t = (tipo ?? "").Trim().ToUpperInvariant();
        if (t != DdpItemEvents.Commerciale && t != DdpItemEvents.Officina)
            return BadRequest(ApiResponse<string>.Fail("Tipo distinta non valido"));

        using var c = _db.Open();
        var righe = c.Query<DdpItemEventDto>(@"
            SELECT e.id                AS Id,
                   e.from_status       AS FromStatus,
                   COALESCE(sf.label, e.from_status) AS FromLabel,
                   e.to_status         AS ToStatus,
                   COALESCE(st.label, e.to_status)   AS ToLabel,
                   st.color_bg         AS ToColorBg,
                   st.color_fg         AS ToColorFg,
                   e.changed_at        AS ChangedAt,
                   e.changed_by_name   AS ChangedBy,
                   e.origin            AS Origin,
                   e.note              AS Note
            FROM ddp_item_events e
            LEFT JOIN ddp_statuses st ON st.status_key = e.to_status
            LEFT JOIN ddp_statuses sf ON sf.status_key = e.from_status
            WHERE e.item_type = @Tipo AND e.item_id = @ItemId
            ORDER BY e.changed_at DESC, e.id DESC",
            new { Tipo = t, ItemId = itemId }).ToList();

        return Ok(ApiResponse<List<DdpItemEventDto>>.Ok(righe));
    }
}
