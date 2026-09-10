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

    [FactRichiedeMySql]
    public void Autorizzare_l_anticipo_rifa_il_conto_della_giornata()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");
        int capo = Dipendente(c, null);
        Grezza(c, mario, "s1", Giorno.AddHours(7).AddMinutes(30), "IN");
        Grezza(c, mario, "s2", Giorno.AddHours(17), "OUT");
        HrAttendanceService servizio = Servizio();
        servizio.RecalculateDay(c, mario, Giorno);

        // Nessuno ha deciso: la giornata parte dalle 8 e sono otto ore tonde.
        Assert.Equal(("08:00", 480, 0), Giornata(c, mario));
        HrDayDto prima = servizio.GetMonthlyTimesheet(mario, 2026, 9).Days.Single(g => g.WorkDate == Giorno);
        Assert.Equal(30, prima.EarlyEntryMinutes);
        Assert.Null(prima.EarlyEntryAuthorized);

        Assert.Null(servizio.SetEarlyEntry(mario, Giorno, authorized: true, autoreId: capo));

        // Autorizzata: vale l'orario timbrato, e la mezz'ora diventa straordinario.
        Assert.Equal(("07:30", 480, 30), Giornata(c, mario));
        HrDayDto dopo = servizio.GetMonthlyTimesheet(mario, 2026, 9).Days.Single(g => g.WorkDate == Giorno);
        Assert.True(dopo.EarlyEntryAuthorized);

        // E si può tornare indietro: la giornata riparte dalle 8.
        Assert.Null(servizio.SetEarlyEntry(mario, Giorno, authorized: false, autoreId: capo));
        Assert.Equal(("08:00", 480, 0), Giornata(c, mario));
        Assert.False(servizio.GetMonthlyTimesheet(mario, 2026, 9).Days
            .Single(g => g.WorkDate == Giorno).EarlyEntryAuthorized);
    }

    [FactRichiedeMySql]
    public void Una_giornata_senza_timbrature_non_si_autorizza()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "42");

        Assert.Contains("non ha timbrature", Servizio().SetEarlyEntry(mario, Giorno, true, mario));
        Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_early_entries"));
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

    private HrAttendanceService Servizio()
    {
        IConfiguration config = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(config, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }
}
