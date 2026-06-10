using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Dapper;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Shared.Models;
using ATEC.PM.Server.Services;

namespace ATEC.PM.Server.Controllers;

// API del sottosistema Gamma Robot: robot → quadri → distinta componenti.
// Consultazione (sola lettura per ora): scheda robot/quadro + vista magazzino per-componente.
[ApiController]
[Route("api/gamma-robot")]
[Authorize]
public class GammaRobotController : ControllerBase
{
    private readonly GammaRobotDbService _gdb;
    public GammaRobotController(GammaRobotDbService gdb)
    {
        _gdb = gdb;
    }

    // ═══════════════════════════════════════════════════════
    // ROBOT — elenco modelli
    // ═══════════════════════════════════════════════════════

    [HttpGet("robots")]
    public IActionResult GetRobots()
    {
        try
        {
            using var c = _gdb.Open();
            var rows = c.Query<GammaRobotDto>(@"
                SELECT r.id AS Id, r.modello AS Modello, r.serie AS Serie,
                       r.brand AS Brand, r.note AS Note,
                       (SELECT COUNT(*) FROM gamma_quadro WHERE robot_id = r.id) AS QuadriCount
                FROM gamma_robot r
                WHERE r.is_active = 1
                ORDER BY r.modello").ToList();
            return Ok(ApiResponse<List<GammaRobotDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<GammaRobotDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // QUADRI — configurazioni di un robot
    // ═══════════════════════════════════════════════════════

    [HttpGet("robots/{robotId}/quadri")]
    public IActionResult GetQuadri(int robotId)
    {
        try
        {
            using var c = _gdb.Open();
            var rows = c.Query<GammaQuadroDto>(@"
                SELECT q.id AS Id, q.robot_id AS RobotId, q.controllore AS Controllore,
                       q.generazione AS Generazione, q.payload AS Payload,
                       q.area_lavoro AS AreaLavoro, q.os_version AS OsVersion,
                       q.system_key AS SystemKey, q.note AS Note,
                       (SELECT COUNT(*) FROM gamma_distinta WHERE quadro_id = q.id) AS ComponentiCount
                FROM gamma_quadro q
                WHERE q.robot_id = @RobotId AND q.is_active = 1
                ORDER BY q.generazione, q.controllore, q.payload", new { RobotId = robotId }).ToList();
            return Ok(ApiResponse<List<GammaQuadroDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<GammaQuadroDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // DISTINTA — componenti di un quadro (col prezzo VB)
    // ═══════════════════════════════════════════════════════

    [HttpGet("quadri/{quadroId}/distinta")]
    public IActionResult GetDistinta(int quadroId)
    {
        try
        {
            using var c = _gdb.Open();
            // Prezzo VB = cost_price * markup_value della (prima) variante del prodotto.
            var rows = c.Query<GammaDistintaItemDto>(@"
                SELECT d.id AS Id, d.quadro_id AS QuadroId, d.product_id AS ProductId,
                       d.sezione AS Sezione, d.slot AS Slot, d.code_raw AS CodeRaw,
                       d.qty AS Qty, d.is_alternate AS IsAlternate, d.is_optional AS IsOptional,
                       d.note AS Note, d.raw_text AS RawText,
                       p.code AS ProductCode, p.name AS ProductName,
                       (SELECT v.cost_price * v.markup_value
                          FROM quote_product_variants v
                         WHERE v.product_id = p.id
                         ORDER BY v.id LIMIT 1) AS PrezzoVb
                FROM gamma_distinta d
                LEFT JOIN quote_products p ON p.id = d.product_id
                WHERE d.quadro_id = @QuadroId
                ORDER BY FIELD(d.sezione,'Schede','Azionamenti','Kit Cavi','Motori','Componenti meccanici','Tastierino','Ventole'),
                         d.slot, d.is_alternate, d.is_optional", new { QuadroId = quadroId }).ToList();
            return Ok(ApiResponse<List<GammaDistintaItemDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<GammaDistintaItemDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // COMPONENTI — elenco anagrafica Gamma Ricambi (vista magazzino)
    // ═══════════════════════════════════════════════════════

    [HttpGet("components")]
    public IActionResult GetComponents()
    {
        try
        {
            using var c = _gdb.Open();
            // Listino "Gamma Ricambi" = price_list_id 4 (gruppo Manipolazione).
            var rows = c.Query<GammaComponentDto>(@"
                SELECT p.id AS ProductId, p.code AS Code, p.name AS Name, c.name AS Categoria,
                       (SELECT v.cost_price * v.markup_value
                          FROM quote_product_variants v
                         WHERE v.product_id = p.id ORDER BY v.id LIMIT 1) AS PrezzoVb,
                       (SELECT COUNT(DISTINCT q.robot_id)
                          FROM gamma_distinta d
                          JOIN gamma_quadro q ON q.id = d.quadro_id
                         WHERE d.product_id = p.id) AS RobotCount
                FROM quote_products p
                JOIN quote_categories c ON c.id = p.category_id
                JOIN quote_groups g ON g.id = c.group_id
                WHERE g.price_list_id = 4 AND p.is_active = 1
                ORDER BY c.sort_order, p.name").ToList();
            return Ok(ApiResponse<List<GammaComponentDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<GammaComponentDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // UTILIZZO — dove entra un componente (vista magazzino per-componente)
    // ═══════════════════════════════════════════════════════

    [HttpGet("products/{productId}/usage")]
    public IActionResult GetUsage(int productId)
    {
        try
        {
            using var c = _gdb.Open();
            // Aggregato per modello+quadro: collassa le varianti di payload/area (stesso robot+controllore)
            // in una riga, con Occorrenze = numero di configurazioni. IsAlternate solo se SEMPRE alternativa.
            var rows = c.Query<GammaUsageDto>(@"
                SELECT r.modello AS Modello, q.controllore AS Controllore, q.generazione AS Generazione,
                       GROUP_CONCAT(DISTINCT d.slot ORDER BY d.slot SEPARATOR ', ') AS Slot,
                       (MIN(d.is_alternate) = 1) AS IsAlternate,
                       COUNT(*) AS Occorrenze
                FROM gamma_distinta d
                JOIN gamma_quadro q ON q.id = d.quadro_id
                JOIN gamma_robot r ON r.id = q.robot_id
                WHERE d.product_id = @ProductId
                GROUP BY r.modello, q.controllore, q.generazione
                ORDER BY r.modello, q.generazione, q.controllore", new { ProductId = productId }).ToList();
            return Ok(ApiResponse<List<GammaUsageDto>>.Ok(rows));
        }
        catch (Exception ex) { return Ok(ApiResponse<List<GammaUsageDto>>.Fail($"Errore: {ex.Message}")); }
    }

    // ═══════════════════════════════════════════════════════
    // EDITOR — scrittura (solo ADMIN). Consultazione resta aperta a tutti.
    // ═══════════════════════════════════════════════════════

    // ── ROBOT (anagrafica modello) ──────────────────────────

    [HttpPost("robots")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult CreateRobot([FromBody] GammaRobotSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Modello))
                return Ok(ApiResponse<int>.Fail("Modello obbligatorio"));
            using var c = _gdb.Open();
            int dup = c.ExecuteScalar<int>("SELECT COUNT(*) FROM gamma_robot WHERE modello=@M",
                new { M = req.Modello.Trim() });
            if (dup > 0) return Ok(ApiResponse<int>.Fail("Esiste già un robot con questo modello"));
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO gamma_robot (modello, serie, brand, note, is_active)
                VALUES (@Modello, @Serie, @Brand, @Note, 1);
                SELECT LAST_INSERT_ID()",
                new { Modello = req.Modello.Trim(), req.Serie,
                      Brand = string.IsNullOrWhiteSpace(req.Brand) ? "ABB" : req.Brand.Trim(), req.Note });
            return Ok(ApiResponse<int>.Ok(id, "Robot creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("robots/{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult UpdateRobot(int id, [FromBody] GammaRobotSaveRequest req)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(req.Modello))
                return Ok(ApiResponse<int>.Fail("Modello obbligatorio"));
            using var c = _gdb.Open();
            int dup = c.ExecuteScalar<int>("SELECT COUNT(*) FROM gamma_robot WHERE modello=@M AND id<>@Id",
                new { M = req.Modello.Trim(), Id = id });
            if (dup > 0) return Ok(ApiResponse<int>.Fail("Esiste già un robot con questo modello"));
            int rows = c.Execute(
                "UPDATE gamma_robot SET modello=@Modello, serie=@Serie, brand=@Brand, note=@Note WHERE id=@Id",
                new { Modello = req.Modello.Trim(), req.Serie,
                      Brand = string.IsNullOrWhiteSpace(req.Brand) ? "ABB" : req.Brand.Trim(), req.Note, Id = id });
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Robot non trovato"));
            return Ok(ApiResponse<int>.Ok(id, "Robot aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("robots/{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult DeleteRobot(int id)
    {
        try
        {
            using var c = _gdb.Open();
            int quadri = c.ExecuteScalar<int>("SELECT COUNT(*) FROM gamma_quadro WHERE robot_id=@Id", new { Id = id });
            if (quadri > 0)
                return Ok(ApiResponse<bool>.Fail($"Impossibile eliminare: il robot ha {quadri} quadri. Eliminali prima."));
            int rows = c.Execute("DELETE FROM gamma_robot WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Robot non trovato"));
            return Ok(ApiResponse<bool>.Ok(true, "Robot eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ── QUADRO (configurazione di un robot) ─────────────────

    [HttpPost("robots/{robotId}/quadri")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult CreateQuadro(int robotId, [FromBody] GammaQuadroSaveRequest req)
    {
        try
        {
            using var c = _gdb.Open();
            int robotExists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM gamma_robot WHERE id=@Id", new { Id = robotId });
            if (robotExists == 0) return Ok(ApiResponse<int>.Fail("Robot non trovato"));
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO gamma_quadro (robot_id, controllore, generazione, payload, area_lavoro, os_version, system_key, note, is_active)
                VALUES (@RobotId, @Controllore, @Generazione, @Payload, @AreaLavoro, @OsVersion, @SystemKey, @Note, 1);
                SELECT LAST_INSERT_ID()",
                new { RobotId = robotId, req.Controllore, req.Generazione, req.Payload,
                      req.AreaLavoro, req.OsVersion, req.SystemKey, req.Note });
            return Ok(ApiResponse<int>.Ok(id, "Quadro creato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("quadri/{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult UpdateQuadro(int id, [FromBody] GammaQuadroSaveRequest req)
    {
        try
        {
            using var c = _gdb.Open();
            int rows = c.Execute(@"
                UPDATE gamma_quadro SET controllore=@Controllore, generazione=@Generazione, payload=@Payload,
                       area_lavoro=@AreaLavoro, os_version=@OsVersion, system_key=@SystemKey, note=@Note
                 WHERE id=@Id",
                new { req.Controllore, req.Generazione, req.Payload, req.AreaLavoro,
                      req.OsVersion, req.SystemKey, req.Note, Id = id });
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Quadro non trovato"));
            return Ok(ApiResponse<int>.Ok(id, "Quadro aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("quadri/{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult DeleteQuadro(int id)
    {
        try
        {
            using var c = _gdb.Open();
            // FK ON DELETE CASCADE elimina la distinta del quadro (il client conferma prima).
            int rows = c.Execute("DELETE FROM gamma_quadro WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Quadro non trovato"));
            return Ok(ApiResponse<bool>.Ok(true, "Quadro eliminato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }

    // ── DISTINTA (righe componente del quadro) ──────────────

    [HttpPost("distinta")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult AddDistinta([FromBody] GammaDistintaAddRequest req)
    {
        try
        {
            using var c = _gdb.Open();
            int quadroExists = c.ExecuteScalar<int>("SELECT COUNT(*) FROM gamma_quadro WHERE id=@Id",
                new { Id = req.QuadroId });
            if (quadroExists == 0) return Ok(ApiResponse<int>.Fail("Quadro non trovato"));

            // Il componente deve appartenere al listino Gamma Ricambi (price_list_id 4).
            string? prodCode = c.QueryFirstOrDefault<string>(@"
                SELECT p.code
                FROM quote_products p
                JOIN quote_categories cat ON cat.id = p.category_id
                JOIN quote_groups g ON g.id = cat.group_id
                WHERE p.id = @Pid AND g.price_list_id = 4", new { Pid = req.ProductId });
            if (prodCode == null)
                return Ok(ApiResponse<int>.Fail("Componente non valido (non è nel listino Gamma Ricambi)"));

            string sezione = string.IsNullOrWhiteSpace(req.Sezione) ? "Schede" : req.Sezione!.Trim();
            // Slot: principale → codice prodotto (è il proprio gruppo-slot);
            //       alternativa → slot del principale (passato dal client).
            string slot = string.IsNullOrWhiteSpace(req.Slot) ? prodCode : req.Slot!.Trim();

            // Guard: stesso prodotto già principale nella stessa (quadro, sezione).
            if (!req.IsAlternate)
            {
                int dup = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM gamma_distinta
                    WHERE quadro_id=@Q AND sezione=@S AND product_id=@P AND is_alternate=0",
                    new { Q = req.QuadroId, S = sezione, P = req.ProductId });
                if (dup > 0)
                    return Ok(ApiResponse<int>.Fail("Questo componente è già presente come principale in questa sezione"));
            }

            int qty = Math.Max(1, req.Qty);
            int id = c.ExecuteScalar<int>(@"
                INSERT INTO gamma_distinta (quadro_id, product_id, sezione, slot, code_raw, qty, is_alternate, is_optional, note)
                VALUES (@QuadroId, @ProductId, @Sezione, @Slot, @CodeRaw, @Qty, @IsAlternate, @IsOptional, @Note);
                SELECT LAST_INSERT_ID()",
                new { req.QuadroId, req.ProductId, Sezione = sezione, Slot = slot, CodeRaw = prodCode,
                      Qty = qty, IsAlternate = req.IsAlternate ? 1 : 0, IsOptional = req.IsOptional ? 1 : 0, req.Note });
            return Ok(ApiResponse<int>.Ok(id, "Componente aggiunto"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpPut("distinta/{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult UpdateDistinta(int id, [FromBody] GammaDistintaUpdateRequest req)
    {
        try
        {
            var sets = new List<string>();
            var dp = new DynamicParameters();
            dp.Add("Id", id);
            if (req.Qty.HasValue) { sets.Add("qty=@Qty"); dp.Add("Qty", Math.Max(1, req.Qty.Value)); }
            if (req.IsAlternate.HasValue) { sets.Add("is_alternate=@IsAlternate"); dp.Add("IsAlternate", req.IsAlternate.Value ? 1 : 0); }
            if (req.IsOptional.HasValue) { sets.Add("is_optional=@IsOptional"); dp.Add("IsOptional", req.IsOptional.Value ? 1 : 0); }
            if (req.Note != null) { sets.Add("note=@Note"); dp.Add("Note", req.Note); }
            if (sets.Count == 0) return Ok(ApiResponse<int>.Fail("Nessun campo da aggiornare"));

            using var c = _gdb.Open();
            int rows = c.Execute($"UPDATE gamma_distinta SET {string.Join(", ", sets)} WHERE id=@Id", dp);
            if (rows == 0) return Ok(ApiResponse<int>.Fail("Riga non trovata"));
            return Ok(ApiResponse<int>.Ok(id, "Aggiornato"));
        }
        catch (Exception ex) { return Ok(ApiResponse<int>.Fail($"Errore: {ex.Message}")); }
    }

    [HttpDelete("distinta/{id}")]
    [Authorize(Roles = "ADMIN")]
    public IActionResult DeleteDistinta(int id)
    {
        try
        {
            using var c = _gdb.Open();
            int rows = c.Execute("DELETE FROM gamma_distinta WHERE id=@Id", new { Id = id });
            if (rows == 0) return Ok(ApiResponse<bool>.Fail("Riga non trovata"));
            return Ok(ApiResponse<bool>.Ok(true, "Rimosso"));
        }
        catch (Exception ex) { return Ok(ApiResponse<bool>.Fail($"Errore: {ex.Message}")); }
    }
}
