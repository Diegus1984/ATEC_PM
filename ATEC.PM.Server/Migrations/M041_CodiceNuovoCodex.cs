using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v41: ampliamento Codex — colonna codice_nuovo (nuova codifica, vedi commento CREATE TABLE).
// Solo schema: nessuna conversione automatica, la ricodifica dei 201xxx è manuale (decisione 21/07/2026).
public sealed class M041_CodiceNuovoCodex : IMigrazione
{
    public int Versione => 41;

    public string Descrizione => "codex_items: colonna codice_nuovo (nuova codifica manuale)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE()
              AND TABLE_NAME = 'codex_items'
              AND COLUMN_NAME = 'codice_nuovo'") == 0)
        {
            c.Execute("ALTER TABLE codex_items ADD COLUMN codice_nuovo VARCHAR(15) NULL AFTER codice", commandTimeout: 600);
            c.Execute("ALTER TABLE codex_items ADD UNIQUE KEY uq_codex_codice_nuovo (codice_nuovo)", commandTimeout: 600);
        }
        log.LogInformation("[Migration v41] Colonna codice_nuovo su codex_items (nuova codifica)");
    }
}
