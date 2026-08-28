using ATEC.PM.Server.Services;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Il ripristino da backup riscrive l'intero database <b>mentre il gestionale è acceso</b>: tutto
/// ciò che il processo tiene in memoria sul conto del database — anagrafiche in cache e regole dei
/// permessi — da quel momento parla di un archivio che non esiste più.
///
/// <para>Il caso che questi test bloccano è quello che <b>fallisce a metà</b>: le tabelle sono già
/// state svuotate, l'import si interrompe, e se lo svuotamento della memoria fosse in fondo al
/// metodo non verrebbe mai eseguito. Il gestionale resterebbe acceso a decidere i permessi con le
/// regole del database sostituito, senza scadenza e senza che niente lo dica: si sistema solo
/// riavviando il servizio, e nessuno sa che va fatto.</para>
/// </summary>
public class RipristinoSvuotaLaMemoriaTests
{
    /// <summary>Un file di ripristino che si rompe a metà lettura: lo zip corrotto, il disco che sparisce.</summary>
    private sealed class LettoreCheEsplode : TextReader
    {
        private int _righe;
        public override string? ReadLine()
        {
            if (++_righe > 2) throw new IOException("pacchetto interrotto (finto)");
            return "-- riga innocua";
        }
    }

    /// <summary>Visibile anche alla classe gemella qui sotto, che prova il caso riuscito.</summary>
    internal static FullBackupService Servizio(DatabaseDiProva db, AnagraficheCache cache, FeatureAccessService access)
    {
        IConfiguration cfg = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["ConnectionStrings:Default"] = db.ConnectionString }).Build();

        return new FullBackupService(db.Servizio(), cfg, NullLogger<FullBackupService>.Instance, cache, access,
            new NetworkShareConnector(NullLogger<NetworkShareConnector>.Instance));
    }
    [FactRichiedeMySql]
    public void RipristinoFallitoAmeta_svuotaComunqueLaMemoria()
    {
        using var db = new DatabaseDiProva("ripristino_rotto");
        db.CreaSchemaCompleto();

        var cache = new AnagraficheCache(NullLogger<AnagraficheCache>.Instance);
        FeatureAccessService access = new(db.Servizio());

        // Memoria «calda»: un'anagrafica in cache e le regole dei permessi già lette.
        cache.Leggi(Anagrafica.AggregazioniDdp, () => "roba del database di prima");
        int livelloAdminPrima = access.GetLevelForRole("ADMIN");
        Assert.True(livelloAdminPrima > 0, "Il database di prova non ha il ruolo ADMIN: il test non proverebbe niente.");

        // Il ripristino svuota tutte le tabelle e poi si rompe.
        Assert.ThrowsAny<Exception>(() => Servizio(db, cache, access).RipristinaDatabase(new LettoreCheEsplode()));

        // Le tabelle SONO state svuotate: è lo stato in cui il difetto si vedeva.
        using (MySqlConnection c = db.Apri())
            Assert.Equal(0, c.ExecuteScalar<int>("SELECT COUNT(*) FROM auth_levels"));

        // …e la memoria non racconta più il database di prima.
        Assert.Equal("riletto", cache.Leggi(Anagrafica.AggregazioniDdp, () => "riletto"));
        Assert.Equal(0, access.GetLevelForRole("ADMIN"));
    }
}

/// <summary>
/// Il ripristino che va a buon fine: la memoria delle anagrafiche si svuota lo stesso.
///
/// <para>Classe a parte per una ragione di tempo: xUnit parallelizza le CLASSI, e qui ogni
/// test ripristina un backup su un database intero.</para>
/// </summary>
public class RipristinoRiuscitoSvuotaLaMemoriaTests
{
    /// <summary>Lo stesso, sul percorso normale: ripristino che arriva in fondo senza errori.</summary>
    [FactRichiedeMySql]
    public void RipristinoRiuscito_svuotaLaMemoria()
    {
        using var db = new DatabaseDiProva("ripristino_ok");
        db.CreaSchemaCompleto();

        var cache = new AnagraficheCache(NullLogger<AnagraficheCache>.Instance);
        FeatureAccessService access = new(db.Servizio());

        cache.Leggi(Anagrafica.TransizioniDdp, () => "matrice vecchia");
        Assert.True(access.GetLevelForRole("ADMIN") > 0);

        // Un pacchetto valido ma vuoto: svuota e non reinserisce niente.
        RipristinoSvuotaLaMemoriaTests.Servizio(db, cache, access).RipristinaDatabase(new StringReader("-- pacchetto vuoto\n"));

        Assert.Equal("riletta", cache.Leggi(Anagrafica.TransizioniDdp, () => "riletta"));
        Assert.Equal(0, access.GetLevelForRole("ADMIN"));
    }
}
