using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Prestazioni;

/// <summary>
/// Blocco E2 — indici mirati e query che li sanno usare.
///
/// <para>Il difetto che questi test proteggono non si vede da nessuna schermata: i numeri restano
/// giusti, l'applicazione funziona, e intanto ogni apertura della home legge l'intera tabella
/// delle ore. Al contrario, una riscrittura sbagliata cambierebbe i totali <b>senza</b> che nulla
/// segnali l'errore — per questo l'equivalenza si prova, non si assume.</para>
/// </summary>
public class IndiciEQueryTests
{
    // ── 1. Le riscritture danno esattamente gli stessi numeri di prima ────────

    // Le due query di PRIMA, ricopiate qui perché non esistono più da nessun'altra parte.
    // #88: le prove sulle somme della home (SqlOreMese/SqlOreSettimana) sono state rimosse
    // INSIEME alle query che proteggevano: le 4 card della vecchia Panoramica non esistono piu.

    /// <summary>
    /// La forma con cui sono stati riscritti i cinque controlli di scadenza:
    /// <c>colonna &lt;= CURDATE() + INTERVAL @Warn DAY</c>. Il parametro sta <b>dentro</b>
    /// l'espressione <c>INTERVAL</c>, che è il punto in cui un driver può non seguire — e
    /// nessuno se ne accorgerebbe guardando il codice, perché è SQL valido a occhio.
    ///
    /// <para>Serve un test perché quelle query vivono in metodi privati di un
    /// <c>BackgroundService</c> che gira ogni 6 ore: se la sintassi non reggesse, il giro
    /// fallirebbe con una riga di log e le notifiche di scadenza smetterebbero di arrivare —
    /// che è esattamente il tipo di guasto che nessuno segnala, perché non si vede.</para>
    /// </summary>
    [FactRichiedeMySql]
    public void IlFiltroDelleScadenze_reggeIlParametroDentroIntervalDay()
    {
        using var db = new DatabaseDiProva("filtro_scadenze");
        db.CreaSchemaCompleto();

        using MySqlConnection c = db.Apri();
        int gruppo = Inserisci(c, "INSERT INTO checklist_groups (name) VALUES ('Gruppo di prova')");

        foreach (int giorni in new[] { -5, 0, 2, 30 })
        {
            c.Execute(@"INSERT INTO checklist_items (group_id, description, due_date, status)
                        VALUES (@G, @T, DATE_ADD(CURDATE(), INTERVAL @D DAY), 'OPEN')",
                new { G = gruppo, T = $"Attività a {giorni} giorni", D = giorni });
        }

        // La stessa forma dei cinque controlli, eseguita dal driver vero con il parametro.
        List<string> entro3 = c.Query<string>(
            "SELECT description FROM checklist_items WHERE due_date <= CURDATE() + INTERVAL @Warn DAY",
            new { Warn = 3 }).ToList();

        // Dentro: scaduta (-5), oggi (0), fra 2 giorni. Fuori: fra 30.
        Assert.Equal(3, entro3.Count);
        Assert.DoesNotContain(entro3, t => t.Contains("30 giorni"));

        // E la forma delle pulizie, che usa BETWEEN sui due estremi.
        int inScadenza = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM checklist_items " +
            "WHERE due_date BETWEEN CURDATE() AND CURDATE() + INTERVAL @Warn DAY",
            new { Warn = 3 });
        Assert.Equal(2, inScadenza);   // oggi e fra 2 giorni; la scaduta resta fuori
    }

    // ── 2. La migrazione lascia gli indici che servono ────────────────────────

    /// <summary>
    /// Gli indici nuovi ci sono <b>e</b> quelli resi ridondanti sono spariti: un indice che è il
    /// prefisso di un altro non fa guadagnare niente in lettura e si paga a ogni scrittura.
    /// </summary>
    [FactRichiedeMySql]
    public void DopoLeMigrazioni_gliIndiciSonoQuelliDecisi()
    {
        using var db = new DatabaseDiProva("indici_v91");
        db.CreaSchemaCompleto();

        using MySqlConnection c = db.Apri();

        Assert.Equal("employee_id,work_date", Colonne(c, "timesheet_entries", "ix_te_employee_date"));
        Assert.Equal("work_date", Colonne(c, "timesheet_entries", "ix_te_work_date"));
        Assert.Equal("notification_type,reference_type,reference_id,created_at",
            Colonne(c, "notifications", "ix_notif_dedup"));

        Assert.Null(Colonne(c, "timesheet_entries", "idx_te_employee"));
        Assert.Null(Colonne(c, "notifications", "idx_type"));
    }

    /// <summary>
    /// Il percorso che conta davvero: il database <b>già in esercizio</b>, che parte con i vecchi
    /// indici addosso — non con quelli del bootstrap. Qui lo stato di prima viene ricostruito a
    /// mano e la v91 rifatta girare sopra.
    ///
    /// <para>È anche la prova che il <c>DROP</c> è possibile: <c>employee_id</c> ha una chiave
    /// esterna e MySQL rifiuta di lasciarla senza un indice utilizzabile. Se la migrazione
    /// togliesse <c>idx_te_employee</c> <b>prima</b> di creare il composito, in produzione
    /// fallirebbe — e da quando un fallimento ferma l'avvio, il gestionale non ripartirebbe.</para>
    ///
    /// <para>La seconda esecuzione di seguito prova l'idempotenza: il motore riesegue una
    /// migrazione se il primo tentativo si è interrotto, e un <c>CREATE INDEX</c> nudo darebbe
    /// «Duplicate key name».</para>
    /// </summary>
    [FactRichiedeMySql]
    public void SuUnDatabaseComeLaProduzione_laV91SostituisceGliIndiciVecchi()
    {
        using var db = new DatabaseDiProva("indici_esistente");
        db.CreaSchemaCompleto();

        using MySqlConnection c = db.Apri();

        // Si rimette lo stato di prima della v91 (quello che c'è sul server aziendale).
        c.Execute("CREATE INDEX `idx_te_employee` ON `timesheet_entries` (`employee_id`)");
        c.Execute("DROP INDEX `ix_te_employee_date` ON `timesheet_entries`");
        c.Execute("DROP INDEX `ix_te_work_date` ON `timesheet_entries`");
        c.Execute("CREATE INDEX `idx_type` ON `notifications` (`notification_type`)");
        c.Execute("DROP INDEX `ix_notif_dedup` ON `notifications`");

        var migrazione = new ATEC.PM.Server.Migrations.M091_IndiciOreENotifiche();
        migrazione.Applica(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
        migrazione.Applica(c, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        Assert.Equal("employee_id,work_date", Colonne(c, "timesheet_entries", "ix_te_employee_date"));
        Assert.Equal("work_date", Colonne(c, "timesheet_entries", "ix_te_work_date"));
        Assert.Equal("notification_type,reference_type,reference_id,created_at",
            Colonne(c, "notifications", "ix_notif_dedup"));
        Assert.Null(Colonne(c, "timesheet_entries", "idx_te_employee"));
        Assert.Null(Colonne(c, "notifications", "idx_type"));
    }

    // ── 3. Il guardiano: le funzioni non tornano sulle colonne filtrate ───────

    /// <summary>
    /// Una funzione applicata alla colonna (<c>YEAR(work_date)=…</c>, <c>DATE(created_at)=…</c>)
    /// rende inutilizzabile <b>qualunque</b> indice su quella colonna. È la forma di difetto più
    /// facile da reintrodurre — si scrive in modo naturale, dà il risultato giusto e costa una
    /// scansione completa della tabella a ogni chiamata. Qui i due file più caldi vengono riletti
    /// e la scrittura vietata torna rossa.
    /// </summary>
    /// <summary>
    /// I modelli sono <b>espressioni regolari</b> e non sottostringhe: con il confronto letterale
    /// bastava scrivere <c>YEAR(te.work_date)</c> o <c>YEAR( work_date</c> con uno spazio per
    /// passare indenne — cioè il guardiano si aggirava senza nemmeno volerlo.
    /// </summary>
    [Theory]
    [InlineData("Controllers/DashboardController.cs", @"\b(YEAR|YEARWEEK|MONTH|WEEK)\s*\(\s*\w*\.?\s*work_date")]
    [InlineData("Services/NotificationService.cs", @"\bDATE\s*\(\s*\w*\.?\s*created_at")]
    // DATEDIFF seguito da un confronto = filtro (indice inutilizzabile). Nella SELECT, dove
    // calcola solo la colonna «days» mostrata a video, resta legittimo e non viene toccato.
    [InlineData("Services/NotificationService.cs", @"\bDATEDIFF\s*\([^)]*\)\s*(<=|<|>=|>|BETWEEN|=)")]
    [InlineData("Services/CodexGeneratorService.cs", @"\bDATE\s*\(\s*\w*\.?\s*reserved_at")]
    public void NelleQueryCalde_nessunaFunzioneSullaColonnaFiltrata(string file, string modelloVietato)
    {
        // Nei commenti si può nominare: servono a spiegare perché non si scrive più così.
        string codice = string.Join('\n', File.ReadAllText(Path.Combine(CartellaServer(), file))
            .Split('\n')
            .Where(r => !r.TrimStart().StartsWith("//")));

        var trovato = System.Text.RegularExpressions.Regex.Match(codice, modelloVietato,
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        Assert.False(trovato.Success,
            $"{file} è tornato a usare «{trovato.Value}»: con una funzione sulla colonna nessun " +
            "indice viene usato e la query legge tutta la tabella. Filtrare per intervallo di date.");
    }

    /// <summary>
    /// Il dedup delle notifiche è una <b>finestra di un giorno</b>, e va contata: allargarla a 30
    /// giorni non rompe nessun modello vietato — la query resta veloce e sargable — ma spegne il
    /// promemoria giornaliero, e le scadenze ancora aperte smettono di riavvisare. Provato: con la
    /// finestra allargata a mano, la suite restava 6/6.
    ///
    /// <para>I punti sono <b>sette</b> e non più otto dalla v93: le anomalie ore non deduplicano
    /// più per giornata di creazione ma per (persona, giorno lavorato), in
    /// <c>notifications.reference_date</c>. La loro vecchia finestra — l'unica ancorata a
    /// <c>te.work_date</c> invece che a oggi — è BUG-014, e il terzo controllo qui sotto vieta
    /// esplicitamente quella forma: la notifica nasceva il giorno in cui si registrano le ore,
    /// cioè quasi sempre fuori dalla finestra in cui la si andava a cercare.</para>
    /// </summary>
    [Fact]
    public void IlDedupDelleNotifiche_restaUnaFinestraDiUnGiornoInSettePunti()
    {
        string sorgente = File.ReadAllText(
            Path.Combine(CartellaServer(), "Services/NotificationService.cs"));

        // L'apertura deve finire lì: `>= CURDATE()` e basta. Contando il solo `created_at >=`,
        // un `>= CURDATE() - INTERVAL 30 DAY` continuerebbe a contare — ed è proprio la mutazione
        // che era passata indenne.
        int aperture = System.Text.RegularExpressions.Regex.Matches(
            sorgente, @"created_at\s*>=\s*CURDATE\(\)\s*(?:AND\b|\r?\n)").Count;
        int chiusure = System.Text.RegularExpressions.Regex.Matches(
            sorgente, @"created_at\s*<\s*\S+\s*\+\s*INTERVAL 1 DAY").Count;

        // La finestra ancorata al giorno LAVORATO invece che a oggi: rileggendola sembra giusta
        // — è corretta, è sargable — e sbaglia solo quando le ore si registrano il giorno dopo.
        int ancorateAlGiornoLavorato = System.Text.RegularExpressions.Regex.Matches(
            sorgente, @"created_at\s*(?:>=|>|<=|<)\s*\w*\.?work_date").Count;

        Assert.True(aperture == 7, $"Le finestre di dedup aperte sono {aperture}, dovrebbero essere 7.");
        Assert.True(chiusure == 7,
            $"Solo {chiusure} finestre su {aperture} durano un giorno: una finestra più larga " +
            "spegne il promemoria giornaliero senza che niente lo segnali.");
        Assert.True(ancorateAlGiornoLavorato == 0,
            "Un dedup è tornato a cercare la notifica nella giornata di work_date: chi compila il " +
            "timesheet il giorno dopo non la trova mai e ne fa nascere una copia a ogni giro " +
            "(BUG-014). La giornata segnalata si scrive in notifications.reference_date.");
    }

    // ── attrezzi ──────────────────────────────────────────────────────────────

    private static string? Colonne(MySqlConnection c, string tabella, string indice) =>
        c.ExecuteScalar<string?>(@"
            SELECT GROUP_CONCAT(column_name ORDER BY seq_in_index)
            FROM information_schema.statistics
            WHERE table_schema = DATABASE() AND table_name = @T AND index_name = @I",
            new { T = tabella, I = indice });

    private static (int Persona, int Fase) SeminaCommessaEPersona(MySqlConnection c)
    {
        int cliente = Inserisci(c, "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int persona = Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        int commessa = Inserisci(c,
            @"INSERT INTO projects (code, title, customer_id, pm_id)
              VALUES ('C20260815.991', 'Commessa di prova', @Cliente, @Pm)",
            new { Cliente = cliente, Pm = persona });
        int fase = Inserisci(c,
            @"INSERT INTO project_phases (project_id, name, phase_template_id)
              VALUES (@Commessa, 'Montaggio in cantiere', NULL)",
            new { Commessa = commessa });
        return (persona, fase);
    }

    private static int Inserisci(MySqlConnection c, string sql, object? par = null)
    {
        c.Execute(sql, par);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static string CartellaServer()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidato = Path.Combine(dir.FullName, "ATEC.PM.Server");
            if (Directory.Exists(candidato)) return candidato;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Cartella ATEC.PM.Server non trovata da " + AppContext.BaseDirectory);
    }
}
