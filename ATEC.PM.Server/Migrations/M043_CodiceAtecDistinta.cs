using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v43: snapshot codice ATEC sulle righe distinta commerciale (piano Acquisti Fase 2).
public sealed class M043_CodiceAtecDistinta : IMigrazione
{
    public int Versione => 43;

    public string Descrizione => "bom_items: atec_code (snapshot codice ATEC in distinta)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'bom_items'
              AND COLUMN_NAME = 'atec_code'") == 0)
        {
            c.Execute("ALTER TABLE bom_items ADD COLUMN atec_code VARCHAR(15) NULL AFTER ddp_type", commandTimeout: 600);
            c.Execute("ALTER TABLE bom_items ADD INDEX IX_BomItems_AtecCode (atec_code)", commandTimeout: 600);
        }
        log.LogInformation("[Migration v43] Colonna atec_code su bom_items");
    }
}
