using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Il cartellino mensile di una persona: la pagina che si apre per prima, e quella che in
/// produzione ha risposto <b>500</b> appena qualcuno ha avuto due giornate nel mese.
///
/// <para>🪤 La causa: le due query leggevano <c>work_date</c> senza alias, e Dapper non
/// abbina <c>work_date</c> a <c>WorkDate</c> se non gli si accende
/// <c>MatchNamesWithUnderscores</c> — che qui non è acceso. Ogni riga tornava con la data a
/// <c>DateTime.MinValue</c>, e il <c>ToDictionary</c> per giorno moriva sul secondo record.
/// Un test con UNA sola giornata non lo avrebbe visto: qui ce ne sono tre.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class CartellinoMensileTests
{
    private readonly SchemaCondiviso _schema;

    public CartellinoMensileTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private const int Anno = 2026;
    private const int Mese = 2;

    [FactRichiedeMySql]
    public void Il_cartellino_legge_piu_giornate_dello_stesso_mese()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        Giornata(c, mario, 4, ordinari: 480);
        Giornata(c, mario, 5, ordinari: 480, straordinari: 60);
        Giornata(c, mario, 6, ordinari: 450);

        HrMonthlyTimesheetDto cartellino = Servizio().GetMonthlyTimesheet(mario, Anno, Mese);

        Assert.Equal("Mario Rossi", cartellino.EmployeeName);
        Assert.True(cartellino.EcosLinked);
        Assert.Equal(28, cartellino.Days.Count);

        var conDati = cartellino.Days.Where(g => g.HasData).ToList();
        Assert.Equal(3, conDati.Count);

        // Le giornate vanno al loro posto: se la data non fosse letta, finirebbero tutte
        // sullo stesso giorno (o la lettura salterebbe del tutto).
        Assert.Equal(new[] { 4, 5, 6 }, conDati.Select(g => g.WorkDate.Day).ToArray());

        HrDayDto quinto = conDati.Single(g => g.WorkDate.Day == 5);
        Assert.Equal("08:00", quinto.ClockIn1);
        Assert.Equal("17:00", quinto.ClockOut2);
        Assert.Equal("8h 0m", quinto.RegularHours);
        Assert.Equal("1h 0m", quinto.Overtime);
    }

    [FactRichiedeMySql]
    public void Le_timbrature_grezze_stanno_sulla_loro_giornata()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        Giornata(c, mario, 4, ordinari: 480);
        Giornata(c, mario, 5, ordinari: 480);
        Timbratura(c, mario, 4, "08:02", "IN");
        Timbratura(c, mario, 4, "17:03", "OUT");
        Timbratura(c, mario, 5, "07:58", "IN");

        HrMonthlyTimesheetDto cartellino = Servizio().GetMonthlyTimesheet(mario, Anno, Mese);

        HrDayDto quarto = cartellino.Days.Single(g => g.WorkDate.Day == 4);
        HrDayDto quinto = cartellino.Days.Single(g => g.WorkDate.Day == 5);
        Assert.Equal(2, quarto.Punches.Count);
        Assert.Single(quinto.Punches);

        // I due stadi che precedono il risultato: il grezzo è l'orario com'è arrivato,
        // il normalizzato quello arrotondato allo scatto.
        Assert.Equal("08:02", quarto.Raw.ClockIn1);
        Assert.Equal("17:03", quarto.Raw.ClockOut1);
        // 08:02 sta dentro la tolleranza di 10', quindi resta l'orario canonico 08:00;
        // 17:03 torna indietro allo scatto. È lo scarto che le tre colonne fanno vedere.
        Assert.Equal("08:00", quarto.Normalized.ClockIn1);
        Assert.Equal("17:00", quarto.Normalized.ClockOut1);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private HrAttendanceService Servizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static int CreaDipendente(MySqlConnection c, string nome, string cognome, string? ecosCode)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code) VALUES (@Nome, @Cognome, @Codice)",
            new { Nome = nome, Cognome = cognome, Codice = ecosCode });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void Giornata(
        MySqlConnection c, int employeeId, int giorno, int ordinari, int straordinari = 0)
    {
        c.Execute(@"
            INSERT INTO hr_days (employee_id, work_date, clock_in_1, clock_out_1, clock_in_2, clock_out_2,
                                 regular_minutes, overtime_minutes, break_minutes, note, has_anomaly)
            VALUES (@Id, @Giorno, '08:00', '12:30', '13:30', '17:00', @Ordinari, @Straordinari, 60, 'OK', 0)",
            new
            {
                Id = employeeId,
                Giorno = new DateTime(Anno, Mese, giorno),
                Ordinari = ordinari,
                Straordinari = straordinari,
            });
    }

    private static void Timbratura(MySqlConnection c, int employeeId, int giorno, string ora, string verso)
    {
        var data = new DateTime(Anno, Mese, giorno);
        DateTime istante = data.Add(TimeSpan.Parse(ora));
        c.Execute(@"
            INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, external_id)
            VALUES (@Id, @Data, @Istante, @Verso, 'ECOS', @Esterno)",
            new
            {
                Id = employeeId,
                Data = data,
                Istante = istante,
                Verso = verso,
                Esterno = $"{employeeId}-{giorno}-{ora}",
            });
    }
}
