using ATEC.PM.Server.Services;
using ATEC.PM.Shared.DTOs;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// BUG-015 — le righe di un preventivo si toccano solo dal proprio preventivo.
///
/// <para>Il difetto: la UPDATE delle righe materiale filtrava per <c>id</c> e basta, mentre quella
/// delle sezioni — tre righe sopra, nello stesso metodo — aveva il vincolo di appartenenza. Chi
/// poteva aprire un preventivo poteva riscrivere contingenza, margine e ombreggiatura delle righe
/// materiale di <b>qualunque altro</b>, mettendone gli id nel corpo della richiesta. Sono
/// percentuali che entrano nel prezzo d'offerta, e nessuna schermata lo avrebbe mai mostrato.</para>
///
/// <para>Il secondo test è la metà che conta di più: la correzione passa dalla sezione madre, e una
/// relazione sbagliata farebbe <b>smettere di salvare</b> il pannello Distribuzione — un guasto
/// peggiore del buco che chiude.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class AppartenenzaPreventivoTests
{

    private readonly SchemaCondiviso _schema;

    /// <summary>
    /// xUnit costruisce una istanza per ogni test: qui si riporta il database condiviso a
    /// com'era appena creato (~45 ms), invece di costruirne uno nuovo (~5 s).
    /// </summary>
    public AppartenenzaPreventivoTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }
    [FactRichiedeMySql]
    public void LeRigheMaterialeDiUnAltroPreventivo_nonSiPossonoToccare()
    {
        using MySqlConnection c = _schema.Apri();

        (int preventivoA, int _, int rigaA) = SeminaPreventivoConRiga(c, "Preventivo A");
        (int _, int _, int rigaB) = SeminaPreventivoConRiga(c, "Preventivo B");

        // Si opera SUL PREVENTIVO A, ma nel corpo si mette l'id di una riga del B.
        var richiesta = new BatchDistributionRequest
        {
            MaterialItems = new List<BatchDistributionItem>
            {
                new() { Id = rigaB, ContingencyPct = 99m, MarginPct = 99m, ContingencyPinned = true, IsShadowed = true },
            },
        };

        using (var tx = c.BeginTransaction())
        {
            CostingDataService.SaveDistributionsBatch(c, CostingDataService.CostingScope.Quote, preventivoA, richiesta, tx);
            tx.Commit();
        }

        decimal contingenzaB = c.ExecuteScalar<decimal>(
            "SELECT contingency_pct FROM quote_material_items WHERE id=@Id", new { Id = rigaB });

        Assert.Equal(0m, contingenzaB);   // 0 = il valore seminato, cioè intatto
        Assert.NotEqual(rigaA, rigaB);    // sanità del banco di prova
    }

    [FactRichiedeMySql]
    public void LeRigheDelProprioPreventivo_continuanoASalvarsi()
    {
        using MySqlConnection c = _schema.Apri();

        (int preventivo, int sezione, int riga) = SeminaPreventivoConRiga(c, "Preventivo unico");

        var richiesta = new BatchDistributionRequest
        {
            Sections = new List<BatchDistributionItem>(),
            MaterialItems = new List<BatchDistributionItem>
            {
                new() { Id = riga, ContingencyPct = 7.5m, MarginPct = 12.25m, MarginPinned = true, IsShadowed = true },
            },
        };

        using (var tx = c.BeginTransaction())
        {
            CostingDataService.SaveDistributionsBatch(c, CostingDataService.CostingScope.Quote, preventivo, richiesta, tx);
            tx.Commit();
        }

        var salvata = c.QueryFirst<(decimal Cont, decimal Marg, bool MargPin, bool Shadow)>(@"
            SELECT contingency_pct, margin_pct, margin_pinned, is_shadowed
            FROM quote_material_items WHERE id=@Id", new { Id = riga });

        Assert.Equal(7.5m, salvata.Cont);
        Assert.Equal(12.25m, salvata.Marg);
        Assert.True(salvata.MargPin);
        Assert.True(salvata.Shadow);
        Assert.True(sezione > 0);
    }

    /// <summary>Un preventivo con una sezione materiale e una riga dentro, tutta a zero.</summary>
    private static (int Preventivo, int Sezione, int Riga) SeminaPreventivoConRiga(MySqlConnection c, string titolo)
    {
        // Partita IVA distinta per cliente: `customers` ha un UNIQUE su vat_number che con due
        // stringhe vuote scatta («Duplicate entry ''»), e questo test crea due clienti.
        int cliente = Inserisci(c,
            "INSERT INTO customers (company_name, vat_number) VALUES (@N, @P)",
            new { N = titolo + " srl", P = "IT" + Guid.NewGuid().ToString("N").Substring(0, 9) });
        int persona = Inserisci(c, "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");
        // quote_number e created_by sono NOT NULL senza default: vanno valorizzati o l'INSERT
        // fallisce con «doesn't have a default value».
        int preventivo = Inserisci(c,
            @"INSERT INTO quotes (customer_id, created_by, quote_number, title, status)
              VALUES (@C, @E, @N, @T, 'DRAFT')",
            new { C = cliente, E = persona, N = "Q-" + titolo, T = titolo });
        int sezione = Inserisci(c,
            @"INSERT INTO quote_material_sections (quote_id, name, markup_value, sort_order, is_enabled)
              VALUES (@Q, 'Materiali', 1.000, 0, 1)", new { Q = preventivo });
        int riga = Inserisci(c,
            @"INSERT INTO quote_material_items (section_id, description, quantity, unit_cost, markup_value,
                                                contingency_pct, margin_pct, sort_order)
              VALUES (@S, 'Riga di prova', 1, 100, 1.000, 0, 0, 0)", new { S = sezione });
        return (preventivo, sezione, riga);
    }

    private static int Inserisci(MySqlConnection c, string sql, object? par = null)
    {
        c.Execute(sql, par);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }
}
