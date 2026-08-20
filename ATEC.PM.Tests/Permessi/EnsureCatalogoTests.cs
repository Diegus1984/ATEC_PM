using ATEC.PM.Server.Services;
using ATEC.PM.Shared;
using ATEC.PM.Tests.Infrastruttura;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Permessi;

/// <summary>
/// EnsureCatalogo (rebuild §12, passo 2): <c>auth_features</c> è una proiezione del catalogo
/// unico. Qui si prova il patto intero su un database vero: allineamento completo e idempotente
/// dopo lo schema, chiave cancellata che rinasce «solo Admin», alias migrato una volta sola coi
/// grant al seguito, ritiro e ripescaggio, micro materializzati come chiavi figlie.
/// </summary>
public sealed class SchemaCatalogoFixture : IDisposable
{
    // Lazy: la fixture non deve aprire MySQL quando i test si saltano (FactRichiedeMySql).
    private readonly Lazy<DatabaseDiProva> _db = new(() =>
    {
        var d = new DatabaseDiProva("catalogo");
        d.CreaSchemaCompleto(); // passa anche da EnsureCatalogo: il DB nasce già allineato
        return d;
    });

    public DatabaseDiProva Db => _db.Value;

    public void Dispose()
    {
        if (_db.IsValueCreated) _db.Value.Dispose();
    }
}

public class EnsureCatalogoTests : IClassFixture<SchemaCatalogoFixture>
{
    private readonly SchemaCatalogoFixture _schema;

    public EnsureCatalogoTests(SchemaCatalogoFixture schema) => _schema = schema;

    private static CatalogoPermessiSync.Esito Allinea(MySqlConnection c) =>
        CatalogoPermessiSync.Allinea(c, NullLogger.Instance);

    [FactRichiedeMySql]
    public void Dopo_lo_schema_completo_il_catalogo_e_allineato_e_idempotente()
    {
        using MySqlConnection c = _schema.Db.Apri();

        // Ogni chiave primaria del catalogo sta in tabella (l'ha già fatto InitDatabase).
        var inTabella = c.Query<string>("SELECT feature_key FROM auth_features")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var mancanti = PermessiCatalogo.VociPrimarie()
            .Select(v => v.Chiave!)
            .Where(k => !inTabella.Contains(k))
            .ToList();
        Assert.True(mancanti.Count == 0, "chiavi del catalogo assenti da auth_features: " + string.Join(", ", mancanti));

        // La chiave morta è ritirata, non cancellata.
        DateTime? ritirataIl = c.ExecuteScalar<DateTime?>(
            "SELECT retired_at FROM auth_features WHERE feature_key = 'data.hourly_cost'");
        Assert.NotNull(ritirataIl);

        // Un secondo giro non ha niente da fare, e su un database appena creato non ci sono
        // orfane: se qui compare qualcosa, una chiave seminata dalle migrazioni manca al catalogo.
        CatalogoPermessiSync.Esito esito = Allinea(c);
        Assert.True(esito.NienteDaFare,
            $"secondo giro non idempotente: nuove={esito.Nuove} rinominate={esito.Rinominate} " +
            $"ritirate={esito.Ritirate} ripescate={esito.Ripescate} etichette={esito.EtichetteAggiornate}");
        Assert.True(esito.Orfane.Count == 0, "orfane su schema appena creato: " + string.Join(", ", esito.Orfane));
    }

    [FactRichiedeMySql]
    public void Una_chiave_cancellata_rinasce_solo_admin()
    {
        using MySqlConnection c = _schema.Db.Apri();
        c.Execute("DELETE FROM auth_features WHERE feature_key = 'nav.bilancio'");

        CatalogoPermessiSync.Esito esito = Allinea(c);

        Assert.Equal(1, esito.Nuove);
        var riga = c.QuerySingle<(string Label, int MinLevel, DateTime? RitirataIl)>(
            "SELECT display_name, min_level, retired_at FROM auth_features WHERE feature_key = 'nav.bilancio'");
        Assert.Equal("Bilancio", riga.Label);
        Assert.Equal(3, riga.MinLevel); // nata «solo Admin»: un rollback al motore vecchio non apre niente
        Assert.Null(riga.RitirataIl);
    }

    [FactRichiedeMySql]
    public void Ritirata_a_mano_viene_ripescata_se_il_catalogo_la_dichiara_viva()
    {
        using MySqlConnection c = _schema.Db.Apri();
        c.Execute("UPDATE auth_features SET retired_at = NOW() WHERE feature_key = 'nav.clienti'");

        CatalogoPermessiSync.Esito esito = Allinea(c);

        Assert.Equal(1, esito.Ripescate);
        Assert.Null(c.ExecuteScalar<DateTime?>(
            "SELECT retired_at FROM auth_features WHERE feature_key = 'nav.clienti'"));
        // La morta vera resta ritirata.
        Assert.NotNull(c.ExecuteScalar<DateTime?>(
            "SELECT retired_at FROM auth_features WHERE feature_key = 'data.hourly_cost'"));
    }

    [FactRichiedeMySql]
    public void Alias_migra_i_grant_una_volta_sola_e_i_micro_si_materializzano()
    {
        using MySqlConnection c = _schema.Db.Apri();
        int dipendente = c.ExecuteScalar<int>("SELECT MIN(id) FROM employees");

        // Un catalogo di prova: una voce rinominata (alias) col micro prices dichiarato.
        var albero = new List<VoceCatalogo>
        {
            new()
            {
                Kind = "sezione", Label = "Prova",
                Figli = new List<VoceCatalogo>
                {
                    new()
                    {
                        Kind = "voce", Chiave = "nav.prova_nuova", Label = "Prova nuova",
                        Alias = "nav.prova_vecchia", Micros = new List<string> { "prices" },
                    },
                },
            },
        };

        try
        {
            c.Execute(@"INSERT INTO auth_features (feature_key, display_name, category, min_level)
                        VALUES ('nav.prova_vecchia', 'Prova vecchia', 'navigation', 3)");
            c.Execute(@"INSERT INTO employee_feature_access (employee_id, feature_key, access, origin)
                        VALUES (@Id, 'nav.prova_vecchia', 'FULL', 'MANO'),
                               (@Id, 'nav.prova_nuova',  'READ', 'MANO')", new { Id = dipendente });
            c.Execute(@"INSERT INTO auth_class_features (class_name, feature_key, access)
                        VALUES ('TECH', 'nav.prova_vecchia', 'FULL')");

            CatalogoPermessiSync.Esito esito = CatalogoPermessiSync.Allinea(c, NullLogger.Instance, albero);

            Assert.Equal(1, esito.Rinominate);

            // auth_features: la vecchia non c'è più, la nuova sì, col nome del catalogo.
            Assert.Equal(0, c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM auth_features WHERE feature_key = 'nav.prova_vecchia'"));
            Assert.Equal("Prova nuova", c.ExecuteScalar<string>(
                "SELECT display_name FROM auth_features WHERE feature_key = 'nav.prova_nuova'"));

            // Il micro è una chiave registrata (jolly e scheda lo vedono, §12.8.3).
            Assert.Equal("Prova nuova — vede prezzi", c.ExecuteScalar<string>(
                "SELECT display_name FROM auth_features WHERE feature_key = 'nav.prova_nuova.prices'"));

            // Grant: la persona aveva GIÀ la chiave nuova → la riga vecchia ridondante sparisce
            // e sopravvive quella esistente; la classe viene rinominata.
            var righe = c.Query<(string Chiave, string Access)>(
                "SELECT feature_key, access FROM employee_feature_access WHERE employee_id = @Id AND feature_key LIKE 'nav.prova%'",
                new { Id = dipendente }).ToList();
            Assert.Single(righe);
            Assert.Equal(("nav.prova_nuova", "READ"), righe[0]);
            Assert.Equal("nav.prova_nuova", c.ExecuteScalar<string>(
                "SELECT feature_key FROM auth_class_features WHERE class_name = 'TECH' AND feature_key LIKE 'nav.prova%'"));

            // Secondo giro: l'alias tace (la vecchia non esiste più), niente da fare sui grant.
            CatalogoPermessiSync.Esito secondo = CatalogoPermessiSync.Allinea(c, NullLogger.Instance, albero);
            Assert.Equal(0, secondo.Rinominate);
            Assert.Equal(0, secondo.Nuove);
        }
        finally
        {
            // Il database è condiviso dagli altri test della classe: le chiavi di prova
            // non devono restare (diventerebbero orfane nel test di idempotenza).
            c.Execute("DELETE FROM auth_features WHERE feature_key LIKE 'nav.prova%'");
            c.Execute("DELETE FROM employee_feature_access WHERE feature_key LIKE 'nav.prova%'");
            c.Execute("DELETE FROM auth_class_features WHERE feature_key LIKE 'nav.prova%'");
        }
    }
}
