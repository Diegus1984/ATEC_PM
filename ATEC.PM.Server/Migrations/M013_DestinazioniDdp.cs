using ATEC.PM.Server.Data;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v13: specifica destinazione sulle righe DDP + seed elenco standard destinazioni (demo V1).
public sealed class M013_DestinazioniDdp : IMigrazione
{
    public int Versione => 13;

    public string Descrizione => "ddp destination_spec + seed destinazioni standard";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bom_items' AND COLUMN_NAME = 'destination_spec'") == 0)
        {
            c.Execute("ALTER TABLE bom_items ADD COLUMN destination_spec VARCHAR(200) NOT NULL DEFAULT '' AFTER destination");
            log.LogInformation("[Migration v13] Aggiunta colonna bom_items.destination_spec");
        }

        if (c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ddp_officina_items' AND COLUMN_NAME = 'destination_spec'") == 0)
        {
            c.Execute("ALTER TABLE ddp_officina_items ADD COLUMN destination_spec VARCHAR(200) NOT NULL DEFAULT '' AFTER destination");
            log.LogInformation("[Migration v13] Aggiunta colonna ddp_officina_items.destination_spec");
        }

        c.Execute("DELETE FROM ddp_destinations WHERE name IN ('DEMO', 'DUE', 'TRE')");

        for (int i = 0; i < DdpDestinationSeed.Names.Length; i++)
        {
            string name = DdpDestinationSeed.Names[i];
            c.Execute(@"
                INSERT INTO ddp_destinations (name, sort_order, is_active)
                SELECT @Name, 0, TRUE
                WHERE NOT EXISTS (SELECT 1 FROM ddp_destinations WHERE name = @Name)",
                new { Name = name });
        }

        log.LogInformation("[Migration v13] destination_spec + {Count} destinazioni standard seedate",
            DdpDestinationSeed.Names.Length);
    }
}
