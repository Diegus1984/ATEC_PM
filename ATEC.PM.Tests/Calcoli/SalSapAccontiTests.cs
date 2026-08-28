using ATEC.PM.Server.Controllers;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Calcoli;

/// <summary>
/// Segnalazione #131 — «SAL / SAP Acconti»: cosa entra nel totale acconti del gestionale.
///
/// <para>È il numero che una persona confronterà con il saldo del conto SAP 1501600001. Se
/// sbaglia perimetro non si rompe niente a video: esce una <b>differenza che non esiste</b>,
/// e qualcuno la va a cercare dentro SAP. Per questo il perimetro sta scritto in un test.</para>
///
/// <para>Il test esegue <see cref="SalController.SapAccontiSql"/>, cioè la query <b>vera</b>:
/// una copia ricopiata qui smetterebbe di sorvegliare quella del server al primo ritocco.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class SalSapAccontiTests
{
    private readonly SchemaCondiviso _schema;

    public SalSapAccontiTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    /// <summary>
    /// La causale «Conto SAP» decide da sola: Acconto dentro, Ricavo fuori, vuoto fuori.
    /// È la regola scritta nella segnalazione — «se il campo si aggiorna a Ricavo la relativa
    /// fattura e il relativo importo sono esclusi dal conteggio».
    /// </summary>
    [FactRichiedeMySql]
    public void Conta_solo_le_righe_con_conto_sap_acconto()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c, "C20260828.201", "ACTIVE", valore: 100_000m);

        Riga(c, commessa, "Acconto", perc: 10m);   // 10.000 €
        Riga(c, commessa, "Acconto", perc: 5m);    //  5.000 €
        Riga(c, commessa, "Ricavo", perc: 80m);    // esclusa
        Riga(c, commessa, "", perc: 5m);           // esclusa (non ancora classificata)

        var totali = Totali(c);
        Assert.Equal(2, totali.TotFatture);
        Assert.Equal(15_000m, totali.Importo);
    }

    /// <summary>
    /// 🪤 Il confronto è sul <b>testo</b> della causale copiato nella riga, non su un id:
    /// maiuscole e spazi non devono far sparire una fattura dal conteggio.
    /// </summary>
    [FactRichiedeMySql]
    public void La_causale_si_riconosce_anche_scritta_diversa()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c, "C20260828.202", "ACTIVE", valore: 50_000m);

        Riga(c, commessa, "ACCONTO", perc: 10m);
        Riga(c, commessa, " Acconto ", perc: 10m);

        Assert.Equal(2, Totali(c).TotFatture);
    }

    /// <summary>
    /// Il perimetro <b>non</b> è quello delle viste operative: un acconto fatturato su una
    /// commessa nel frattempo chiusa sta ancora dentro il conto SAP, quindi deve contare.
    /// Restano fuori solo le cose che non sono fatturato vero — bozze, commesse annullate e
    /// le Altre Attività a codice libero, che nel SAL non entrano dalla #85.
    /// </summary>
    [FactRichiedeMySql]
    public void Le_commesse_chiuse_contano_bozze_annullate_e_altre_attivita_no()
    {
        using MySqlConnection c = _schema.Apri();

        Riga(c, Commessa(c, "C20260828.203", "COMPLETED", 100_000m), "Acconto", 10m); // 10.000 €
        Riga(c, Commessa(c, "C20260828.204", "ON_HOLD", 100_000m), "Acconto", 5m);    //  5.000 €
        Riga(c, Commessa(c, "C20260828.205", "DRAFT", 100_000m), "Acconto", 50m);     // fuori
        Riga(c, Commessa(c, "C20260828.206", "CANCELLED", 100_000m), "Acconto", 50m); // fuori
        Riga(c, Commessa(c, "INTERNA_UFFICIO", "ACTIVE", 100_000m), "Acconto", 50m);  // fuori

        var totali = Totali(c);
        Assert.Equal(2, totali.TotFatture);
        Assert.Equal(15_000m, totali.Importo);
    }

    /// <summary>
    /// L'importo è <c>valore ordine × %SAL / 100</c>, con le 10 cifre decimali della #130:
    /// il totale che va confrontato con SAP deve tornare al centesimo.
    /// </summary>
    [FactRichiedeMySql]
    public void L_importo_usa_la_percentuale_intera_non_arrotondata()
    {
        using MySqlConnection c = _schema.Apri();
        int commessa = Commessa(c, "C20260828.207", "ACTIVE", valore: 142_625m);

        Riga(c, commessa, "Acconto", perc: 8.13278177m);

        Assert.Equal(11_599.38m, Math.Round(Totali(c).Importo, 2));
    }

    /// <summary>Senza righe acconto il totale è zero, non NULL: il client mostra «0», non «—».</summary>
    [FactRichiedeMySql]
    public void Senza_acconti_i_totali_sono_zero()
    {
        using MySqlConnection c = _schema.Apri();
        var totali = Totali(c);
        Assert.Equal(0, totali.TotFatture);
        Assert.Equal(0m, totali.Importo);
    }

    // ── attrezzi ──────────────────────────────────────────────────────────────

    private static (int TotFatture, decimal Importo) Totali(MySqlConnection c)
    {
        var r = c.QuerySingle(SalController.SapAccontiSql,
            new { Causale = SalController.CausaleAcconto });
        return ((int)r.TotFatture, (decimal)r.Importo);
    }

    private static void Riga(MySqlConnection c, int commessa, string contoSap, decimal perc) =>
        c.Execute(@"INSERT INTO sal_rows (project_id, step, perc, conto_sap, n_fatt)
                    VALUES (@P, 'Step', @Perc, @Conto, '2026000001')",
            new { P = commessa, Perc = perc, Conto = contoSap });

    /// <summary>
    /// Una commessa col suo foglio SAL (serve <c>project_sal.valore</c>: senza importo ordine
    /// l'Importo Fattura non esiste).
    /// 🪤 Un cliente solo per tutto il test: <c>customers</c> ha un vincolo unico sulla partita
    /// IVA e due clienti con la partita vuota si pestano i piedi al secondo inserimento.
    /// </summary>
    private int Commessa(MySqlConnection c, string codice, string stato, decimal valore)
    {
        if (_cliente == 0)
        {
            c.Execute("INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
            _cliente = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
            c.Execute("INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
            _pm = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        }

        c.Execute(@"INSERT INTO projects (code, title, customer_id, pm_id, status)
                    VALUES (@Code, 'Prova acconti', @Cliente, @Pm, @Stato)",
            new { Code = codice, Cliente = _cliente, Pm = _pm, Stato = stato });
        int id = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");

        c.Execute("INSERT INTO project_sal (project_id, cliente, valore) VALUES (@P, 'Cliente di prova', @V)",
            new { P = id, V = valore });
        return id;
    }

    private int _cliente;
    private int _pm;
}
