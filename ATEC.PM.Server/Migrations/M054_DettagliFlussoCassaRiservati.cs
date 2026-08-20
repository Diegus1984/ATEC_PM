using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M054_DettagliFlussoCassaRiservati : IMigrazione
{
    public int Versione => 54;

    public string Descrizione => "sezioni commessa Dettagli/Flusso di Cassa riservate a PM/ADMIN";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Sezioni interne alla commessa riservate a PM e ADMIN. Verbali, Milestone,
        // SAL e Lavorazioni riusano le chiavi del menu principale (un solo posto da
        // cambiare); Dettagli e Flusso di Cassa hanno chiavi proprie.
        c.Execute(@"
            INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
                ('project.dettagli',     'Commessa — Dettagli',        'project', 2, 'HIDDEN'),
                ('project.flusso_cassa', 'Commessa — Flusso di Cassa', 'project', 2, 'HIDDEN')
            ON DUPLICATE KEY UPDATE min_level = VALUES(min_level), display_name = VALUES(display_name)");

        log.LogInformation("[Migration v54] Sezioni commessa Dettagli e Flusso di Cassa riservate a PM e ADMIN");
    }
}
