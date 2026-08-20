using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M056_VociMenuRisorseSegnalazioni : IMigrazione
{
    public int Versione => 56;

    public string Descrizione => "registrate le voci di menu Risorse e Segnalazioni in auth_features";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Voci di menu che il codice usava SENZA registrarle in auth_features: per la
        // regola "feature non registrata = accesso libero" erano visibili a chiunque e
        // non comparivano nella pagina «Permessi». Si registrano al livello 0 per NON
        // cambiare quello che vede la gente oggi: da qui in avanti il livello si alza
        // (o si abbassa) dalla pagina, che è il punto della registrazione.
        c.Execute(@"
            INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
                ('nav.risorse',     'Risorse (planner e ferie)', 'navigation', 0, 'HIDDEN'),
                ('nav.bug_reports', 'Segnalazioni',              'navigation', 0, 'HIDDEN')
            ON DUPLICATE KEY UPDATE display_name = VALUES(display_name)");

        log.LogInformation("[Migration v56] Voci Risorse e Segnalazioni registrate nei permessi");
    }
}
