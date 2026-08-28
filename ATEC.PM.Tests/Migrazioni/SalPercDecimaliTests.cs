using ATEC.PM.Server.Migrations;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Migrazioni;

/// <summary>
/// Segnalazione #130 — la <b>% SAL</b> deve tenere 10 cifre decimali.
///
/// <para>La percentuale non è un'etichetta descrittiva: è il numero da cui esce l'importo che
/// va in fattura. Su un ordine da 142.625,00 € la riga da 11.599,38 € vale
/// <c>8,13278177%</c>, e con la colonna a <c>DECIMAL(6,3)</c> MySQL arrotondava a
/// <c>8,133</c> — <b>senza dire niente</b> — facendo uscire 11.600,45 €. Un troncamento
/// silenzioso è esattamente il tipo di guasto che nessuno nota finché non lo trova il
/// cliente sulla fattura, ed è il motivo per cui questo test esiste.</para>
///
/// <para>Il test parte dalla forma <b>vecchia</b> della colonna: su uno schema già aggiornato
/// la M114 non farebbe niente e non proverebbe nulla.</para>
/// </summary>
public class SalPercDecimaliTests
{
    /// <summary>La percentuale della #130, con le sue 8 cifre decimali vere.</summary>
    private const decimal PercDellaSegnalazione = 8.13278177m;

    [FactRichiedeMySql]
    public void La_percentuale_sal_tiene_dieci_decimali_e_non_perde_quelle_gia_scritte()
    {
        using var db = new DatabaseDiProva("salperc");
        db.CreaSchemaCompleto(); // qui la M114 è già passata: la colonna nasce larga
        using MySqlConnection c = db.Apri();

        // ── si torna alla forma vecchia, per provare davvero la migrazione ──
        c.Execute("ALTER TABLE sal_rows MODIFY perc DECIMAL(6,3) NULL");
        int commessa = Commessa(c);
        int rigaVecchia = InserisciRiga(c, commessa, PercDellaSegnalazione);

        // Il difetto della #130, riprodotto: con 3 decimali la percentuale è già persa.
        Assert.Equal(8.133m, Perc(c, rigaVecchia));

        // ── la migrazione ──
        new M114_SalPercDecimali().Applica(c, NullLogger.Instance);

        Assert.Equal(10, Scala(c));
        // Allargare non tocca quello che c'era: il valore resta il 8,133 già scritto.
        Assert.Equal(8.133m, Perc(c, rigaVecchia));

        // ── quello per cui è stata fatta: una riga nuova tiene tutte le cifre ──
        int rigaNuova = InserisciRiga(c, commessa, PercDellaSegnalazione);
        Assert.Equal(PercDellaSegnalazione, Perc(c, rigaNuova));

        // E l'importo che ne esce è quello concordato col cliente, al centesimo.
        decimal importo = c.ExecuteScalar<decimal>(
            "SELECT ROUND(142625 * perc / 100, 2) FROM sal_rows WHERE id = @R", new { R = rigaNuova });
        Assert.Equal(11599.38m, importo);

        // Le 10 cifre piene, non solo le 8 della segnalazione.
        int rigaPiena = InserisciRiga(c, commessa, 12.3456789012m);
        Assert.Equal(12.3456789012m, Perc(c, rigaPiena));

        // ── rieseguibile: la seconda volta non fa e non rompe niente ──
        new M114_SalPercDecimali().Applica(c, NullLogger.Instance);
        Assert.Equal(10, Scala(c));
        Assert.Equal(PercDellaSegnalazione, Perc(c, rigaNuova));
    }

    // ── attrezzi ──────────────────────────────────────────────────────────────

    private static int Scala(MySqlConnection c) => c.ExecuteScalar<int>(@"
        SELECT NUMERIC_SCALE FROM information_schema.columns
        WHERE table_schema = DATABASE() AND table_name = 'sal_rows' AND column_name = 'perc'");

    private static decimal Perc(MySqlConnection c, int riga) =>
        c.ExecuteScalar<decimal>("SELECT perc FROM sal_rows WHERE id = @R", new { R = riga });

    private static int InserisciRiga(MySqlConnection c, int commessa, decimal perc)
    {
        c.Execute(
            "INSERT INTO sal_rows (project_id, step, perc) VALUES (@P, 'Step', @Perc)",
            new { P = commessa, Perc = perc });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    private static int Commessa(MySqlConnection c)
    {
        c.Execute("INSERT INTO customers (company_name) VALUES ('Cliente di prova')");
        int cliente = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        c.Execute("INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        int pm = c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
        c.Execute(@"INSERT INTO projects (code, title, customer_id, pm_id, status)
                    VALUES ('C20260828.130', 'Prova % SAL', @Cliente, @Pm, 'ACTIVE')",
            new { Cliente = cliente, Pm = pm });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }
}
