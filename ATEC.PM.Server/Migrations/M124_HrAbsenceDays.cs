using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M124 — <c>hr_absence_days</c>: le assenze di Ecos <b>giorno per giorno</b>.
///
/// <para><c>PeopleAbsenceRequestGetAll</c> dà una riga per <i>richiesta</i> (dal 3 al 6, ora
/// inizio, ora fine): per metterla nel calendario e nel cartellino la spezzavamo noi nei
/// singoli giorni, tirando a indovinare quali contano e quante ore per ciascuno — e la
/// regola di Ecos non è banale (nella richiesta a più giorni l'ora di inizio vale per il
/// primo giorno, quella di fine per l'ultimo). <c>PeopleAbsenceRequestRefineWorkAll</c> è
/// la stessa richiesta già spezzata da Ecos col suo calendario: una riga per giorno <b>e
/// per tratto</b> (mattina e pomeriggio di una giornata intera sono due righe), con ora
/// inizio e fine di quel tratto. Vedi <c>docs/guide/ECOS-API-CATALOGO.md</c>.</para>
///
/// <para>Questa tabella è lo <b>specchio</b> di quelle righe: si sostituisce a finestre di
/// date (dentro la finestra scaricata quello che Ecos non manda più si toglie), chiave
/// esterna <c>(ecos_absence_id, ecos_refine_id)</c>. <c>hr_absences</c> resta la tabella
/// delle richieste (una riga per richiesta, con approvatore, note, stato): qui ci sono le
/// ore sui giorni.</para>
/// </summary>
public sealed class M124_HrAbsenceDays : IMigrazione
{
    public int Versione => 124;

    public string Descrizione =>
        "HR: hr_absence_days, le assenze di Ecos spezzate giorno per giorno (PeopleAbsenceRequestRefineWorkAll)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS `hr_absence_days` (
                `id` INT AUTO_INCREMENT PRIMARY KEY,
                `employee_id` INT NOT NULL,
                `work_date` DATE NOT NULL,
                `ecos_absence_id` VARCHAR(50) NOT NULL COMMENT 'AbsenceRequestID della richiesta madre',
                `ecos_refine_id` VARCHAR(50) NOT NULL COMMENT 'AbsenceRequestRefineID: progressivo del tratto dentro la richiesta',
                `category_code` VARCHAR(10) NOT NULL COMMENT 'CategoryCode di Ecos: F, P, M, I…',
                `category_desc` VARCHAR(100) NULL,
                `absence_type` VARCHAR(20) NOT NULL COMMENT 'VACATION, PERMIT, SICKNESS, INJURY, OTHER',
                `status` VARCHAR(20) NOT NULL COMMENT 'APPROVED, PENDING, REJECTED, CANCELLED',
                `hour_begin` TIME NULL,
                `hour_end` TIME NULL,
                `minutes` INT NULL COMMENT 'Minuti del tratto (fine - inizio); NULL se Ecos non dà gli orari',
                `ecos_update_date` DATETIME NULL,
                `synced_at` DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                UNIQUE INDEX `uq_hr_absence_days_ecos` (`ecos_absence_id`, `ecos_refine_id`),
                INDEX `idx_hr_absence_days_emp_date` (`employee_id`, `work_date`),
                INDEX `idx_hr_absence_days_date` (`work_date`),
                FOREIGN KEY (`employee_id`) REFERENCES `employees`(`id`) ON DELETE CASCADE
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;",
            commandTimeout: 600);

        log.LogInformation("[M124] hr_absence_days pronta: le assenze di Ecos giorno per giorno.");
    }
}
