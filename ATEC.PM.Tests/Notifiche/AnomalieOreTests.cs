using ATEC.PM.Server.Migrations;
using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Notifiche;

/// <summary>
/// BUG-014 — l'avviso «Ore anomale» e la sua unica regola: <b>una notifica per persona e per
/// giornata lavorata</b>, né una in più né una in meno.
///
/// <para>Sono due errori opposti e tutti e due invisibili da qualsiasi schermata:</para>
/// <list type="bullet">
/// <item><b>una in più</b> — il dedup cercava la notifica nella giornata di <c>work_date</c>
/// invece che per giornata segnalata. Chi compila il timesheet il giorno dopo (cioè quasi
/// tutti) faceva nascere una copia a ogni giro del controllo, ogni 6 ore.</item>
/// <item><b>una in meno</b> — il riferimento è la persona, quindi due giornate anomale della
/// stessa persona erano indistinguibili: la pulizia dei promemoria superati ne teneva una sola
/// e l'altra spariva senza che nessuno l'avesse letta.</item>
/// </list>
///
/// <para>Il tempo qui è quello vero: il controllo guarda <c>CURDATE()</c> e i due giorni
/// precedenti, e le date dei test sono relative a oggi apposta — è proprio lo scarto fra il
/// giorno lavorato e il giorno in cui la notifica nasce a far comparire il difetto.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class AnomalieOreTests
{

    private readonly SchemaCondiviso _schema;

    /// <summary>
    /// xUnit costruisce una istanza per ogni test: qui si riporta il database condiviso a
    /// com'era appena creato (~45 ms), invece di costruirne uno nuovo (~5 s).
    /// </summary>
    public AnomalieOreTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }
    // ── 1. Una sola notifica, per quante volte giri il controllo ──────────────

    /// <summary>
    /// Il caso normale della segnalazione: le ore del giorno prima, inserite stamattina. La
    /// notifica nasce oggi — cioè fuori dalla giornata lavorata — e il giro successivo la deve
    /// riconoscere come già data.
    /// </summary>
    [FactRichiedeMySql]
    public void LeOreDiIeri_generanoUnaNotificaSola_ancheDopoPiuGiri()
    {
        using MySqlConnection c = _schema.Apri();

        Scenario s = Semina(c);
        Ore(c, s, giorniFa: 1, ore: 12.5m);

        NotificationService notifiche = Servizio(_schema);

        Assert.Equal(1, notifiche.SegnalaOreGiornaliereAnomale());
        Assert.Equal(0, notifiche.SegnalaOreGiornaliereAnomale());
        Assert.Equal(0, notifiche.SegnalaOreGiornaliereAnomale());

        Assert.Equal(1, Anomalie(c));
        Assert.Equal(Giorno(-1), GiornataSegnalata(c, s.Dipendente));
    }

    /// <summary>
    /// Ore aggiunte sulla stessa giornata già segnalata: il totale cresce, ma il fatto è
    /// sempre quello. Nessun secondo avviso (il testo resta quello del primo — aggiornarlo
    /// sarebbe un'altra funzione, non un dedup).
    /// </summary>
    [FactRichiedeMySql]
    public void OreCheSalgonoAncora_nonFannoNascereUnSecondoAvviso()
    {
        using MySqlConnection c = _schema.Apri();

        Scenario s = Semina(c);
        Ore(c, s, giorniFa: 1, ore: 11m);

        NotificationService notifiche = Servizio(_schema);
        Assert.Equal(1, notifiche.SegnalaOreGiornaliereAnomale());

        Ore(c, s, giorniFa: 1, ore: 3m);
        Assert.Equal(0, notifiche.SegnalaOreGiornaliereAnomale());
        Assert.Equal(1, Anomalie(c));
    }

    // ── 2. Due giornate anomale restano due notifiche ─────────────────────────

    /// <summary>
    /// Due giorni storti di fila della stessa persona sono due fatti distinti. È il caso che si
    /// perderebbe deduplicando su «l'ho già segnalata oggi», ed è anche quello che la pulizia
    /// dei promemoria superati cancellava per conto suo, prima della v93.
    /// </summary>
    [FactRichiedeMySql]
    public void DueGiornateAnomale_dellaStessaPersona_sonoDueNotificheDistinte()
    {
        using MySqlConnection c = _schema.Apri();

        Scenario s = Semina(c);
        Ore(c, s, giorniFa: 2, ore: 11m);
        Ore(c, s, giorniFa: 1, ore: 13m);

        NotificationService notifiche = Servizio(_schema);
        Assert.Equal(2, notifiche.SegnalaOreGiornaliereAnomale());

        List<DateTime> giornate = c.Query<DateTime>(@"
            SELECT reference_date FROM notifications
            WHERE notification_type = 'TIMESHEET_ANOMALY' AND reference_type = 'EMPLOYEE'
            ORDER BY reference_date").ToList();

        Assert.Equal(new[] { Giorno(-2), Giorno(-1) }, giornate);

        // …e la pulizia non le fonde: è la riga che il destinatario vede nella campanella.
        notifiche.CleanResolvedNotifications();

        Assert.Equal(2, c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            WHERE n.notification_type = 'TIMESHEET_ANOMALY' AND n.reference_type = 'EMPLOYEE'
              AND nr.employee_id = @Pm", new { Pm = s.Pm }));
    }

    /// <summary>
    /// La pulizia deve continuare a fare il suo mestiere su tutto il resto: due promemoria
    /// della stessa scadenza (dove <c>reference_date</c> è NULL) restano uno solo, il più
    /// recente. È il caso che si romperebbe scrivendo <c>=</c> al posto di <c>&lt;=&gt;</c>.
    /// </summary>
    [FactRichiedeMySql]
    public void SenzaGiornata_laPuliziaTieneAncoraSoloIlPromemoriaPiuRecente()
    {
        using MySqlConnection c = _schema.Apri();

        Scenario s = Semina(c);
        NotificationService notifiche = Servizio(_schema);

        notifiche.Create("PROJECT_DUE", "WARNING", "Commessa in scadenza", "scade tra 2 g",
            "PROJECT", s.Commessa, s.Commessa, null, new[] { s.Pm });
        notifiche.Create("PROJECT_DUE", "WARNING", "Commessa in scadenza", "scade tra 1 g",
            "PROJECT", s.Commessa, s.Commessa, null, new[] { s.Pm });

        notifiche.CleanResolvedNotifications();

        Assert.Equal("scade tra 1 g", c.ExecuteScalar<string>(@"
            SELECT n.message FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            WHERE n.notification_type = 'PROJECT_DUE' AND nr.employee_id = @Pm",
            new { Pm = s.Pm }));
    }

    // ── 3. Il pregresso: le copie già arrivate alla campanella ────────────────

    /// <summary>
    /// In produzione le copie ci sono già, e la loro giornata è scritta solo nel testo del
    /// messaggio. La v93 la rimette al suo posto e tiene una riga per giornata — senza fondere
    /// giornate diverse, che è la parte facile da sbagliare.
    /// </summary>
    [FactRichiedeMySql]
    public void LaMigrazione_riportaLaGiornataDalTesto_eTogliLeCopie()
    {
        using MySqlConnection c = _schema.Apri();

        Scenario s = Semina(c);

        // Tre copie della stessa giornata (come le creava il giro ogni 6 ore) + una giornata
        // diversa, che non deve sparire.
        int a1 = NotificaVecchia(c, s, "12,5h registrate il 13/08/2026");
        int a2 = NotificaVecchia(c, s, "12,5h registrate il 13/08/2026");
        int a3 = NotificaVecchia(c, s, "13,0h registrate il 13/08/2026");
        int altra = NotificaVecchia(c, s, "11,0h registrate il 14/08/2026");

        new M093_AnomalieOrePerGiorno().Applica(c, NullLogger.Instance);

        Assert.Equal(new DateTime(2026, 8, 13), c.ExecuteScalar<DateTime?>(
            "SELECT reference_date FROM notifications WHERE id = @Id", new { Id = a3 }));

        List<int> visibili = c.Query<int>(@"
            SELECT n.id FROM notification_recipients nr
            JOIN notifications n ON n.id = nr.notification_id
            WHERE nr.employee_id = @Pm ORDER BY n.id", new { Pm = s.Pm }).ToList();

        Assert.Equal(new[] { a3, altra }, visibili);
        Assert.DoesNotContain(a1, visibili);
        Assert.DoesNotContain(a2, visibili);

        // Le due rimaste senza destinatario non le vede più nessuno: eliminate.
        Assert.Equal(0, c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM notifications WHERE id IN (@A1, @A2)", new { A1 = a1, A2 = a2 }));
    }

    /// <summary>
    /// Un messaggio di forma diversa (o di un altro tipo di avviso) non deve essere interpretato
    /// a forza: meglio la giornata a NULL — comportamento di prima — che una data inventata.
    /// </summary>
    [FactRichiedeMySql]
    public void LaMigrazione_nonInventaLaGiornata_quandoIlTestoNonLaContiene()
    {
        using MySqlConnection c = _schema.Apri();

        Scenario s = Semina(c);
        int strana = NotificaVecchia(c, s, "ore fuori norma, vedi timesheet");

        new M093_AnomalieOrePerGiorno().Applica(c, NullLogger.Instance);

        Assert.Null(c.ExecuteScalar<DateTime?>(
            "SELECT reference_date FROM notifications WHERE id = @Id", new { Id = strana }));
    }

    // ── aiuti ─────────────────────────────────────────────────────────────────

    private sealed record Scenario(int Dipendente, int Pm, int Commessa, int Fase);

    private static NotificationService Servizio(SchemaCondiviso schema) =>
        new(schema.Servizio(), new AnagraficheCache(NullLogger<AnagraficheCache>.Instance));

    /// <summary>Un dipendente che lavora, un PM che riceve l'avviso, una commessa attiva con una fase.</summary>
    private static Scenario Semina(MySqlConnection c)
    {
        int cliente = Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int pm = Inserisci(c,
            "INSERT INTO employees (first_name, last_name, user_role) VALUES ('Paola', 'Bianchi', 'PM')");
        int dipendente = Inserisci(c,
            "INSERT INTO employees (first_name, last_name, user_role) VALUES ('Mario', 'Rossi', 'TECH')");
        // La data di consegna serve davvero: senza, la pulizia considera «risolto» qualunque
        // promemoria di scadenza della commessa e lo toglie di mezzo (punto 4).
        int commessa = Inserisci(c, @"
            INSERT INTO projects (code, title, customer_id, pm_id, status, end_date_planned)
            VALUES ('C20260816.001', 'Commessa di prova', @Cliente, @Pm, 'ACTIVE',
                    DATE_ADD(CURDATE(), INTERVAL 10 DAY))",
            new { Cliente = cliente, Pm = pm });
        int fase = Inserisci(c, @"
            INSERT INTO project_phases (project_id, custom_name, budget_hours)
            VALUES (@P, 'Montaggio', 100)", new { P = commessa });

        return new Scenario(dipendente, pm, commessa, fase);
    }

    private static void Ore(MySqlConnection c, Scenario s, int giorniFa, decimal ore) =>
        c.Execute(@"
            INSERT INTO timesheet_entries (employee_id, project_phase_id, work_date, hours)
            VALUES (@E, @F, DATE_SUB(CURDATE(), INTERVAL @G DAY), @H)",
            new { E = s.Dipendente, F = s.Fase, G = giorniFa, H = ore });

    /// <summary>Una notifica com'era prima della v93: senza <c>reference_date</c>, con la data nel testo.</summary>
    private static int NotificaVecchia(MySqlConnection c, Scenario s, string messaggio)
    {
        int id = Inserisci(c, @"
            INSERT INTO notifications
                (notification_type, severity, title, message, reference_type, reference_id)
            VALUES ('TIMESHEET_ANOMALY', 'WARNING', 'Ore anomale — Mario Rossi', @M, 'EMPLOYEE', @E)",
            new { M = messaggio, E = s.Dipendente });

        c.Execute("INSERT INTO notification_recipients (notification_id, employee_id) VALUES (@N, @P)",
            new { N = id, P = s.Pm });
        return id;
    }

    private static int Anomalie(MySqlConnection c) => c.ExecuteScalar<int>(@"
        SELECT COUNT(*) FROM notifications
        WHERE notification_type = 'TIMESHEET_ANOMALY' AND reference_type = 'EMPLOYEE'");

    private static DateTime GiornataSegnalata(MySqlConnection c, int dipendente) =>
        c.ExecuteScalar<DateTime>(@"
            SELECT reference_date FROM notifications
            WHERE notification_type = 'TIMESHEET_ANOMALY' AND reference_type = 'EMPLOYEE'
              AND reference_id = @E", new { E = dipendente });

    private static DateTime Giorno(int scarto) => DateTime.Today.AddDays(scarto);

    private static int Inserisci(MySqlConnection c, string sql, object? par = null)
    {
        c.Execute(sql, par);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }
}
