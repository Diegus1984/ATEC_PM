using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// L'entrata prima delle 8 vale solo se qualcuno la autorizza (Diego, 10/09/2026: «l'orario di
/// inizio al mattino è alle 8; se l'orario arrotondato è prima delle 8 bisogna avere un
/// pulsante Autorizza / Non autorizzare — se premo autorizza mantengo l'orario, altrimenti va
/// approssimato alle 8, questo perché c'è gente che arriva, timbra alle 7:30 e si fa mezz'ora
/// di straordinario non autorizzato tutti i giorni»). Senza decisione vale il no.
/// </summary>
public class EntrataAnticipataMotoreTests
{
    private static readonly DateTime Giorno = new(2026, 9, 10);
    private static readonly DateTime Domani = new(2026, 9, 11);

    private static TimesheetDay Calcola(DateTime giorno, bool autorizzata, params (int H, int M, string Verso)[] t) =>
        TimesheetEngine.Calcola(
            giorno,
            t.Select(x => new RawPunch(giorno.AddHours(x.H).AddMinutes(x.M), x.Verso)),
            giorno.AddDays(1),
            new TimesheetEngine.EmployeeConfig(true, autorizzata));

    [Fact]
    public void Senza_autorizzazione_la_giornata_comincia_alle_otto()
    {
        TimesheetDay c = Calcola(Giorno, autorizzata: false, (7, 30, "IN"), (17, 0, "OUT"));

        // L'ora che vale è le 8; sotto restano gli orari veri, così si vede cos'è successo.
        Assert.Equal("08:00", c.Entrata1);
        Assert.Equal("07:30", c.RawEntrata1);
        Assert.Equal("07:30", c.NormEntrata1);

        // Otto ore tonde: la mezz'ora davanti non diventa straordinario.
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("0h 0m", c.Overtime);
    }

    [Fact]
    public void Autorizzata_vale_l_orario_timbrato_e_la_mezz_ora_si_conta()
    {
        TimesheetDay c = Calcola(Giorno, autorizzata: true, (7, 30, "IN"), (17, 0, "OUT"));

        Assert.Equal("07:30", c.Entrata1);
        Assert.Equal("8h 0m", c.RegularHours);
        Assert.Equal("0h 30m", c.Overtime);
    }

    [Fact]
    public void Le_giornate_prima_della_regola_restano_come_sono_state_pagate()
    {
        // Febbraio: già controllato e pagato con l'orario timbrato. Non si riscrive all'indietro.
        var febbraio = new DateTime(2026, 2, 5);
        TimesheetDay c = Calcola(febbraio, autorizzata: false, (7, 30, "IN"), (17, 0, "OUT"));

        Assert.Equal("07:30", c.Entrata1);
        Assert.Equal("0h 30m", c.Overtime);
    }

    [Fact]
    public void Chi_comincia_prima_delle_cinque_sta_facendo_un_altro_turno()
    {
        // Le 4 del mattino non sono «arrivare in anticipo»: la regola delle 8 non le tocca.
        TimesheetDay c = Calcola(Giorno, autorizzata: false, (4, 0, "IN"), (12, 0, "OUT"));
        Assert.Equal("04:00", c.Entrata1);
    }

    [Fact]
    public void Sull_orario_gia_buono_non_cambia_niente()
    {
        TimesheetDay c = Calcola(Giorno, autorizzata: false, (8, 0, "IN"), (12, 30, "OUT"),
            (13, 30, "IN"), (17, 0, "OUT"));

        Assert.Equal("08:00", c.Entrata1);
        Assert.Equal("OK", c.Note);
        Assert.Equal("8h 0m", c.RegularHours);
    }
}

/// <summary>
/// La decisione salvata e il ricalcolo che ne segue: premere «Autorizza» deve cambiare le ore
/// della giornata, non solo scrivere una riga da qualche parte.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class EntrataAnticipataServizioTests
{
    private readonly SchemaCondiviso _schema;

    public EntrataAnticipataServizioTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    /// <summary>Ieri: una giornata chiusa (oggi sarebbe «in corso» e non si calcola).</summary>
    private static readonly DateTime Giorno = new(2026, 9, 9);

    /// <summary>
    /// Diego, 10/09/2026: «sia che accetto o che rifiuto l'ingresso in anticipo, fai
    /// aggiornamento dell'orario di ingresso su Ecos e sul locale», senza chiedere conferma.
    /// Rifiutare vuol dire che la giornata comincia alle 8: e quelle vanno anche su Ecos.
    /// </summary>
    [FactRichiedeMySql]
    public async Task Rifiutare_porta_l_entrata_alle_otto_su_Ecos_e_qui()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        int capo = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(30), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");
        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate());
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        (string? errore, string? avviso) = await servizio.SetEarlyEntryAsync(mario, Giorno, authorized: false, capo);

        Assert.Null(errore);
        Assert.Null(avviso);

        // Una modifica sola, sull'entrata: il resto della giornata non c'entra.
        Assert.Equal(2, ecos.UrlChiamati.Count);
        Assert.Contains("Edit=true", ecos.UrlChiamati[1]);
        Assert.Contains("StampID=s1", ecos.CorpiInviati[1]);
        Assert.Contains("StampDateTime=2026-09-09+08%3A00%3A00", ecos.CorpiInviati[1]);

        // Specchio: qui l'entrata è le 8, l'uscita è rimasta dov'era.
        Assert.Equal(
            (Giorno.AddHours(8), (DateTime?)Giorno.AddHours(8)),
            c.QuerySingle<(DateTime PunchedAt, DateTime? SuEcos)>(
                "SELECT punched_at, ecos_punched_at FROM hr_punches WHERE external_id = 's1'"));
        Assert.Equal(Giorno.AddHours(17),
            c.ExecuteScalar<DateTime>("SELECT punched_at FROM hr_punches WHERE external_id = 's2'"));

        // La giornata è rifatta, e il registro tiene la storia.
        Assert.Equal(("08:00", 480, 0), Giornata(c, mario));
        var registro = c.QuerySingle<(string StampId, DateTime PunchedAt, DateTime SentTime, string Outcome, string Message)>(
            "SELECT ecos_stamp_id, punched_at, sent_time, outcome, message FROM hr_ecos_sends WHERE employee_id = @Id",
            new { Id = mario });
        Assert.Equal("s1", registro.StampId);
        Assert.Equal(Giorno.AddHours(7).AddMinutes(30), registro.PunchedAt);
        Assert.Equal(Giorno.AddHours(8), registro.SentTime);
        Assert.Equal("OK", registro.Outcome);
        Assert.Contains("rifiutata", registro.Message);
    }

    [FactRichiedeMySql]
    public async Task Approvare_porta_su_Ecos_l_orario_timbrato_arrotondato()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        int capo = Dipendente(c, null);
        // Timbra 07:35: dentro la tolleranza, l'ora che vale è le 07:30.
        Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(35), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");
        var ecos = new InvioEcosTests.EcosFinto(InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaUpdate());
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        (string? errore, string? avviso) = await servizio.SetEarlyEntryAsync(mario, Giorno, authorized: true, capo);

        Assert.Null(errore);
        Assert.Null(avviso);
        Assert.Contains("StampDateTime=2026-09-09+07%3A30%3A00", ecos.CorpiInviati[1]);

        // Qui l'entrata è le 07:30 e la mezz'ora davanti si conta.
        Assert.Equal(Giorno.AddHours(7).AddMinutes(30),
            c.ExecuteScalar<DateTime>("SELECT punched_at FROM hr_punches WHERE external_id = 's1'"));
        Assert.Equal(("07:30", 480, 30), Giornata(c, mario));
    }

    [FactRichiedeMySql]
    public async Task Se_Ecos_rifiuta_la_modifica_qui_non_si_cambia_niente()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        int capo = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(30), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");
        var ecos = new InvioEcosTests.EcosFinto(
            InvioEcosTests.RispostaToken(), InvioEcosTests.RispostaErrore("-2", "Record not found"));
        HrAttendanceService servizio = Servizio(ecos);
        servizio.RecalculateDay(c, mario, Giorno);

        (string? errore, string? avviso) = await servizio.SetEarlyEntryAsync(mario, Giorno, authorized: false, capo);

        // La decisione vale comunque, ma l'orario qui resta quello di prima: i due non si
        // devono scollare, e il perché si legge.
        Assert.Null(errore);
        Assert.NotNull(avviso);
        Assert.Contains("Record not found", avviso);
        Assert.Equal(Giorno.AddHours(7).AddMinutes(30),
            c.ExecuteScalar<DateTime>("SELECT punched_at FROM hr_punches WHERE external_id = 's1'"));
        Assert.Equal("ERROR",
            c.ExecuteScalar<string>("SELECT outcome FROM hr_ecos_sends WHERE employee_id = @Id", new { Id = mario }));

        // Il conto della giornata rispetta lo stesso la decisione: si parte dalle 8.
        Assert.Equal(("08:00", 480, 0), Giornata(c, mario));
    }

    [FactRichiedeMySql]
    public async Task Una_giornata_senza_timbrature_non_si_decide()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        var ecos = new InvioEcosTests.EcosFinto();

        (string? errore, _) = await Servizio(ecos).SetEarlyEntryAsync(mario, Giorno, true, mario);

        Assert.Contains("non ha timbrature", errore);
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_early_entries"));
        Assert.Empty(ecos.UrlChiamati);
    }

    private static (string Entrata, int Ordinari, int Straordinari) Giornata(MySqlConnection c, int employeeId) =>
        c.QuerySingle<(string, int, int)>(
            @"SELECT clock_in_1, regular_minutes, overtime_minutes
              FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = employeeId, Giorno });

    private static int Dipendente(MySqlConnection c, string? ecosCode)
    {
        c.Execute("INSERT INTO employees (first_name, last_name, ecos_empl_code) VALUES ('Mario', 'Rossi', @Codice)",
            new { Codice = ecosCode });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void Grezza(MySqlConnection c, int employeeId, string stampId, DateTime orario, string verso)
    {
        c.Execute(@"INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, external_id)
                    VALUES (@Id, @Giorno, @Quando, @Verso, 'ECOS', @Stamp)",
            new { Id = employeeId, Giorno = orario.Date, Quando = orario, Verso = verso, Stamp = stampId });
    }

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
}
