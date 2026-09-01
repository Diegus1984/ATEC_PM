using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Il turno di notte dal database in su: l'import che lo taglia a mezzanotte, la rettifica
/// che lo chiude a mano, e le due pagine che poi lo mostrano (cartellino mensile e
/// calendario).
///
/// <para>Il motore da solo non basta a garantire niente di tutto questo: la notte la
/// riconosce solo se qualcuno gli passa le timbrature dei giorni confinanti, e il giorno di
/// ieri va ricalcolato quando arriva la timbratura di oggi. Sono proprietà del
/// <b>servizio</b>, e si provano solo con le tabelle vere.</para>
///
/// <para>La giornata di prova è la notte vera di Monge: mercoledì 19 agosto 2026 entra alle
/// 19:46 ed esce alle 06:03 del 20.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class TurnoNotturnoServizioTests
{
    private readonly SchemaCondiviso _schema;

    public TurnoNotturnoServizioTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private const int Anno = 2026;
    private const int Mese = 8;
    private static readonly DateTime Mercoledi = new(Anno, Mese, 19);
    private static readonly DateTime Giovedi = new(Anno, Mese, 20);

    [FactRichiedeMySql]
    public void Import_di_una_notte_mette_il_turno_intero_sul_giorno_che_lo_apre()
    {
        using MySqlConnection c = _schema.Apri();
        int monge = CreaDipendente(c, "Matteo", "Monge", "1059");

        Servizio().ImportPunches(c, new List<EcosPunch>
        {
            Ecos("n1", Mercoledi.AddHours(19).AddMinutes(46), "IN"),
            Ecos("n2", Giovedi.AddHours(6).AddMinutes(3), "OUT"),
        });

        // Mercoledì: il turno intero, dieci ore, con la mezzanotte in mezzo.
        (string E1, string U1, int Ord, int Str, bool Anom, string Nota) sera = Cartellino(c, monge, Mercoledi);
        Assert.Equal("20:00", sera.E1);
        Assert.Equal("24:00", sera.U1);
        Assert.Equal(480, sera.Ord);
        Assert.Equal(120, sera.Str);
        Assert.False(sera.Anom);

        // Giovedì: le ore di stanotte le ha contate mercoledì, e la giornata lo dice.
        (string E1, string U1, int Ord, int Str, bool Anom, string Nota) mattino = Cartellino(c, monge, Giovedi);
        Assert.Equal(0, mattino.Ord);
        Assert.Equal(0, mattino.Str);
        Assert.False(mattino.Anom);
        Assert.Contains("contano sul giorno prima", mattino.Nota);

        // 🪤 La nota fa il giro MySQL→DTO: l'emoji è a 4 byte e la colonna deve reggerla
        // (hr_days.note è utf8mb4). Se un giorno tornasse utf8mb3, qui si vedrebbe subito.
        Assert.Contains(NightShift.NoteMarker, mattino.Nota);

        // Dieci ore di turno, dieci ore sul cartellino.
        Assert.Equal(600, sera.Ord + sera.Str + mattino.Ord + mattino.Str);
    }

    [FactRichiedeMySql]
    public void La_notte_resta_anomala_finche_non_arriva_l_uscita()
    {
        using MySqlConnection c = _schema.Apri();
        int monge = CreaDipendente(c, "Matteo", "Monge", "1059");

        // Primo scarico: c'è solo l'entrata serale. Non si inventa niente.
        Servizio().ImportPunches(c, new List<EcosPunch>
        {
            Ecos("n1", Mercoledi.AddHours(19).AddMinutes(46), "IN"),
        });

        (string E1, string U1, int Ord, int Str, bool Anom, string Nota) sera = Cartellino(c, monge, Mercoledi);
        Assert.True(sera.Anom);
        Assert.Contains("INCOMPLETO", sera.Nota);
        Assert.Equal(0, sera.Ord);
    }

    [FactRichiedeMySql]
    public void L_uscita_che_arriva_il_giorno_dopo_rimette_a_posto_la_notte()
    {
        using MySqlConnection c = _schema.Apri();
        int monge = CreaDipendente(c, "Matteo", "Monge", "1059");
        HrAttendanceService servizio = Servizio();

        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Ecos("n1", Mercoledi.AddHours(19).AddMinutes(46), "IN"),
        });
        Assert.True(Cartellino(c, monge, Mercoledi).Anom);

        // Secondo scarico, il giorno dopo: la giornata di IERI va rifatta anche se non
        // l'ha toccata nessuno. È quello che fa SegnaConVicine.
        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Ecos("n1", Mercoledi.AddHours(19).AddMinutes(46), "IN"),
            Ecos("n2", Giovedi.AddHours(6).AddMinutes(3), "OUT"),
        });

        (string E1, string U1, int Ord, int Str, bool Anom, string Nota) sera = Cartellino(c, monge, Mercoledi);
        Assert.False(sera.Anom);
        Assert.Equal("24:00", sera.U1);
        Assert.Equal(480, sera.Ord);
        Assert.Equal(120, sera.Str);
    }

    [FactRichiedeMySql]
    public void Una_rettifica_al_mattino_chiude_la_notte_del_giorno_prima()
    {
        using MySqlConnection c = _schema.Apri();
        int monge = CreaDipendente(c, "Matteo", "Monge", "1059");
        int capo = CreaDipendente(c, "Paolo", "Zanoni", "1");
        HrAttendanceService servizio = Servizio();

        servizio.ImportPunches(c, new List<EcosPunch>
        {
            Ecos("n1", Mercoledi.AddHours(19).AddMinutes(46), "IN"),
        });
        Assert.True(Cartellino(c, monge, Mercoledi).Anom);

        // L'uscita non è mai arrivata da Ecos: la mette a mano l'ufficio (mai la persona
        // sul proprio cartellino).
        string? errore = servizio.AddAdjustment(
            new HrAdjustmentRequest
            {
                EmployeeId = monge,
                PunchedAt = Giovedi.AddHours(6).AddMinutes(3),
                Direction = "OUT",
                Reason = "Uscita dal turno di notte non registrata dal badge",
            },
            autoreId: capo);

        Assert.Null(errore);
        Assert.False(Cartellino(c, monge, Mercoledi).Anom);
        Assert.Equal("24:00", Cartellino(c, monge, Mercoledi).U1);
    }

    [FactRichiedeMySql]
    public void Gli_stadi_del_cartellino_mensile_vedono_la_notte_come_hr_days()
    {
        using MySqlConnection c = _schema.Apri();
        int monge = CreaDipendente(c, "Matteo", "Monge", "1059");
        Servizio().ImportPunches(c, new List<EcosPunch>
        {
            Ecos("n1", Mercoledi.AddHours(19).AddMinutes(46), "IN"),
            Ecos("n2", Giovedi.AddHours(6).AddMinutes(3), "OUT"),
        });

        HrMonthlyTimesheetDto cartellino = Servizio().GetMonthlyTimesheet(monge, Anno, Mese);
        HrDayDto sera = cartellino.Days.Single(g => g.WorkDate.Day == 19);

        // 🪤 I due stadi si ricalcolano al volo: se lì il contesto della notte mancasse,
        // le tre colonne direbbero una cosa e il blocco finale un'altra.
        Assert.Equal("24:00", sera.ClockOut1);
        Assert.Equal("24:00", sera.Normalized.ClockOut1);
        Assert.Equal("24:00", sera.Raw.ClockOut1);
        Assert.Equal("19:46", sera.Raw.ClockIn1);
    }

    [FactRichiedeMySql]
    public void Le_due_meta_della_notte_non_sono_ore_mancanti_sul_calendario()
    {
        using MySqlConnection c = _schema.Apri();
        int monge = CreaDipendente(c, "Matteo", "Monge", "1059");
        Servizio().ImportPunches(c, new List<EcosPunch>
        {
            Ecos("n1", Mercoledi.AddHours(19).AddMinutes(46), "IN"),
            Ecos("n2", Giovedi.AddHours(6).AddMinutes(3), "OUT"),
        });

        HrMonthlyCalendarDto cal = Servizio().GetMonthlyCalendar(Anno, Mese, null);
        HrCalendarRowDto presenza = cal.Rows.Single(r => r.VoceType == "PRESENZA");

        // Il 19 ha il turno intero (dieci ore) e il 20 ha zero ore proprie: nessuna delle
        // due è una giornata mancante, e rosse non ci vanno.
        Assert.Equal("P", presenza.Days[19].Text);
        Assert.Equal("GREEN", presenza.Days[19].Color);
        Assert.Equal("P", presenza.Days[20].Text);
        Assert.Equal("GREEN", presenza.Days[20].Color);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private HrAttendanceService Servizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static EcosPunch Ecos(string id, DateTime orario, string verso) =>
        new(id, orario, "1059", "Monge, Matteo", verso, "Sede");

    private static int CreaDipendente(MySqlConnection c, string nome, string cognome, string? ecosCode)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code) VALUES (@Nome, @Cognome, @Codice)",
            new { Nome = nome, Cognome = cognome, Codice = ecosCode });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static (string E1, string U1, int Ord, int Str, bool Anom, string Nota) Cartellino(
        MySqlConnection c, int employeeId, DateTime giorno) =>
        c.QuerySingle<(string, string, int, int, bool, string)>(
            @"SELECT clock_in_1 AS E1, clock_out_1 AS U1, regular_minutes AS Ord,
                     overtime_minutes AS Str, has_anomaly AS Anom, note AS Nota
              FROM hr_days WHERE employee_id = @Id AND work_date = @Giorno",
            new { Id = employeeId, Giorno = giorno.Date });
}
