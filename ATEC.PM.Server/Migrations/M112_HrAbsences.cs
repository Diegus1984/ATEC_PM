using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M112 — HR Absences and Vacation/Permit Requests (PIANO-HR-PRESENZE.md, Fase 2).
/// Replaces the legacy empty <c>absences</c> table with a structured <c>hr_absences</c> table
/// supporting date ranges, partial/hourly leaves, approval workflow, and Ecos synchronization.
/// </summary>
public sealed class M112_HrAbsences : IMigrazione
{
    public int Versione => 112;

    public string Descrizione =>
        "HR: hr_absences table for vacations, hourly permits, and approval workflow";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Drop legacy empty absences table if present
        if (TableExists(c, "absences"))
        {
            int rowCount = c.ExecuteScalar<int>("SELECT COUNT(*) FROM absences");
            if (rowCount == 0)
            {
                c.Execute("DROP TABLE absences", commandTimeout: 600);
                log.LogInformation("[M112] Dropped legacy empty table absences.");
            }
        }

        if (!TableExists(c, "hr_absences"))
        {
            c.Execute(@"
                CREATE TABLE `hr_absences` (
                    `id` INT AUTO_INCREMENT PRIMARY KEY,
                    `employee_id` INT NOT NULL,
                    `date_from` DATE NOT NULL,
                    `date_to` DATE NOT NULL,
                    `hours` DECIMAL(4,1) NULL COMMENT 'Null = full days, or specific hours per day (e.g. 4.0)',
                    `is_full_day` TINYINT(1) NOT NULL DEFAULT 1 COMMENT '1 = full day(s), 0 = partial day / hourly permit',
                    `absence_type` VARCHAR(20) NOT NULL DEFAULT 'VACATION' COMMENT 'VACATION, PERMIT, SICKNESS, INJURY, OTHER',
                    `status` VARCHAR(20) NOT NULL DEFAULT 'PENDING' COMMENT 'PENDING, APPROVED, REJECTED, CANCELLED',
                    `source` VARCHAR(20) NOT NULL DEFAULT 'ATEC' COMMENT 'ATEC, ECOS, MANUAL',
                    `ecos_absence_id` VARCHAR(50) NULL COMMENT 'External ID from EcosAgile',
                    `approved_by` INT NULL,
                    `approved_at` DATETIME NULL,
                    `rejection_reason` VARCHAR(255) NULL,
                    `notes` TEXT NULL,
                    `created_by` INT NULL,
                    `created_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
                    `updated_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    INDEX `idx_hr_absences_emp_dates` (`employee_id`, `date_from`, `date_to`),
                    INDEX `idx_hr_absences_status` (`status`),
                    INDEX `idx_hr_absences_ecos_id` (`ecos_absence_id`),
                    INDEX `idx_hr_absences_dates` (`date_from`, `date_to`),
                    FOREIGN KEY (`employee_id`) REFERENCES `employees`(`id`) ON DELETE CASCADE,
                    FOREIGN KEY (`approved_by`) REFERENCES `employees`(`id`) ON DELETE SET NULL,
                    FOREIGN KEY (`created_by`) REFERENCES `employees`(`id`) ON DELETE SET NULL
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
                commandTimeout: 600);

            log.LogInformation("[M112] Created table hr_absences.");
        }
    }

    private static bool TableExists(MySqlConnection c, string table) =>
        c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = DATABASE() AND table_name = @Table",
            new { Table = table }) > 0;
}
