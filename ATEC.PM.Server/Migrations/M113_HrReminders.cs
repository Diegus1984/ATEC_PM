using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M113 — I solleciti per le timbrature mancanti (PIANO-HR-PRESENZE.md, Fase 1).
///
/// <para>Una riga per <b>giornata sollecitata</b>, non per email: è il taglio del
/// <c>MailLog</c> del programma «Timbrature», e serve a rispondere alla domanda che si fa
/// chi guarda il calendario — «questo buco l'ho già chiesto?». Con una riga per email
/// quella risposta si perderebbe dentro un elenco di destinatari.</para>
///
/// <para>Unicità su <c>(employee_id, work_date)</c>: il secondo sollecito sullo stesso
/// giorno aggiorna la data invece di accumulare righe. Chi sollecita due volte vuole
/// sapere QUANDO è stata l'ultima, non quante volte.</para>
/// </summary>
public sealed class M113_HrReminders : IMigrazione
{
    public int Versione => 113;

    public string Descrizione =>
        "HR: hr_reminders, i solleciti per le giornate senza timbrature";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS hr_reminders (
                id           BIGINT      NOT NULL AUTO_INCREMENT,
                employee_id  INT         NOT NULL,
                work_date    DATE        NOT NULL,
                sent_at      DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
                sent_by      INT         NULL,
                channel      VARCHAR(20) NOT NULL DEFAULT 'SMTP' COMMENT 'SMTP = inviata dal server, MAILTO = aperta nel client di posta',
                PRIMARY KEY (id),
                UNIQUE KEY uq_hr_reminders (employee_id, work_date),
                KEY idx_hr_reminders_data (work_date),
                CONSTRAINT fk_hr_reminders_dip FOREIGN KEY (employee_id)
                    REFERENCES employees (id) ON DELETE CASCADE,
                CONSTRAINT fk_hr_reminders_autore FOREIGN KEY (sent_by)
                    REFERENCES employees (id) ON DELETE SET NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci",
            commandTimeout: 600);

        log.LogInformation("[M113] hr_reminders pronta: il calendario può dire quali buchi sono già stati chiesti.");
    }
}
