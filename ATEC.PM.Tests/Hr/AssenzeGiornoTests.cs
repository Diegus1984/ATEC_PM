using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Le assenze di Ecos <b>giorno per giorno</b> (<c>PeopleAbsenceRequestRefineWorkAll</c> →
/// <c>hr_absence_days</c>, 08/09/2026): lo specchio a finestre e la lettura aggregata che
/// calendario, cartellino e giustificazione preferiscono alla nostra espansione
/// dell'intervallo della richiesta.
///
/// <para>Il caso che decide tutto: una richiesta dal 3 al 6 con «ora inizio 16:15» è per
/// Ecos tre quarti d'ora il 3 e giornate intere dopo; spezzandola noi diventava «4 ore al
/// giorno». Le ore del giorno le sa solo Ecos, e qui si verifica che siano quelle a vincere.</para>
/// </summary>
public class RegoleAssenzeGiornoTests
{
    [Theory]
    [InlineData("F", "VACATION")]
    [InlineData("P", "PERMIT")]
    [InlineData("M", "SICKNESS")]
    [InlineData("MA", "SICKNESS")]
    [InlineData("I", "INJURY")]
    [InlineData("IN", "INJURY")]
    [InlineData("XYZ", "OTHER")]
    public void La_causale_di_Ecos_diventa_la_nostra(string codice, string atteso)
    {
        Assert.Equal(atteso, HrAttendanceService.TipoAssenza(codice));
    }

    [Theory]
    [InlineData("ACCEPTED", false, "APPROVED")]
    [InlineData("REQUEST", false, "PENDING")]
    [InlineData("REJECT", false, "REJECTED")]
    [InlineData("REJECTED", false, "REJECTED")]
    [InlineData("ACCEPTED", true, "CANCELLED")]
    public void Lo_stato_di_Ecos_diventa_il_nostro(string codice, bool cancellata, string atteso)
    {
        Assert.Equal(atteso, HrAttendanceService.StatoRichiesta(codice, cancellata));
    }

    [Fact]
    public void La_finestra_dell_import_guarda_indietro_e_avanti()
    {
        var oggi = new DateTime(2026, 9, 8);
        (DateTime dal, DateTime al) = HrAttendanceService.FinestraAssenzeGiorno(oggi);
        Assert.Equal(new DateTime(2026, 7, 10), dal);
        Assert.Equal(new DateTime(2027, 3, 7), al);
    }

    [Theory]
    [InlineData("16:15:00", "17:00:00", 45)]
    [InlineData("08:00:00", "12:30:00", 270)]
    [InlineData("13:30", "17:00", 210)]
    [InlineData("", "17:00:00", null)]
    [InlineData("17:00:00", "16:00:00", null)]
    [InlineData("boh", "17:00:00", null)]
    public void I_minuti_del_tratto_sono_fine_meno_inizio(string inizio, string fine, int? attesi)
    {
        Assert.Equal(attesi, EcosClient.MinutiFra(inizio, fine));
    }
}

[Collection(SchemaCondiviso.Nome)]
public class AssenzeGiornoTests
{
    private readonly SchemaCondiviso _schema;

    public AssenzeGiornoTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // Febbraio 2026: giovedì 5, venerdì 6, lunedì 9, martedì 10.
    private const int Anno = 2026;
    private const int Mese = 2;
    private static readonly DateTime Primo = new(Anno, Mese, 1);
    private static readonly DateTime Ultimo = new(Anno, Mese, 28);

    [FactRichiedeMySql]
    public void Lo_specchio_scrive_i_giorni_e_toglie_quelli_spariti_dalla_finestra()
    {
        using MySqlConnection c = _schema.Apri();
        CreaDipendente(c, "Mario", "Rossi", "42");
        HrAttendanceService servizio = Servizio();

        // Ferie intere il 4 e il 5 (una richiesta, due tratti AL GIORNO: 🪤 il progressivo del
        // tratto si ripete per ogni giorno, come in produzione), tre quarti d'ora di ROL il 6,
        // una riga di una persona non collegata che va saltata, e lo stesso tratto mandato
        // due volte, che non deve fermare niente.
        var giorni = new List<EcosAbsenceDay>
        {
            Tratto("r1", "1", "42", G(4), "08:00:00", "12:30:00", "F", "ACCEPTED"),
            Tratto("r1", "2", "42", G(4), "13:30:00", "17:00:00", "F", "ACCEPTED"),
            Tratto("r1", "1", "42", G(5), "08:00:00", "12:30:00", "F", "ACCEPTED"),
            Tratto("r1", "2", "42", G(5), "13:30:00", "17:00:00", "F", "ACCEPTED"),
            Tratto("r2", "1", "42", G(6), "16:15:00", "17:00:00", "P", "ACCEPTED"),
            Tratto("r2", "1", "42", G(6), "16:15:00", "17:00:00", "P", "ACCEPTED"),
            Tratto("r3", "1", "99", G(6), "08:00:00", "12:30:00", "F", "ACCEPTED"),
        };
        Assert.Equal((5, 0, 0), servizio.SyncAbsenceDays(c, giorni, Primo, Ultimo));
        Assert.Equal(5, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_absence_days"));

        // Il giro dopo: le ferie sono state annullate su Ecos, il ROL è diventato un'ora.
        var dopo = new List<EcosAbsenceDay>
        {
            Tratto("r2", "1", "42", G(6), "16:00:00", "17:00:00", "P", "ACCEPTED"),
        };
        Assert.Equal((0, 1, 4), servizio.SyncAbsenceDays(c, dopo, Primo, Ultimo));
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_absence_days"));
        Assert.Equal(60, c.ExecuteScalar<int>("SELECT minutes FROM hr_absence_days WHERE ecos_absence_id = 'r2'"));

        // 🪤 Scarico vuoto: non è una finestra vuota, è un filtro che non ha risposto. Niente cancellazioni.
        Assert.Equal((0, 0, 0), servizio.SyncAbsenceDays(c, new List<EcosAbsenceDay>(), Primo, Ultimo));
        Assert.Equal(1, c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_absence_days"));
    }

    [FactRichiedeMySql]
    public void Nel_calendario_le_ore_di_Ecos_vincono_sull_intervallo_della_richiesta()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");

        // Giovedì 5: lavorate sette ore e un quarto, e una richiesta di ROL che, letta come
        // intervallo, direbbe «4 ore»; Ecos la spezza in tre quarti d'ora quel giorno.
        GiornataLavorata(c, mario, giorno: 5, ordinari: 435);
        RichiestaMadre(c, mario, G(5), G(5), ore: 4m, tipo: "PERMIT", ecosId: "r2");
        GiornoDiAssenza(c, mario, G(5), "r2", "1", "P", "PERMIT", 45);

        // Venerdì 6: ferie intere, due tratti da Ecos e nessuna timbratura.
        GiornoDiAssenza(c, mario, G(6), "r1", "1", "F", "VACATION", 270);
        GiornoDiAssenza(c, mario, G(6), "r1", "2", "F", "VACATION", 210);

        HrMonthlyCalendarDto cal = Servizio().GetMonthlyCalendar(Anno, Mese, null);
        HrCalendarRowDto permessi = Riga(cal, "PERMESSI");
        HrCalendarRowDto ferie = Riga(cal, "FERIE");
        HrCalendarRowDto presenza = Riga(cal, "PRESENZA");

        // Il 5: tre quarti d'ora (0,75 → «0.8» col formato del calendario), non le 4 della richiesta.
        Assert.Equal("0.8", permessi.Days[5].Text);
        Assert.Equal("P", presenza.Days[5].Text);

        // Il 6: giornata intera di ferie, teal perché viene da Ecos, e nessun «?».
        Assert.Equal("8", ferie.Days[6].Text);
        Assert.Equal("TEAL", ferie.Days[6].Color);
        Assert.NotEqual("?", presenza.Days[6].Text);
    }

    [FactRichiedeMySql]
    public void Nel_cartellino_la_giornata_di_Ecos_porta_le_ore_del_giorno()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        GiornoDiAssenza(c, mario, G(9), "r5", "1", "P", "PERMIT", 45);
        GiornoDiAssenza(c, mario, G(10), "r6", "1", "F", "VACATION", 270);
        GiornoDiAssenza(c, mario, G(10), "r6", "2", "F", "VACATION", 210);
        // Una richiesta in attesa non entra nel cartellino: conta solo l'approvato.
        GiornoDiAssenza(c, mario, G(11), "r7", "1", "F", "VACATION", 480, stato: "PENDING");

        HrMonthlyTimesheetDto cartellino = Servizio().GetMonthlyTimesheet(mario, Anno, Mese);
        HrDayDto lunedi = cartellino.Days.Single(d => d.WorkDate.Day == 9);
        HrDayDto martedi = cartellino.Days.Single(d => d.WorkDate.Day == 10);
        HrDayDto mercoledi = cartellino.Days.Single(d => d.WorkDate.Day == 11);

        Assert.True(lunedi.HasData);
        Assert.StartsWith("PERMIT (0", lunedi.Note);
        Assert.Equal("VACATION", martedi.Note);
        Assert.False(mercoledi.HasData);
    }

    [FactRichiedeMySql]
    public void Una_giornata_coperta_da_Ecos_non_si_giustifica_da_qui()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = CreaDipendente(c, "Mario", "Rossi", "42");
        GiornoDiAssenza(c, mario, G(9), "r5", "1", "P", "PERMIT", 45);

        HrGiustificaInfoDto info = Servizio().GetGiustificaInfo(mario, G(9));

        Assert.Contains("Ecos", info.Blocco);
        Assert.Equal(0.75m, info.OreCorrenti);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private static DateTime G(int giorno) => new(Anno, Mese, giorno);

    private HrAttendanceService Servizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static EcosAbsenceDay Tratto(
        string richiesta, string tratto, string emplCode, DateTime giorno,
        string inizio, string fine, string causale, string stato) =>
        new(richiesta, tratto, emplCode, "Rossi, Mario", giorno, inizio, fine,
            EcosClient.MinutiFra(inizio, fine), causale, causale == "F" ? "Ferie" : "ROL", stato, "REQUEST",
            giorno.AddDays(-1));

    private static HrCalendarRowDto Riga(HrMonthlyCalendarDto cal, string voceType) =>
        cal.Rows.Single(r => r.VoceType == voceType);

    private static int CreaDipendente(MySqlConnection c, string nome, string cognome, string? ecosCode)
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, ecos_empl_code) VALUES (@Nome, @Cognome, @Codice)",
            new { Nome = nome, Cognome = cognome, Codice = ecosCode });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void GiornataLavorata(MySqlConnection c, int employeeId, int giorno, int ordinari) =>
        c.Execute(@"
            INSERT INTO hr_days (employee_id, work_date, clock_in_1, clock_out_1, clock_in_2, clock_out_2,
                                 regular_minutes, overtime_minutes, break_minutes, note, has_anomaly)
            VALUES (@Id, @Giorno, '08:00', '12:30', '13:30', '16:15', @Ordinari, 0, 60, 'OK', 0)",
            new { Id = employeeId, Giorno = G(giorno), Ordinari = ordinari });

    private static void RichiestaMadre(
        MySqlConnection c, int employeeId, DateTime dal, DateTime al, decimal ore, string tipo, string ecosId) =>
        c.Execute(@"
            INSERT INTO hr_absences (employee_id, date_from, date_to, hours, is_full_day, absence_type, status, source, ecos_absence_id)
            VALUES (@Id, @Dal, @Al, @Ore, 0, @Tipo, 'APPROVED', 'ECOS', @Ecos)",
            new { Id = employeeId, Dal = dal, Al = al, Ore = ore, Tipo = tipo, Ecos = ecosId });

    private static void GiornoDiAssenza(
        MySqlConnection c, int employeeId, DateTime giorno, string richiesta, string tratto,
        string codice, string tipo, int minuti, string stato = "APPROVED") =>
        c.Execute(@"
            INSERT INTO hr_absence_days (employee_id, work_date, ecos_absence_id, ecos_refine_id, category_code,
                                         absence_type, status, minutes)
            VALUES (@Id, @Giorno, @Richiesta, @Tratto, @Codice, @Tipo, @Stato, @Minuti)",
            new
            {
                Id = employeeId, Giorno = giorno, Richiesta = richiesta, Tratto = tratto,
                Codice = codice, Tipo = tipo, Stato = stato, Minuti = minuti,
            });
}
