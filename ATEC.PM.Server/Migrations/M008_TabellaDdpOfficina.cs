using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v8: tabella dedicata DDP Officina (particolari meccanici, vedi commento CREATE TABLE in InitDatabase).
public sealed class M008_TabellaDdpOfficina : IMigrazione
{
    public int Versione => 8;

    public string Descrizione => "ddp_officina_items";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_officina_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            part_number VARCHAR(100) DEFAULT '',
            description VARCHAR(300) DEFAULT '',
            quantity DECIMAL(10,3) DEFAULT 0,
            material VARCHAR(200) DEFAULT '',
            treatment VARCHAR(200) DEFAULT '',
            supplier_name VARCHAR(200) DEFAULT '',
            unit_cost DECIMAL(10,2) DEFAULT 0,
            item_status VARCHAR(20) DEFAULT 'DO',
            requested_by VARCHAR(100) DEFAULT '',
            danea_ref VARCHAR(100) DEFAULT '',
            date_needed DATE,
            destination VARCHAR(200) DEFAULT '',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_ddpoff_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        log.LogInformation("[Migration v8] Creata tabella ddp_officina_items (distinta officina)");
    }
}
