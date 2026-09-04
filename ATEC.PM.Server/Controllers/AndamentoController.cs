using System.Security.Claims;
using ATEC.PM.Server.Authorization;

namespace ATEC.PM.Server.Controllers;

/// <summary>
/// **Andamento** — la sezione con cui il venditore riferisce alla proprietà.
///
/// <para>Questa metà è quella delle <b>offerte</b>: KPI dell'anno, andamento mensile su cinque
/// anni, per venditore e per tipo, analisi di mercato per categoria, chance e portafoglio
/// ponderato, le quindici aperte più grandi. La metà delle <b>commesse</b> arriva dal Bilancio
/// e dal SAL. Piano: <c>docs/piani/PIANO-ANDAMENTO-COMMERCIALE.md</c>.</para>
///
/// <para><b>I conti non stanno qui</b>: vivono in <see cref="AndamentoOfferteCalcolo"/>, che è
/// codice puro e provato a parte. Qui si leggono le righe e si passano di là.</para>
/// </summary>
[ApiController]
[Route("api/andamento")]
[Authorize]
[RequireFeature("nav.andamento")]
public class AndamentoController : ControllerBase
{
    private readonly DbService _db;
    private readonly FeatureAccessService _access;

    public AndamentoController(DbService db, FeatureAccessService access)
    {
        _db = db;
        _access = access;
    }

    private int CurrentEmployeeId =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out int id) ? id : 0;

    /// <summary>
    /// Chi non vede il fatturato non vede gli importi. È la stessa chiave della Dashboard e del
    /// Bilancio: i conteggi e le percentuali restano, gli euro si azzerano.
    /// </summary>
    private bool VedeImporti() =>
        _access.CanAccessUser(CurrentEmployeeId, User.FindFirst(ClaimTypes.Role)?.Value, "data.revenue");

    /// <summary>
    /// L'andamento delle offerte dell'anno indicato (default: quello corrente), con i filtri
    /// facoltativi per venditore e categoria.
    /// </summary>
    [HttpGet("offerte")]
    public IActionResult GetOfferte(
        [FromQuery] int? year, [FromQuery] string? sellerCode, [FromQuery] string? category)
    {
        try
        {
            int anno = year is > 0 ? year.Value : DateTime.Now.Year;
            using var c = _db.Open();

            // Cinque anni bastano: il confronto pluriennale ne mostra cinque e il resto
            // dell'analisi guarda solo l'anno scelto.
            var righe = c.Query<RigaGrezza>(@"
                SELECT o.id AS Id, o.year AS Anno, o.number AS Numero,
                       o.type_code AS TipoCodice, COALESCE(t.name,'') AS TipoNome,
                       COALESCE(t.category,'Altro') AS Categoria,
                       o.seller_code AS VenditoreCodice, COALESCE(s.name,'') AS VenditoreNome,
                       o.offer_date AS Data, o.customer_name AS Cliente, o.description AS Descrizione,
                       o.amount AS Importo, o.amount_max AS ImportoMax,
                       o.status AS Stato, o.chance AS Chance,
                       o.order_amount AS ImportoOrdine, o.is_time_material AS AConsuntivo
                FROM sales_offers o
                LEFT JOIN sales_offer_types t ON t.code = o.type_code
                LEFT JOIN sales_offer_sellers s ON s.code = o.seller_code
                WHERE o.year BETWEEN @Da AND @A
                  AND (@Seller IS NULL OR o.seller_code = @Seller)
                  AND (@Cat IS NULL OR COALESCE(t.category,'Altro') = @Cat)",
                new
                {
                    Da = anno - 4,
                    A = anno,
                    Seller = string.IsNullOrWhiteSpace(sellerCode) ? null : sellerCode,
                    Cat = string.IsNullOrWhiteSpace(category) ? null : category,
                }).ToList();

            List<AndamentoOfferteCalcolo.Riga> conv = righe.Select(r => new AndamentoOfferteCalcolo.Riga(
                r.Id, r.Anno, r.Numero, r.TipoCodice ?? "", r.TipoNome ?? "", r.Categoria ?? "Altro",
                r.VenditoreCodice ?? "", r.VenditoreNome ?? "", r.Data, r.Cliente ?? "", r.Descrizione ?? "",
                r.Importo, r.ImportoMax, r.Stato ?? "", r.Chance, r.ImportoOrdine, r.AConsuntivo)).ToList();

            AndamentoOfferteDto dto = AndamentoOfferteCalcolo.Calcola(conv, anno);

            // Portafoglio nel tempo: si ricostruisce dai cambi di stato registrati. Sullo
            // storico importato non ce n'è nessuno, e in quel caso la lista resta vuota con
            // una nota — meglio dire «non lo so» che disegnare una linea inventata.
            var cambi = c.Query<CambioStato>(@"
                SELECT offer_id AS OfferId, changed_at AS Quando, COALESCE(old_value,'') AS StatoPrecedente
                FROM sales_offer_log
                WHERE field = 'status' AND changed_at >= @Da
                ORDER BY changed_at DESC",
                new { Da = DateTime.Today.AddMonths(-13) }).ToList();

            dto.Portafoglio = AndamentoOfferteCalcolo.Portafoglio(
                conv,
                cambi.Select(x => (x.OfferId, x.Quando, x.StatoPrecedente)).ToList(),
                DateTime.Today);

            if (dto.Portafoglio.Count == 0)
                dto.PortafoglioNota =
                    "Il registro modifiche non copre ancora questo periodo: l'andamento del portafoglio "
                    + "comincia a raccontare qualcosa dopo qualche mese di uso.";

            if (!VedeImporti()) AzzeraImporti(dto);

            return Ok(ApiResponse<AndamentoOfferteDto>.Ok(dto));
        }
        catch (Exception ex) { return Ok(ApiResponse<AndamentoOfferteDto>.Fail($"Errore: {ex.Message}")); }
    }

    /// <summary>
    /// Toglie gli euro lasciando i conteggi. Non è un filtro sulle righe: chi non vede il
    /// fatturato deve poter comunque sapere quante offerte sono state emesse e quante prese.
    /// </summary>
    private static void AzzeraImporti(AndamentoOfferteDto dto)
    {
        AndamentoKpiDto k = dto.Kpi;
        k.ValoreEmesse = k.ValoreEmesseMax = k.ValorePrese = k.ValoreOrdini = 0;
        k.ValoreAperte = k.ValoreAperteMax = k.ValorePerse = k.ValoreSospese = 0;
        k.PortafoglioPonderato = 0;

        foreach (AndamentoMeseDto m in dto.Mensile) { m.Emesse = m.Prese = m.Aperte = 0; }
        foreach (AndamentoSerieAnnoDto s in dto.MensileMultiAnno)
            s.Mesi = s.Mesi.Select(_ => 0m).ToList();
        foreach (AndamentoGruppoDto g in dto.PerVenditore.Concat(dto.PerTipo).Concat(dto.PerCategoria).Concat(dto.TopClienti))
        { g.ValoreEmesse = g.ValorePrese = g.ValoreAperte = 0; }
        foreach (AndamentoChanceDto ch in dto.PerChance) { ch.Valore = ch.ValorePonderato = 0; }
        foreach (AndamentoOffertaDto o in dto.TopAperte) o.Importo = null;
        foreach (AndamentoPortafoglioDto p in dto.Portafoglio) p.Valore = 0;
    }

    private sealed class RigaGrezza
    {
        public int Id { get; set; }
        public int Anno { get; set; }
        public int? Numero { get; set; }
        public string? TipoCodice { get; set; }
        public string? TipoNome { get; set; }
        public string? Categoria { get; set; }
        public string? VenditoreCodice { get; set; }
        public string? VenditoreNome { get; set; }
        public DateTime? Data { get; set; }
        public string? Cliente { get; set; }
        public string? Descrizione { get; set; }
        public decimal? Importo { get; set; }
        public decimal? ImportoMax { get; set; }
        public string? Stato { get; set; }
        public int? Chance { get; set; }
        public decimal? ImportoOrdine { get; set; }
        public bool AConsuntivo { get; set; }
    }

    private sealed class CambioStato
    {
        public int OfferId { get; set; }
        public DateTime Quando { get; set; }
        public string StatoPrecedente { get; set; } = "";
    }
}
