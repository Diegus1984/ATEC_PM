using ATEC.PM.Server.Services;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Un solo database di prova per tutti i test che non stanno provando le migrazioni.
///
/// <para><b>Perché.</b> Costruire lo schema costa <b>~5 secondi</b> (119 tabelle + 101
/// migrazioni) e la suite lo rifaceva 62 volte: cinque minuti spesi a ricostruire ogni volta
/// la stessa identica cosa. Qui si costruisce <b>una volta sola</b> e ogni test riparte da
/// pulito in <b>~45 millisecondi</b>.</para>
///
/// <para><b>Come si pulisce.</b> Non con <c>TRUNCATE</c>: misurato, svuotare 119 tabelle
/// InnoDB costa 4,4 s — <b>più che ricreare il database</b>, perché ogni TRUNCATE ricrea il
/// tablespace della tabella. Si fa invece il contrario: appena creato lo schema si fotografa
/// l'ultimo <c>id</c> di ogni tabella (110 su 119 ce l'hanno), e la pulizia cancella <b>solo
/// le righe più recenti</b> — quelle che ha aggiunto il test. I dati di partenza (utenze,
/// stati DDP, transizioni, aggregazioni) restano dove sono, quindi non serve nemmeno
/// riseminarli.</para>
///
/// <para><b>Chi NON deve usarlo.</b> I test del motore migrazioni e quelli che provano una
/// migrazione su dati pregressi (partono da un database vuoto <i>per definizione</i>: su uno
/// schema già aggiornato non proverebbero niente), e chi tocca lo <b>schema</b> invece dei dati
/// — <c>IndiciEQueryTests</c> crea e cancella indici, <c>RipristinoSvuotaLaMemoriaTests</c>
/// ripristina un backup e si porta via tutto. Quelli continuano con
/// <see cref="DatabaseDiProva"/>.</para>
///
/// <para>Le classi che lo usano stanno tutte nella stessa collection xUnit
/// (<see cref="Nome"/>), quindi girano <b>una alla volta</b>: il database è uno, e due test
/// in parallelo si pesterebbero i piedi.</para>
/// </summary>
public sealed class SchemaCondiviso : IDisposable
{
    public const string Nome = "schema-condiviso";

    private readonly DatabaseDiProva _db;

    /// <summary>Ultimo id presente a schema appena creato, tabella per tabella.</summary>
    private readonly Dictionary<string, long> _ultimoIdIniziale = new();

    /// <summary>Tabelle senza colonna <c>id</c> che nascono vuote: si svuotano e basta.</summary>
    private readonly List<string> _daSvuotare = new();

    /// <summary>
    /// Tabelle senza <c>id</c> che nascono <b>piene</b> (configurazione: <c>app_config</c>,
    /// <c>ddp_aggregation_states</c>, <c>ddp_status_transitions</c>). Di queste si tiene una
    /// copia delle righe di partenza — poche centinaia — e la pulizia le rimette esattamente
    /// com'erano: senza, un test che cambia la configurazione la lascerebbe cambiata per tutti
    /// quelli che vengono dopo, e il guasto salterebbe fuori in un test che non c'entra niente.
    /// </summary>
    private readonly Dictionary<string, List<IDictionary<string, object>>> _configIniziale = new();

    public SchemaCondiviso()
    {
        _db = new DatabaseDiProva("condiviso");
        _db.CreaSchemaCompleto();
        Fotografa();
    }

    public string ConnectionString => _db.ConnectionString;

    public MySqlConnection Apri() => _db.Apri();

    public DbService Servizio() => _db.Servizio();

    public void Esegui(string sql)
    {
        using MySqlConnection c = _db.Apri();
        c.Execute(sql);
    }

    /// <summary>
    /// Riporta il database a com'era appena creato. Da chiamare nel <b>costruttore</b> della
    /// classe di test: xUnit ne crea una istanza nuova per ogni test, quindi ognuno parte da
    /// pulito senza doversene ricordare.
    /// </summary>
    public void Pulisci()
    {
        using MySqlConnection c = _db.Apri();
        // Le chiavi esterne si spengono per non dover indovinare l'ordine di cancellazione:
        // qui si sta rimettendo il database a uno stato che era già coerente.
        c.Execute("SET FOREIGN_KEY_CHECKS = 0");
        try
        {
            foreach (var kv in _ultimoIdIniziale)
                c.Execute($"DELETE FROM `{kv.Key}` WHERE id > {kv.Value}");
            foreach (string tabella in _daSvuotare)
                c.Execute($"DELETE FROM `{tabella}`");
            foreach (var kv in _configIniziale)
                RimettiRighe(c, kv.Key, kv.Value);
        }
        finally
        {
            c.Execute("SET FOREIGN_KEY_CHECKS = 1");
        }
    }

    private void Fotografa()
    {
        using MySqlConnection c = _db.Apri();

        // `schema_migrations` resta fuori da tutto: cancellarne le righe farebbe ripartire
        // le migrazioni al primo DbService che tocca questo database.
        List<string> tabelle = c.Query<string>(@"
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE'
              AND table_name <> 'schema_migrations'").ToList();

        var conId = c.Query<string>(@"
            SELECT table_name FROM information_schema.columns
            WHERE table_schema = DATABASE() AND column_name = 'id'").ToHashSet();

        foreach (string tabella in tabelle)
        {
            if (conId.Contains(tabella))
            {
                _ultimoIdIniziale[tabella] =
                    c.ExecuteScalar<long?>($"SELECT COALESCE(MAX(id), 0) FROM `{tabella}`") ?? 0;
            }
            else if (c.ExecuteScalar<int>($"SELECT COUNT(*) FROM `{tabella}`") == 0)
            {
                _daSvuotare.Add(tabella);
            }
            else
            {
                // Senza id e già piena: è configurazione, se ne tiene una copia da rimettere.
                _configIniziale[tabella] = c.Query($"SELECT * FROM `{tabella}`")
                    .Cast<IDictionary<string, object>>()
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Rimette una tabella di configurazione esattamente com'era: via tutto e reinserimento
    /// delle righe fotografate, in un solo comando (sono poche centinaia, costa millisecondi).
    /// </summary>
    private static void RimettiRighe(
        MySqlConnection c, string tabella, List<IDictionary<string, object>> righe)
    {
        c.Execute($"DELETE FROM `{tabella}`");
        if (righe.Count == 0) return;

        string[] colonne = righe[0].Keys.ToArray();
        string elenco = string.Join(", ", colonne.Select(x => $"`{x}`"));

        var parametri = new DynamicParameters();
        var gruppi = new List<string>();
        for (int i = 0; i < righe.Count; i++)
        {
            gruppi.Add("(" + string.Join(", ", colonne.Select(x => $"@{x}_{i}")) + ")");
            foreach (string colonna in colonne)
                parametri.Add($"{colonna}_{i}", righe[i][colonna]);
        }

        c.Execute($"INSERT INTO `{tabella}` ({elenco}) VALUES {string.Join(", ", gruppi)}", parametri);
    }

    public void Dispose() => _db.Dispose();
}

/// <summary>
/// La collection che tiene insieme i test a schema condiviso. Serve a xUnit per due cose:
/// creare <see cref="SchemaCondiviso"/> una volta sola, e non far girare in parallelo classi
/// che scrivono sullo stesso database.
/// </summary>
[CollectionDefinition(SchemaCondiviso.Nome)]
public class SchemaCondivisoCollection : ICollectionFixture<SchemaCondiviso>
{
}
