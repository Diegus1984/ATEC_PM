using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M110 — Rename Italian HR column names on <c>employees</c> to English (M109 first draft).
/// No-op when English names are already in place.
/// </summary>
public sealed class M110_HrEmployeeColumnsEnglish : IMigrazione
{
    public int Versione => 110;

    public string Descrizione =>
        "HR: rename hr_deve_timbrare / hr_ore_giornaliere / hr_conteggia_straord to English";

    public void Applica(MySqlConnection c, ILogger log)
    {
        RenameIfNeeded(c, log,
            oldName: "hr_deve_timbrare",
            newName: "hr_must_punch",
            definition: "TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'false = forfait, no punch required'");

        RenameIfNeeded(c, log,
            oldName: "hr_ore_giornaliere",
            newName: "hr_daily_hours",
            definition: "DECIMAL(3,1) NOT NULL DEFAULT 8.0 COMMENT 'Contract daily hours (4/6/8)'");

        RenameIfNeeded(c, log,
            oldName: "hr_conteggia_straord",
            newName: "hr_counts_overtime",
            definition: "TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'false = overtime zeroed on timesheet'");
    }

    private static void RenameIfNeeded(
        MySqlConnection c, ILogger log, string oldName, string newName, string definition)
    {
        bool hasOld = ColumnExists(c, oldName);
        bool hasNew = ColumnExists(c, newName);

        if (hasOld && !hasNew)
        {
            c.Execute(
                $"ALTER TABLE `employees` CHANGE `{oldName}` `{newName}` {definition}",
                commandTimeout: 600);
            log.LogInformation("[M110] Renamed employees.{Old} → {New}.", oldName, newName);
        }
    }

    private static bool ColumnExists(MySqlConnection c, string column) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'employees'
              AND column_name = @Column", new { Column = column }) > 0;
}
