using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

public sealed class M055_CronistoriaStatiRighe : IMigrazione
{
    public int Versione => 55;

    public string Descrizione => "ddp_item_events: cronistoria stati righe distinta";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Cronistoria delle righe di distinta (commerciale e officina): una voce per
        // ogni cambio di stato. Sostituisce l'idea di una colonna-data per stato:
        // gli stati sono configurabili, le colonne no. Da qui si ricava "consegnato il",
        // "ordinato il", ecc., e si sa anche CHI ha cambiato.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_item_events (
            id INT AUTO_INCREMENT PRIMARY KEY,
            item_type VARCHAR(12) NOT NULL,
            item_id INT NOT NULL,
            project_id INT NOT NULL,
            from_status VARCHAR(20) NULL,
            to_status VARCHAR(20) NOT NULL,
            changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            changed_by_id INT NULL,
            changed_by_name VARCHAR(150) NOT NULL DEFAULT '',
            origin VARCHAR(12) NOT NULL DEFAULT 'UTENTE',
            note VARCHAR(300) NOT NULL DEFAULT '',
            INDEX ix_ddp_events_item (item_type, item_id, changed_at),
            INDEX ix_ddp_events_project (project_id),
            INDEX ix_ddp_events_stato (item_type, to_status)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Ricostruzione del pregresso da quel poco che il database già sapeva:
        // marcata come RICOSTR perché la data è dedotta, non registrata sul momento.
        int recuperati = c.Execute(@"
            INSERT INTO ddp_item_events
                (item_type, item_id, project_id, from_status, to_status, changed_at, changed_by_name, origin, note)
            SELECT 'COMMERCIAL', b.id, b.project_id, NULL, 'IO', b.date_ordered, '', 'RICOSTR', 'dedotto dalla data ordine'
            FROM bom_items b
            WHERE b.date_ordered IS NOT NULL");

        recuperati += c.Execute(@"
            INSERT INTO ddp_item_events
                (item_type, item_id, project_id, from_status, to_status, changed_at, changed_by_name, origin, note)
            SELECT 'COMMERCIAL', b.id, b.project_id, NULL, 'DISP', b.date_received, '', 'RICOSTR', 'dedotto dalla data di ricevimento'
            FROM bom_items b
            WHERE b.date_received IS NOT NULL");

        recuperati += c.Execute(@"
            INSERT INTO ddp_item_events
                (item_type, item_id, project_id, from_status, to_status, changed_at, changed_by_name, origin, note)
            SELECT 'OFFICINA', o.id, o.project_id, NULL, 'IO', o.order_date, '', 'RICOSTR', 'dedotto dalla data ordine'
            FROM ddp_officina_items o
            WHERE o.order_date IS NOT NULL");

        // Stato attuale di ogni riga: la data migliore che abbiamo è updated_at.
        // Si salta se per quella riga esiste già una voce con lo stesso stato.
        recuperati += c.Execute(@"
            INSERT INTO ddp_item_events
                (item_type, item_id, project_id, from_status, to_status, changed_at, changed_by_name, origin, note)
            SELECT 'COMMERCIAL', b.id, b.project_id, NULL, b.item_status,
                   COALESCE(b.updated_at, b.created_at, NOW()), '', 'RICOSTR', 'stato al momento dell attivazione'
            FROM bom_items b
            WHERE COALESCE(b.item_status, '') <> ''
              AND NOT EXISTS (SELECT 1 FROM ddp_item_events e
                              WHERE e.item_type='COMMERCIAL' AND e.item_id=b.id AND e.to_status=b.item_status)");

        recuperati += c.Execute(@"
            INSERT INTO ddp_item_events
                (item_type, item_id, project_id, from_status, to_status, changed_at, changed_by_name, origin, note)
            SELECT 'OFFICINA', o.id, o.project_id, NULL, o.item_status,
                   COALESCE(o.updated_at, o.created_at, NOW()), '', 'RICOSTR', 'stato al momento dell attivazione'
            FROM ddp_officina_items o
            WHERE COALESCE(o.item_status, '') <> ''
              AND NOT EXISTS (SELECT 1 FROM ddp_item_events e
                              WHERE e.item_type='OFFICINA' AND e.item_id=o.id AND e.to_status=o.item_status)");

        log.LogInformation("[Migration v55] Cronistoria righe DDP creata ({Recuperati} voci ricostruite dal pregresso)", recuperati);
    }
}
