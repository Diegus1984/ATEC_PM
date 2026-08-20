using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M053_SezionePmRiservata : IMigrazione
{
    public int Versione => 53;

    public string Descrizione => "sezione PM e configurazioni DDP riservate a PM/ADMIN";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Sezione «PM» e configurazioni DDP riservate a PM e ADMIN (livello >= 2).
        // Molte di queste voci non erano registrate in auth_features e, per la regola
        // "feature non registrata = accesso libero", risultavano visibili a chiunque.
        c.Execute(@"
            INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
                ('nav.mom',              'Verbali e Note MoM',   'navigation', 2, 'HIDDEN'),
                ('nav.gestore_ddp',      'Gestore DDP',          'navigation', 2, 'HIDDEN'),
                ('nav.checklist',        'Check list',           'navigation', 2, 'HIDDEN'),
                ('nav.milestones',       'Milestones',           'navigation', 2, 'HIDDEN'),
                ('nav.work_requests',    'Lavorazioni',          'navigation', 2, 'HIDDEN'),
                ('nav.sal',              'SAL / Fatturazione',   'navigation', 2, 'HIDDEN'),
                ('nav.scadenze',         'Scadenze',             'navigation', 2, 'HIDDEN'),
                ('nav.ddp_destinazioni', 'Conf. DDP',            'navigation', 2, 'HIDDEN'),
                ('nav.ddp_aggregazioni', 'Aggregazioni DDP',     'navigation', 2, 'HIDDEN')
            ON DUPLICATE KEY UPDATE min_level = VALUES(min_level), display_name = VALUES(display_name)");

        log.LogInformation("[Migration v53] Voci PM e Conf./Aggregazioni DDP riservate a PM e ADMIN");
    }
}
