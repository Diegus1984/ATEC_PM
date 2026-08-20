using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v44: ciclo RDO Acquisti (testata + righe BOM + offerte fornitori).
public sealed class M044_CicloRdoAcquisti : IMigrazione
{
    public int Versione => 44;

    public string Descrizione => "purchase_rfqs + items + offers (ciclo RDO Acquisti)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS purchase_rfqs (
            id INT AUTO_INCREMENT PRIMARY KEY,
            atec_code VARCHAR(15) NOT NULL,
            description VARCHAR(300) NOT NULL DEFAULT '',
            status VARCHAR(20) NOT NULL DEFAULT 'DRAFT',
            notes TEXT,
            created_by INT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            sent_at DATETIME NULL,
            closed_at DATETIME NULL,
            updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
            INDEX IX_PurchaseRfqs_Atec (atec_code),
            INDEX IX_PurchaseRfqs_Status (status)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        c.Execute(@"CREATE TABLE IF NOT EXISTS purchase_rfq_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            rfq_id INT NOT NULL,
            bom_item_id INT NOT NULL,
            project_id INT NOT NULL,
            quantity DECIMAL(10,3) NOT NULL DEFAULT 0,
            FOREIGN KEY (rfq_id) REFERENCES purchase_rfqs(id) ON DELETE CASCADE,
            FOREIGN KEY (bom_item_id) REFERENCES bom_items(id) ON DELETE CASCADE,
            UNIQUE KEY uq_rfq_bom (rfq_id, bom_item_id),
            INDEX IX_PurchaseRfqItems_Bom (bom_item_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        c.Execute(@"CREATE TABLE IF NOT EXISTS purchase_rfq_offers (
            id INT AUTO_INCREMENT PRIMARY KEY,
            rfq_id INT NOT NULL,
            supplier_id INT NOT NULL,
            catalog_item_id INT NULL,
            unit_price DECIMAL(12,2) NULL,
            valid_until DATE NULL,
            notes TEXT,
            email_sent_at DATETIME NULL,
            is_winner TINYINT(1) NOT NULL DEFAULT 0,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (rfq_id) REFERENCES purchase_rfqs(id) ON DELETE CASCADE,
            FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE CASCADE,
            UNIQUE KEY uq_rfq_supplier (rfq_id, supplier_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
        log.LogInformation("[Migration v44] Tabelle purchase_rfq* (ciclo RDO)");
    }
}
