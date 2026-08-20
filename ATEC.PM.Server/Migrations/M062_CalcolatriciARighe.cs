using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v62: calcolatrici a righe (blocco 5 del piano V32).
//  - project_calc_sheets / project_calc_rows: il DETTAGLIO del calcolo di una voce di
//    costo, che fino a ieri non esisteva da nessuna parte — a video restava solo il
//    totale e il ragionamento che l'aveva prodotto si perdeva.
//    Il foglio è generico (`calc_key`): oggi lo usa «Lavorazioni Officine» a preventivo,
//    domani le 4 calcolatrici della Trasferta (blocco 6) senza altre migrazioni.
//    `amount_pinned` = il pattern «valore digitato = congelato» di contingency_pinned;
//    `linked_source` = il marcatore di provenienza di project_cashflow_categories
//    (le righe generate si rigenerano, quelle a mano non si toccano).
//  - tariff_options tipo HOURLY_RATE: le tariffe orarie delle Officine interne
//    (nel prototipo una lista a parte, di default 40 e 50 €/ora).
public sealed class M062_CalcolatriciARighe : IMigrazione
{
    public int Versione => 62;

    public string Descrizione => "Calcolatrici a righe: project_calc_sheets/rows, tariff_options HOURLY_RATE";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Stessa definizione del ramo dev (LIVELLO 4, dopo project_order_lines).
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_calc_sheets (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            calc_key VARCHAR(40) NOT NULL,
            row_version INT NOT NULL DEFAULT 0,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            updated_by INT NULL,
            UNIQUE KEY uk_calc_sheet (project_id, calc_key),
            CONSTRAINT fk_pcs_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_calc_rows (
            id INT AUTO_INCREMENT PRIMARY KEY,
            sheet_id INT NOT NULL,
            group_key VARCHAR(20) NOT NULL DEFAULT '',
            description VARCHAR(500) NOT NULL DEFAULT '',
            quantity DECIMAL(12,3) NULL,
            unit_cost DECIMAL(14,4) NULL,
            amount DECIMAL(14,2) NULL,
            amount_pinned TINYINT(1) NOT NULL DEFAULT 0,
            markup_value DECIMAL(5,3) NOT NULL DEFAULT 1.450,
            linked_source VARCHAR(60) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            CONSTRAINT fk_pcr_sheet FOREIGN KEY (sheet_id) REFERENCES project_calc_sheets(id) ON DELETE CASCADE,
            INDEX idx_pcr_sheet (sheet_id, group_key, sort_order)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // INSERT IGNORE sulla UNIQUE (tariff_type, value): idempotente e non tocca
        // eventuali tariffe già aggiunte a mano.
        int seeded = c.Execute(@"INSERT IGNORE INTO tariff_options (tariff_type, value)
            VALUES ('HOURLY_RATE', 40.000), ('HOURLY_RATE', 50.000)");

        log.LogInformation(
            "[Migration v62] Calcolatrici a righe: project_calc_sheets e project_calc_rows create; {Seeded} tariffe orarie predefinite inserite",
            seeded);
    }
}
