using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// La pausa dedotta che va su Ecos (09/09/2026, Diego dal «Controllo di ieri»: «se le ore
/// calcolate sono corrette vorrei poterle sincronizzare con Ecos, se l'ora di pausa non esiste,
/// la devo inserire»). La regola di QUALI timbrature si inventano è la parte delicata: sbagliare
/// vuol dire strisciate in più su Ecos e giornate «da verificare» al prossimo import.
/// </summary>
public class PausaDedottaTests
{
    private static readonly DateTime Giorno = new(2026, 2, 5);

    private static (string, DateTime) Esistente(string verso, int h, int m) => (verso, Giorno.AddHours(h).AddMinutes(m));

    [Fact]
    public void Con_due_sole_timbrature_la_pausa_dedotta_sono_due_strisciate_da_inserire()
    {
        List<HrAttendanceService.TimbraturaDedotta> d = HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa 1h detratta", "12:30*", "13:30*",
            new[] { Esistente("IN", 8, 0), Esistente("OUT", 17, 0) });

        Assert.Equal(2, d.Count);
        Assert.Equal(("OUT", Giorno.AddHours(12).AddMinutes(30)), (d[0].Direction, d[0].Quando));
        Assert.Equal(("IN", Giorno.AddHours(13).AddMinutes(30)), (d[1].Direction, d[1].Quando));
    }

    [Fact]
    public void Con_una_entrata_e_due_uscite_parte_solo_il_rientro()
    {
        // L'uscita con l'asterisco è una timbratura VERA (12:30 timbrata): è coperta, resta.
        List<HrAttendanceService.TimbraturaDedotta> d = HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa implicita (1 IN / 2 OUT)", "12:30*", "13:30*",
            new[] { Esistente("IN", 8, 0), Esistente("OUT", 12, 30), Esistente("OUT", 17, 0) });

        HrAttendanceService.TimbraturaDedotta sola = Assert.Single(d);
        Assert.Equal("IN", sola.Direction);
        Assert.Equal(Giorno.AddHours(13).AddMinutes(30), sola.Quando);
    }

    [Theory]
    // Quattro timbrature con la pausa troppo corta: la pausa è forzata ma le strisciate CI SONO.
    [InlineData("AUTO_P: Pausa 1h forzata")]
    [InlineData("OK")]
    [InlineData("Turno mattutino")]
    [InlineData("⚠ INCOMPLETO: Uscita mancante")]
    [InlineData("")]
    [InlineData(null)]
    public void Nelle_altre_giornate_non_si_inventa_niente(string? nota)
    {
        Assert.Empty(HrAttendanceService.TimbratureDedotte(
            Giorno, nota, "12:30*", "13:30*", new[] { Esistente("IN", 8, 0) }));
    }

    [Fact]
    public void La_nota_con_la_coda_della_notte_vale_lo_stesso_e_le_24_00_non_si_leggono()
    {
        Assert.Equal(2, HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa 1h detratta · 🌙 Notte: il turno finisce domattina", "12:30*", "13:30*",
            Array.Empty<(string, DateTime)>()).Count);

        // Un orario che non è un'ora del giorno resta fuori da sé.
        Assert.Empty(HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa 1h detratta", "24:00*", "00:00", Array.Empty<(string, DateTime)>()));
    }

    [Fact]
    public void Una_pausa_gia_coperta_da_una_rettifica_non_si_reinserisce()
    {
        // HR ha già messo il rientro delle 13:30 come rettifica: parte solo l'uscita.
        List<HrAttendanceService.TimbraturaDedotta> d = HrAttendanceService.TimbratureDedotte(
            Giorno, "AUTO_P: Pausa 1h detratta", "12:30*", "13:30*",
            new[] { Esistente("IN", 8, 0), Esistente("IN", 13, 30), Esistente("OUT", 17, 0) });

        HrAttendanceService.TimbraturaDedotta sola = Assert.Single(d);
        Assert.Equal("OUT", sola.Direction);
    }
}

/// <summary>
/// «Allinea Ecos» sul database: il resoconto dice cosa si scrive, la pausa dedotta diventa
/// timbrature vere (su Ecos e qui), la giornata si ricalcola subito, un timeout non si riprova.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class AllineaEcosTests
{
    private readonly SchemaCondiviso _schema;

    public AllineaEcosTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static readonly DateTime Giorno = new(2026, 2, 5);

    [FactRichiedeMySql]
    public void Il_resoconto_elenca_modifiche_e_pausa_da_inserire_con_la_giornata_calcolata()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(58), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17).AddMinutes(12), "OUT");
        HrAttendanceService servizio = Servizio(new InvioEcosTests.EcosFinto());
        servizio.RecalculateDay(c, mario, Giorno);

        HrEcosPlanDto piano = servizio.GetEcosPlan(mario, Giorno);

        Assert.True(piano.Configured);
        Assert.Equal("AUTO_P: Pausa 1h detratta", piano.Note);
        Assert.Equal("8h 0m", piano.RegularHours);
        Assert.Equal("1h 0m", piano.BreakTime);
        Assert.Equal("12:30*", piano.ClockOut1);
        Assert.Equal(
            new[] { "UPDATE", "UPDATE", "INSERT_BREAK", "INSERT_BREAK" },
            piano.Operations.Select(o => o.Kind).ToArray());
        Assert.Equal("Entrata 07:58 → 08:00", piano.Operations[0].Label);
        Assert.Equal("Uscita 17:12 → 17:00", piano.Operations[1].Label);
        Assert.Equal("Uscita 12:30 (pausa)", piano.Operations[2].Label);
        Assert.Equal("Entrata 13:30 (pausa)", piano.Operations[3].Label);
        Assert.Equal(4, piano.ToWrite);
        Assert.True(piano.CanSend);
        Assert.Equal("", piano.Message);

        // Il cartellino porta lo stesso flag della riga: la pausa è da inserire.
        HrDayDto giornata = servizio.GetMonthlyTimesheet(mario, 2026, 2).Days.Single(g => g.WorkDate == Giorno);
        Assert.True(giornata.EcosBreakToInsert);
    }

    [FactRichiedeMySql]
    public void Con_un_anomalia_non_si_allinea_niente()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(58), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(12).AddMinutes(33), "OUT");
        Grezza(c, mario, "s3", Giorno.AddHours(13).AddMinutes(19), "IN");
        HrAttendanceService servizio = Servizio(new InvioEcosTests.EcosFinto());
        servizio.RecalculateDay(c, mario, Giorno);

        HrEcosPlanDto piano = servizio.GetEcosPlan(mario, Giorno);

        Assert.True(piano.HasAnomaly);
        Assert.False(piano.CanSend);
        Assert.Contains("anomalia", piano.Message);
        // Le modifiche degli orari ci sarebbero, ma non si toccano finché la giornata non è sistemata.
        Assert.DoesNotContain(piano.Operations, o => o.Kind == "INSERT_BREAK");
    }

    [FactRichiedeMySql]
    public async Task La_pausa_dedotta_si_inserisce_su_Ecos_e_la_giornata_diventa_timbrata()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        long entrata = Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        long uscita = Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");

        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaBadge("246b3548"),
            InvioEcosTests.RispostaInsert("s9", "5374", "42"), InvioEcosTests.RispostaInsert("s10", "5374", "42"));
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        HrEcosSendResultDto esito = await servizio.SendDayToEcosAsync(mario, Giorno, autore);

        Assert.True(esito.Success, esito.Message);
        Assert.Equal(0, esito.Sent);
        Assert.Equal(0, esito.Inserted);
        Assert.Equal(2, esito.BreakInserted);
        Assert.Contains("2 timbrature di pausa inserite", esito.Message);

        // Token, badge, due inserimenti col badge: uscita alle 12:30 e rientro alle 13:30.
        Assert.Equal(4, ecos.UrlChiamati.Count);
        Assert.Contains("ApiName=PeopleBadgeGetAll", ecos.UrlChiamati[1]);
        Assert.Contains("ApiName=PeopleStampPost&ReturnAllPostedRecord=1", ecos.UrlChiamati[2]);
        Assert.Contains("BadgeCode=246b3548", ecos.CorpiInviati[2]);
        Assert.Contains("VersusCode=OUT", ecos.CorpiInviati[2]);
        Assert.Contains("StampDateTime=2026-02-05+12%3A30%3A00", ecos.CorpiInviati[2]);
        Assert.Contains("VersusCode=IN", ecos.CorpiInviati[3]);
        Assert.Contains("StampDateTime=2026-02-05+13%3A30%3A00", ecos.CorpiInviati[3]);
        Assert.Contains("pausa+pranzo+dedotta", ecos.CorpiInviati[3]);

        // Qui la pausa è nata come timbratura di Ecos (StampID), col motivo e l'autore.
        var pausa = c.Query<(string Source, string ExternalId, DateTime PunchedAt, string Direction, string? Reason, int? CreatedBy)>(
            @"SELECT source, external_id, punched_at, direction, reason, created_by FROM hr_punches
              WHERE employee_id = @Id AND id NOT IN (@E, @U) ORDER BY punched_at",
            new { Id = mario, E = entrata, U = uscita }).ToList();
        Assert.Equal(2, pausa.Count);
        Assert.Equal(("ECOS", "s9", Giorno.AddHours(12).AddMinutes(30), "OUT"), (pausa[0].Source, pausa[0].ExternalId, pausa[0].PunchedAt, pausa[0].Direction));
        Assert.Equal(("ECOS", "s10", Giorno.AddHours(13).AddMinutes(30), "IN"), (pausa[1].Source, pausa[1].ExternalId, pausa[1].PunchedAt, pausa[1].Direction));
        Assert.Equal(HrAttendanceService.TimbraturaDedotta.Motivo, pausa[0].Reason);
        Assert.Equal(autore, pausa[0].CreatedBy);

        // La giornata si è ricalcolata subito: pausa timbrata, non più dedotta, stesse ore.
        var giornata = c.QuerySingle<(string Note, int Regular, int Break, string ClockOut1)>(
            "SELECT note, regular_minutes, break_minutes, clock_out_1 FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = mario, Giorno });
        Assert.Equal("OK", giornata.Note);
        Assert.Equal(480, giornata.Regular);
        Assert.Equal(60, giornata.Break);
        Assert.Equal("12:30", giornata.ClockOut1);

        // Registro: due inserimenti (senza orario precedente), legati alle timbrature nuove.
        var registro = c.Query<(long? PunchId, string StampId, DateTime? Previous, string Outcome, string? Message)>(
            "SELECT punch_id, ecos_stamp_id, previous_time, outcome, message FROM hr_ecos_sends WHERE employee_id = @Id ORDER BY id",
            new { Id = mario }).ToList();
        Assert.Equal(2, registro.Count);
        Assert.All(registro, r => Assert.Equal("OK", r.Outcome));
        Assert.All(registro, r => Assert.Null(r.Previous));
        Assert.All(registro, r => Assert.NotNull(r.PunchId));
        Assert.Equal("s9", registro[0].StampId);
        Assert.Contains("pausa dedotta", registro[0].Message);

        // Poi non c'è più niente da scrivere, e il flag sulla riga si spegne.
        HrEcosPlanDto dopo = servizio.GetEcosPlan(mario, Giorno);
        Assert.Equal(0, dopo.ToWrite);
        Assert.False(dopo.CanSend);
        Assert.False(servizio.GetMonthlyTimesheet(mario, 2026, 2).Days.Single(g => g.WorkDate == Giorno).EcosBreakToInsert);
    }

    [FactRichiedeMySql]
    public async Task Un_timeout_sulla_pausa_resta_nel_registro_e_non_si_riprova_alla_cieca()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42", emplId: 5374);
        int autore = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(8), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");

        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaBadge("246b3548"), InvioEcosTests.EcosFinto.Timeout);
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        HrEcosSendResultDto esito = await servizio.SendDayToEcosAsync(mario, Giorno, autore);

        Assert.False(esito.Success);
        Assert.Equal(0, esito.BreakInserted);
        Assert.Equal(1, esito.Failed);
        // Al primo errore ci si ferma: il rientro non si è nemmeno tentato.
        Assert.Equal(3, ecos.UrlChiamati.Count);
        // Nessuna timbratura nata qui; nel registro l'esito incerto senza timbratura.
        Assert.Equal(2, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_punches WHERE employee_id = @Id", new { Id = mario }));
        var registro = c.QuerySingle<(long? PunchId, string Outcome, string? Message)>(
            "SELECT punch_id, outcome, message FROM hr_ecos_sends WHERE employee_id = @Id", new { Id = mario });
        Assert.Null(registro.PunchId);
        Assert.Equal("ERROR", registro.Outcome);
        Assert.StartsWith("Esito incerto", registro.Message);

        // Una pausa a metà non si completa alla cieca: il resoconto lo dice e non scrive niente.
        HrEcosPlanDto piano = servizio.GetEcosPlan(mario, Giorno);
        Assert.Equal(0, piano.ToWrite);
        Assert.False(piano.CanSend);
        Assert.Contains(piano.Operations, o => o.Kind == "UNCERTAIN");
        Assert.Contains("senza risposta certa", piano.Message);

        // E nemmeno un secondo «Invia» la riprova.
        var ecosDopo = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken());
        HrEcosSendResultDto secondo = await Servizio(ecosDopo).SendDayToEcosAsync(mario, Giorno, autore);
        Assert.True(secondo.Success);
        Assert.Empty(ecosDopo.UrlChiamati);
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

    private static int Dipendente(MySqlConnection c, string? ecosCode, int? emplId = null)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code, ecos_empl_id) VALUES ('Mario', 'Rossi', @Codice, @EmplId)",
            new { Codice = ecosCode, EmplId = emplId });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static long Grezza(MySqlConnection c, int employeeId, string stampId, DateTime orario, string verso)
    {
        c.Execute(@"INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, external_id)
                    VALUES (@Id, @Giorno, @Ora, @Verso, 'ECOS', @Stamp)",
            new { Id = employeeId, Giorno = orario.Date, Ora = orario, Verso = verso, Stamp = stampId });
        return c.ExecuteScalar<long>("SELECT LAST_INSERT_ID()");
    }
}
