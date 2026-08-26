using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Il semaforo delle righe SAL (<see cref="SalAlert"/>), unificato il 24/08/2026.
///
/// <para>La regola viveva scritta <b>due volte</b>, in due <c>CASE</c> gemelli dentro
/// <c>/api/sal/summary</c> e <c>/api/sal/prospetto</c>. Due copie allineate non danno
/// fastidio a nessuno finché restano allineate: il guaio è che quando smettono non se ne
/// accorge nessuno, e infatti è già costato le segnalazioni #114 e #117 — la card della
/// Dashboard e il pallino del menu mostravano numeri diversi da quelli scritti dentro la
/// pagina SAL.</para>
///
/// <para>L'espressione NON si ricopia qui dentro: si esegue quella del server
/// (<see cref="SalAlert.CaseSql"/>), altrimenti il test sorveglia una copia e non la regola —
/// cioè esattamente il difetto che si sta chiudendo.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class SalAlertTests
{
    private readonly SchemaCondiviso _schema;

    public SalAlertTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    [FactRichiedeMySql]
    public void LaFatturaNonIncassataOltreIlSaldo_eIncasso()
    {
        using MySqlConnection c = _schema.Apri();
        // Emessa, saldo previsto dieci giorni fa, non pagata.
        Assert.Equal("incasso", Classifica(c, stato: "emessa", pagamento: "30 gg.", giorniDataFatt: -40, ggSaldo: 30));
    }

    /// <summary>
    /// <c>gg_saldo</c> è <b>NOT NULL con default 0</b>, e con 0 la data prevista di saldo
    /// coincide con quella di fattura. Ne segue una cosa che sorprende chi legge il CASE:
    /// una riga <b>non ancora emessa</b> con data già passata e gg saldo 0 conta come
    /// <c>incasso</c>, non come <c>warn</c> — perché l'incasso ha la precedenza ed è
    /// «indipendente dallo stato di fatturazione» (regola v10/D11).
    ///
    /// <para>Non è una novità dell'unificazione: facevano così <b>entrambi</b> i CASE di
    /// prima. È scritto qui perché la prossima persona non lo scambi per un difetto.</para>
    /// </summary>
    [FactRichiedeMySql]
    public void ConGgSaldoZero_ilSaldoScadeIlGiornoStessoDellaFattura()
    {
        using MySqlConnection c = _schema.Apri();
        Assert.Equal("incasso", Classifica(c, stato: "", pagamento: "", giorniDataFatt: -1, ggSaldo: 0));
    }

    /// <summary>
    /// L'incasso <b>vince su tutto</b>: una riga può essere insieme scaduta di fatturazione e
    /// di incasso, e deve contare una volta sola — o i totali del Prospetto e delle due viste
    /// non tornerebbero mai.
    /// </summary>
    [FactRichiedeMySql]
    public void LIncasso_vinceSulWarn()
    {
        using MySqlConnection c = _schema.Apri();
        // Non emessa (quindi anche 'warn') ma col saldo già scaduto: conta come incasso.
        Assert.Equal("incasso", Classifica(c, stato: "", pagamento: "30 gg.", giorniDataFatt: -40, ggSaldo: 30));
    }

    /// <summary>Pagata: nessun incasso da inseguire, per quanto vecchia sia la data.</summary>
    [FactRichiedeMySql]
    public void LaRigaPagata_nonEmaiIncasso()
    {
        using MySqlConnection c = _schema.Apri();
        Assert.Equal("attesa", Classifica(c, stato: "emessa", pagamento: "Pagata", giorniDataFatt: -400, ggSaldo: 30));
    }

    /// <summary>
    /// Da fatturare e in ritardo. I gg saldo sono ampi apposta: con lo 0 di default il saldo
    /// scadrebbe lo stesso giorno della fattura e la riga passerebbe a <c>incasso</c>, che ha
    /// la precedenza (vedi <see cref="ConGgSaldoZero_ilSaldoScadeIlGiornoStessoDellaFattura"/>).
    /// </summary>
    [FactRichiedeMySql]
    public void LaDaFatturareConDataArrivata_eWarn()
    {
        using MySqlConnection c = _schema.Apri();
        Assert.Equal("warn", Classifica(c, stato: "", pagamento: "", giorniDataFatt: -1, ggSaldo: 30));
        // Oggi compreso: la data «arrivata» comprende il giorno stesso.
        Assert.Equal("warn", Classifica(c, stato: "", pagamento: "", giorniDataFatt: 0, ggSaldo: 30));
    }

    /// <summary>
    /// Il pre-warning parte dal <b>lunedì della settimana precedente</b>, non «entro 7 giorni»:
    /// è proprio la differenza fra le due regole che faceva divergere i conteggi (#117).
    /// Una data fra 40 giorni non è imminente in nessuna delle due letture.
    /// </summary>
    [FactRichiedeMySql]
    public void LaDaFatturareLontana_nonEsegnalata()
    {
        using MySqlConnection c = _schema.Apri();
        Assert.Equal("", Classifica(c, stato: "", pagamento: "", giorniDataFatt: 40, ggSaldo: 30));
    }

    /// <summary>
    /// Dal lunedì della settimana precedente in poi la riga è in pre-warning. Si sceglie una
    /// data futura che cade di sicuro dentro quella finestra: il lunedì della settimana
    /// prossima — il suo «lunedì precedente» è il lunedì di questa settimana, già passato.
    /// </summary>
    [FactRichiedeMySql]
    public void LaDaFatturareDellaSettimanaProssima_ePre()
    {
        using MySqlConnection c = _schema.Apri();
        // Lunedì prossimo = lunedì di questa settimana + 7.
        int aLunediProssimo = c.ExecuteScalar<int>(
            "SELECT DATEDIFF(DATE_ADD(DATE_SUB(CURDATE(), INTERVAL WEEKDAY(CURDATE()) DAY), INTERVAL 7 DAY), CURDATE())");
        Assert.Equal("pre", Classifica(c, stato: "", pagamento: "", giorniDataFatt: aLunediProssimo, ggSaldo: 30));
    }

    /// <summary>Emessa e nei termini: non è un allarme, ma il Prospetto la distingue.</summary>
    [FactRichiedeMySql]
    public void LaEmessaNeiTermini_eAttesa()
    {
        using MySqlConnection c = _schema.Apri();
        Assert.Equal("attesa", Classifica(c, stato: "emessa", pagamento: "30 gg.", giorniDataFatt: -1, ggSaldo: 300));
    }

    /// <summary>Senza data ipotizzata non c'è niente da segnalare: nessun confronto regge.</summary>
    [FactRichiedeMySql]
    public void SenzaDataFattura_nessunSemaforo()
    {
        using MySqlConnection c = _schema.Apri();
        Assert.Equal("", Classifica(c, stato: "", pagamento: "", giorniDataFatt: null, ggSaldo: 30));
    }

    /// <summary>
    /// La stessa espressione deve dare lo stesso esito <b>con e senza alias</b>: il summary la
    /// usa sulle colonne nude di <c>sal_rows</c>, il prospetto su una sottoquery con alias. Se
    /// le due forme divergessero tornerebbero a divergere anche i due endpoint, che è tutto il
    /// motivo per cui questa classe esiste.
    /// </summary>
    [FactRichiedeMySql]
    public void ConAliasESenzaAlias_stessoEsito()
    {
        using MySqlConnection c = _schema.Apri();
        SeminaRighe(c);

        List<string> nude = c.Query<string>(
            $"SELECT {SalAlert.CaseSql()} FROM sal_rows ORDER BY id").ToList();
        List<string> conAlias = c.Query<string>(
            $"SELECT {SalAlert.CaseSql("t")} FROM (SELECT * FROM sal_rows) t ORDER BY t.id").ToList();

        Assert.NotEmpty(nude);
        Assert.Equal(nude, conAlias);
    }

    /// <summary>
    /// Le due query vere della pagina SAL girano, e <b>dicono la stessa cosa</b>: per ogni
    /// classe di allarme il conteggio del riepilogo coincide col numero di righe che il
    /// prospetto marca con quella classe.
    ///
    /// <para>È l'invariante delle segnalazioni #114 e #117: quando i due numeri divergono, la
    /// pagina SAL dice una cosa e la Dashboard (o il pallino del menu) un'altra, e chi guarda
    /// non sa a chi credere. Qui si eseguono <c>SalController.ProspettoSql</c> e
    /// <c>SalController.SummarySql</c> — quelle vere, non una copia — quindi il test cade
    /// anche se a rompersi è il resto della query, non solo il semaforo.</para>
    /// </summary>
    [FactRichiedeMySql]
    public void IlRiepilogoEIlProspetto_diconoLaStessaCosa()
    {
        using MySqlConnection c = _schema.Apri();
        SeminaRighe(c);

        // Il prospetto ritorna una ventina di colonne e `Alert` non è la prima: va pescata per
        // nome, o Dapper mappa su string la prima che trova (ProjectId) e il test confronta
        // numeri di commessa con nomi di allarme.
        List<string> alertProspetto = c.Query<string>(
            $"SELECT Alert FROM ({ATEC.PM.Server.Controllers.SalController.ProspettoSql}) x")
            .Select(a => a ?? "").ToList();

        var riepilogo = c.Query<(int Warn, int Pre, int Incasso)>(
            $"SELECT Warn, Pre, Incasso FROM ({ATEC.PM.Server.Controllers.SalController.SummarySql}) r")
            .ToList();

        foreach (string classe in new[] { "warn", "pre", "incasso" })
        {
            int daProspetto = alertProspetto.Count(a => a == classe);
            int daRiepilogo = classe switch
            {
                "warn" => riepilogo.Sum(r => r.Warn),
                "pre" => riepilogo.Sum(r => r.Pre),
                _ => riepilogo.Sum(r => r.Incasso),
            };
            Assert.True(daProspetto == daRiepilogo,
                $"'{classe}': il prospetto ne conta {daProspetto}, il riepilogo {daRiepilogo}");
        }

        // Almeno un allarme c'è: un confronto fra due zeri non proverebbe niente.
        Assert.Contains(alertProspetto, a => a is "warn" or "pre" or "incasso");
    }

    // ── attrezzi ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserisce una riga SAL e ritorna come la classifica l'espressione VERA del server.
    /// Le date si danno in giorni da oggi, così il test non invecchia.
    /// </summary>
    private string Classifica(
        MySqlConnection c, string stato, string pagamento, int? giorniDataFatt, int ggSaldo)
    {
        int progetto = Commessa(c);
        c.Execute(@"
            INSERT INTO sal_rows (project_id, step, perc, stato, pagamento, data_fatt, gg_saldo)
            VALUES (@P, 'Step', 10, @S, @Pg,
                    CASE WHEN @G IS NULL THEN NULL ELSE DATE_ADD(CURDATE(), INTERVAL @G DAY) END, @Gg)",
            new { P = progetto, S = stato, Pg = pagamento, G = giorniDataFatt, Gg = ggSaldo });
        int riga = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");

        return c.ExecuteScalar<string>(
            $"SELECT {SalAlert.CaseSql()} FROM sal_rows WHERE id = @R", new { R = riga }) ?? "";
    }

    /// <summary>Una riga per classe, per il confronto alias / senza alias.</summary>
    private void SeminaRighe(MySqlConnection c)
    {
        Classifica(c, "emessa", "30 gg.", -40, 30);   // incasso
        Classifica(c, "", "", -1, 30);                // warn
        Classifica(c, "", "", 40, 30);                // niente
        Classifica(c, "emessa", "Pagata", -400, 30);  // attesa
        Classifica(c, "", "", null, 30);              // niente (senza data)
        Classifica(c, "", "", -1, 0);                 // incasso per gg saldo 0
    }

    /// <summary>
    /// Una commessa sola per test, riusata da tutte le righe seminate.
    /// 🪤 Non una per riga: <c>customers</c> ha un vincolo unico sulla partita IVA, e due
    /// clienti con la partita vuota si pestano i piedi al secondo inserimento.
    /// </summary>
    private int _commessa;

    private int Commessa(MySqlConnection c)
    {
        if (_commessa > 0) return _commessa;

        c.Execute("INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int cliente = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        c.Execute("INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        int pm = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        c.Execute(@"INSERT INTO projects (code, title, customer_id, pm_id, status)
                    VALUES ('C20260824.001', 'Prova SAL', @Cliente, @Pm, 'ACTIVE')",
            new { Cliente = cliente, Pm = pm });
        _commessa = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        return _commessa;
    }
}
