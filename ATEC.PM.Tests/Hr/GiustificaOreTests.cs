using ATEC.PM.Server.Services.Hr;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Hr;

/// <summary>
/// Segnalazione #132 — giustificare le ore mancanti dal Calendario mensile, come nel
/// programma «Timbrature» (<c>dgCalendar_MouseDoubleClick</c> + <c>CausaleDialog</c>).
///
/// <para>La regola che conta, e che qui resta ferma, è <b>quali causali sono ammesse</b>:
/// con timbrature vere e parziali si può solo completare la giornata (permesso o
/// infortunio), perché ferie e malattia sono giornate intere e con mezza giornata timbrata
/// non stanno in piedi. Sbagliarla non rompe niente a video: mette delle ferie su una
/// giornata in cui la persona era al lavoro, e lo scopre il consulente a fine mese.</para>
///
/// <para>Le altre due porte dell'originale — solo giornate già passate, mai i giorni non
/// lavorativi — e le due che l'originale non aveva bisogno di avere: un'assenza che arriva
/// da Ecos non si tocca (là è il padrone del dato), e una richiesta a più giorni non si
/// spezza da qui.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class GiustificaOreTests
{
    private readonly SchemaCondiviso _schema;

    public GiustificaOreTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    // Febbraio 2026, tutto passato: domenica 1, lunedì 2, giovedì 5, venerdì 6, sabato 7.
    private const int Anno = 2026;
    private const int Mese = 2;

    /// <summary>
    /// Chi registra la causale: nel VB era l'utente di Windows, qui l'id di chi è dentro.
    /// 🪤 Deve essere un dipendente VERO: <c>hr_absences.created_by</c> e
    /// <c>approved_by</c> hanno la chiave esterna su <c>employees</c>.
    /// </summary>
    private int _autore;

    private int Autore(MySqlConnection c)
    {
        if (_autore == 0)
        {
            c.Execute("INSERT INTO employees (first_name, last_name) VALUES ('Anna', 'Ufficio')");
            _autore = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        }
        return _autore;
    }

    [FactRichiedeMySql]
    public void Giornata_vuota_ammette_tutte_le_causali_e_l_intero_buco()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);

        HrGiustificaInfoDto info = Servizio().GetGiustificaInfo(mario, Data(5));

        Assert.Equal("", info.Blocco);
        Assert.Equal(new[] { "FE", "PE", "MA", "IN" }, info.Causali.ToArray());
        Assert.Equal(0m, info.OreLavorate);
        Assert.Equal(8m, info.OreMancanti);
        Assert.Equal("", info.CausaleCorrente);
        Assert.False(info.PuoRimuovere);
    }

    /// <summary>
    /// Il cuore della segnalazione: mezza giornata timbrata si completa, non si trasforma
    /// in ferie. Nell'originale erano le due liste <c>{"", "PE", "IN"}</c> e
    /// <c>{"", "FE", "PE", "MA", "IN"}</c>.
    /// </summary>
    [FactRichiedeMySql]
    public void Con_timbrature_parziali_solo_permesso_o_infortunio()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        Lavorata(c, mario, giorno: 5, ordinari: 240); // 4h su 8

        HrAttendanceService srv = Servizio();
        HrGiustificaInfoDto info = srv.GetGiustificaInfo(mario, Data(5));

        Assert.Equal(new[] { "PE", "IN" }, info.Causali.ToArray());
        Assert.Equal(4m, info.OreLavorate);
        Assert.Equal(4m, info.OreMancanti);

        // Le ferie su una giornata lavorata a metà vengono respinte, non «aggiustate».
        Assert.Contains("PE (permesso)", srv.SaveGiustifica(Richiesta(mario, 5, "FE"), Autore(c)));
        Assert.Equal(0, Assenze(c, mario));

        Assert.Null(srv.SaveGiustifica(Richiesta(mario, 5, "PE"), Autore(c)));
        Assert.Equal(("PERMIT", 4m, "MANUAL", "APPROVED"), Assenza(c, mario, 5));
    }

    /// <summary>Lo straordinario conta come lavorato: se la giornata è piena non manca niente.</summary>
    [FactRichiedeMySql]
    public void Giornata_piena_non_ha_niente_da_giustificare()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        Lavorata(c, mario, giorno: 5, ordinari: 420, straordinari: 60); // 7h + 1h

        HrGiustificaInfoDto info = Servizio().GetGiustificaInfo(mario, Data(5));

        Assert.Equal(8m, info.OreLavorate);
        Assert.Equal(0m, info.OreMancanti);
        Assert.Equal("Nessuna ora da giustificare per questo giorno.", info.Blocco);
    }

    [FactRichiedeMySql]
    public void Solo_giornate_passate_e_lavorative()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        HrAttendanceService srv = Servizio();

        // Oggi non è ancora finito: nell'originale il controllo è `>= DateTime.Today`.
        Assert.Equal("Si giustificano solo le giornate già passate.",
            srv.GetGiustificaInfo(mario, DateTime.Today).Blocco);

        // Domenica 1 e sabato 7 di febbraio 2026.
        Assert.Contains("non lavorativa", srv.GetGiustificaInfo(mario, Data(1)).Blocco);
        Assert.Contains("non lavorativa", srv.GetGiustificaInfo(mario, Data(7)).Blocco);

        // E il salvataggio rifà le stesse porte, non si fida di chi chiama.
        Assert.Contains("non lavorativa", srv.SaveGiustifica(Richiesta(mario, 1, "FE"), Autore(c)));
        Assert.Equal(0, Assenze(c, mario));
    }

    /// <summary>🪤 Ecos è il padrone del suo dato: da qui si guarda e basta.</summary>
    [FactRichiedeMySql]
    public void L_assenza_che_arriva_da_ecos_non_si_tocca()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        AssenzaEsistente(c, mario, dal: 5, al: 5, tipo: "SICKNESS", sorgente: "ECOS");

        HrAttendanceService srv = Servizio();
        HrGiustificaInfoDto info = srv.GetGiustificaInfo(mario, Data(5));

        Assert.Contains("Ecos", info.Blocco);
        Assert.Equal("MA", info.CausaleCorrente);
        Assert.False(info.PuoRimuovere);

        Assert.Contains("Ecos", srv.SaveGiustifica(Richiesta(mario, 5, ""), Autore(c)));
        Assert.Equal(1, Assenze(c, mario)); // è ancora lì
    }

    /// <summary>
    /// 🪤 Nel VB le giustificazioni erano una tabella a parte; qui condividono
    /// <c>hr_absences</c> con le richieste ferie. Una richiesta dal 3 al 6 non si spezza
    /// cliccando sul 5: resterebbe approvata una cosa diversa da quella approvata.
    /// </summary>
    [FactRichiedeMySql]
    public void Una_richiesta_a_piu_giorni_non_si_spezza_da_qui()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        AssenzaEsistente(c, mario, dal: 3, al: 6, tipo: "VACATION", sorgente: "ATEC");

        HrAttendanceService srv = Servizio();
        Assert.Contains("Richieste", srv.GetGiustificaInfo(mario, Data(5)).Blocco);
        Assert.Contains("Richieste", srv.SaveGiustifica(Richiesta(mario, 5, "PE"), Autore(c)));
        Assert.Equal(1, Assenze(c, mario));
    }

    /// <summary>
    /// Salvare due volte la stessa giornata riscrive la riga, non ne aggiunge una seconda:
    /// il calendario ne mostrerebbe una sola e l'altra resterebbe invisibile a sballare i totali.
    /// </summary>
    [FactRichiedeMySql]
    public void Risalvare_riscrive_la_riga_e_la_rimozione_la_toglie()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        HrAttendanceService srv = Servizio();

        Assert.Null(srv.SaveGiustifica(Richiesta(mario, 5, "FE"), Autore(c)));
        Assert.Null(srv.SaveGiustifica(Richiesta(mario, 5, "MA"), Autore(c)));

        Assert.Equal(1, Assenze(c, mario));
        Assert.Equal(("SICKNESS", 8m, "MANUAL", "APPROVED"), Assenza(c, mario, 5));

        HrGiustificaInfoDto info = srv.GetGiustificaInfo(mario, Data(5));
        Assert.Equal("MA", info.CausaleCorrente);
        Assert.True(info.PuoRimuovere);

        Assert.Null(srv.SaveGiustifica(Richiesta(mario, 5, ""), Autore(c)));
        Assert.Equal(0, Assenze(c, mario));

        // E senza niente da togliere la rimozione lo dice, invece di riuscire a vuoto.
        Assert.Contains("nessuna causale da togliere", srv.SaveGiustifica(Richiesta(mario, 5, ""), Autore(c)));
    }

    /// <summary>
    /// La griglia si aggiorna da sola: la causale appena scritta colora FERIE e spegne il
    /// «?» rosso della riga PRESENZA. È il punto 4 della segnalazione, misurato sul
    /// calendario vero invece che a occhio.
    /// </summary>
    [FactRichiedeMySql]
    public void Dopo_il_salvataggio_il_calendario_non_segna_piu_il_buco()
    {
        using MySqlConnection c = _schema.Apri();
        int mario = Dipendente(c);
        HrAttendanceService srv = Servizio();

        HrCalendarRowDto presenzaPrima = Riga(srv.GetMonthlyCalendar(Anno, Mese, null), "PRESENZA");
        Assert.Equal("?", presenzaPrima.Days[5].Text);
        Assert.Equal("RED", presenzaPrima.Days[5].Color);

        Assert.Null(srv.SaveGiustifica(Richiesta(mario, 5, "FE"), Autore(c)));

        HrMonthlyCalendarDto dopo = srv.GetMonthlyCalendar(Anno, Mese, null);
        HrCalendarRowDto presenza = Riga(dopo, "PRESENZA");
        HrCalendarRowDto ferie = Riga(dopo, "FERIE");

        Assert.Equal("", presenza.Days[5].Text);
        Assert.Equal("BLUE", presenza.Days[5].Color);
        Assert.Equal("8", ferie.Days[5].Text);
        Assert.Equal("8h", ferie.Total);
    }

    // ── Attrezzi ──────────────────────────────────────────────────────────────

    private HrAttendanceService Servizio()
    {
        IConfiguration configVuota = new ConfigurationBuilder().Build();
        var ecos = new EcosClient(configVuota, NullLogger<EcosClient>.Instance);
        return new HrAttendanceService(_schema.Servizio(), ecos, NullLogger<HrAttendanceService>.Instance);
    }

    private static DateTime Data(int giorno) => new(Anno, Mese, giorno);

    private static HrGiustificaRequest Richiesta(int employeeId, int giorno, string causale) =>
        new() { EmployeeId = employeeId, Date = Data(giorno), Causale = causale };

    private static HrCalendarRowDto Riga(HrMonthlyCalendarDto cal, string voceType) =>
        cal.Rows.First(r => r.VoceType == voceType);

    private static int Dipendente(MySqlConnection c)
    {
        c.Execute("INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static void Lavorata(
        MySqlConnection c, int employeeId, int giorno, int ordinari, int straordinari = 0)
    {
        c.Execute(@"
            INSERT INTO hr_days (employee_id, work_date, clock_in_1, clock_out_1,
                                 regular_minutes, overtime_minutes, break_minutes, note, has_anomaly)
            VALUES (@Id, @Giorno, '08:00', '12:00', @Ordinari, @Straordinari, 0, 'OK', 0)",
            new { Id = employeeId, Giorno = Data(giorno), Ordinari = ordinari, Straordinari = straordinari });
    }

    private static void AssenzaEsistente(
        MySqlConnection c, int employeeId, int dal, int al, string tipo, string sorgente)
    {
        c.Execute(@"
            INSERT INTO hr_absences (employee_id, date_from, date_to, hours, is_full_day,
                                     absence_type, status, source)
            VALUES (@Id, @Dal, @Al, 8, 1, @Tipo, 'APPROVED', @Sorgente)",
            new { Id = employeeId, Dal = Data(dal), Al = Data(al), Tipo = tipo, Sorgente = sorgente });
    }

    private static int Assenze(MySqlConnection c, int employeeId) =>
        c.ExecuteScalar<int>("SELECT COUNT(*) FROM hr_absences WHERE employee_id = @Id",
            new { Id = employeeId });

    private static (string Tipo, decimal Ore, string Sorgente, string Stato) Assenza(
        MySqlConnection c, int employeeId, int giorno) =>
        c.QuerySingle<(string, decimal, string, string)>(@"
            SELECT absence_type, hours, source, status
            FROM hr_absences WHERE employee_id = @Id AND date_from = @G",
            new { Id = employeeId, G = Data(giorno) });
}
