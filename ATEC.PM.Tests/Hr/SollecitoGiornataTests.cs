using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Il sollecito della singola giornata (PIANO-HR-PORT-ORIGINALE.md, voci 1, 3 e 6).
///
/// <para>Quello che si difende qui è <b>quali giornate danno il sollecito</b>: è la regola
/// che, se sbaglia, sbaglia in silenzio — o si scrive a una persona per una giornata a
/// posto, o si lascia scoperto un buco vero. La regola sta in un posto solo
/// (<see cref="HrDayReminder.Serve"/>) e la usano sia il pulsante 📧 sia il filtro
/// «📧 Da segnalare»: se divergessero, il filtro mostrerebbe righe senza pulsante.</para>
///
/// <para>🪤 <c>HasAnomaly</c> NON è la regola: è <c>Note.StartsWith("⚠")</c> e prende solo
/// INCOMPLETO ed ERR. L'uscita <i>stimata</i> alle 17:00 non è un'anomalia per il motore ma
/// nell'originale il sollecito ce l'ha, ed è giusto — quella è un'ora indovinata.</para>
/// </summary>
public class SollecitoGiornataRegolaTests
{
    private static readonly DateTime Ieri = new DateTime(2026, 2, 5);
    private static readonly DateTime Oggi = new DateTime(2026, 2, 6);

    [Theory]
    // Le sei parole chiave dell'originale (ReportPage.xaml.vb:208-214).
    [InlineData("⚠ INCOMPLETO: Solo entrata", true)]
    [InlineData("⚠ ERR: Verificare timbrature", true)]
    [InlineData("AUTO_P: Uscita mancante - Stimata 17:00", true)]
    [InlineData("⚠ Permesso parziale 4h (ECOS) ma nessuna timbratura — verificare", true)]
    [InlineData("Permesso rettificato: ECOS 4h → 2h", true)]
    [InlineData("Permesso annullato (ECOS 4h → giornata completa)", true)]
    // Le pause dedotte NON danno il sollecito: sono regole applicate con sicurezza.
    [InlineData("AUTO_P: Pausa 1h forzata", false)]
    [InlineData("AUTO_P: Pausa 1h detratta", false)]
    [InlineData("AUTO_P: Pausa implicita (1 IN / 2 OUT)", false)]
    [InlineData("OK", false)]
    [InlineData("Recupero pausa pranzo", false)]
    [InlineData("Turno mattutino", false)]
    [InlineData("Turno pomeridiano", false)]
    [InlineData("Giornata in corso", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void La_regola_dell_originale_decide_quali_giornate_si_sollecitano(string? nota, bool atteso) =>
        Assert.Equal(atteso, HrDayReminder.Serve(nota, Ieri, Oggi));

    [Fact]
    public void La_giornata_di_oggi_non_si_sollecita_mai()
    {
        // È ancora aperta: «manca l'uscita» sarebbe una segnalazione a vuoto.
        Assert.False(HrDayReminder.Serve("⚠ INCOMPLETO: Solo entrata", Oggi, Oggi));
        Assert.True(HrDayReminder.Serve("⚠ INCOMPLETO: Solo entrata", Ieri, Oggi));
    }

    [Fact]
    public void L_oggetto_non_porta_piu_il_prefisso_eTime()
    {
        string oggetto = HrDayReminder.Oggetto(new DateTime(2026, 2, 5));
        Assert.Equal("Segnalazione timbrature — 05/02/2026", oggetto);
        Assert.DoesNotContain("eTime", oggetto);
    }

    [Fact]
    public void Il_corpo_ha_i_tre_blocchi_dell_originale_e_gli_orari_grezzi()
    {
        var giornata = new HrDayDto
        {
            WorkDate = Ieri,
            HasData = true,
            Note = "AUTO_P: Uscita mancante - Stimata 17:00",
            RegularHours = "8h 0m",
            Overtime = "0h 0m",
            Raw = new HrDayStageDto
            {
                ClockIn1 = "07:58",
                ClockOut1 = "12:34",
                ClockIn2 = "13:20",
                ClockOut2 = "--:--",
            },
        };

        string corpo = HrDayReminder.Corpo("Mario", Ieri, giornata, "Ufficio Personale");

        Assert.StartsWith("Gentile Mario,", corpo);
        Assert.Contains("giovedì 05 febbraio 2026", corpo);
        Assert.Contains("TIMBRATURE REGISTRATE:", corpo);
        // Gli orari sono i GREZZI: la persona deve riconoscere quello che ha fatto.
        Assert.Contains("  Entrata 1:  07:58", corpo);
        Assert.Contains("  Uscita 2:   --:--", corpo);
        Assert.Contains("PROBLEMA RILEVATO:", corpo);
        Assert.Contains("Il sistema ha stimato l'uscita alle 17:00.", corpo);
        Assert.Contains("RISULTATO ELABORAZIONE:", corpo);
        Assert.Contains("  Ore ordinarie:    8h 0m", corpo);
        Assert.Contains("Ufficio Personale", corpo);
        Assert.EndsWith("Ufficio Risorse Umane — ATEC S.r.l.", corpo);
    }

    [Fact]
    public void Il_nome_composto_non_si_taglia_al_primo_spazio()
    {
        // 🪤 «Maria Grazia» tagliata diventerebbe «Maria», e la stessa persona sarebbe
        // salutata in due modi diversi dal sollecito della giornata e da quello mensile.
        var giornata = new HrDayDto { WorkDate = Ieri, Note = "⚠ ERR: Verificare timbrature" };
        Assert.StartsWith(
            "Gentile Maria Grazia,", HrDayReminder.Corpo("Maria Grazia", Ieri, giornata, ""));

        // La regola dell'originale resta: da «Cognome, Nome» si prende quello dopo la virgola.
        Assert.StartsWith(
            "Gentile Mario,", HrDayReminder.Corpo("Rossi, Mario", Ieri, giornata, ""));
    }

    [Fact]
    public void Senza_firma_configurata_resta_la_sola_riga_dell_ufficio()
    {
        // Nell'originale, quando il mittente era un indirizzo email, in fondo si leggeva
        // «Ufficio Risorse Umane» due volte di fila.
        var giornata = new HrDayDto { WorkDate = Ieri, Note = "⚠ ERR: Verificare timbrature" };
        string corpo = HrDayReminder.Corpo("Mario", Ieri, giornata, "");

        Assert.Contains("Cordiali saluti,\nUfficio Risorse Umane — ATEC S.r.l.", corpo);
    }

    [Fact]
    public void Ogni_tipo_di_nota_ha_la_sua_frase()
    {
        Assert.Contains("manca l'uscita", Dettaglio("⚠ INCOMPLETO: Solo entrata"));
        Assert.Contains("impossibile elaborare", Dettaglio("⚠ ERR: Verificare timbrature"));
        Assert.Contains("pausa pranzo di 1h è stata detratta", Dettaglio("AUTO_P: Pausa 1h detratta"));
        Assert.Contains("ha inserito automaticamente la pausa", Dettaglio("AUTO_P: Pausa implicita (1 IN / 2 OUT)"));
        Assert.Contains("forzato 1h di pausa", Dettaglio("AUTO_P: Pausa 1h forzata"));
        Assert.Contains("Nessuna timbratura registrata", Dettaglio(""));
    }

    private static string Dettaglio(string nota) =>
        HrDayReminder.Dettaglio(new HrDayDto { Note = nota, RegularHours = "0h 0m", Overtime = "0h 0m" });
}

/// <summary>
/// Il sollecito della giornata su database vero: il flag arriva col cartellino, la mail si
/// registra con il suo testo e la Cronologia la rilegge.
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class SollecitoGiornataTests
{
    private readonly SchemaCondiviso _schema;

    public SollecitoGiornataTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private const int Anno = 2026;
    private const int Mese = 2;

    [FactRichiedeMySql]
    public void Il_cartellino_porta_gia_deciso_quali_giornate_si_sollecitano()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);

        // Giovedì 5: uscita stimata → si sollecita. Venerdì 6: giornata regolare → no.
        Giornata(c, mario, 5, "AUTO_P: Uscita mancante - Stimata 17:00", anomalia: false);
        Giornata(c, mario, 6, "OK", anomalia: false);
        Giornata(c, mario, 9, "⚠ INCOMPLETO: Solo entrata", anomalia: true);

        HrMonthlyTimesheetDto cartellino = Servizio().GetMonthlyTimesheet(mario, Anno, Mese);

        Assert.True(Giorno(cartellino, 5).CanRemind);
        Assert.False(Giorno(cartellino, 6).CanRemind);
        Assert.True(Giorno(cartellino, 9).CanRemind);

        // 🪤 Il 5 NON è un'anomalia per il motore, ma il sollecito ce l'ha: la regola non è
        // HasAnomaly.
        Assert.False(Giorno(cartellino, 5).HasAnomaly);
    }

    [FactRichiedeMySql]
    public void Il_sollecito_pronto_porta_destinatario_oggetto_e_corpo()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "mario.rossi@atec.srl");
        Giornata(c, mario, 5, "⚠ INCOMPLETO: Solo entrata", anomalia: true);

        HrDayReminderDto sollecito = Servizio().GetDayReminder(mario, Data(5), "Ufficio Personale");

        Assert.True(sollecito.CanRemind);
        Assert.Equal("mario.rossi@atec.srl", sollecito.Email);
        Assert.Equal("Segnalazione timbrature — 05/02/2026", sollecito.Subject);
        Assert.Contains("Gentile Mario,", sollecito.Body);
        Assert.Equal("", sollecito.Blocco);
        Assert.Null(sollecito.LastReminderAt);
    }

    [FactRichiedeMySql]
    public void Senza_email_il_sollecito_e_bloccato_col_messaggio_dell_originale()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, email: null);
        Giornata(c, mario, 5, "⚠ INCOMPLETO: Solo entrata", anomalia: true);

        HrDayReminderDto sollecito = Servizio().GetDayReminder(mario, Data(5), "");

        Assert.True(sollecito.CanRemind);
        Assert.Contains("Nessuna email configurata per", sollecito.Blocco);
    }

    [FactRichiedeMySql]
    public void Una_giornata_a_posto_non_si_sollecita()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        Giornata(c, mario, 5, "OK", anomalia: false);

        HrDayReminderDto sollecito = Servizio().GetDayReminder(mario, Data(5), "");

        Assert.False(sollecito.CanRemind);
        Assert.Contains("non ha anomalie", sollecito.Blocco);
    }

    [FactRichiedeMySql]
    public void Il_sollecito_registrato_torna_col_cartellino_e_nella_cronologia()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "mario.rossi@atec.srl");
        Giornata(c, mario, 5, "⚠ INCOMPLETO: Solo entrata", anomalia: true);

        HrAttendanceService servizio = Servizio();
        servizio.MarkDayReminder(
            mario, Data(5), "mario.rossi@atec.srl",
            "Segnalazione timbrature — 05/02/2026", "Gentile Mario,\n\n…", mario, "SMTP");

        // Il tooltip del pulsante dice QUANDO: il dato viaggia col cartellino.
        HrDayDto giorno = Giorno(servizio.GetMonthlyTimesheet(mario, Anno, Mese), 5);
        Assert.NotNull(giorno.LastReminderAt);

        HrReminderLogDto log = servizio.GetReminderLog(Anno, Mese, null);
        HrReminderLogRowDto riga = Assert.Single(log.Rows);
        Assert.Equal("mario.rossi@atec.srl", riga.Email);
        Assert.Equal("Segnalazione timbrature — 05/02/2026", riga.Subject);
        Assert.Contains("Gentile Mario,", riga.Body);
        Assert.Equal("SMTP", riga.Channel);
        Assert.Equal(Data(5), riga.WorkDate);
        Assert.Equal("Mario Rossi", riga.EmployeeName);
    }

    [FactRichiedeMySql]
    public void Il_secondo_sollecito_sulla_stessa_giornata_aggiorna_invece_di_accodare()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c, "mario.rossi@atec.srl");
        HrAttendanceService servizio = Servizio();

        servizio.MarkDayReminder(mario, Data(5), "a@atec.srl", "Primo", "corpo 1", mario, "MAILTO");
        servizio.MarkDayReminder(mario, Data(5), "b@atec.srl", "Secondo", "corpo 2", mario, "SMTP");

        HrReminderLogRowDto riga = Assert.Single(servizio.GetReminderLog(Anno, Mese, null).Rows);
        Assert.Equal("Secondo", riga.Subject);
        Assert.Equal("SMTP", riga.Channel);
    }

    [FactRichiedeMySql]
    public void Le_righe_scritte_prima_della_M117_restano_senza_testo()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);

        // Com'era prima della M117: solo giornata e canale.
        c.Execute(
            "INSERT INTO hr_reminders (employee_id, work_date, channel) VALUES (@Id, @Giorno, 'SMTP')",
            new { Id = mario, Giorno = Data(5) });

        HrReminderLogRowDto riga = Assert.Single(Servizio().GetReminderLog(Anno, Mese, null).Rows);
        Assert.Null(riga.Body);
        Assert.Null(riga.Subject);
    }

    [FactRichiedeMySql]
    public void La_cronologia_si_filtra_per_mese_del_giorno_e_per_dipendente()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        int luigi = Dipendente(c, email: null, nome: "Luigi", cognome: "Verdi", codice: "43");

        HrAttendanceService servizio = Servizio();
        servizio.MarkDayReminder(mario, Data(5), null, "Feb", "x", mario, "SMTP");
        servizio.MarkDayReminder(luigi, Data(6), null, "Feb", "x", mario, "SMTP");
        // 🪤 Gennaio: il filtro guarda il GIORNO di riferimento, non la spedizione.
        servizio.MarkDayReminder(mario, new DateTime(Anno, 1, 20), null, "Gen", "x", mario, "SMTP");

        Assert.Equal(2, servizio.GetReminderLog(Anno, Mese, null).Rows.Count);
        Assert.Single(servizio.GetReminderLog(Anno, Mese, mario).Rows);
        Assert.Single(servizio.GetReminderLog(Anno, 1, null).Rows);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private static HrDayDto Giorno(HrMonthlyTimesheetDto cartellino, int giorno) =>
        cartellino.Days.Single(g => g.WorkDate.Day == giorno);

    private static DateTime Data(int giorno) => new(Anno, Mese, giorno);

    private HrAttendanceService Servizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static int Dipendente(
        MySqlConnection c, string? email = "mario.rossi@atec.srl",
        string nome = "Mario", string cognome = "Rossi", string codice = "42")
    {
        c.Execute(
            "INSERT INTO employees (first_name, last_name, email, ecos_empl_code) VALUES (@N, @C, @E, @K)",
            new { N = nome, C = cognome, E = email, K = codice });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void Giornata(MySqlConnection c, int employeeId, int giorno, string nota, bool anomalia) =>
        c.Execute(@"
            INSERT INTO hr_days (employee_id, work_date, clock_in_1, clock_out_1,
                                 regular_minutes, overtime_minutes, break_minutes, note, has_anomaly)
            VALUES (@Id, @Giorno, '08:00', '12:00', 480, 0, 60, @Nota, @Anomalia)",
            new { Id = employeeId, Giorno = Data(giorno), Nota = nota, Anomalia = anomalia });
}
