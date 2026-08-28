using ATEC.PM.Server.Services;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Elenchi;

/// <summary>
/// L'ordine dell'elenco commesse (<c>GET /api/projects</c>, l'albero della pagina Commesse):
/// data crescente letta dal codice, e <b>nessuna eccezione che non sia dichiarata</b>.
///
/// <para>La sola eccezione ammessa è la commessa <b>chiusa</b> aperta da un deep-link: il client
/// la chiede con <c>includeId</c> e il server la tiene in testa, perché ordinata da chiusa
/// finirebbe nelle ultime pagine dello scroll infinito e l'albero resterebbe senza il nodo
/// della commessa che si vede a destra.</para>
///
/// <para>🪤 Fino al 24/08/2026 quell'eccezione non guardava lo stato: valeva per <b>qualunque</b>
/// riga passata come <c>includeId</c>. Siccome il client passa come <c>includeId</c> la commessa
/// selezionata all'apertura dell'albero, la commessa <b>appena creata</b> — su cui si atterra
/// subito dopo il salvataggio — compariva in cima all'elenco invece che in fondo, dove la mette
/// la sua data. Nessun errore, nessun messaggio: solo l'ordine cronologico che sembrava rotto.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class OrdineCommesseTests
{
    private readonly SchemaCondiviso _schema;

    // Qui si provano DATI, non lo schema: si riparte da pulito in millisecondi
    // invece di ricostruire il database (che a questa classe costava un minuto).
    public OrdineCommesseTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    /// <summary>Titolo delle righe seminate qui: lo schema di prova ne semina già di suoi.</summary>
    private const string Titolo = "PROVA ORDINE ELENCO";

    /// <summary>
    /// La stessa coppia WHERE + ORDER BY di <c>ProjectsController.CostruisciFiltroElenco</c>
    /// quando l'albero chiede le sole aperte più la riga del deep-link. Il filtro sul titolo è
    /// l'unica aggiunta: tiene fuori le commesse che <c>InitDatabase</c> semina da sé.
    /// </summary>
    private static List<string> ElencoAlbero(MySqlConnection c, int includeId) => c.Query<string>($@"
        SELECT p.code FROM projects p
        WHERE p.title = @Titolo
          AND (p.status NOT IN {ProjectSorting.ClosedStatusesSql} OR p.id = @IncludeId)
        ORDER BY {ProjectSorting.DeepLinkChiusaInTesta()}, {ProjectSorting.OrderBy("p", "p.status")}",
        new { IncludeId = includeId, Titolo }).ToList();

    [FactRichiedeMySql]
    public void LaCommessaAppenaCreata_restaInFondo_nonInTesta()
    {
        using MySqlConnection c = _schema.Apri();

        // Le date che contano: una commessa vecchia, una di mezzo e quella di oggi (l'ultima creata).
        Semina(c, "C241204_166", "ON_HOLD");
        int mediana = Semina(c, "C260605_208", "ACTIVE");
        int appenaCreata = Semina(c, "C260824_210", "ACTIVE");

        string[] perData = { "C241204_166", "C260605_208", "C260824_210" };

        // Deep-link sulla commessa appena creata: è aperta, quindi l'ordine non cambia di una riga.
        Assert.Equal(perData, ElencoAlbero(c, appenaCreata));

        // E lo stesso vale per una qualsiasi altra aperta raggiunta da deep-link.
        Assert.Equal(perData, ElencoAlbero(c, mediana));
    }

    [FactRichiedeMySql]
    public void LaCommessaChiusaDelDeepLink_restaInTesta()
    {
        using MySqlConnection c = _schema.Apri();

        Semina(c, "C241204_166", "ON_HOLD");
        Semina(c, "C260824_210", "ACTIVE");
        int chiusa = Semina(c, "C260415_203", "COMPLETED");

        // Senza il pin la chiusa finirebbe ultima (le chiuse vanno in fondo) e con lo scroll
        // infinito l'albero resterebbe senza il suo nodo: è il caso per cui includeId esiste.
        Assert.Equal(
            new[] { "C260415_203", "C241204_166", "C260824_210" },
            ElencoAlbero(c, chiusa));
    }

    [FactRichiedeMySql]
    public void SenzaDeepLink_leCommesseVannoInOrdineDiData_eLeAttivitaInFondo()
    {
        using MySqlConnection c = _schema.Apri();

        // Ordine di inserimento apposta sbagliato, e i due formati di codice mescolati:
        // 'C20260805.500' alfabetico verrebbe PRIMA di 'C241204_166' ('0' < '4').
        Semina(c, "C260824_210", "ACTIVE");
        Semina(c, "SERVICE _ SANGRATO", "ACTIVE");
        Semina(c, "C20260805.500", "ACTIVE");
        Semina(c, "C241204_166", "ON_HOLD");

        Assert.Equal(
            new[] { "C241204_166", "C20260805.500", "C260824_210", "SERVICE _ SANGRATO" },
            ElencoAlbero(c, 0));
    }

    private static int Semina(MySqlConnection c, string codice, string stato)
    {
        c.Execute(
            @"INSERT INTO projects (code, title, customer_id, pm_id, status)
              VALUES (@Code, @Titolo, @Cliente, @Pm, @Status)",
            new { Code = codice, Titolo, Status = stato, Cliente = Cliente(c), Pm = Pm(c) });
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }

    /// <summary>Cliente e PM di comodo: `projects` li vuole, all'ordinamento non servono.</summary>
    private static int Cliente(MySqlConnection c) => PrimoOppureCrea(c,
        "SELECT id FROM customers LIMIT 1",
        "INSERT INTO customers (company_name) VALUES ('Cliente di prova')");

    private static int Pm(MySqlConnection c) => PrimoOppureCrea(c,
        "SELECT id FROM employees LIMIT 1",
        "INSERT INTO employees (first_name, last_name) VALUES ('Mario', 'Rossi')");

    private static int PrimoOppureCrea(MySqlConnection c, string select, string insert)
    {
        int? esistente = c.ExecuteScalar<int?>(select);
        if (esistente is int id) return id;
        c.Execute(insert);
        return c.ExecuteScalar<int>("SELECT LAST_INSERT_ID()");
    }
}
