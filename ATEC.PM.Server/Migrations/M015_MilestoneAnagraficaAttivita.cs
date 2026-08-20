using ATEC.PM.Server.Services;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v15: moduli "Anagrafica attività" (catalogo globale activity_catalog) + "Milestone"
// (pianificazione per-commessa project_milestones). Crea le tabelle (idempotente), seeda il
// catalogo standard una-tantum e registra la feature key di navigazione della nuova pagina.
public sealed class M015_MilestoneAnagraficaAttivita : IMigrazione
{
    public int Versione => 15;

    public string Descrizione => "activity_catalog + project_milestones + nav.anagrafica_attivita";

    public void Applica(MySqlConnection c, ILogger log)
    {
        MilestonesDbService.InitTables(c, log);
        MilestonesDbService.SeedCatalog(c, log);

        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.anagrafica_attivita', 'Anagrafica Attività', 'navigation', 2, 'HIDDEN')");

        log.LogInformation("[Migration v15] Tabelle activity_catalog + project_milestones, seed catalogo, feature key nav.anagrafica_attivita");
    }
}
