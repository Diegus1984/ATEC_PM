using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v68: trasferta a righe nella sezione di preventivo (segnalazione #33).
//  - project_cost_travel_rows: una riga per persona, sulla forma di travel_step_rows;
//  - conversione una-tantum dei 7 campi digitati a mano (project_cost_resources) in righe,
//    dettaglio delle calcolatrici compreso.
// I 7 campi legacy NON si cancellano: li usa ancora il Commerciale e li interroga la
// guardia anti-cancellazione delle tariffe (TravelTariffsController). Il doppio conteggio
// lo evita la regola di lettura: se la sezione ha almeno una riga si usano SOLO le righe.
public sealed class M068_TrasfertaNelPreventivo : IMigrazione
{
    public int Versione => 68;

    public string Descrizione => "Trasferta a righe nel preventivo: project_cost_travel_rows + conversione dei 7 campi manuali";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Stessa definizione del ramo dev (LIVELLO 4, dopo project_cost_resources).
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cost_travel_rows (
            id INT AUTO_INCREMENT PRIMARY KEY,
            section_id INT NOT NULL,
            resource_id INT NULL,
            person_name VARCHAR(200) NOT NULL DEFAULT '',
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
            CONSTRAINT fk_pctr_section  FOREIGN KEY (section_id)  REFERENCES project_cost_sections(id) ON DELETE CASCADE,
            CONSTRAINT fk_pctr_resource FOREIGN KEY (resource_id) REFERENCES project_cost_resources(id) ON DELETE SET NULL,
            INDEX idx_pctr_section (section_id, sort_order)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        (int converted, int sheets, int rounded) = ConvertLegacyTravelFields(c);

        log.LogInformation(
            "[Migration v68] Trasferta a righe nel preventivo: tabella project_cost_travel_rows creata; {Converted} risorse convertite in righe, {Sheets} fogli di calcolo scritti, {Rounded} righe con notti arrotondate dai giorni frazionari",
            converted, sheets, rounded);
    }
}
