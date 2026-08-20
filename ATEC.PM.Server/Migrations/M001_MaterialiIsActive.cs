using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v1: aggiunge project_material_items.is_active (allineamento quote)
public sealed class M001_MaterialiIsActive : IMigrazione
{
    public int Versione => 1;

    public string Descrizione => "project_material_items.is_active";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool hasColumn = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE()
              AND table_name = 'project_material_items'
              AND column_name = 'is_active'") > 0;

        if (!hasColumn)
        {
            c.Execute("ALTER TABLE project_material_items ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT TRUE");
            log.LogInformation("[Migration v1] Aggiunta colonna project_material_items.is_active");
        }
    }
}
