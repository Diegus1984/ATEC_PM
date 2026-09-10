using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M131 — le entrate prima delle 8 si autorizzano una per una.
///
/// <para>Diego, 10/09/2026: «l'orario di inizio al mattino è alle 8; se l'orario arrotondato
/// è prima delle 8 bisogna avere un pulsante Autorizza / Non autorizzare: se premo autorizza
/// mantengo l'orario, altrimenti va approssimato alle 8, questo perché c'è gente che arriva,
/// timbra alle 7:30 e si fa mezz'ora di straordinario non autorizzato tutti i giorni».</para>
///
/// <para>Qui sta solo la decisione (una riga per persona e giorno, che non esiste finché
/// nessuno decide): senza riga vale il NO, cioè l'entrata conta dalle 8. La decisione entra
/// nel calcolo attraverso <c>RecalculateDay</c>, che rifà la giornata quando si preme il
/// pulsante. Le giornate già calcolate non si toccano: la regola vale dal giorno in cui è
/// stata introdotta in avanti (<c>TimesheetRules.EarlyEntryRuleFrom</c>), perché quelle di
/// prima sono già state guardate e pagate così.</para>
/// </summary>
public sealed class M131_HrEntrateAnticipate : IMigrazione
{
    public int Versione => 131;

    public string Descrizione =>
        "HR: hr_early_entries, l'entrata prima delle 8 vale solo se autorizzata";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS hr_early_entries (
                employee_id  INT         NOT NULL,
                work_date    DATE        NOT NULL,
                authorized   TINYINT(1)  NOT NULL COMMENT '1 = l orario timbrato vale, 0 = la giornata parte dalle 8',
                decided_by   INT         NULL,
                decided_at   DATETIME    NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (employee_id, work_date),
                KEY idx_hr_early_entries_data (work_date),
                CONSTRAINT fk_hr_early_dip FOREIGN KEY (employee_id)
                    REFERENCES employees (id) ON DELETE CASCADE,
                CONSTRAINT fk_hr_early_autore FOREIGN KEY (decided_by)
                    REFERENCES employees (id) ON DELETE SET NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci",
            commandTimeout: 600);

        log.LogInformation("[M131] hr_early_entries pronta: le entrate anticipate si autorizzano una per una.");
    }
}
