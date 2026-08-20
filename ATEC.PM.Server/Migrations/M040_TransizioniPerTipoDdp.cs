using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v40: matrice transizioni PER TIPO di distinta (regola 20/07/2026: nella DDP commerciale
// DC non deve esistere — il materiale commerciale si acquista e basta — mentre in officina sì,
// incluso DO→DC per dirottare alla produzione interna un pezzo "da ordinare").
// ddp_type ∈ {COMMERCIAL, OFFICINA}; la matrice v39 (senza tipo) viene duplicata nei due tipi.
// Nuova riga speciale from_key='INIZIO' = finestra di partenza per le righe senza stato
// (COMMERCIAL senza DC); se assente, fallback permissivo come per gli altri stati.
public sealed class M040_TransizioniPerTipoDdp : IMigrazione
{
    public int Versione => 40;

    public string Descrizione => "matrice transizioni per tipo DDP + finestra INIZIO (commerciale senza DC)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool hasType = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'ddp_status_transitions'
              AND COLUMN_NAME = 'ddp_type'") > 0;
        if (!hasType)
        {
            c.Execute("ALTER TABLE ddp_status_transitions ADD COLUMN ddp_type VARCHAR(20) NOT NULL DEFAULT '' FIRST");
            c.Execute("ALTER TABLE ddp_status_transitions DROP PRIMARY KEY, ADD PRIMARY KEY (ddp_type, from_key, to_key)");
        }

        // Duplica la matrice "senza tipo" (v39 o pre-esistente) nei due tipi, poi la rimuove.
        c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    SELECT 'COMMERCIAL', from_key, to_key FROM ddp_status_transitions WHERE ddp_type = ''");
        c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    SELECT 'OFFICINA', from_key, to_key FROM ddp_status_transitions WHERE ddp_type = ''");
        c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type = ''");

        // Divergenze: commerciale senza DC (né in ingresso né in uscita); officina apre DO→DC.
        c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type = 'COMMERCIAL' AND (from_key = 'DC' OR to_key = 'DC')");
        c.Execute("INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key) VALUES ('OFFICINA','DO','DC')");

        // Finestra di partenza (riga INIZIO dell'Excel): tutti gli stati attivi,
        // meno DC sulla commerciale.
        c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    SELECT 'COMMERCIAL', 'INIZIO', status_key FROM ddp_statuses
                    WHERE is_active = TRUE AND status_key <> 'DC'");
        c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                    SELECT 'OFFICINA', 'INIZIO', status_key FROM ddp_statuses
                    WHERE is_active = TRUE");

        log.LogInformation("[Migration v40] Matrice transizioni sdoppiata per tipo (COMMERCIAL senza DC, OFFICINA con DO→DC) + righe INIZIO");
    }
}
