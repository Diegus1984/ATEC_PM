using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// La griglia del calendario mensile, misurata contro le regole del programma
/// «Timbrature» (<c>CalendarPage.xaml.vb</c>, <c>CaricaDatiMensili</c>): una riga per voce,
/// il verde sul lavorato, il grigio su domeniche e sabati, il «?» rosso sul giorno feriale
/// senza niente, le fasce di straordinario che compaiono solo se hanno ore.
///
/// <para>Sono le regole che l'ufficio legge a colpo d'occhio da anni: qui restano ferme
/// anche se domani cambia la pagina che le disegna.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class CalendarioPresenzeTests
{
    private readonly SchemaCondiviso _schema;

    public CalendarioPresenzeTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // Febbraio 2026, tutto passato: domenica 1, lunedì 2, giovedì 5, venerdì 6, sabato 7.
    private const int Anno = 2026;
    private const int Mese = 2;

    [FactRichiedeMySql]
    public void Una_riga_per_voce_nell_ordine_dell_originale()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        GiornataLavorata(c, mario, giorno: 5, ordinari: 480);

        HrMonthlyCalendarDto cal = Servizio().GetMonthlyCalendar(Anno, Mese, null);

        Assert.Equal(28, cal.DaysInMonth);
        Assert.Equal(new[] { "ORE ORDINARIE", "PRESENZA", "FERIE", "PERMESSI", "MALATTIA", "INFORTUNIO" },
            cal.Rows.Select(r => r.Voce).ToArray());

        // Il nome (con la matricola) sta solo sulla prima riga: sotto è la stessa persona.
        Assert.Equal("Mario Rossi\nMatr. 42", cal.Rows[0].Employee);
        Assert.All(cal.Rows.Skip(1), r => Assert.Equal("", r.Employee));
        Assert.All(cal.Rows, r => Assert.Equal("Mario Rossi", r.EmployeeKey));
        Assert.Equal(mario, cal.Rows[0].EmployeeId);

        // Intestazioni: sabato e domenica sono giorni non lavorativi.
        Assert.Equal("D", cal.DayLabels[1]);
        Assert.True(cal.NonWorkingDays[1]);
        Assert.True(cal.NonWorkingDays[7]);
        Assert.False(cal.NonWorkingDays[5]);
    }

    [FactRichiedeMySql]
    public void Giorno_lavorato_verde_domenica_grigia_giorno_vuoto_rosso()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        GiornataLavorata(c, mario, giorno: 5, ordinari: 480);

        HrMonthlyCalendarDto cal = Servizio().GetMonthlyCalendar(Anno, Mese, null);
        HrCalendarRowDto ordinarie = Riga(cal, "ORE_ORDINARIE");
        HrCalendarRowDto presenza = Riga(cal, "PRESENZA");

        // Giovedì 5: otto ore piene, verde, e la presenza segnata «P».
        Assert.Equal("8", ordinarie.Days[5].Text);
        Assert.Equal("GREEN", ordinarie.Days[5].Color);
        Assert.Equal("P", presenza.Days[5].Text);
        Assert.Equal("GREEN", presenza.Days[5].Color);
        Assert.Contains("E1: 08:00", ordinarie.Days[5].Tooltip);

        // Domenica 1 e sabato 7: grigi, senza niente scritto.
        Assert.Equal("GRAY", ordinarie.Days[1].Color);
        Assert.Equal("", ordinarie.Days[1].Text);
        Assert.Equal("GRAY", ordinarie.Days[7].Color);

        // Lunedì 2, feriale e passato, senza timbrature né assenze: è un buco.
        Assert.Equal("?", presenza.Days[2].Text);
        Assert.Equal("RED", presenza.Days[2].Color);
        Assert.Equal("RED", ordinarie.Days[2].Color);
    }

    [FactRichiedeMySql]
    public void Le_fasce_di_straordinario_compaiono_solo_se_hanno_ore()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        GiornataLavorata(c, mario, giorno: 5, ordinari: 480);
        GiornataLavorata(c, mario, giorno: 6, ordinari: 480, straordinari: 90,
            fasce: """{"A":"1h 30m"}""");

        HrMonthlyCalendarDto cal = Servizio().GetMonthlyCalendar(Anno, Mese, null);

        // Delle nove fasce compare solo quella con ore, subito sotto le ore ordinarie.
        string[] voci = cal.Rows.Select(r => r.Voce).ToArray();
        Assert.Equal("ORE ORDINARIE", voci[0]);
        Assert.Equal("STRAORD. 20%", voci[1]);
        Assert.DoesNotContain("STRAORD. FEST. 55%", voci);

        HrCalendarRowDto fasciaA = Riga(cal, "STRAORD_A");
        Assert.Equal("1.5", fasciaA.Days[6].Text);
        Assert.Equal("ORANGE", fasciaA.Days[6].Color);
        Assert.Equal("1.5h", fasciaA.Total);   // punto decimale, come CalcolaTotale nel VB

        // Il totale delle ordinarie somma i giorni, come CalcolaTotale.
        Assert.Equal("16h", Riga(cal, "ORE_ORDINARIE").Total);
    }

    [FactRichiedeMySql]
    public void Ferie_sulla_riga_ferie_e_il_colore_dice_da_dove_arrivano()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        Assenza(c, mario, giorno: 3, tipo: "VACATION", ore: 8, sorgente: "ATEC");
        Assenza(c, mario, giorno: 4, tipo: "SICKNESS", ore: 8, sorgente: "ECOS");

        HrMonthlyCalendarDto cal = Servizio().GetMonthlyCalendar(Anno, Mese, null);

        HrCalendarRowDto ferie = Riga(cal, "FERIE");
        Assert.Equal("8", ferie.Days[3].Text);
        Assert.Equal("BLUE", ferie.Days[3].Color);
        Assert.Equal("8h", ferie.Total);

        // Già approvata su Ecos: colore diverso, perché è un dato che non possiamo cambiare noi.
        HrCalendarRowDto malattia = Riga(cal, "MALATTIA");
        Assert.Equal("TEAL", malattia.Days[4].Color);

        // Sulla riga presenza l'assenza si vede come colore, non come «?»: non è un buco.
        HrCalendarRowDto presenza = Riga(cal, "PRESENZA");
        Assert.Equal("BLUE", presenza.Days[3].Color);
        Assert.Equal("", presenza.Days[3].Text);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private HrAttendanceService Servizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static HrCalendarRowDto Riga(HrMonthlyCalendarDto cal, string voceType) =>
        cal.Rows.Single(r => r.VoceType == voceType);

    private static int CreaDipendente(MySqlConnection c, string nome, string cognome, string? ecosCode)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code) VALUES (@Nome, @Cognome, @Codice)",
            new { Nome = nome, Cognome = cognome, Codice = ecosCode });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void GiornataLavorata(
        MySqlConnection c, int employeeId, int giorno, int ordinari,
        int straordinari = 0, string? fasce = null)
    {
        c.Execute(@"
            INSERT INTO hr_days (employee_id, work_date, clock_in_1, clock_out_1, clock_in_2, clock_out_2,
                                 regular_minutes, overtime_minutes, break_minutes, bands_json, note, has_anomaly)
            VALUES (@Id, @Giorno, '08:00', '12:30', '13:30', '17:00',
                    @Ordinari, @Straordinari, 60, @Fasce, 'OK', 0)",
            new
            {
                Id = employeeId,
                Giorno = new DateTime(Anno, Mese, giorno),
                Ordinari = ordinari,
                Straordinari = straordinari,
                Fasce = fasce,
            });
    }

    private static void Assenza(
        MySqlConnection c, int employeeId, int giorno, string tipo, decimal ore, string sorgente)
    {
        var data = new DateTime(Anno, Mese, giorno);
        c.Execute(@"
            INSERT INTO hr_absences (employee_id, date_from, date_to, hours, is_full_day,
                                     absence_type, status, source)
            VALUES (@Id, @Data, @Data, @Ore, 1, @Tipo, 'APPROVED', @Sorgente)",
            new { Id = employeeId, Data = data, Ore = ore, Tipo = tipo, Sorgente = sorgente });
    }
}
