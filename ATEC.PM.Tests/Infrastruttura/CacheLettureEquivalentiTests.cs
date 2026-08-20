using ATEC.PM.Server.Services;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// La cache delle anagrafiche legge le tabelle <b>in blocco</b> e poi risponde dalla memoria,
/// mentre senza cache si legge <b>mirato</b> con un WHERE. Sono due query diverse, e queste prove
/// servono a garantire che diano la stessa risposta — comprese le sfumature che si perdono
/// facilmente passando da un WHERE a un raggruppamento in memoria: maiuscole delle chiavi, righe
/// vuote, elenchi inesistenti.
///
/// <para>Senza questi test la cache sarebbe «più veloce e forse diversa», che sulle transizioni di
/// stato vuol dire lasciar passare un avanzamento vietato (o bloccarne uno legittimo) nel punto in
/// cui si aggiudica una RDO.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class CacheLettureEquivalentiTests
{
    private readonly SchemaCondiviso _schema;

    /// <summary>
    /// xUnit costruisce una istanza per ogni test: qui si riporta il database condiviso a
    /// com'era appena creato — configurazione compresa, che questi test cambiano apposta.
    /// </summary>
    public CacheLettureEquivalentiTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }
    private static AnagraficheCache NuovaCache() => new(NullLogger<AnagraficheCache>.Instance);

    // ── aggregazioni DDP ──────────────────────────────────────────────────────

    [FactRichiedeMySql]
    public void Aggregazioni_conEsenzaCache_stessoRisultato()
    {
        using MySqlConnection c = _schema.Apri();

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO ddp_aggregations (code, name, description, kind, sort_order, is_active)
            VALUES ('Z1', 'Prova', '', 'STATUS', 99, 1);
            SELECT LAST_INSERT_ID()");
        c.Execute("INSERT INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A,'ANN'),(@A,'SOSP')",
            new { A = id });

        string[] senza = DdpAggregationSet.Load(c, "Z1");
        string[] con = DdpAggregationSet.Load(c, "Z1", NuovaCache());

        Assert.Equal(senza.OrderBy(x => x), con.OrderBy(x => x));
        Assert.Equal(2, con.Length);
    }

    /// <summary>Aggregazione senza righe: entrambe le strade devono dare l'elenco VUOTO, che vale «nessuna esclusione».</summary>
    [FactRichiedeMySql]
    public void AggregazioneInesistente_daElencoVuotoInEntrambiIcasi()
    {
        using MySqlConnection c = _schema.Apri();

        Assert.Empty(DdpAggregationSet.Load(c, "NONESISTE"));
        Assert.Empty(DdpAggregationSet.Load(c, "NONESISTE", NuovaCache()));
    }

    [FactRichiedeMySql]
    public void Aggregazioni_dopoLaModifica_laCacheInvalidataRileggeIlNuovo()
    {
        using MySqlConnection c = _schema.Apri();
        AnagraficheCache cache = NuovaCache();

        int id = c.ExecuteScalar<int>(@"
            INSERT INTO ddp_aggregations (code, name, description, kind, sort_order, is_active)
            VALUES ('Z2', 'Prova', '', 'STATUS', 99, 1);
            SELECT LAST_INSERT_ID()");
        c.Execute("INSERT INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A,'ANN')", new { A = id });

        Assert.Single(DdpAggregationSet.Load(c, "Z2", cache));

        c.Execute("INSERT INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A,'SOSP')", new { A = id });
        Assert.Single(DdpAggregationSet.Load(c, "Z2", cache));   // ancora il valore in memoria: è voluto

        cache.Invalida(Anagrafica.AggregazioniDdp);              // ciò che fa il controller dopo il commit
        Assert.Equal(2, DdpAggregationSet.Load(c, "Z2", cache).Length);
    }

    // ── matrice delle transizioni ─────────────────────────────────────────────

    /// <summary>
    /// Il caso che conta: la stessa transizione deve essere ammessa (o vietata) allo stesso modo
    /// con e senza cache. Si prova sulla matrice VERA del database, non su una inventata.
    /// </summary>
    [FactRichiedeMySql]
    public void Transizioni_conEsenzaCache_stessoVerdettoSuTuttaLaMatrice()
    {
        using MySqlConnection c = _schema.Apri();
        AnagraficheCache cache = NuovaCache();

        var righe = c.Query<(string DdpType, string FromKey, string ToKey)>(
            "SELECT ddp_type AS DdpType, from_key AS FromKey, to_key AS ToKey FROM ddp_status_transitions").ToList();
        Assert.NotEmpty(righe);   // se la matrice fosse vuota il test non proverebbe niente

        var stati = c.Query<string>("SELECT status_key FROM ddp_statuses").ToList();
        Assert.NotEmpty(stati);

        int provati = 0;
        foreach ((string tipo, string da, _) in righe)
        {
            foreach (string a in stati)
            {
                string? senza = DdpTransitionService.Validate(c, tipo, da, a);
                string? con = DdpTransitionService.Validate(c, tipo, da, a, cache);
                Assert.Equal(senza, con);
                provati++;
            }
        }

        Assert.True(provati > 20, $"Solo {provati} transizioni provate: la matrice di prova è troppo piccola.");
    }

    /// <summary>
    /// Lo stato di partenza vuoto (riga nuova) usa la finestra «INIZIO»: passando dal WHERE al
    /// raggruppamento in memoria è il caso che si perde per primo.
    /// </summary>
    [FactRichiedeMySql]
    public void Transizioni_statoDiPartenzaVuoto_stessoVerdetto()
    {
        using MySqlConnection c = _schema.Apri();
        AnagraficheCache cache = NuovaCache();

        foreach (string tipo in new[] { DdpTransitionService.TypeCommercial, DdpTransitionService.TypeOfficina })
        {
            foreach (string a in c.Query<string>("SELECT status_key FROM ddp_statuses"))
            {
                Assert.Equal(
                    DdpTransitionService.Validate(c, tipo, null, a),
                    DdpTransitionService.Validate(c, tipo, null, a, cache));
            }
        }
    }

    /// <summary>
    /// Stato terminale: nella matrice è una riga con <c>to_key</c> VUOTO, che significa «da qui
    /// non si va da nessuna parte». Se il raggruppamento in memoria buttasse via quelle righe, la
    /// cache direbbe «(tipo, stato) non governato» e lascerebbe passare qualunque avanzamento.
    /// </summary>
    [FactRichiedeMySql]
    public void Transizioni_statoTerminale_restaTerminaleAncheDallaCache()
    {
        using MySqlConnection c = _schema.Apri();

        c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type='COMMERCIAL' AND from_key='ZZ'");
        c.Execute(@"INSERT INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    VALUES ('COMMERCIAL', 'ZZ', '')");

        string? senza = DdpTransitionService.Validate(c, DdpTransitionService.TypeCommercial, "ZZ", "DISP");
        string? con = DdpTransitionService.Validate(c, DdpTransitionService.TypeCommercial, "ZZ", "DISP", NuovaCache());

        Assert.Equal(senza, con);
        Assert.NotNull(con);
        Assert.Contains("terminale", con!);
    }

    /// <summary>Chiavi scritte in minuscolo: il WHERE di MySQL non distingue le maiuscole, il dizionario in memoria deve fare lo stesso.</summary>
    [FactRichiedeMySql]
    public void Transizioni_chiaviInMinuscolo_stessoVerdetto()
    {
        using MySqlConnection c = _schema.Apri();

        c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    VALUES ('COMMERCIAL', 'yy', 'disp')");

        string? senza = DdpTransitionService.Validate(c, DdpTransitionService.TypeCommercial, "YY", "DISP");
        string? con = DdpTransitionService.Validate(c, DdpTransitionService.TypeCommercial, "YY", "DISP", NuovaCache());

        Assert.Equal(senza, con);
    }
}
