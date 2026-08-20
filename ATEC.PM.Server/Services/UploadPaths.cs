using Microsoft.Extensions.Configuration;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Percorsi delle cartelle di upload.
///
/// REGOLA: i file caricati dagli utenti NON devono MAI finire sotto la cartella del
/// programma (`AppContext.BaseDirectory`, cioè `C:\ATEC_PM\Server` sul server). Ogni
/// aggiornamento sostituisce quella cartella in blocco — la vecchia diventa
/// `Server.precedente` — e si porterebbe via gli allegati. Vanno nella radice degli
/// upload (`C:\ATEC_PM\Uploads`), che sopravvive agli aggiornamenti ed è inclusa
/// nel backup completo.
/// </summary>
public static class UploadPaths
{
    /// <summary>Allegati del CMS/preventivi: serviti staticamente su /uploads/cms.</summary>
    public static string Cms(IConfiguration config) =>
        config["Uploads:CmsPath"] ?? Path.Combine(UploadsRoot(config), "cms");

    /// <summary>
    /// Allegati delle segnalazioni (screenshot dei bug). Volutamente FUORI da /uploads/cms:
    /// quella cartella è servita in anonimo, questi passano dall'endpoint autenticato.
    /// </summary>
    public static string Bugs(IConfiguration config) =>
        config["Uploads:BugsPath"] ?? Path.Combine(UploadsRoot(config), "bugs");

    /// <summary>
    /// Allegati delle chat SENZA commessa (#78). Quelle di commessa restano nella cartella
    /// della commessa (`&lt;server_path&gt;\Chat\{id}`): lì è dove il PM se li aspetta. Una chat senza
    /// commessa non ha quella cartella, e questi file non possono finire sotto il programma.
    /// </summary>
    public static string Chat(IConfiguration config) =>
        config["Uploads:ChatPath"] ?? Path.Combine(UploadsRoot(config), "chat");

    /// <summary>
    /// Radice degli upload: la cartella che contiene `cms`. Si ricava da CmsPath così una
    /// sola impostazione basta per tutti; solo se manca del tutto si ripiega (dev) sulla
    /// cartella del programma.
    /// </summary>
    private static string UploadsRoot(IConfiguration config)
    {
        string? cms = config["Uploads:CmsPath"];
        if (!string.IsNullOrWhiteSpace(cms))
        {
            string? parent = Path.GetDirectoryName(cms.TrimEnd('\\', '/'));
            if (!string.IsNullOrWhiteSpace(parent)) return parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "uploads");
    }
}
