using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Le regole del Registro Offerte (<see cref="SalesOfferRules"/>), portate da
/// <c>mutations.js</c> dell'applicativo autonomo ATEC Offerte.
///
/// <para>Sono la parte che, se si rompe, si rompe in silenzio: un numero doppio o un
/// progressivo che salta non danno nessun errore a video — si scoprono mesi dopo, quando due
/// offerte diverse girano col medesimo numero. Vedi
/// <c>docs/piani/PIANO-ANDAMENTO-COMMERCIALE.md</c>.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class RegistroOfferteTests
{
    private readonly SchemaCondiviso _schema;

    public RegistroOfferteTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // ── Numero composto ──────────────────────────────────────────────────────────

    [Fact]
    public void IlNumeroComposto_eTipoProgressivoAnnoSigla()
    {
        Assert.Equal("S097-2026-EC", SalesOfferRules.Composed("S", 97, 2026, "EC"));
        // Progressivo sempre a tre cifre, anche oltre il 999 (non lo tronca).
        Assert.Equal("R001-2022-FF", SalesOfferRules.Composed("R", 1, 2022, "FF"));
        Assert.Equal("AMU164-2026-GS", SalesOfferRules.Composed("AMU", 164, 2026, "GS"));
    }

    /// <summary>
    /// Una bozza appena riservata ha solo numero e anno: i pezzi mancanti spariscono senza
    /// rompere il resto, esattamente com'era nell'applicativo HTML.
    /// </summary>
    [Fact]
    public void IlNumeroComposto_reggeIPezziMancanti()
    {
        Assert.Equal("164-2026-", SalesOfferRules.Composed(null, 164, 2026, null));
        Assert.Equal("--", SalesOfferRules.Composed(null, null, null, null));
    }

    // ── Progressivo dell'anno ────────────────────────────────────────────────────

    [FactRichiedeMySql]
    public void IlProssimoNumero_eIlMassimoDellAnnoPiuUno()
    {
        using MySqlConnection c = _schema.Apri();
        Inserisci(c, anno: 2026, numero: 1);
        Inserisci(c, anno: 2026, numero: 164);
        Inserisci(c, anno: 2025, numero: 900); // altro anno: non c'entra

        Assert.Equal(165, SalesOfferRules.NextNumber(c, 2026));
        Assert.Equal(901, SalesOfferRules.NextNumber(c, 2025));
    }

    /// <summary>Anno senza offerte: si parte da 1, non da 0.</summary>
    [FactRichiedeMySql]
    public void UnAnnoVuoto_partedaUno()
    {
        using MySqlConnection c = _schema.Apri();
        Assert.Equal(1, SalesOfferRules.NextNumber(c, 2030));
    }

    /// <summary>
    /// <b>La trappola per cui questo test esiste.</b> Nel vecchio Excel le nove offerte 2026
    /// numerate 353–361 proseguivano la serie 2025 e sono marcate <c>is_legacy_series</c>: se
    /// entrassero nel massimo, il prossimo numero del 2026 salterebbe da 165 a 362 e la
    /// numerazione dell'anno sarebbe persa per sempre, senza nessun errore.
    /// </summary>
    [FactRichiedeMySql]
    public void LaSerieVecchia_nonAlzaIlProgressivo()
    {
        using MySqlConnection c = _schema.Apri();
        Inserisci(c, anno: 2026, numero: 164);
        Inserisci(c, anno: 2026, numero: 353, serieVecchia: true);
        Inserisci(c, anno: 2026, numero: 361, serieVecchia: true);

        Assert.Equal(165, SalesOfferRules.NextNumber(c, 2026));
    }

    /// <summary>Le 35 righe storiche senza numero non contano e non fanno saltare il conto.</summary>
    [FactRichiedeMySql]
    public void LeRigheSenzaNumero_nonContano()
    {
        using MySqlConnection c = _schema.Apri();
        Inserisci(c, anno: 2024, numero: null);
        Inserisci(c, anno: 2024, numero: 12);

        Assert.Equal(13, SalesOfferRules.NextNumber(c, 2024));
    }

    // ── Unicità: guardia applicativa, non vincolo di tabella ─────────────────────

    [FactRichiedeMySql]
    public void IlNumeroGiaUsato_eOccupato()
    {
        using MySqlConnection c = _schema.Apri();
        int id = Inserisci(c, anno: 2026, numero: 50);

        Assert.True(SalesOfferRules.NumberTaken(c, 2026, 50));
        Assert.False(SalesOfferRules.NumberTaken(c, 2026, 51));
        Assert.False(SalesOfferRules.NumberTaken(c, 2025, 50));
        // Modificando l'offerta stessa il suo numero non è «occupato»: se no si darebbe del
        // duplicato a sé stessa e nessuna offerta sarebbe più modificabile.
        Assert.False(SalesOfferRules.NumberTaken(c, 2026, 50, exceptId: id));
    }

    /// <summary>
    /// <b>Perché non c'è una UNIQUE su (year, number).</b> Nei dati storici i numeri 282–285 del
    /// 2025 sono duplicati davvero: con un vincolo di tabella l'import dello storico si
    /// fermerebbe lì. La regola sta nell'applicazione, che può dire di sì all'import e di no a
    /// chi crea — come per <c>projects.code</c>.
    /// </summary>
    [FactRichiedeMySql]
    public void IDuplicatiStorici_sonoImportabili()
    {
        using MySqlConnection c = _schema.Apri();
        Inserisci(c, anno: 2025, numero: 282);
        Inserisci(c, anno: 2025, numero: 282); // non deve esplodere: nessuna UNIQUE

        Assert.Equal(2, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sales_offers WHERE year = 2025 AND number = 282"));
        // ...ma la guardia lo vede, quindi chi crea a mano viene fermato.
        Assert.True(SalesOfferRules.NumberTaken(c, 2025, 282));
    }

    // ── Validazione ──────────────────────────────────────────────────────────────

    [Fact]
    public void UnOffertaCompleta_eValida()
    {
        Assert.Empty(SalesOfferRules.Validate(Valida(), numeroOccupato: false));
    }

    /// <summary>La bozza è un numero riservato e basta: non le si chiede cliente, tipo o data.</summary>
    [Fact]
    public void LaBozza_nonChiedeNiente()
    {
        var b = new SalesOfferSaveRequest { Year = 2026, Number = 165, Status = "bozza" };
        Assert.Empty(SalesOfferRules.Validate(b, numeroOccupato: false));
    }

    [Fact]
    public void SenzaClienteVenditoreTipoOData_nonPassa()
    {
        var o = new SalesOfferSaveRequest { Year = 2026, Number = 1, Status = "aperta" };
        Dictionary<string, string> e = SalesOfferRules.Validate(o, numeroOccupato: false);

        Assert.Contains("customerName", e.Keys);
        Assert.Contains("sellerCode", e.Keys);
        Assert.Contains("typeCode", e.Keys);
        Assert.Contains("offerDate", e.Keys);
    }

    [Fact]
    public void LaDataFuoriDallAnno_nonPassa()
    {
        SalesOfferSaveRequest o = Valida();
        o.OfferDate = new DateTime(2025, 12, 31);
        Assert.Contains("offerDate", SalesOfferRules.Validate(o, numeroOccupato: false).Keys);
    }

    [Fact]
    public void IlNumeroOccupato_eUnErrore()
    {
        Assert.Contains("number", SalesOfferRules.Validate(Valida(), numeroOccupato: true).Keys);
        // In creazione il controllo si salta (null): lì il numero è una proposta e si rinumera.
        Assert.Empty(SalesOfferRules.Validate(Valida(), numeroOccupato: null));
    }

    /// <summary>La forbice di prezzo va nel verso giusto: il «fino a» non sta sotto la base.</summary>
    [Fact]
    public void IlFinoA_nonPuoEssereMinoreDellImporto()
    {
        SalesOfferSaveRequest o = Valida();
        o.Amount = 1000;
        o.AmountMax = 900;
        Assert.Contains("amountMax", SalesOfferRules.Validate(o, numeroOccupato: false).Keys);

        o.AmountMax = 1000; // uguale va bene: forbice chiusa
        Assert.Empty(SalesOfferRules.Validate(o, numeroOccupato: false));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(10, true)]
    [InlineData(100, true)]
    [InlineData(null, true)]   // non valutata
    [InlineData(35, false)]    // non è un passo di 10
    [InlineData(110, false)]
    [InlineData(-10, false)]
    public void LaChance_vaDaZeroACentoAPassiDiDieci(int? chance, bool valida)
    {
        SalesOfferSaveRequest o = Valida();
        o.Chance = chance;
        Assert.Equal(valida, !SalesOfferRules.Validate(o, numeroOccupato: false).ContainsKey("chance"));
    }

    /// <summary>
    /// «Presa» vuol dire che c'è un ordine: o a corpo (un importo) o a consuntivo. Senza né
    /// l'uno né l'altro l'offerta risulterebbe vinta ma senza valore, e i totali dell'Andamento
    /// direbbero zero.
    /// </summary>
    [Fact]
    public void LaPresaSenzaOrdine_nonPassa()
    {
        SalesOfferSaveRequest o = Valida();
        o.Status = "presa";
        Assert.Contains("orderAmount", SalesOfferRules.Validate(o, numeroOccupato: false).Keys);

        o.OrderAmount = 12_500m;
        Assert.Empty(SalesOfferRules.Validate(o, numeroOccupato: false));

        o.OrderAmount = null;
        o.IsTimeMaterial = true; // a consuntivo: l'importo non c'è per definizione
        Assert.Empty(SalesOfferRules.Validate(o, numeroOccupato: false));
    }

    [Fact]
    public void UnoStatoInventato_nonPassa()
    {
        SalesOfferSaveRequest o = Valida();
        o.Status = "vinta";
        Assert.Contains("status", SalesOfferRules.Validate(o, numeroOccupato: false).Keys);
    }

    // ── Aggancio col preventivo ──────────────────────────────────────────────────

    /// <summary>
    /// Lo stato del preventivo <b>suggerisce</b> l'esito del registro, non lo impone: chi lo
    /// applica lo fa solo sulle righe ancora aperte, perché la fonte di verità resta il
    /// registro (copre anche le offerte che in ATEC PM non esistono).
    /// </summary>
    [Theory]
    [InlineData("accepted", "presa")]
    [InlineData("converted", "presa")]
    [InlineData("rejected", "persa")]
    [InlineData("draft", null)]
    [InlineData("sent", null)]
    [InlineData("negotiation", null)]
    [InlineData("expired", null)]
    [InlineData(null, null)]
    public void LoStatoDelPreventivo_suggerisceLEsito(string? statoQuote, string? atteso)
    {
        Assert.Equal(atteso, SalesOfferRules.EsitoDaStatoPreventivo(statoQuote));
    }

    // ── Anagrafiche seminate ─────────────────────────────────────────────────────

    /// <summary>
    /// I 13 tipi impianto nascono col bootstrap e portano la categoria: senza, l'Analisi di
    /// mercato (impianti / interventi / ricambi) non ha su cosa raggrupparsi.
    /// </summary>
    [FactRichiedeMySql]
    public void ITipiImpianto_nasconoConLaCategoriaGiusta()
    {
        using MySqlConnection c = _schema.Apri();

        Assert.Equal(13, c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offer_types"));
        Assert.Equal("Impianto", Categoria(c, "S"));
        Assert.Equal("Impianto", Categoria(c, "AMU"));
        Assert.Equal("Intervento", Categoria(c, "I"));
        Assert.Equal("Intervento", Categoria(c, "SW"));
        Assert.Equal("Ricambio", Categoria(c, "R"));
    }

    /// <summary>
    /// Le sigle venditore ci sono tutte e dieci, e i quattro cessati (FF, GS, MS, DS — insieme
    /// l'83% dello storico) nascono <b>spenti</b>: non lavorano più in ATEC, quindi restano nei
    /// grafici storici ma fuori dalle tendine di chi crea un'offerta nuova.
    /// </summary>
    [FactRichiedeMySql]
    public void ISigleVenditore_ciSonoTutteEIcessatiSonoSpenti()
    {
        using MySqlConnection c = _schema.Apri();

        Assert.Equal(10, c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offer_sellers"));
        foreach (string cessato in new[] { "FF", "GS", "MS", "DS" })
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT is_active FROM sales_offer_sellers WHERE code = @C", new { C = cessato }));
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT is_active FROM sales_offer_sellers WHERE code = 'EC'"));
        Assert.Equal("Francesco Ferrari", c.ExecuteScalar<string>("SELECT name FROM sales_offer_sellers WHERE code = 'FF'"));
        Assert.Equal("Maurizio Spandre", c.ExecuteScalar<string>("SELECT name FROM sales_offer_sellers WHERE code = 'MS'"));
    }

    /// <summary>
    /// Ogni sigla si aggancia alla scheda del dipendente omonimo: è quello che dice al server
    /// quali offerte sono «mie». L'aggancio è per nome e cognome, non per iniziali — in azienda
    /// ci sono due GV e tre MC.
    /// </summary>
    [FactRichiedeMySql]
    public void LeSigle_siAggancianoAlDipendenteOmonimo()
    {
        using MySqlConnection c = _schema.Apri();

        // Sul database di prova esiste solo l'utenza «Admin ATEC» del bootstrap: nessun venditore
        // omonimo, quindi l'aggancio non trova nessuno. Si semina la persona e si rilancia.
        c.Execute(@"INSERT INTO employees (first_name, last_name, status, user_role)
                    VALUES ('Francesco','Ferrari','TERMINATED','TECH')");
        c.Execute(@"UPDATE sales_offer_sellers s
                    JOIN employees e ON CONCAT(e.first_name,' ',e.last_name) = s.name
                    SET s.employee_id = e.id
                    WHERE s.employee_id IS NULL AND s.name <> ''");

        Assert.NotNull(c.ExecuteScalar<int?>("SELECT employee_id FROM sales_offer_sellers WHERE code = 'FF'"));
        // Chi non ha un omonimo in anagrafica resta scollegato, senza rompere niente.
        Assert.Null(c.ExecuteScalar<int?>("SELECT employee_id FROM sales_offer_sellers WHERE code = 'EC'"));
    }

    // ── Aiuti ────────────────────────────────────────────────────────────────────

    private static string? Categoria(MySqlConnection c, string code) =>
        c.ExecuteScalar<string>("SELECT category FROM sales_offer_types WHERE code = @C", new { C = code });

    private static SalesOfferSaveRequest Valida() => new()
    {
        Year = 2026,
        Number = 165,
        TypeCode = "S",
        SellerCode = "EC",
        OfferDate = new DateTime(2026, 9, 4),
        CustomerName = "ABB Robotics Italy",
        Status = "aperta",
        Amount = 12_000m,
    };

    private static int Inserisci(MySqlConnection c, int anno, int? numero, bool serieVecchia = false) =>
        (int)c.ExecuteScalar<long>(@"
            INSERT INTO sales_offers (year, number, type_code, seller_code, customer_name, status, is_legacy_series)
            VALUES (@Anno, @Numero, 'S', 'EC', 'Prova', 'aperta', @Vecchia);
            SELECT LAST_INSERT_ID()",
            new { Anno = anno, Numero = numero, Vecchia = serieVecchia ? 1 : 0 });
}
