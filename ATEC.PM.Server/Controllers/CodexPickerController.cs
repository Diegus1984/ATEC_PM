using ATEC.PM.Server.Authorization;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ATEC.PM.Server.Controllers;

// Vista Codex del picker unico delle DDP (#128): l'elenco Codex NON basta, perché il
// codice fornitore/produttore — la chiave di ricerca più intuitiva per chi compila la
// distinta — vive sull'articolo Danea ABBINATO (catalog_items.codex_item_id), non sui
// campi storici di codex_items. Qui i due mondi escono già uniti: una riga per
// abbinamento (multi-fornitore = più righe), e i codici senza abbinamento restano
// visibili con i campi Codex di ripiego.
[ApiController]
[Route("api/codex/picker")]
[Authorize]
// Le stesse porte del picker: chi lavora su una delle due DDP (o sulle inbox).
[RequireFeature("project.ddp_commerciale", "project.ddp_officina", "nav.gestore_ddp",
    "nav.officina_inbox", "nav.acquisti_inbox")]
public class CodexPickerController : ControllerBase
{
    private readonly DbService _db;
    public CodexPickerController(DbService db) { _db = db; }

    [HttpGet]
    public IActionResult Get(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? codicePrefixes = null,
        [FromQuery] string? codice = null,
        [FromQuery] string? descr = null,
        [FromQuery] string? articolo = null,
        [FromQuery] string? codiceFornitore = null,
        [FromQuery] string? fornitore = null,
        [FromQuery] string? produttore = null)
    {
        try
        {
            (page, pageSize, int offset) = PagedQueryHelper.Normalize(page, pageSize);
            var clauses = new List<string>();
            var dp = new Dapper.DynamicParameters();

            // I codici storici commerciali MAI ricodificati (codice che inizia per 2/3
            // solo per caso del vecchio schema) non sono codici ATEC: fuori dal picker.
            clauses.Add(@"NOT (COALESCE(cx.codice_nuovo,'') = ''
                          AND (cx.codice LIKE '2%' OR cx.codice LIKE '3%'))");

            // Prefissi famiglia: valgono sul codice ATEC effettivo (nuovo se c'è,
            // altrimenti il codice), così i vecchi ricodificati stanno nella loro famiglia.
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
                        ors.Add($"COALESCE(NULLIF(cx.codice_nuovo,''), cx.codice) LIKE @{pn}");
                        dp.Add(pn, parts[i] + "%");
                    }
                    clauses.Add("(" + string.Join(" OR ", ors) + ")");
                }
            }

            void AddLike(string sqlExpr, string? filter, string param)
            {
                string? pat = PagedQueryHelper.ToLikePattern(filter);
                if (pat == null) return;
                clauses.Add($"{sqlExpr} LIKE @{param}");
                dp.Add(param, pat);
            }

            // Il codice ATEC viaggia col punto a video e senza nel DB: filtro depuntato.
            if (codice != null) codice = codice.Replace(".", "");
            AddLike("REPLACE(COALESCE(NULLIF(cx.codice_nuovo,''), cx.codice), '.', '')", codice, "Codice");
            // La descrizione buona può stare sul Codex o sull'articolo: basta una delle due.
            string? descrPat = PagedQueryHelper.ToLikePattern(descr);
            if (descrPat != null)
            {
                clauses.Add("(cx.descr LIKE @Descr OR ci.description LIKE @Descr)");
                dp.Add("Descr", descrPat);
            }
            AddLike("ci.code", articolo, "Articolo");
            AddLike("ci.supplier_code", codiceFornitore, "CodiceFornitore");
            AddLike("COALESCE(s.company_name, cx.fornitore)", fornitore, "Fornitore");
            AddLike("ci.manufacturer", produttore, "Produttore");

            string from = @"
            FROM codex_items cx
            LEFT JOIN catalog_items ci ON ci.codex_item_id = cx.id AND ci.is_active = 1
            LEFT JOIN suppliers s ON s.id = ci.supplier_id";
            string where = "WHERE " + string.Join(" AND ", clauses);

            using var c = _db.Open();
            int total = c.ExecuteScalar<int>($"SELECT COUNT(*) {from} {where}", dp);
            dp.Add("Limit", pageSize);
            dp.Add("Offset", offset);

            var rows = c.Query<CodexPickerRow>($@"
            SELECT cx.id AS CodexId,
                   COALESCE(NULLIF(cx.codice_nuovo,''), cx.codice) AS CodiceAtec,
                   COALESCE(cx.descr,'') AS Descr,
                   COALESCE(cx.um,'') AS UmCodex,
                   COALESCE(cx.fornitore,'') AS FornitoreCodex,
                   cx.prezzo_forn AS PrezzoCodex,
                   ci.id AS CatalogItemId,
                   COALESCE(ci.code,'') AS CodiceArticolo,
                   COALESCE(ci.supplier_code,'') AS CodiceFornitore,
                   COALESCE(ci.unit,'') AS UnitArticolo,
                   ci.unit_cost AS CostoArticolo,
                   ci.supplier_id AS SupplierId,
                   COALESCE(s.company_name,'') AS FornitoreNome,
                   COALESCE(ci.manufacturer,'') AS Produttore
            {from}
            {where}
            ORDER BY CodiceAtec, ci.code
            LIMIT @Limit OFFSET @Offset", dp).ToList();

            int loaded = offset + rows.Count;
            return Ok(ApiResponse<PagedResult<CodexPickerRow>>.Ok(new PagedResult<CodexPickerRow>
            {
                Items = rows,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                HasMore = loaded < total,
            }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResult<CodexPickerRow>>.Fail(ex.Message));
        }
    }

    // #142: i lavorati 101 con grezzo commerciale (derivazione #135), visti dal lato
    // acquisti — CodexId/CodiceAtec sono del 101, articolo/fornitore/costo vengono
    // dall'abbinamento Danea del SUO 201. Un abbinamento = una riga, come sopra; un 201
    // senza articoli resta visibile con una riga sola (fornitore di ripiego dal Codex),
    // perché è proprio il caso «scoperto» che l'operatore deve vedere e sistemare.
    [HttpGet("derivati-101")]
    public IActionResult GetDerivati101(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 0,
        [FromQuery] string? codice = null,
        [FromQuery] string? descr = null,
        [FromQuery] string? articolo = null,
        [FromQuery] string? codiceFornitore = null,
        [FromQuery] string? fornitore = null,
        [FromQuery] string? produttore = null)
    {
        try
        {
            (page, pageSize, int offset) = PagedQueryHelper.Normalize(page, pageSize);
            // La derivazione vive solo sui particolari a disegno, ma il vincolo esplicito
            // tiene fuori eventuali refusi storici di codex_item_references.
            var clauses = new List<string>
            {
                "COALESCE(NULLIF(cx.codice_nuovo,''), cx.codice) LIKE '1%'"
            };
            var dp = new Dapper.DynamicParameters();

            void AddLike(string sqlExpr, string? filter, string param)
            {
                string? pat = PagedQueryHelper.ToLikePattern(filter);
                if (pat == null) return;
                clauses.Add($"{sqlExpr} LIKE @{param}");
                dp.Add(param, pat);
            }

            if (codice != null) codice = codice.Replace(".", "");
            AddLike("REPLACE(COALESCE(NULLIF(cx.codice_nuovo,''), cx.codice), '.', '')", codice, "Codice");
            string? descrPat = PagedQueryHelper.ToLikePattern(descr);
            if (descrPat != null)
            {
                clauses.Add("(cx.descr LIKE @Descr OR ci.description LIKE @Descr)");
                dp.Add("Descr", descrPat);
            }
            AddLike("ci.code", articolo, "Articolo");
            AddLike("ci.supplier_code", codiceFornitore, "CodiceFornitore");
            AddLike("COALESCE(s.company_name, g.fornitore)", fornitore, "Fornitore");
            AddLike("ci.manufacturer", produttore, "Produttore");

            string from = @"
            FROM codex_items cx
            JOIN codex_item_references r ON r.source_codex_id = cx.id AND r.ref_type = '201'
            JOIN codex_items g ON g.id = r.ref_codex_id
            LEFT JOIN catalog_items ci ON ci.codex_item_id = g.id AND ci.is_active = 1
            LEFT JOIN suppliers s ON s.id = ci.supplier_id";
            string where = "WHERE " + string.Join(" AND ", clauses);

            using var c = _db.Open();
            int total = c.ExecuteScalar<int>($"SELECT COUNT(*) {from} {where}", dp);
            dp.Add("Limit", pageSize);
            dp.Add("Offset", offset);

            var rows = c.Query<CodexPickerRow>($@"
            SELECT cx.id AS CodexId,
                   COALESCE(NULLIF(cx.codice_nuovo,''), cx.codice) AS CodiceAtec,
                   COALESCE(cx.descr,'') AS Descr,
                   COALESCE(cx.um,'') AS UmCodex,
                   COALESCE(g.fornitore,'') AS FornitoreCodex,
                   g.prezzo_forn AS PrezzoCodex,
                   ci.id AS CatalogItemId,
                   COALESCE(ci.code,'') AS CodiceArticolo,
                   COALESCE(ci.supplier_code,'') AS CodiceFornitore,
                   COALESCE(ci.unit,'') AS UnitArticolo,
                   ci.unit_cost AS CostoArticolo,
                   ci.supplier_id AS SupplierId,
                   COALESCE(s.company_name,'') AS FornitoreNome,
                   COALESCE(ci.manufacturer,'') AS Produttore,
                   g.id AS GrezzoCodexId,
                   COALESCE(NULLIF(g.codice_nuovo,''), g.codice) AS GrezzoCodice
            {from}
            {where}
            ORDER BY CodiceAtec, ci.code
            LIMIT @Limit OFFSET @Offset", dp).ToList();

            int loaded = offset + rows.Count;
            return Ok(ApiResponse<PagedResult<CodexPickerRow>>.Ok(new PagedResult<CodexPickerRow>
            {
                Items = rows,
                TotalCount = total,
                Page = page,
                PageSize = pageSize,
                HasMore = loaded < total,
            }));
        }
        catch (Exception ex)
        {
            return Ok(ApiResponse<PagedResult<CodexPickerRow>>.Fail(ex.Message));
        }
    }
}
