using ATEC.PM.Server.Services;
using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v61: Bilancio commessa (blocco 4 del piano V32).
//  - project_order_lines: l'ordine cliente diventa multi-riga (Ordine / Posizione / Importo).
//    `projects.revenue` NON sparisce: resta la fonte di SAL, dashboard e cash flow ed è
//    tenuta allineata dal server a COALESCE(SUM(amount),0) ad ogni scrittura sulle righe.
//    Nessun backfill: le commesse senza righe si comportano esattamente come prima, la
//    prima riga viene materializzata alla prima apertura del Bilancio.
//  - projects.sale_total: «Totale Vendita» da preventivo, digitato a mano (rif. CALCOLO G205).
//    NULL = non compilato (≠ 0), così «Delta Ordine» resta vuoto invece di valere l'ordine.
//  - ddp_officina_items.work_type: natura della lavorazione (Internal/External), oggi
//    ricavabile solo da project_work_requests.type — che però esiste per i soli codici 101
//    e si perde quando la riga chiude. Congelarla in colonna è l'unico modo di non perderla.
//  - feature key nav.bilancio per la nuova pagina cross-commessa.
public sealed class M061_BilancioCommessa : IMigrazione
{
    public int Versione => 61;

    public string Descrizione => "Bilancio commessa: project_order_lines, projects.sale_total, ddp_officina_items.work_type, nav.bilancio";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Stessa definizione del ramo dev (LIVELLO 4, dopo projects).
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_order_lines (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            order_ref VARCHAR(100) NOT NULL DEFAULT '',
            order_position VARCHAR(10) NOT NULL DEFAULT '',
            amount DECIMAL(14,2) NULL,
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            CONSTRAINT fk_pol_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_pol_project (project_id, sort_order)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        AddColumnIfMissing(c, "projects", "sale_total",
            "DECIMAL(14,2) NULL AFTER revenue");

        bool addedWorkType = AddColumnIfMissing(c, "ddp_officina_items", "work_type",
            "VARCHAR(20) NOT NULL DEFAULT '' AFTER item_status");

        // Backfill in cascata, solo dove il tipo è ancora vuoto (quindi ripetibile e
        // non distruttivo se qualcuno ha già classificato a mano).
        // 1) fonte autorevole: la lavorazione collegata 1:1 (uq_pwr_ddp_officina).
        int fromWr = c.Execute(@"
            UPDATE ddp_officina_items o
            JOIN project_work_requests wr ON wr.ddp_officina_item_id = o.id
            SET o.work_type = wr.type
            WHERE TRIM(COALESCE(o.work_type,'')) = ''
              AND wr.type IN ('Internal','External')", commandTimeout: 600);

        // 2) ripiego: lo stato corrente della riga, con la stessa regola di
        //    WorkRequestDdpSync.TypeFromOfficinaStatus. Copre solo le righe ancora
        //    «in corso»: una riga già chiusa (DISP/PAR/MIT) resta non classificata,
        //    l'informazione non esiste più da nessuna parte e va messa a mano.
        int fromStatus = c.Execute(@"
            UPDATE ddp_officina_items
            SET work_type = CASE COALESCE(item_status,'')
                                WHEN 'DC' THEN 'Internal'
                                ELSE 'External'
                            END
            WHERE TRIM(COALESCE(work_type,'')) = ''
              AND COALESCE(item_status,'') IN ('DC','DO','RO','IO')", commandTimeout: 600);

        c.Execute(@"INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.bilancio', 'Bilancio Commessa', 'navigation', 2, 'HIDDEN')
            ON DUPLICATE KEY UPDATE display_name = VALUES(display_name), category = VALUES(category)");

        log.LogInformation(
            "[Migration v61] Bilancio commessa: project_order_lines creata, sale_total su projects, work_type su ddp_officina_items ({Added}) — classificate {FromWr} righe da lavorazione + {FromStatus} da stato; feature nav.bilancio registrata",
            addedWorkType ? "nuova" : "già presente", fromWr, fromStatus);
    }
}
