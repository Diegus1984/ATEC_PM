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
public class DestinazionePacchettiTests
{
    private static FullBackupService Servizio(DatabaseDiProva db, Dictionary<string, string?> config)
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
        using var db = new DatabaseDiProva("dest_pacchetti_default");
        db.CreaSchemaCompleto();
        var servizio = Servizio(db, new Dictionary<string, string?> { ["Backup:Path"] = @"C:\Prova_Backups" });

        (string percorso, string origine) = servizio.DestinazionePacchetti();

        Assert.Equal("predefinita", origine);
        Assert.Equal(@"C:\Prova_Backups\Pacchetti", percorso);
    }

    [FactRichiedeMySql]
    public void ConAppsettings_valeAppsettings()
    {
        using var db = new DatabaseDiProva("dest_pacchetti_file");
        db.CreaSchemaCompleto();
        var servizio = Servizio(db, new Dictionary<string, string?>
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
        using var db = new DatabaseDiProva("dest_pacchetti_pagina");
        db.CreaSchemaCompleto();
        using (var c = db.Servizio().Open())
            c.Execute(@"INSERT INTO app_config (config_key, config_value)
                        VALUES (@K, @V)",
                new { K = FullBackupService.ChiavePercorso, V = @"\\pc-nuovo\backup" });

        var servizio = Servizio(db, new Dictionary<string, string?>
        {
            ["Backup:PackagePath"] = @"\\nas-vecchio\backup",
        });

        (string percorso, string origine) = servizio.DestinazionePacchetti();

        Assert.Equal("pagina", origine);
        Assert.Equal(@"\\pc-nuovo\backup", percorso);
    }

    /// <summary>Un percorso locale scrivibile passa la prova; uno impossibile la fallisce con un errore parlante.</summary>
    [FactRichiedeMySql]
    public void ProvaDestinazione_localeScrivibileEImpossibile()
    {
        using var db = new DatabaseDiProva("dest_pacchetti_prova");
        db.CreaSchemaCompleto();
        var servizio = Servizio(db, new Dictionary<string, string?>());

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
