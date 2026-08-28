using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M111 — English names for HR schema (tables, columns, config key, punch source values).
/// Renames Italian identifiers from M107 when present; no-op on fresh installs that
/// already use English names from the updated M107.
/// </summary>
public sealed class M111_HrSchemaEnglish : IMigrazione
{
    public int Versione => 111;

    public string Descrizione =>
        "HR: hr_timbrature/hr_giornate → hr_punches/hr_days, English column names";

    public void Applica(MySqlConnection c, ILogger log)
    {
        if (TableExists(c, "hr_timbrature") && !TableExists(c, "hr_punches"))
        {
            c.Execute("RENAME TABLE hr_timbrature TO hr_punches", commandTimeout: 600);
            log.LogInformation("[M111] Renamed table hr_timbrature → hr_punches.");
        }

        if (TableExists(c, "hr_giornate") && !TableExists(c, "hr_days"))
        {
            c.Execute("RENAME TABLE hr_giornate TO hr_days", commandTimeout: 600);
            log.LogInformation("[M111] Renamed table hr_giornate → hr_days.");
        }

        if (TableExists(c, "hr_punches"))
        {
            RenameColumn(c, "hr_punches", "giorno", "work_date", "DATE NOT NULL");
            RenameColumn(c, "hr_punches", "orario", "punched_at", "DATETIME NOT NULL");
            RenameColumn(c, "hr_punches", "verso", "direction", "VARCHAR(20) NOT NULL");
            RenameColumn(c, "hr_punches", "origine", "source", "VARCHAR(20) NOT NULL DEFAULT 'ECOS'");
            RenameColumn(c, "hr_punches", "id_esterno", "external_id", "VARCHAR(50) NULL");
            RenameColumn(c, "hr_punches", "luogo", "location", "VARCHAR(100) NULL");
            RenameColumn(c, "hr_punches", "motivo", "reason", "VARCHAR(255) NULL");
            RenameColumn(c, "hr_punches", "creata_da", "created_by", "INT NULL");
            RenameColumn(c, "hr_punches", "creata_il", "created_at", "DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP");

            c.Execute(
                "UPDATE hr_punches SET source = 'ADJUSTMENT' WHERE source = 'RETTIFICA'",
                commandTimeout: 600);
        }

        if (TableExists(c, "hr_days"))
        {
            RenameColumn(c, "hr_days", "giorno", "work_date", "DATE NOT NULL");
            RenameColumn(c, "hr_days", "entrata1", "clock_in_1", "VARCHAR(10) NULL");
            RenameColumn(c, "hr_days", "uscita1", "clock_out_1", "VARCHAR(10) NULL");
            RenameColumn(c, "hr_days", "entrata2", "clock_in_2", "VARCHAR(10) NULL");
            RenameColumn(c, "hr_days", "uscita2", "clock_out_2", "VARCHAR(10) NULL");
            RenameColumn(c, "hr_days", "minuti_ordinari", "regular_minutes", "INT NOT NULL DEFAULT 0");
            RenameColumn(c, "hr_days", "minuti_straord", "overtime_minutes", "INT NOT NULL DEFAULT 0");
            RenameColumn(c, "hr_days", "minuti_pausa", "break_minutes", "INT NOT NULL DEFAULT 0");
            RenameColumn(c, "hr_days", "fasce_json", "bands_json", "JSON NULL");
            RenameColumn(c, "hr_days", "nota", "note", "VARCHAR(255) NULL");
            RenameColumn(c, "hr_days", "anomalia", "has_anomaly", "TINYINT(1) NOT NULL DEFAULT 0");
            RenameColumn(c, "hr_days", "calcolato_il", "calculated_at", "DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP");
            RenameColumn(c, "hr_days", "regole_versione", "rules_version", "INT NOT NULL DEFAULT 1");
        }

        if (TableExists(c, "app_config"))
        {
            c.Execute(@"
                UPDATE app_config SET config_key = 'hr_sync_punches_from'
                WHERE config_key = 'hr_sync_timbrature_da'",
                commandTimeout: 600);
        }
    }

    private static bool TableExists(MySqlConnection c, string table) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = @Table",
            new { Table = table }) > 0;

    private static void RenameColumn(
        MySqlConnection c, string table, string oldName, string newName, string definition)
    {
        if (!ColumnExists(c, table, oldName) || ColumnExists(c, table, newName))
            return;

        c.Execute(
            $"ALTER TABLE `{table}` CHANGE `{oldName}` `{newName}` {definition}",
            commandTimeout: 600);
    }

    private static bool ColumnExists(MySqlConnection c, string table, string column) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = @Table AND column_name = @Column",
            new { Table = table, Column = column }) > 0;
}
