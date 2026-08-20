using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v30: aggiunge tabella ddp_treatments per i trattamenti gestiti da anagrafica
public sealed class M030_Trattamenti : IMigrazione
{
    public int Versione => 30;

    public string Descrizione => "ddp_treatments: tabella e seed iniziale trattamenti";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS ddp_treatments (
                id INT AUTO_INCREMENT PRIMARY KEY,
                name VARCHAR(200) NOT NULL UNIQUE,
                sort_order INT NOT NULL DEFAULT 0,
                is_active TINYINT(1) NOT NULL DEFAULT 1,
                created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;");

        c.Execute(@"
            INSERT IGNORE INTO ddp_treatments (name, sort_order, is_active) VALUES
            ('ANODIZZATO', 10, 1),
            ('BRUNITO', 20, 1),
            ('ZINCATO', 30, 1),
            ('VERNICIATO', 40, 1),
            ('SABBIATO', 50, 1);");

        // Backfill dei trattamenti già digitati a mano sulle righe officina
        c.Execute(@"INSERT IGNORE INTO ddp_treatments (name, sort_order, is_active)
            SELECT DISTINCT TRIM(treatment), 100, 1
            FROM ddp_officina_items
            WHERE TRIM(COALESCE(treatment, '')) <> ''");

        log.LogInformation("[Migration v30] Tabella ddp_treatments creata e popolata");
    }
}
