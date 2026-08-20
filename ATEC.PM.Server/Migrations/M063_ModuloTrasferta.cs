using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v63: Gestione Trasferta (blocco 6 del piano V32) — modulo nuovo, non c'era niente.
//  - travel_steps / travel_step_rows: gli step di trasferta e le loro righe-persona;
//  - project_calc_rows.multiplier: terzo fattore della riga, serve alla calcolatrice Auto
//    (Km × Rimborso × Numero Tratte). NULL vale 1, quindi le righe del blocco 5 non cambiano;
//  - feature key nav.trasferta per la nuova pagina.
// Il dettaglio delle 4 calcolatrici NON ha tabelle proprie: sta nei fogli del blocco 5.
public sealed class M063_ModuloTrasferta : IMigrazione
{
    public int Versione => 63;

    public string Descrizione => "Gestione Trasferta: travel_steps, travel_step_rows, project_calc_rows.multiplier, nav.trasferta";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool addedMultiplier = AddColumnIfMissing(c, "project_calc_rows", "multiplier",
            "DECIMAL(12,3) NULL AFTER unit_cost");

        // Stessa definizione del ramo dev (LIVELLO 4, dopo project_calc_rows).
        c.Execute(@"CREATE TABLE IF NOT EXISTS travel_steps (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            description VARCHAR(300) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_ts_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_ts_project (project_id, sort_order)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS travel_step_rows (
            id INT AUTO_INCREMENT PRIMARY KEY,
            step_id INT NOT NULL,
            employee_id INT NULL,
            person_name VARCHAR(200) NOT NULL DEFAULT '',
            start_date DATE NULL,
            end_date DATE NULL,
            exclude_sat TINYINT(1) NOT NULL DEFAULT 0,
            exclude_sun TINYINT(1) NOT NULL DEFAULT 0,
            hours DECIMAL(10,2) NULL,
            hourly_rate DECIMAL(10,3) NULL,
            nights INT NULL,
            night_price DECIMAL(12,2) NULL,
            meal_cost DECIMAL(12,2) NULL,
            allowance_cost DECIMAL(12,2) NULL,
            car_cost DECIMAL(12,2) NULL,
            transport_cost DECIMAL(12,2) NULL,
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_tsr_step FOREIGN KEY (step_id) REFERENCES travel_steps(id) ON DELETE CASCADE,
            INDEX idx_tsr_step (step_id, sort_order)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.trasferta', 'Gestione Trasferta', 'navigation', 2, 'HIDDEN')
            ON DUPLICATE KEY UPDATE display_name = VALUES(display_name), category = VALUES(category)");

        log.LogInformation(
            "[Migration v63] Gestione Trasferta: travel_steps e travel_step_rows create, multiplier su project_calc_rows ({Added}); feature nav.trasferta registrata",
            addedMultiplier ? "nuova" : "già presente");
    }
}
