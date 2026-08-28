using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M109 — HR attendance settings on the employee record (plan: <c>PIANO-HR-PRESENZE.md</c>).
///
/// <para>Port from «Timbrature» <c>Users.xaml</c>:
/// <c>IsForfait</c> → <c>hr_must_punch</c> (inverted: forfait = must_punch false),
/// <c>ForfaitHours</c> → <c>hr_daily_hours</c>,
/// <c>IncludeOvertime</c> → <c>hr_counts_overtime</c>.</para>
///
/// <para>Conservative defaults: must punch, 8h, overtime counted — same as the VB app.</para>
/// </summary>
public sealed class M109_EmployeeHrPresenze : IMigrazione
{
    public int Versione => 109;

    public string Descrizione =>
        "HR profile: hr_must_punch, hr_daily_hours, hr_counts_overtime on employees";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool addedMustPunch = AddColumnIfMissing(
            c, "employees", "hr_must_punch",
            "TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'false = forfait, no punch required'");

        bool addedDailyHours = AddColumnIfMissing(
            c, "employees", "hr_daily_hours",
            "DECIMAL(3,1) NOT NULL DEFAULT 8.0 COMMENT 'Contract daily hours (4/6/8)'");

        bool addedCountsOvertime = AddColumnIfMissing(
            c, "employees", "hr_counts_overtime",
            "TINYINT(1) NOT NULL DEFAULT 1 COMMENT 'false = overtime zeroed on timesheet'");

        if (addedMustPunch || addedDailyHours || addedCountsOvertime)
            log.LogInformation(
                "[M109] HR columns on employees: must_punch={MustPunch}, daily_hours={DailyHours}, counts_overtime={CountsOvertime}.",
                addedMustPunch, addedDailyHours, addedCountsOvertime);
    }
}
