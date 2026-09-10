using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// M132 — l'indennità di trasferta, giorno per giorno.
///
/// <para>Diego, 10/09/2026, dal foglio «PRESENZE 08 AGOSTO 26.xlsx»: sotto le voci di ogni
/// persona il consulente tiene una riga «TRASFERTA - €» con un importo per giornata (ad
/// agosto 20 o 40 euro, dieci persone, 4.300 euro in tutto) e il totale a fine riga. Nel
/// gestionale quella riga non c'era: si mette qui.</para>
///
/// <para>Un importo per persona e giorno, scelto fra le tariffe dell'indennità di trasferta
/// che stanno già in anagrafica (<c>tariff_options</c>, tipo <c>DAILY_ALLOWANCE</c>): la
/// tariffa NON si copia qui, si copia il suo valore — cambiare domani l'anagrafica non deve
/// riscrivere i mesi già chiusi. Niente FK verso <c>hr_days</c>: la giornata si ricalcola in
/// continuazione, la trasferta è una scelta di HR e resta.</para>
/// </summary>
public sealed class M132_HrTrasferte : IMigrazione
{
    public int Versione => 132;

    public string Descrizione => "HR: hr_travel_days, l'indennita di trasferta giorno per giorno";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"
            CREATE TABLE IF NOT EXISTS hr_travel_days (
                employee_id INT           NOT NULL,
                work_date   DATE          NOT NULL,
                amount      DECIMAL(10,2) NOT NULL COMMENT 'Euro della giornata, dalle tariffe indennita trasferta',
                created_by  INT           NULL,
                created_at  DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
                PRIMARY KEY (employee_id, work_date),
                KEY idx_hr_travel_days_data (work_date),
                CONSTRAINT fk_hr_travel_dip FOREIGN KEY (employee_id)
                    REFERENCES employees (id) ON DELETE CASCADE,
                CONSTRAINT fk_hr_travel_autore FOREIGN KEY (created_by)
                    REFERENCES employees (id) ON DELETE SET NULL
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci",
            commandTimeout: 600);

        log.LogInformation("[M132] hr_travel_days pronta: la riga «TRASFERTA - €» del foglio presenze.");
    }
}
