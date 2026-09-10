using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// La regola dei giorni del «Controllo di ieri» (09/09/2026): il blocco finisce nel giorno scelto
/// e torna indietro sui riposi fino all'ultimo giorno lavorativo. È la risposta alla domanda di
/// Diego «come gestiamo le timbrature fatte venerdì, sabato o domenica, il lunedì?»: di lunedì si
/// vedono tutti e tre. Se la regola sbaglia, sbaglia in silenzio — un venerdì mai controllato, o un
/// sabato lavorato che nessuno vede — quindi si prova giorno per giorno sul calendario del 2026.
/// </summary>
public class ControlloGiornalieroRegolaTests
{
    // 2026: il 9 settembre è mercoledì, Pasqua è il 5 aprile, il 2 giugno è martedì.
    private static readonly DateTime Mercoledi9Set = new(2026, 9, 9);

    [Theory]
    // Un giorno lavorativo si controlla da solo.
    [InlineData("2026-09-08", "2026-09-08", "2026-09-08")] // martedì
    [InlineData("2026-09-07", "2026-09-07", "2026-09-07")] // lunedì
    [InlineData("2026-09-04", "2026-09-04", "2026-09-04")] // venerdì
    // Di lunedì «ieri» è domenica: si torna a venerdì passando dal sabato.
    [InlineData("2026-09-06", "2026-09-04", "2026-09-06")]
    // Un sabato scelto a mano porta con sé il venerdì.
    [InlineData("2026-09-05", "2026-09-04", "2026-09-05")]
    // Festa infrasettimanale: il giorno dopo il 2 giugno si vedono lunedì 1 e martedì 2.
    [InlineData("2026-06-02", "2026-06-01", "2026-06-02")]
    // Lunedì dell'Angelo (6 aprile 2026): da venerdì 3 a lunedì 6.
    [InlineData("2026-04-06", "2026-04-03", "2026-04-06")]
    // Epifania di martedì: lunedì 5 e martedì 6.
    [InlineData("2026-01-06", "2026-01-05", "2026-01-06")]
    // Patrono di Borgaro (22 gennaio, giovedì): mercoledì 21 e giovedì 22.
    [InlineData("2026-01-22", "2026-01-21", "2026-01-22")]
    // Natale di venerdì e Santo Stefano di sabato: da giovedì 24 a sabato 26.
    [InlineData("2026-12-26", "2026-12-24", "2026-12-26")]
    public void Il_blocco_finisce_nel_giorno_scelto_e_comprende_l_ultimo_giorno_lavorativo(
        string scelto, string daAtteso, string aAtteso)
    {
        (DateTime da, DateTime a) = HrControlloGiornaliero.Intervallo(DateTime.Parse(scelto));

        Assert.Equal(DateTime.Parse(daAtteso), da);
        Assert.Equal(DateTime.Parse(aAtteso), a);
    }

    [Fact]
    public void All_apertura_si_controlla_ieri()
    {
        Assert.Equal(new DateTime(2026, 9, 8), HrControlloGiornaliero.Predefinito(Mercoledi9Set));
        // Anche con l'ora dentro: conta il giorno.
        Assert.Equal(new DateTime(2026, 9, 8), HrControlloGiornaliero.Predefinito(Mercoledi9Set.AddHours(9)));
    }

    [Theory]
    [InlineData("2026-09-07", true)]  // lunedì
    [InlineData("2026-09-05", false)] // sabato
    [InlineData("2026-09-06", false)] // domenica
    [InlineData("2026-06-02", false)] // Festa della Repubblica
    [InlineData("2026-04-06", false)] // Lunedì dell'Angelo
    [InlineData("2026-01-22", false)] // patrono
    public void I_giorni_di_riposo_sono_sabato_domenica_e_i_festivi_del_motore(string giorno, bool lavorativo) =>
        Assert.Equal(lavorativo, HrControlloGiornaliero.GiornoLavorativo(DateTime.Parse(giorno)));

    [Fact]
    public void Il_blocco_prima_finisce_il_giorno_che_precede_l_inizio()
    {
        // Da «venerdì 4 – domenica 6» la freccia indietro chiede giovedì 3.
        Assert.Equal(new DateTime(2026, 9, 3), HrControlloGiornaliero.Precedente(new DateTime(2026, 9, 4)));
    }

    [Theory]
    // Da giovedì 3 si passa a «venerdì, sabato e domenica» (fine domenica 6), non a un venerdì nudo.
    [InlineData("2026-09-03", "2026-09-06")]
    // Da domenica 6 si passa a lunedì 7, da lunedì a martedì.
    [InlineData("2026-09-06", "2026-09-07")]
    [InlineData("2026-09-07", "2026-09-08")]
    // Da martedì 8 si arriva a oggi (mercoledì 9): la giornata in corso si può guardare.
    [InlineData("2026-09-08", "2026-09-09")]
    public void Il_blocco_dopo_comincia_il_giorno_seguente_e_si_estende_sui_riposi(string fine, string atteso) =>
        Assert.Equal(DateTime.Parse(atteso), HrControlloGiornaliero.Successivo(DateTime.Parse(fine), Mercoledi9Set));

    [Fact]
    public void Oltre_oggi_non_si_va()
    {
        Assert.Null(HrControlloGiornaliero.Successivo(Mercoledi9Set, Mercoledi9Set));
        Assert.Null(HrControlloGiornaliero.Successivo(Mercoledi9Set.AddDays(3), Mercoledi9Set));

        // Di sabato 5, da giovedì 3 si arriva a sabato 5 (oggi), non a domenica 6.
        DateTime sabato5 = new(2026, 9, 5);
        Assert.Equal(sabato5, HrControlloGiornaliero.Successivo(new DateTime(2026, 9, 3), sabato5));
    }
}

/// <summary>
/// Il «Controllo di ieri» letto dal database: tutti i dipendenti che timbrano, tre giorni per
/// ciascuno quando il giorno scelto è una domenica, con le stesse giornate del cartellino.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class ControlloGiornalieroTests
{
    private readonly SchemaCondiviso _schema;

    public ControlloGiornalieroTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // Febbraio 2026: venerdì 6, sabato 7, domenica 8.
    private static readonly DateTime Venerdi = new(2026, 2, 6);
    private static readonly DateTime Sabato = new(2026, 2, 7);
    private static readonly DateTime Domenica = new(2026, 2, 8);

    [FactRichiedeMySql]
    public void Di_lunedi_si_vedono_venerdi_sabato_e_domenica_di_tutti()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        int anna = CreaDipendente(c, "Anna", "Bianchi", "43");
        int luca = CreaDipendente(c, "Luca", "Neri", ecosCode: null);
        // Chi è a forfait non timbra: fuori dal controllo.
        int carlo = CreaDipendente(c, "Carlo", "Verdi", "44", mustPunch: false);

        Giornata(c, mario, Venerdi, "08:00", "12:30", "13:30", "17:00", ordinari: 480, nota: "OK");
        Giornata(c, mario, Sabato, "08:00", "12:00", null, null, ordinari: 0, nota: "Turno mattutino");
        Timbratura(c, mario, Sabato, "07:58", "IN");
        Timbratura(c, mario, Sabato, "12:02", "OUT");
        // Anna il venerdì non ha timbrato: giornata senza dati, già sollecitata.
        c.Execute(@"INSERT INTO hr_reminders (employee_id, work_date, sent_by, channel)
                    VALUES (@Id, @Giorno, @Id, 'SMTP')", new { Id = anna, Giorno = Venerdi });

        HrDailyCheckDto controllo = Servizio().GetDailyCheck(Domenica);

        Assert.Equal(Domenica, controllo.Date);
        Assert.Equal(Venerdi, controllo.From);
        Assert.Equal(Domenica, controllo.To);
        Assert.Equal(new DateTime(2026, 2, 5), controllo.PreviousDate);
        Assert.Equal(new DateTime(2026, 2, 9), controllo.NextDate);
        Assert.Equal(new[] { true, false, false }, controllo.Days.Select(g => g.IsWorkingDay).ToArray());

        // Tutti quelli che timbrano, per cognome; il forfettario no.
        Assert.Equal(new[] { anna, luca, mario }, controllo.Employees.Select(e => e.EmployeeId).ToArray());
        Assert.DoesNotContain(controllo.Employees, e => e.EmployeeId == carlo);
        Assert.All(controllo.Employees, e => Assert.Equal(3, e.Days.Count));

        HrDailyCheckEmployeeDto rossi = controllo.Employees.Single(e => e.EmployeeId == mario);
        Assert.True(rossi.EcosLinked);
        HrDayDto venerdi = rossi.Days[0];
        Assert.True(venerdi.HasData);
        Assert.Equal("08:00", venerdi.ClockIn1);
        Assert.Equal("17:00", venerdi.ClockOut2);
        Assert.Equal("8h 0m", venerdi.RegularHours);
        HrDayDto sabato = rossi.Days[1];
        Assert.True(sabato.HasData);
        Assert.Equal(2, sabato.Punches.Count);
        Assert.Equal("07:58", sabato.Raw.ClockIn1);
        Assert.False(rossi.Days[2].HasData);
        Assert.True(rossi.Days[2].IsHoliday);

        HrDailyCheckEmployeeDto bianchi = controllo.Employees.Single(e => e.EmployeeId == anna);
        Assert.False(bianchi.Days[0].HasData);
        Assert.NotNull(bianchi.Days[0].LastReminderAt);
        Assert.Null(bianchi.Days[1].LastReminderAt);

        Assert.False(controllo.Employees.Single(e => e.EmployeeId == luca).EcosLinked);
    }

    [FactRichiedeMySql]
    public void Un_giorno_lavorativo_si_controlla_da_solo_e_la_giornata_e_quella_del_cartellino()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        Giornata(c, mario, Venerdi, "08:00", "12:30", "13:30", "17:00", ordinari: 480, nota: "OK", straordinari: 60);
        Timbratura(c, mario, Venerdi, "08:02", "IN");
        Timbratura(c, mario, Venerdi, "17:03", "OUT");

        HrAttendanceService servizio = Servizio();
        HrDailyCheckDto controllo = servizio.GetDailyCheck(Venerdi);
        HrDayDto dalCartellino = servizio.GetMonthlyTimesheet(mario, 2026, 2).Days.Single(g => g.WorkDate == Venerdi);

        Assert.Single(controllo.Days);
        HrDayDto dalControllo = controllo.Employees.Single().Days.Single();

        // Stessa funzione, stesso risultato: orari, ore, stadi, sollecito.
        Assert.Equal(dalCartellino.ClockIn1, dalControllo.ClockIn1);
        Assert.Equal(dalCartellino.Overtime, dalControllo.Overtime);
        Assert.Equal(dalCartellino.Raw.ClockOut1, dalControllo.Raw.ClockOut1);
        Assert.Equal(dalCartellino.Normalized.ClockOut1, dalControllo.Normalized.ClockOut1);
        Assert.Equal(dalCartellino.Punches.Count, dalControllo.Punches.Count);
        Assert.Equal(dalCartellino.CanRemind, dalControllo.CanRemind);
        Assert.Equal("1h 0m", dalControllo.Overtime);
    }

    [FactRichiedeMySql]
    public void Senza_data_si_controlla_ieri()
    {
        using MySqlConnection c = _schema.Apri();
        CreaDipendente(c, "Mario", "Rossi", "42");

        HrDailyCheckDto controllo = Servizio().GetDailyCheck(null);

        Assert.Equal(DateTime.Today.AddDays(-1), controllo.To);
        Assert.Null(HrControlloGiornaliero.Successivo(DateTime.Today, DateTime.Today));
        Assert.True(controllo.From <= controllo.To);
    }

    /// <summary>
    /// Diego, 10/09/2026: «se le ore sono meno delle ore previste le abbiamo giustificate,
    /// vorrei si vedesse in modo da non doverle rigiustificare». Vale anche qui, non solo sul
    /// cartellino: è questa la pagina che HR guarda al mattino. La giornata vera è quella di
    /// Cassano del 09/09: sette ore lavorate e un'ora di permesso.
    /// </summary>
    [FactRichiedeMySql]
    public void La_causale_che_copre_le_ore_mancanti_si_vede_anche_qui()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        Giornata(c, mario, Venerdi, "09:00", "12:30", "13:30", "17:00", ordinari: 420, nota: "OK");
        Timbratura(c, mario, Venerdi, "09:00", "IN");
        Timbratura(c, mario, Venerdi, "17:00", "OUT");
        c.Execute(@"
            INSERT INTO hr_absences (employee_id, date_from, date_to, hours, is_full_day, absence_type, status)
            VALUES (@Id, @Giorno, @Giorno, 1, 0, 'PERMIT', 'APPROVED')",
            new { Id = mario, Giorno = Venerdi });

        HrDayDto giornata = Servizio().GetDailyCheck(Venerdi)
            .Employees.Single(e => e.EmployeeId == mario)
            .Days.Single(g => g.WorkDate == Venerdi);

        // L'ora che manca è coperta, e la copertura viaggia con la giornata: la pagina scrive
        // «Tutto regolare · 1h di permesso» e il pulsante della causale resta acceso.
        Assert.Equal(0, giornata.ShortMinutes);
        Assert.Equal("PERMIT", giornata.JustifiedType);
        Assert.Equal(1m, giornata.JustifiedHours);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private HrAttendanceService Servizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static int CreaDipendente(
        MySqlConnection c, string nome, string cognome, string? ecosCode, bool mustPunch = true)
    {
        c.Execute(
            @"INSERT INTO employees (first_name, last_name, ecos_empl_code, hr_must_punch)
              VALUES (@Nome, @Cognome, @Codice, @MustPunch)",
            new { Nome = nome, Cognome = cognome, Codice = ecosCode, MustPunch = mustPunch });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void Giornata(
        MySqlConnection c, int employeeId, DateTime giorno,
        string? in1, string? out1, string? in2, string? out2,
        int ordinari, string nota, int straordinari = 0)
    {
        c.Execute(@"
            INSERT INTO hr_days (employee_id, work_date, clock_in_1, clock_out_1, clock_in_2, clock_out_2,
                                 regular_minutes, overtime_minutes, break_minutes, note, has_anomaly)
            VALUES (@Id, @Giorno, @In1, @Out1, @In2, @Out2, @Ordinari, @Straordinari, 60, @Nota, 0)",
            new
            {
                Id = employeeId,
                Giorno = giorno,
                In1 = in1,
                Out1 = out1,
                In2 = in2,
                Out2 = out2,
                Ordinari = ordinari,
                Straordinari = straordinari,
                Nota = nota,
            });
    }

    private static void Timbratura(MySqlConnection c, int employeeId, DateTime giorno, string ora, string verso)
    {
        DateTime istante = giorno.Add(TimeSpan.Parse(ora));
        c.Execute(@"
            INSERT INTO hr_punches (employee_id, work_date, punched_at, direction, source, external_id)
            VALUES (@Id, @Data, @Istante, @Verso, 'ECOS', @Esterno)",
            new
            {
                Id = employeeId,
                Data = giorno,
                Istante = istante,
                Verso = verso,
                Esterno = $"{employeeId}-{giorno:yyyyMMdd}-{ora}",
            });
    }
}
