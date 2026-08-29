using System.Net;
using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// La risincronizzazione mirata (PIANO-HR-PORT-ORIGINALE.md, B1 e B2): «Risincronizza questo
/// giorno» e «Sincronizza il mese».
///
/// <para>Le due proprietà che, se sbagliano, sbagliano in silenzio:</para>
/// <list type="number">
///   <item><b>Il cursore non si muove.</b> È un ripescaggio mirato, non un avanzamento
///   dell'import: spostarlo aprirebbe una finestra cieca sul mese corrente da cui le
///   correzioni non tornerebbero più. Nell'originale il cursore veniva riscritto anche dopo
///   una sync forzata di un mese passato — è l'errore da non ripetere.</item>
///   <item><b>Dentro la finestra si ha la fotografia completa</b>, quindi le timbrature che
///   su Ecos non ci sono più si tolgono anche qui. Fuori dalla finestra no, e le rettifiche
///   non si toccano mai: quelle sono nostre.</item>
/// </list>
/// </summary>
public class IntervalloMesiTests
{
    [Fact]
    public void Un_giorno_e_un_mese_solo()
    {
        var mesi = HrAttendanceService
            .MesiDellIntervallo(new DateTime(2026, 2, 5), new DateTime(2026, 2, 5)).ToList();
        Assert.Equal(new[] { (2026, 2) }, mesi);
    }

    [Fact]
    public void L_intervallo_a_cavallo_di_capodanno_prende_tutti_i_mesi()
    {
        var mesi = HrAttendanceService
            .MesiDellIntervallo(new DateTime(2025, 12, 20), new DateTime(2026, 2, 3)).ToList();
        Assert.Equal(new[] { (2025, 12), (2026, 1), (2026, 2) }, mesi);
    }
}

[Collection(SchemaCondiviso.Nome)]
public class RisincronizzaGiornoTests
{
    private readonly SchemaCondiviso _schema;

    public RisincronizzaGiornoTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static readonly DateTime Giorno = new(2026, 2, 5);
    private static readonly DateTime GiornoDopo = new(2026, 2, 6);
    private const string CursoreIniziale = "2026-02-01 08:00:00";

    [FactRichiedeMySql]
    public async Task Risincronizzare_un_giorno_toglie_le_timbrature_sparite_da_Ecos()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42");

        // Quello che abbiamo: quattro timbrature il 5, due il 6.
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(9), "OUT");   // su Ecos non c'è più
        Grezza(c, mario, "s3", Giorno.AddHours(13), "IN");
        Grezza(c, mario, "s4", Giorno.AddHours(17), "OUT");
        Grezza(c, mario, "s5", GiornoDopo.AddHours(8), "IN");
        Grezza(c, mario, "s6", GiornoDopo.AddHours(17), "OUT");
        ScriviCursore(c);

        var handler = new EcosFinto(
            RispostaToken(),
            RispostaTimbrature(
                Riga("s1", "2026-02-05 08:00:00", "42", "IN"),
                Riga("s3", "2026-02-05 13:00:00", "42", "IN"),
                Riga("s4", "2026-02-05 17:00:00", "42", "OUT"),
                Riga("s5", "2026-02-06 08:00:00", "42", "IN"),
                Riga("s6", "2026-02-06 17:00:00", "42", "OUT")));

        HrImportResultDto esito = await Servizio(handler).ImportWindowAsync(mario, Giorno, Giorno);

        Assert.True(esito.Success);
        Assert.Null(IdEsterno(c, mario, "s2"));
        Assert.Equal(3, Timbrature(c, mario, Giorno));
        // Il giorno dopo non è nella finestra: non si tocca.
        Assert.Equal(2, Timbrature(c, mario, GiornoDopo));
    }

    [FactRichiedeMySql]
    public async Task Il_cursore_dell_import_non_si_sposta()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42");
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        ScriviCursore(c);

        var handler = new EcosFinto(
            RispostaToken(),
            RispostaTimbrature(Riga("s1", "2026-02-05 08:00:00", "42", "IN")));

        await Servizio(handler).ImportWindowAsync(mario, Giorno, Giorno);

        Assert.Equal(CursoreIniziale, c.ExecuteScalar<string>(
            "SELECT config_value FROM app_config WHERE config_key = 'hr_sync_punches_from'"));
    }

    [FactRichiedeMySql]
    public async Task Le_rettifiche_e_le_righe_di_altri_dipendenti_restano_dove_sono()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42");
        int luigi = Dipendente(c, "Luigi", "Verdi", "43");

        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        Rettifica(c, mario, Giorno.AddHours(17), "OUT", "Uscita dimenticata");
        Grezza(c, luigi, "L1", Giorno.AddHours(8), "IN");
        ScriviCursore(c);

        // Ecos non ha più NIENTE per il 5 febbraio.
        var handler = new EcosFinto(RispostaToken(), RispostaTimbrature());

        await Servizio(handler).ImportWindowAsync(mario, Giorno, Giorno);

        // Il grezzo di Mario sparito su Ecos se ne va…
        Assert.Null(IdEsterno(c, mario, "s1"));
        // …ma la sua rettifica resta: è nostra, il grezzo non la comanda.
        Assert.Equal(1, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id AND source = 'ADJUSTMENT'",
            new { Id = mario }));
        // …e Luigi non è stato chiesto: non si tocca.
        Assert.Equal(1, Timbrature(c, luigi, Giorno));
    }

    [FactRichiedeMySql]
    public async Task Una_timbratura_spostata_su_un_altro_giorno_viene_ricollocata_non_persa()
    {
        // 🪤 Il motivo per cui si scarica il mese e si cancella solo dentro la finestra: se
        // si filtrasse anche l'inserimento al giorno chiesto, la timbratura spostata da Ecos
        // sparirebbe dal 5 e non ricomparirebbe da nessuna parte.
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42");
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        ScriviCursore(c);

        var handler = new EcosFinto(
            RispostaToken(),
            RispostaTimbrature(Riga("s1", "2026-02-06 08:00:00", "42", "IN")));

        await Servizio(handler).ImportWindowAsync(mario, Giorno, Giorno);

        Assert.Equal(0, Timbrature(c, mario, Giorno));
        Assert.Equal(1, Timbrature(c, mario, GiornoDopo));
    }

    [FactRichiedeMySql]
    public async Task Sul_mese_di_tutti_una_risposta_vuota_non_cancella_niente()
    {
        // 🪤 Zero righe dall'intero mese non è un mese vuoto: è molto più probabile che il
        // filtro non abbia funzionato. Sulla finestra larga proseguire vorrebbe dire
        // svuotare il mese di TUTTI in silenzio, quindi ci si ferma.
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42");
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        ScriviCursore(c);

        var handler = new EcosFinto(RispostaToken(), RispostaTimbrature());
        HrImportResultDto esito = await Servizio(handler)
            .ImportWindowAsync(null, new DateTime(2026, 2, 1), new DateTime(2026, 2, 28));

        Assert.True(esito.Success);
        Assert.Contains("niente è stato modificato", esito.Message);
        Assert.Equal(1, Timbrature(c, mario, Giorno));
    }

    [FactRichiedeMySql]
    public async Task Il_mese_si_chiede_a_Ecos_col_filtro_YearMonth()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", "42");
        ScriviCursore(c);

        var handler = new EcosFinto(RispostaToken(), RispostaTimbrature());
        await Servizio(handler).ImportWindowAsync(mario, Giorno, Giorno);

        // Il secondo POST è quello delle timbrature: dentro ci va il filtro del mese,
        // altrimenti si scaricherebbe tutto lo storico per rifare un giorno.
        Assert.Contains("YearMonth", handler.CorpiInviati[1]);
        Assert.Contains("202602", handler.CorpiInviati[1]);
    }

    [FactRichiedeMySql]
    public async Task Un_dipendente_non_collegato_a_Ecos_lo_dice_invece_di_svuotargli_il_cartellino()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "Mario", "Rossi", ecosCode: null);
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");

        HrImportResultDto esito = await Servizio(new EcosFinto(RispostaToken()))
            .ImportWindowAsync(mario, Giorno, Giorno);

        Assert.False(esito.Success);
        Assert.Contains("non collegato", esito.Message);
        Assert.Equal(1, Timbrature(c, mario, Giorno));
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private HrAttendanceService Servizio(HttpMessageHandler handler)
    {
        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ecos:UserId"] = "utente",
                ["Ecos:Password"] = "segreta",
                ["Ecos:ClientId"] = "atec",
            })
            .Build();
        var ecos = new EcosClient(config, NullLogger<EcosClient>.Instance, new HttpClient(handler));
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static string RispostaToken() => """
        { "ECOSAGILE_TABLE_DATA": {
            "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "MESSAGE": "" },
            "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": { "AuthToken": "tok-1" } } } }
        """;

    private static string RispostaTimbrature(params string[] righe) =>
        righe.Length == 0
            ? """
              { "ECOSAGILE_TABLE_DATA": {
                  "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "TRUE" },
                  "ECOSAGILE_DATA": "" } }
              """
            : $$"""
              { "ECOSAGILE_TABLE_DATA": {
                  "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "LASTPAGE": "TRUE" },
                  "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": [ {{string.Join(",", righe)}} ] } } }
              """;

    private static string Riga(string id, string quando, string emplCode, string verso) =>
        $$"""
        { "StampID": "{{id}}", "StampDateTime": "{{quando}}", "EmplCode": "{{emplCode}}",
          "NameComplete": "Rossi, Mario", "VersusCode": "{{verso}}",
          "StampLocationName": "Sede", "YearMonth": "202602",
          "UpdateDate": "2026-02-07 09:00:00", "StatusCode": "" }
        """;

    private static int Dipendente(MySqlConnection c, string nome, string cognome, string? ecosCode)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code) VALUES (@N, @C, @K)",
            new { N = nome, C = cognome, K = ecosCode });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void Grezza(MySqlConnection c, int employeeId, string idEsterno, DateTime quando, string verso) =>
        c.Execute(@"
            INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, external_id)
            VALUES (@Id, @Giorno, @Quando, @Verso, 'ECOS', @Esterno)",
            new { Id = employeeId, Giorno = quando.Date, Quando = quando, Verso = verso, Esterno = idEsterno });

    private static void Rettifica(
        MySqlConnection c, int employeeId, DateTime quando, string verso, string motivo) =>
        c.Execute(@"
            INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, reason, created_by)
            VALUES (@Id, @Giorno, @Quando, @Verso, 'ADJUSTMENT', @Motivo, @Id)",
            new { Id = employeeId, Giorno = quando.Date, Quando = quando, Verso = verso, Motivo = motivo });

    private static void ScriviCursore(MySqlConnection c) =>
        c.Execute(@"
            INSERT INTO app_config (config_key, config_value, description)
            VALUES ('hr_sync_punches_from', @V, 'prova')
            ON DUPLICATE KEY UPDATE config_value = @V",
            new { V = CursoreIniziale });

    private static int Timbrature(MySqlConnection c, int employeeId, DateTime giorno) =>
        c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = employeeId, Giorno = giorno.Date });

    private static long? IdEsterno(MySqlConnection c, int employeeId, string idEsterno) =>
        c.ExecuteScalar<long?>(
            "SELECT id FROM hr_punches WHERE employee_id = @Id AND external_id = @Esterno",
            new { Id = employeeId, Esterno = idEsterno });

    /// <summary>Ecos finto: risponde i corpi in coda, uno per chiamata, e registra i POST.</summary>
    private sealed class EcosFinto : HttpMessageHandler
    {
        private readonly Queue<string> _corpi;

        public EcosFinto(params string[] corpi) => _corpi = new Queue<string>(corpi);

        public List<string> CorpiInviati { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Content != null)
                CorpiInviati.Add(request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());

            string corpo = _corpi.Count > 0 ? _corpi.Dequeue() : RispostaTimbrature();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(corpo),
            });
        }
    }
}
