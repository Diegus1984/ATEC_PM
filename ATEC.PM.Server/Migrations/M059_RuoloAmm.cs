using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M059_RuoloAmm : IMigrazione
{
    public int Versione => 59;

    public string Descrizione => "ruolo AMM (amministrazione) con concessioni per ruolo";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Ruolo di reparto AMMINISTRAZIONE. La scala dei livelli è lineare e non sa
        // dire «il SAL sì, il resto del livello 2 no»: si aggiunge quindi un secondo
        // criterio, le concessioni per ruolo (auth_role_features). Un ruolo con
        // access_mode = 'GRANTS' NON eredita più niente dal livello: vede solo le
        // funzioni che gli sono state concesse (lista bianca).
        int hasAccessMode = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'auth_levels'
              AND COLUMN_NAME = 'access_mode'");
        if (hasAccessMode == 0)
        {
            c.Execute("ALTER TABLE auth_levels ADD COLUMN access_mode VARCHAR(10) NOT NULL DEFAULT 'LEVEL'");
        }

        // level_value era UNIQUE: un ruolo di reparto deve poter stare sullo stesso
        // rango di un altro (AMM parte da 0 come un tecnico). L'indice si chiama
        // 'level_value' se creato dallo schema originale — si legge da information_schema
        // perché su installazioni vecchie potrebbe avere un altro nome.
        var uniqueIndexes = c.Query<string>(@"
            SELECT DISTINCT INDEX_NAME FROM information_schema.STATISTICS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'auth_levels'
              AND COLUMN_NAME = 'level_value' AND NON_UNIQUE = 0").ToList();
        foreach (string indexName in uniqueIndexes)
            c.Execute($"ALTER TABLE auth_levels DROP INDEX `{indexName}`");

        c.Execute(@"CREATE TABLE IF NOT EXISTS auth_role_features (
            id INT AUTO_INCREMENT PRIMARY KEY,
            role_name VARCHAR(30) NOT NULL,
            feature_key VARCHAR(100) NOT NULL,
            access VARCHAR(10) NOT NULL DEFAULT 'FULL',
            UNIQUE KEY uk_role_feature (role_name, feature_key)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // I dati economici del SAL erano riservati «dal livello PM in su» con un
        // confronto cablato: diventano una funzione, così si possono concedere
        // all'amministrazione senza promuoverla a PM. Livello 2 = nessun cambiamento
        // per i ruoli esistenti.
        c.Execute(@"
            INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('sal.economics', 'SAL — Dati economici', 'data', 2, 'HIDDEN')
            ON DUPLICATE KEY UPDATE display_name = VALUES(display_name)");

        c.Execute(@"
            INSERT INTO auth_levels (level_value, role_name, display_name, sort_order, access_mode)
            VALUES (0, 'AMM', 'Amministrazione', 4, 'GRANTS')
            ON DUPLICATE KEY UPDATE access_mode = 'GRANTS', display_name = VALUES(display_name)");

        // Segnalazioni e SAL piene, Clienti in sola lettura (decisione dell'utente
        // del 03/08/2026). Il resto — Dashboard, Commesse, Timesheet compresi — resta fuori.
        c.Execute(@"
            INSERT INTO auth_role_features (role_name, feature_key, access) VALUES
                ('AMM', 'nav.bug_reports', 'FULL'),
                ('AMM', 'nav.sal',         'FULL'),
                ('AMM', 'sal.economics',   'FULL'),
                ('AMM', 'nav.clienti',     'READ')
            ON DUPLICATE KEY UPDATE access = VALUES(access)");

        log.LogInformation("[Migration v59] Ruolo AMM creato con concessioni Segnalazioni/SAL (piene) e Clienti (sola lettura)");
    }
}
