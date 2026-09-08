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
/// Le regole pure di «Invia a Ecos» (08/09/2026): cosa si manda e cosa no.
/// </summary>
public class TimbraturaEcosTests
{
    private static readonly DateTime Giorno = new(2026, 2, 5);

    private static HrAttendanceService.TimbraturaEcos T(int ore, int minuti, string verso, DateTime? suEcos = null) =>
        new(1, "s1", verso, Giorno.AddHours(ore).AddMinutes(minuti), suEcos, null);

    [Theory]
    [InlineData(7, 58, "IN", 8, 0)]    // entrata: sale allo scatto
    [InlineData(8, 0, "IN", 8, 0)]     // già sullo scatto
    [InlineData(8, 9, "IN", 8, 0)]     // entro la tolleranza di 10': resta
    [InlineData(17, 12, "OUT", 17, 0)] // uscita: scende
    [InlineData(17, 4, "OUT", 17, 0)]
    [InlineData(12, 32, "OUT", 12, 30)]
    [InlineData(13, 28, "IN", 13, 30)]
    public void L_orario_arrotondato_e_quello_del_motore(int h, int m, string verso, int hAtt, int mAtt)
    {
        Assert.Equal(Giorno.AddHours(hAtt).AddMinutes(mAtt), T(h, m, verso).Arrotondata);
    }

    [Fact]
    public void Si_manda_solo_se_l_arrotondato_differisce_da_quello_che_Ecos_ha()
    {
        Assert.True(T(7, 58, "IN").DaInviare);                                   // Ecos ha 07:58, va 08:00
        Assert.False(T(8, 0, "IN").DaInviare);                                   // già allineata
        Assert.False(T(7, 58, "IN", Giorno.AddHours(8)).DaInviare);              // già inviata
        Assert.True(T(7, 58, "IN", Giorno.AddHours(8).AddMinutes(30)).DaInviare); // su Ecos c'è altro
    }

    [Fact]
    public void Un_entrata_alle_23_55_arrotonda_al_giorno_dopo_e_non_si_manda()
    {
        HrAttendanceService.TimbraturaEcos t = T(23, 55, "IN");
        Assert.Equal(Giorno.AddDays(1), t.Arrotondata);
        Assert.False(t.Inviabile);
        Assert.False(t.DaInviare);
    }

    [Theory]
    [InlineData("2026-07-15 08:00:00", -120)] // ora legale
    [InlineData("2026-01-15 08:00:00", -60)]  // ora solare
    public void UserTZ_e_lo_scarto_dall_UTC_col_segno_di_JavaScript(string quando, int atteso)
    {
        Assert.Equal(atteso, EcosClient.UserTz(DateTime.ParseExact(quando, "yyyy-MM-dd HH:mm:ss", null)));
    }

    [Fact]
    public void L_esito_di_una_Post_e_OK_oppure_un_errore_spiegato()
    {
        Assert.Equal("Correct Record Update", EcosClient.EsitoScrittura(InvioEcosTests.RispostaUpdate(), "PeopleStampPost"));

        var ex = Assert.Throws<EcosApiException>(() =>
            EcosClient.EsitoScrittura(InvioEcosTests.RispostaErrore("-17", "Key missing"), "PeopleStampPost"));
        Assert.Contains("Key missing", ex.Message);
        Assert.Contains("-17", ex.Message);

        // Edit=true a corpo vuoto: Ecos risponde VUOTO, e anche quello è un errore.
        Assert.Throws<EcosApiException>(() => EcosClient.EsitoScrittura("", "PeopleStampPost"));
    }
}

/// <summary>
/// Il pulsante «Invia a Ecos» sul database: gli orari arrotondati partono, il registro li
/// tiene con l'ora originale, l'import riconosce l'eco e vince Ecos quando là cambiano.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class InvioEcosTests
{
    private readonly SchemaCondiviso _schema;

    public InvioEcosTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static readonly DateTime Giorno = new(2026, 2, 5);

    [FactRichiedeMySql]
    public async Task Manda_gli_orari_arrotondati_e_li_registra_con_l_ora_originale()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        int autore = Dipendente(c, null);
        long entrata = Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(58), "IN");
        long uscita = Grezza(c, mario, "s2", Giorno.AddHours(17).AddMinutes(12), "OUT");

        var ecos = new EcosFinto(RispostaToken(), RispostaUpdate(), RispostaUpdate());
        HrEcosSendResultDto esito = await Servizio(ecos).SendDayToEcosAsync(mario, Giorno, autore);

        Assert.True(esito.Success, esito.Message);
        Assert.Equal(2, esito.Sent);
        Assert.Equal(0, esito.Failed);

        // Le chiamate: token, poi una modifica per timbratura, con Edit=true e la chiave nel corpo.
        Assert.Equal(3, ecos.UrlChiamati.Count);
        Assert.All(ecos.UrlChiamati.Skip(1), u => Assert.Contains("ApiName=PeopleStampPost&Edit=true", u));
        Assert.Contains("StampID=s1", ecos.CorpiInviati[1]);
        Assert.Contains("StampDateTime=2026-02-05+08%3A00%3A00", ecos.CorpiInviati[1]);
        Assert.Contains("UserTZ=-60", ecos.CorpiInviati[1]);
        Assert.Contains("StampID=s2", ecos.CorpiInviati[2]);
        Assert.Contains("StampDateTime=2026-02-05+17%3A00%3A00", ecos.CorpiInviati[2]);

        // L'ora timbrata non si tocca; accanto c'è quella che Ecos ha adesso.
        var riga = c.QuerySingle<(DateTime PunchedAt, DateTime? SuEcos, DateTime? Quando)>(
            "SELECT punched_at, ecos_punched_at, ecos_sent_at FROM hr_punches WHERE id = @Id", new { Id = entrata });
        Assert.Equal(Giorno.AddHours(7).AddMinutes(58), riga.PunchedAt);
        Assert.Equal(Giorno.AddHours(8), riga.SuEcos);
        Assert.NotNull(riga.Quando);

        // Il registro: ora originale, ora inviata, esito e autore.
        var registro = c.Query<(long PunchId, DateTime PunchedAt, DateTime SentTime, string Outcome, int SentBy)>(
            "SELECT punch_id, punched_at, sent_time, outcome, sent_by FROM hr_ecos_sends WHERE employee_id = @Id ORDER BY id",
            new { Id = mario }).ToList();
        Assert.Equal(2, registro.Count);
        Assert.Equal((entrata, Giorno.AddHours(7).AddMinutes(58), Giorno.AddHours(8), "OK", autore), registro[0]);
        Assert.Equal((uscita, Giorno.AddHours(17).AddMinutes(12), Giorno.AddHours(17), "OK", autore), registro[1]);

        // La seconda volta non c'è niente da mandare: nessuna chiamata oltre... nessuna.
        var ecosDopo = new EcosFinto(RispostaToken());
        HrEcosSendResultDto secondo = await Servizio(ecosDopo).SendDayToEcosAsync(mario, Giorno, autore);
        Assert.True(secondo.Success);
        Assert.Equal(0, secondo.Sent);
        Assert.Empty(ecosDopo.UrlChiamati);
        Assert.Contains("già", secondo.Message);
    }

    [FactRichiedeMySql]
    public async Task Una_timbratura_gia_sullo_scatto_e_una_rettifica_non_partono()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        c.Execute(@"INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, reason)
                    VALUES (@Id, @Giorno, @Ora, 'OUT', 'ADJUSTMENT', 'uscita dimenticata')",
            new { Id = mario, Giorno, Ora = Giorno.AddHours(17).AddMinutes(4) });

        var ecos = new EcosFinto(RispostaToken());
        HrEcosSendResultDto esito = await Servizio(ecos).SendDayToEcosAsync(mario, Giorno, mario);

        Assert.True(esito.Success);
        Assert.Equal(1, esito.Total);   // solo la timbratura di Ecos conta
        Assert.Equal(0, esito.Sent);
        Assert.Empty(ecos.UrlChiamati);
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_ecos_sends"));
    }

    [FactRichiedeMySql]
    public async Task Un_errore_di_Ecos_finisce_nel_registro_e_ferma_l_invio()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        long entrata = Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(58), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17).AddMinutes(12), "OUT");

        var ecos = new EcosFinto(RispostaToken(), RispostaErrore("-1", "Token expired"), RispostaUpdate());
        HrEcosSendResultDto esito = await Servizio(ecos).SendDayToEcosAsync(mario, Giorno, mario);

        Assert.False(esito.Success);
        Assert.Equal(0, esito.Sent);
        Assert.Equal(1, esito.Failed);
        Assert.Contains("Token expired", esito.Message);
        Assert.Contains("1 non tentate", esito.Message);
        // Token + la prima modifica: la seconda non si tenta.
        Assert.Equal(2, ecos.UrlChiamati.Count);

        var registro = c.QuerySingle<(long PunchId, string Outcome, string Message)>(
            "SELECT punch_id, outcome, message FROM hr_ecos_sends");
        Assert.Equal(entrata, registro.PunchId);
        Assert.Equal("ERROR", registro.Outcome);
        Assert.Contains("Token expired", registro.Message);
        // Niente segnato come inviato.
        Assert.Null(c.ExecuteScalar<DateTime?>("SELECT ecos_punched_at FROM hr_punches WHERE id = @Id", new { Id = entrata }));
    }

    [FactRichiedeMySql]
    public async Task L_import_riconosce_l_eco_del_nostro_invio_e_l_ora_timbrata_resta()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        long entrata = Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(58), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17).AddMinutes(12), "OUT");
        HrAttendanceService servizio = Servizio(new EcosFinto(RispostaToken(), RispostaUpdate(), RispostaUpdate()));
        await servizio.SendDayToEcosAsync(mario, Giorno, mario);

        // Ecos rimanda le timbrature con gli orari arrotondati che gli abbiamo scritto noi.
        HrImportResultDto eco = servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s1", Giorno.AddHours(8), "IN"),
            Timbratura("s2", Giorno.AddHours(17), "OUT"),
        });
        Assert.Equal(0, eco.PunchesUpdated);
        Assert.Equal(Giorno.AddHours(7).AddMinutes(58),
            c.ExecuteScalar<DateTime>("SELECT punched_at FROM hr_punches WHERE id = @Id", new { Id = entrata }));
        Assert.Equal(Giorno.AddHours(8),
            c.ExecuteScalar<DateTime?>("SELECT ecos_punched_at FROM hr_punches WHERE id = @Id", new { Id = entrata }));

        // Su Ecos qualcuno sposta l'entrata alle 08:30: vince Ecos, e l'invio decade.
        HrImportResultDto cambiata = servizio.ImportPunches(c, new List<EcosPunch>
        {
            Timbratura("s1", Giorno.AddHours(8).AddMinutes(30), "IN"),
        });
        Assert.Equal(1, cambiata.PunchesUpdated);
        Assert.Equal(Giorno.AddHours(8).AddMinutes(30),
            c.ExecuteScalar<DateTime>("SELECT punched_at FROM hr_punches WHERE id = @Id", new { Id = entrata }));
        Assert.Null(c.ExecuteScalar<DateTime?>("SELECT ecos_punched_at FROM hr_punches WHERE id = @Id", new { Id = entrata }));
        Assert.Null(c.ExecuteScalar<DateTime?>("SELECT ecos_sent_at FROM hr_punches WHERE id = @Id", new { Id = entrata }));
    }

    // ── attrezzi ──────────────────────────────────────────────────────────────

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

    private static int Dipendente(MySqlConnection c, string? ecosCode)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code) VALUES ('Mario', 'Rossi', @Codice)",
            new { Codice = ecosCode });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static long Grezza(MySqlConnection c, int employeeId, string stampId, DateTime orario, string verso)
    {
        c.Execute(@"INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, external_id)
                    VALUES (@Id, @Giorno, @Ora, @Verso, 'ECOS', @Stamp)",
            new { Id = employeeId, Giorno = orario.Date, Ora = orario, Verso = verso, Stamp = stampId });
        return c.ExecuteScalar<long>("SELECT LAST_INSERT_ID()");
    }

    private static EcosPunch Timbratura(string id, DateTime orario, string verso) =>
        // Senza luogo, come le righe inserite da Grezza: qui si guarda solo l'orario.
        new(id, orario, "42", "Rossi, Mario", verso, null, orario.AddMinutes(1));

    private static string RispostaToken() => """
        { "ECOSAGILE_TABLE_DATA": {
            "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "MESSAGE": "" },
            "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": { "AuthToken": "tok-1" } } } }
        """;

    /// <summary>Come risponde Ecos a una modifica riuscita (visto l'08/09/2026).</summary>
    internal static string RispostaUpdate() => """
        { "ECOSAGILE_TABLE_DATA": {
            "ECOSAGILE_ERROR_MESSAGE": { "CODE": "OK", "ERROR_CODE": "0", "RECORDCOUNT": "0", "MESSAGE": "Correct Record Update" },
            "ECOSAGILE_DATA": { "ECOSAGILE_DATA_ROW": { "StampID": "10341189" } } } }
        """;

    internal static string RispostaErrore(string codice, string messaggio) => $$"""
        { "ECOSAGILE_TABLE_DATA": {
            "ECOSAGILE_ERROR_MESSAGE": { "CODE": "FAIL", "ERROR_CODE": "{{codice}}", "RECORDCOUNT": "0", "MESSAGE": "{{messaggio}}" },
            "ECOSAGILE_DATA": "" } }
        """;

    private sealed class EcosFinto : HttpMessageHandler
    {
        private readonly Queue<string> _corpi;

        public EcosFinto(params string[] corpi) => _corpi = new Queue<string>(corpi);

        public List<string> CorpiInviati { get; } = new();
        public List<string> UrlChiamati { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            UrlChiamati.Add(request.RequestUri?.ToString() ?? "");
            CorpiInviati.Add(request.Content == null ? "" : request.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult());
            string corpo = _corpi.Count > 0 ? _corpi.Dequeue() : RispostaErrore("-99", "Nessuna risposta preparata");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(corpo) });
        }
    }
}
