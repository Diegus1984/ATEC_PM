using System.Text;
using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// L'import dello storico offerte (<see cref="SalesOfferImportService"/>) e l'abbinamento
/// nome → cliente (<see cref="SalesOfferMatching"/>), portato da <c>mutations.js</c>.
///
/// <para>È la fase che si fa <b>una volta sola</b>: se sbaglia, sbaglia su 1.795 righe e nessuno
/// se ne accorge finché non si guardano i grafici. Da qui la simulazione e questi test.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class ImportOfferteTests
{
    private readonly SchemaCondiviso _schema;

    public ImportOfferteTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // ── La chiave con cui due nomi si confrontano ────────────────────────────────

    [Theory]
    [InlineData("ABB Robotics Italy S.p.A.", "abb robotics italy")]
    [InlineData("EUROTHERM S.r.l.", "eurotherm")]
    [InlineData("RECOM INFORMATICA ITALIA S.a.s.", "recom informatica italia")]
    [InlineData("Società Metallurgica", "societa metallurgica")]
    [InlineData("SO.EL.BA di Walter Bassan", "so el ba di walter bassan")]
    [InlineData("Müller GmbH", "muller")]
    [InlineData("  A. BENEVENUTA & C.  ", "a benevenuta c")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void LaChiaveDelNome_normalizzaSenzaPerdereLIdentita(string? nome, string atteso)
    {
        Assert.Equal(atteso, SalesOfferMatching.KeyName(nome));
    }

    /// <summary>
    /// La forma giuridica sparisce anche quando la punteggiatura l'ha già spezzata in lettere
    /// sciolte: è per questo che l'espressione ammette gli spazi in mezzo («s p a»).
    /// </summary>
    [Fact]
    public void LaFormaGiuridica_spariscelnOgniScrittura()
    {
        foreach (string variante in new[] { "Rossi SPA", "Rossi S.p.A.", "Rossi S P A", "Rossi spa" })
            Assert.Equal("rossi", SalesOfferMatching.KeyName(variante));

        foreach (string variante in new[] { "Bianchi SRL", "Bianchi S.r.l.", "Bianchi s.r.l.s." })
            Assert.Equal("bianchi", SalesOfferMatching.KeyName(variante));
    }

    // ── L'abbinamento ────────────────────────────────────────────────────────────

    private static readonly List<(int Id, string Nome)> Rubrica = new()
    {
        (1, "ABB Robotics Italy S.p.A."),
        (2, "ADLEREVO S.p.A."),
        (3, "FINDER S.p.A."),
        (4, "Finder Componenti S.r.l."),
        (5, "GOLDEN CAR"),
    };

    [Fact]
    public void ChiaveIdentica_eLaStessaAzienda()
    {
        Assert.Equal(1, SalesOfferMatching.MatchClient(Rubrica, "abb robotics italy srl"));
        Assert.Equal(5, SalesOfferMatching.MatchClient(Rubrica, "Golden Car"));
    }

    /// <summary>Contenimento a parole intere, nei due versi: è il caso di gran lunga più comune.</summary>
    [Fact]
    public void NomePiuCortoOPiuLungo_siRiconosce()
    {
        // «adlerevo» sta dentro «adlerevo stabilimento pesaro»
        Assert.Equal(2, SalesOfferMatching.MatchClient(Rubrica, "ADLEREVO Stabilimento Pesaro"));
    }

    /// <summary>
    /// <b>Con due candidati non si indovina.</b> «finder» sta dentro sia «finder» che «finder
    /// componenti»: meglio un nome scollegato, che si assegna a mano in due secondi, di
    /// un'offerta attribuita al cliente sbagliato — che non se ne accorge nessuno.
    /// </summary>
    [Fact]
    public void DueCandidati_nonCollegaNiente()
    {
        // Chiave esatta: «finder» è identico al terzo, quindi vince la prima regola.
        Assert.Equal(3, SalesOfferMatching.MatchClient(Rubrica, "FINDER SPA"));
        // Senza chiave esatta restano due candidati per contenimento: nessuno.
        var ambigua = new List<(int, string)> { (4, "Finder Componenti S.r.l."), (6, "Finder Automazione S.r.l.") };
        Assert.Null(SalesOfferMatching.MatchClient(ambigua, "Finder"));
    }

    [Fact]
    public void SottoITreCaratteri_soloLaCorrispondenzaEsatta()
    {
        var rubrica = new List<(int, string)> { (1, "AB Impianti"), (2, "AB") };
        Assert.Equal(2, SalesOfferMatching.MatchClient(rubrica, "AB"));     // esatta: sì
        Assert.Null(SalesOfferMatching.MatchClient(rubrica, "A"));          // troppo corta
        Assert.Null(SalesOfferMatching.MatchClient(Rubrica, ""));
    }

    /// <summary>Nello storico i codici commessa convivono col punto e col trattino basso.</summary>
    [Theory]
    [InlineData("C221221.001", "C221221_001")]
    [InlineData("c260415_203", "C260415_203")]
    [InlineData(" C260605_208_1 ", "C260605_208_1")]
    [InlineData(null, "")]
    public void IlCodiceCommessa_siConfrontaInUnaFormaSola(string? grezzo, string atteso)
    {
        Assert.Equal(atteso, SalesOfferMatching.KeyProjectCode(grezzo));
    }

    // ── L'import vero ────────────────────────────────────────────────────────────

    /// <summary>
    /// Un giro completo: clienti creati, offerte inserite, abbinamento per nome, aggancio alla
    /// commessa per codice, contatti al seguito.
    /// </summary>
    [FactRichiedeMySql]
    public void LImport_creaIClientiCollegaLeOfferteEAgganciaLaCommessa()
    {
        using MySqlConnection c = _schema.Apri();
        int clienteEsistente = SeminaClienteEsistente(c, "EUROTHERM S.r.l.", codice: "2821", piva: "00513380014");
        int commessa = SeminaCommessa(c, "C260415_203", clienteEsistente);

        SalesOfferImportReport r = Importa(c, Archivio);

        // Rubrica: EUROTHERM c'era già (riconosciuto per codice), gli altri due sono nuovi.
        Assert.Equal(3, r.ClientiLetti);
        Assert.Equal(2, r.ClientiCreati);
        Assert.Equal(7, r.OfferteInserite);
        Assert.Equal(0, r.OfferteGiaPresenti);

        // Il cliente riconosciuto NON è stato duplicato e ha preso i campi che gli mancavano.
        Assert.Equal(1, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM customers WHERE easyfatt_code = '2821'"));
        Assert.Equal("Torino", c.ExecuteScalar<string>(
            "SELECT city FROM customers WHERE id = @Id", new { Id = clienteEsistente }));

        // Abbinamento: cinque offerte trovano il cliente; «Ditta Ignota» no, e la bozza
        // senza nome non conta né di qua né di là.
        Assert.Equal(5, r.CollegateACliente);
        Assert.Equal(1, r.SenzaCliente);
        Assert.Equal("Ditta Ignota", r.NomiNonCollegati.Single().Nome);

        // Il codice commessa aggancia la commessa vera.
        Assert.Equal(1, r.CollegateACommessa);
        Assert.Equal(commessa, c.ExecuteScalar<int?>(
            "SELECT project_id FROM sales_offers WHERE legacy_id = 'r10'"));

        // Il contatto al seguito dell'offerta è entrato.
        Assert.Equal(1, r.ContattiInseriti);
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offer_followups"));
    }

    /// <summary>
    /// <b>La trappola per cui esiste <c>legacy_id</c>.</b> I quattro duplicati 282-285 del 2025
    /// hanno lo stesso anno, lo stesso numero e lo stesso venditore: con quella chiave la
    /// seconda esecuzione ne butterebbe via metà. Rieseguire non deve cambiare niente.
    /// </summary>
    [FactRichiedeMySql]
    public void RieseguireLImport_nonDuplicaNiente()
    {
        using MySqlConnection c = _schema.Apri();

        SalesOfferImportReport primo = Importa(c, Archivio);
        int dopoIlPrimo = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offers");
        int clientiDopoIlPrimo = c.ExecuteScalar<int>("SELECT COUNT(*) FROM customers");

        SalesOfferImportReport secondo = Importa(c, Archivio);

        Assert.Equal(7, primo.OfferteInserite);
        Assert.Equal(0, secondo.OfferteInserite);
        Assert.Equal(7, secondo.OfferteGiaPresenti);
        Assert.Equal(0, secondo.ClientiCreati);
        Assert.Equal(dopoIlPrimo, c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offers"));
        Assert.Equal(clientiDopoIlPrimo, c.ExecuteScalar<int>("SELECT COUNT(*) FROM customers"));

        // I due duplicati con lo stesso anno+numero+sigla ci sono ancora tutti e due.
        Assert.Equal(2, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sales_offers WHERE year = 2025 AND number = 282"));
    }

    /// <summary>La simulazione conta tutto e non scrive niente: è così che la si prova in produzione.</summary>
    [FactRichiedeMySql]
    public void LaSimulazione_diceCosaSuccederebbeSenzaScrivere()
    {
        using MySqlConnection c = _schema.Apri();
        int offertePrima = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offers");
        int clientiPrima = c.ExecuteScalar<int>("SELECT COUNT(*) FROM customers");

        SalesOfferImportReport r = Importa(c, Archivio, simulazione: true);

        Assert.True(r.Simulazione);
        Assert.Equal(7, r.OfferteInserite);
        // La rubrica di partenza non contiene nessuno dei tre soggetti: entrerebbero tutti.
        Assert.Equal(3, r.ClientiCreati);
        // ...ma non è entrato niente.
        Assert.Equal(offertePrima, c.ExecuteScalar<int>("SELECT COUNT(*) FROM sales_offers"));
        Assert.Equal(clientiPrima, c.ExecuteScalar<int>("SELECT COUNT(*) FROM customers"));
    }

    /// <summary>
    /// I 16 soggetti senza P. IVA devono entrare tutti. <c>customers.vat_number</c> ha una UNIQUE
    /// e il default stringa vuota: inserendoli con <c>''</c> il secondo esploderebbe.
    /// </summary>
    [FactRichiedeMySql]
    public void IClientiSenzaPartitaIva_entranoTutti()
    {
        using MySqlConnection c = _schema.Apri();

        Importa(c, """
        { "clienti": [
            { "id": "c1", "nome": "Alfa Senza Piva", "codice": "9001", "piva": "" },
            { "id": "c2", "nome": "Beta Senza Piva", "codice": "9002", "piva": "" }
          ], "offers": [] }
        """);

        Assert.Equal(2, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM customers WHERE company_name LIKE '%Senza Piva'"));
        Assert.Equal(2, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM customers WHERE vat_number IS NULL AND company_name LIKE '%Senza Piva'"));
    }

    /// <summary>
    /// Lo storico entra <b>com'è</b>: la serie vecchia resta marcata (e quindi non alza il
    /// progressivo), le righe senza numero non fanno saltare l'inserimento, e i campi che il
    /// piano non aveva previsto — codice commessa, n. commessa, OK grezzo — non si perdono.
    /// </summary>
    [FactRichiedeMySql]
    public void LoStorico_entraComEsenzaPerdereNiente()
    {
        using MySqlConnection c = _schema.Apri();
        Importa(c, Archivio);

        var vecchia = c.QueryFirst<(int Serie, string Numero)>(
            "SELECT is_legacy_series AS Serie, legacy_number AS Numero FROM sales_offers WHERE legacy_id = 'r20'");
        Assert.Equal(1, vecchia.Serie);
        Assert.Equal("S353-2025-EC", vecchia.Numero);

        Assert.Null(c.ExecuteScalar<int?>("SELECT number FROM sales_offers WHERE legacy_id = 'r21'"));

        var commessa = c.QueryFirst<(string Codice, string Num, string Ok)>(
            "SELECT project_code AS Codice, comm_number AS Num, legacy_ok AS Ok FROM sales_offers WHERE legacy_id = 'r10'");
        Assert.Equal("C260415_203", commessa.Codice);
        Assert.Equal("203", commessa.Num);
        Assert.Equal("OK", commessa.Ok);

        // Il progressivo 2025 non si lascia alzare dalla serie vecchia (283 > 282, non 354).
        Assert.Equal(283, SalesOfferRules.NextNumber(c, 2025));
    }

    // ── Aiuti ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Archivio di prova in scala: gli stessi casi che nei dati veri fanno male — cliente da
    /// riconoscere per codice, nome che si abbina per contenimento, nome ignoto, duplicati con
    /// lo stesso anno+numero+sigla, serie vecchia, riga senza numero, contatto al seguito.
    /// </summary>
    private const string Archivio = """
    {
      "clienti": [
        { "id": "c1", "nome": "EUROTHERM S.r.l.", "codice": "2821", "piva": "00513380014",
          "citta": "Torino", "cap": "10122", "prov": "TO", "nazione": "Italia", "sito": "www.eurotherm.it" },
        { "id": "c2", "nome": "ABB Robotics Italy S.p.A.", "codice": "9101", "piva": "11111111111" },
        { "id": "c3", "nome": "GOLDEN CAR", "codice": "9102", "piva": "" }
      ],
      "offers": [
        { "id": "r10", "anno": 2026, "numero": 12, "tipo": "S", "venditore": "EC", "data": "2026-03-02",
          "cliente": "Eurotherm", "importo": 5000, "stato": "presa", "importoOrdine": 5000,
          "commessa": "C260415_203", "commNum": "203", "okRaw": "OK",
          "followup": [ { "ts": "2026-03-05", "data": "2026-03-05", "note": "richiamato, decidono a fine mese" } ] },
        { "id": "r11", "anno": 2026, "numero": 13, "tipo": "I", "venditore": "EC", "data": "2026-03-03",
          "cliente": "ABB Robotics Italy", "importo": 1200, "stato": "aperta", "chance": 40 },
        { "id": "r12", "anno": 2026, "numero": 14, "tipo": "R", "venditore": "FF", "data": "2026-03-04",
          "cliente": "Ditta Ignota", "importo": 300, "stato": "aperta" },
        { "id": "r13", "anno": 2025, "numero": 282, "tipo": "I", "venditore": "EC", "data": "2025-08-26",
          "cliente": "GOLDEN CAR", "stato": "aperta" },
        { "id": "r14", "anno": 2025, "numero": 282, "tipo": "I", "venditore": "EC", "data": "2025-09-10",
          "cliente": "Golden Car", "importo": 203, "stato": "aperta" },
        { "id": "r20", "anno": 2025, "numero": 353, "tipo": "S", "venditore": "EC", "data": "2025-12-01",
          "cliente": "Eurotherm", "stato": "aperta", "serieVecchia": true, "numeroFile": "S353-2025-EC" },
        { "id": "r21", "anno": 2024, "numero": null, "tipo": "", "venditore": "", "cliente": "", "stato": "bozza" }
      ]
    }
    """;

    private static SalesOfferImportReport Importa(MySqlConnection c, string json, bool simulazione = false)
    {
        using var s = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return new SalesOfferImportService().Importa(c, s, me: null, simulazione);
    }

    private static int SeminaClienteEsistente(MySqlConnection c, string nome, string codice, string piva) =>
        (int)c.ExecuteScalar<long>(@"
            INSERT INTO customers (company_name, easyfatt_code, vat_number) VALUES (@N, @C, @P);
            SELECT LAST_INSERT_ID()", new { N = nome, C = codice, P = piva });

    private static int SeminaCommessa(MySqlConnection c, string codice, int clienteId)
    {
        int pm = c.ExecuteScalar<int>("SELECT MIN(id) FROM employees");
        return (int)c.ExecuteScalar<long>(@"
            INSERT INTO projects (code, title, customer_id, pm_id, status)
            VALUES (@Code, 'Commessa di prova', @Cust, @Pm, 'ACTIVE');
            SELECT LAST_INSERT_ID()", new { Code = codice, Cust = clienteId, Pm = pm });
    }
}
