using ATEC.PM.Server.Services;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATEC.PM.Tests.Infrastruttura;

/// <summary>
/// Da dove viene la destinazione dei pacchetti di backup completo.
///
/// <para>L'ordine è: impostazione della pagina Backup (app_config) → appsettings.json →
/// cartella locale predefinita. Sbagliarlo è silenzioso e costoso: i pacchetti
/// finirebbero in un posto diverso da quello che la pagina DICE, e la copia «fuori dal
/// server» che uno crede di avere non esisterebbe — se ne accorgerebbe chi un giorno
/// cerca il backup per un ripristino.</para>
/// </summary>
[Collection(SchemaCondiviso.Nome)]
public class DestinazionePacchettiTests
{
    private readonly SchemaCondiviso _schema;

    // Si prova da dove esce la destinazione dei pacchetti, non lo schema:
    // lo schema condiviso basta e avanza, e costa millisecondi invece di secondi.
    public DestinazionePacchettiTests(SchemaCondiviso schema)
    {
        _schema = schema;
        _schema.Pulisci();
    }

    private static FullBackupService Servizio(SchemaCondiviso db, Dictionary<string, string?> config)
    {
        config["ConnectionStrings:Default"] = db.ConnectionString;
        IConfiguration cfg = new ConfigurationBuilder().AddInMemoryCollection(config).Build();
        var cache = new AnagraficheCache(NullLogger<AnagraficheCache>.Instance);
        return new FullBackupService(db.Servizio(), cfg, NullLogger<FullBackupService>.Instance,
            cache, new FeatureAccessService(db.Servizio()),
            new NetworkShareConnector(NullLogger<NetworkShareConnector>.Instance));
    }

    [FactRichiedeMySql]
    public void SenzaImpostazioni_valeLaCartellaLocalePredefinita()
    {
        var servizio = Servizio(_schema, new Dictionary<string, string?> { ["Backup:Path"] = @"C:\Prova_Backups" });

        (string percorso, string origine) = servizio.DestinazionePacchetti();

        Assert.Equal("predefinita", origine);
        Assert.Equal(@"C:\Prova_Backups\Pacchetti", percorso);
    }

    [FactRichiedeMySql]
    public void ConAppsettings_valeAppsettings()
    {
        var servizio = Servizio(_schema, new Dictionary<string, string?>
        {
            ["Backup:PackagePath"] = @"\\nas\backup\pm",
        });

        (string percorso, string origine) = servizio.DestinazionePacchetti();

        Assert.Equal("appsettings", origine);
        Assert.Equal(@"\\nas\backup\pm", percorso);
    }

    /// <summary>
    /// La pagina vince su appsettings: è il punto della funzione — cambiare NAS senza
    /// mettere le mani nei file del server. Se un refactor invertisse l'ordine, la
    /// pagina mostrerebbe un percorso e il servizio scriverebbe nell'altro.
    /// </summary>
    [FactRichiedeMySql]
    public void LaPaginaVinceSuAppsettings()
    {
        using (var c = _schema.Servizio().Open())
            c.Execute(@"INSERT INTO app_config (config_key, config_value)
                        VALUES (@K, @V)",
                new { K = FullBackupService.ChiavePercorso, V = @"\\pc-nuovo\backup" });

        var servizio = Servizio(_schema, new Dictionary<string, string?>
        {
            ["Backup:PackagePath"] = @"\\nas-vecchio\backup",
        });

        (string percorso, string origine) = servizio.DestinazionePacchetti();

        Assert.Equal("pagina", origine);
        Assert.Equal(@"\\pc-nuovo\backup", percorso);
    }

    /// <summary>
    /// La pulizia a tempo elimina i backup oltre la soglia ma tiene SEMPRE le copie più
    /// recenti, qualunque età abbiano: se il notturno resta fermo per mesi, l'anzianità
    /// da sola non deve poter cancellare gli ultimi backup rimasti.
    /// </summary>
    [FactRichiedeMySql]
    public void PulisciBackupVecchi_EliminaIVecchiMaTieneLaScorta()
    {

        string dirPacchetti = Path.Combine(Path.GetTempPath(), $"atec_puli_zip_{Guid.NewGuid():N}");
        string dirDump = Path.Combine(Path.GetTempPath(), $"atec_puli_sql_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dirPacchetti);
        Directory.CreateDirectory(dirDump);
        try
        {
            var servizio = Servizio(_schema, new Dictionary<string, string?>
            {
                ["Backup:PackagePath"] = dirPacchetti,
                ["Backup:Path"] = dirDump,
                ["Backup:PackageKeep"] = "2",
                ["Backup:GiorniConservazione"] = "60",
            });

            // 4 pacchetti TUTTI vecchi di 90 giorni: oltre soglia, ma i 2 più recenti
            // sono la scorta e devono restare.
            for (int i = 0; i < 4; i++)
            {
                string f = Path.Combine(dirPacchetti, $"atec_pm_completo_2026010{i + 1}_020000.zip");
                File.WriteAllText(f, "zip finto");
                File.SetCreationTime(f, DateTime.Now.AddDays(-90 - i));
            }
            // 7 dump: 6 vecchi di 90 giorni + 1 recente. Scorta fissa 5 → restano i 5
            // più recenti (il fresco + 4 vecchi), gli altri 2 se ne vanno.
            for (int i = 0; i < 6; i++)
            {
                string f = Path.Combine(dirDump, $"atec_pm_auto_2026010{i + 1}_020000.sql");
                File.WriteAllText(f, "dump finto");
                File.SetCreationTime(f, DateTime.Now.AddDays(-90 - i));
            }
            string fresco = Path.Combine(dirDump, "atec_pm_auto_fresco.sql");
            File.WriteAllText(fresco, "dump fresco");

            servizio.PulisciBackupVecchi();

            Assert.Equal(2, Directory.GetFiles(dirPacchetti, "*.zip").Length);
            Assert.Equal(5, Directory.GetFiles(dirDump, "*.sql").Length);
            Assert.True(File.Exists(fresco), "Il dump recente non va mai toccato.");
        }
        finally
        {
            Directory.Delete(dirPacchetti, recursive: true);
            Directory.Delete(dirDump, recursive: true);
        }
    }

    /// <summary>Un percorso locale scrivibile passa la prova; uno impossibile la fallisce con un errore parlante.</summary>
    [FactRichiedeMySql]
    public void ProvaDestinazione_localeScrivibileEImpossibile()
    {
        var servizio = Servizio(_schema, new Dictionary<string, string?>());

        string buona = Path.Combine(Path.GetTempPath(), $"atec_prova_dest_{Guid.NewGuid():N}");
        try
        {
            Assert.Null(servizio.ProvaDestinazione(buona, null, null));
        }
        finally
        {
            if (Directory.Exists(buona)) Directory.Delete(buona, recursive: true);
        }

        // Percorso impossibile GARANTITO: una directory «dentro» un file esistente.
        // Una lettera di unità inventata (es. Q:) non lo è — in azienda le share
        // mappate usano proprio quelle lettere e il test scriverebbe lì davvero.
        string fileDiSbarramento = Path.Combine(Path.GetTempPath(), $"atec_sbarra_{Guid.NewGuid():N}.txt");
        File.WriteAllText(fileDiSbarramento, "non sono una cartella");
        try
        {
            string? errore = servizio.ProvaDestinazione(
                Path.Combine(fileDiSbarramento, "sotto"), null, null);
            Assert.NotNull(errore);
        }
        finally
        {
            File.Delete(fileDiSbarramento);
        }
    }
}
