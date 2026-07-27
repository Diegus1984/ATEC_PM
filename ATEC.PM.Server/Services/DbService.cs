using ATEC.PM.Server.Data;
using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Services;

public class DbService
{
    private readonly string _cs;
    private readonly ILogger<DbService> _logger;

    public DbService(IConfiguration config, ILogger<DbService> logger)
    {
        _cs = config.GetConnectionString("Default")!;
        _logger = logger;
    }

    public MySqlConnection Open()
    {
        var conn = new MySqlConnection(_cs);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Esegue UPDATE dinamico su un singolo campo con whitelist di sicurezza.
    /// Restituisce null se ok, oppure il messaggio di errore se il campo non è consentito.
    /// </summary>
    public string? UpdateField(string table, int id, string field, string? value,
        HashSet<string> allowedFields, string? extraWhere = null, object? extraParams = null)
    {
        if (!allowedFields.Contains(field))
            return $"Campo '{field}' non consentito";

        using var c = Open();
        string where = extraWhere != null ? $"id=@id AND {extraWhere}" : "id=@id";
        string sql = $"UPDATE `{table}` SET `{field}`=@Value WHERE {where}";

        DynamicParameters dp = new();
        dp.Add("Value", value);
        dp.Add("id", id);
        if (extraParams != null)
        {
            foreach (var prop in extraParams.GetType().GetProperties())
                dp.Add(prop.Name, prop.GetValue(extraParams));
        }

        c.Execute(sql, dp);
        return null;
    }

    public string GetConfig(string key, string defaultValue = "")
    {
        using var c = Open();
        return c.ExecuteScalar<string?>(
            "SELECT config_value FROM app_config WHERE config_key=@Key", new { Key = key }) ?? defaultValue;
    }

    private void EnsureDatabaseExists()
    {
        var csb = new MySqlConnectionStringBuilder(_cs);
        string dbName = csb.Database;
        csb.Database = "";

        using var conn = new MySqlConnection(csb.ConnectionString);
        conn.Open();
        conn.Execute($"CREATE DATABASE IF NOT EXISTS `{dbName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
        Console.WriteLine($"[DB] Database '{dbName}' verificato/creato.");
    }

    public void InitDatabase(bool productionMode = false)
    {
        _logger.LogInformation("[InitDatabase] Avvio verifica/creazione schema (mode={Mode})...",
            productionMode ? "PRODUCTION" : "DEVELOPMENT");
        EnsureDatabaseExists();
        using var c = Open();

        EnsureSchemaMigrationsTable(c);

        if (productionMode)
        {
            int currentVersion = GetSchemaVersion(c);
            _logger.LogInformation("[InitDatabase] Schema versione corrente: {Version}", currentVersion);
            ApplyVersionedMigrations(c, currentVersion);

            // Modulo Preventivi/Catalogo
            new QuoteDbService(this).ApplyMigrations(c);

            int prodTableCount = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()");
            _logger.LogInformation("[InitDatabase] Schema verificato: {TableCount} tabelle presenti", prodTableCount);
            return;
        }

        // ══════════════════════════════════════════════════════════
        // LIVELLO 0 — Tabelle senza dipendenze
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS app_config (
            config_key VARCHAR(100) PRIMARY KEY,
            config_value VARCHAR(500) DEFAULT '',
            description VARCHAR(200) DEFAULT '',
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(10) NOT NULL UNIQUE,
            name VARCHAR(100) NOT NULL,
            hourly_cost DECIMAL(8,2) NOT NULL DEFAULT 0,
            default_markup DECIMAL(5,3) NOT NULL DEFAULT 1.450,
            sort_order INT DEFAULT 0,
            is_active BOOLEAN DEFAULT TRUE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS employees (
            id INT AUTO_INCREMENT PRIMARY KEY,
            first_name VARCHAR(100) NOT NULL,
            last_name VARCHAR(100) NOT NULL,
            email VARCHAR(200) DEFAULT '',
            emp_type VARCHAR(20) DEFAULT 'INTERNAL',
            supplier_id INT NULL,
            status VARCHAR(20) DEFAULT 'ACTIVE',
            username VARCHAR(50),
            password_hash VARCHAR(255) DEFAULT '',
            user_role VARCHAR(20) DEFAULT 'TECH',
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            INDEX idx_emp_status (status)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS customers (
            id INT AUTO_INCREMENT PRIMARY KEY,
            company_name VARCHAR(200) NOT NULL,
            contact_name VARCHAR(100) DEFAULT '',
            email VARCHAR(200) DEFAULT '',
            pec VARCHAR(255) DEFAULT '',
            phone VARCHAR(100) DEFAULT '',
            cell VARCHAR(50) DEFAULT '',
            address VARCHAR(300) DEFAULT '',
            vat_number VARCHAR(50) DEFAULT '',
            fiscal_code VARCHAR(50) DEFAULT '',
            payment_terms VARCHAR(255) DEFAULT '',
            sdi_code VARCHAR(50) DEFAULT '',
            easyfatt_code VARCHAR(50) DEFAULT '',
            easyfatt_id INT DEFAULT 0,
            notes TEXT,
            is_active BOOLEAN DEFAULT TRUE,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_Customer_Vat (vat_number)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS suppliers (
            id INT AUTO_INCREMENT PRIMARY KEY,
            company_name VARCHAR(200) NOT NULL,
            contact_name VARCHAR(100) DEFAULT '',
            email VARCHAR(200) DEFAULT '',
            phone VARCHAR(100) DEFAULT '',
            address VARCHAR(300) DEFAULT '',
            vat_number VARCHAR(50) DEFAULT '',
            fiscal_code VARCHAR(50) DEFAULT '',
            notes TEXT,
            is_active BOOLEAN DEFAULT TRUE,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_Supplier_Vat (vat_number)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS holidays (
            id INT AUTO_INCREMENT PRIMARY KEY,
            holiday_date DATE NOT NULL,
            description VARCHAR(100) DEFAULT '',
            year INT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS tariff_options (
            id INT AUTO_INCREMENT PRIMARY KEY,
            tariff_type VARCHAR(30) NOT NULL,
            value DECIMAL(10,3) NOT NULL,
            UNIQUE KEY UQ_TariffVal (tariff_type, value)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_destinations (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Stati DDP (etichetta + colori riga/combo, editabili da Conf. DDP).
        // La chiave (status_key) è il valore salvato in bom_items.item_status: NON va cambiata.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_statuses (
            id INT AUTO_INCREMENT PRIMARY KEY,
            status_key VARCHAR(30) NOT NULL UNIQUE,
            label VARCHAR(100) NOT NULL,
            color_bg VARCHAR(9) NOT NULL DEFAULT '#CCCCCC',
            color_fg VARCHAR(9) NOT NULL DEFAULT '#000000',
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Seed delle causali DDP reali (matrice stati v7, 20/07/2026). INSERT IGNORE su status_key:
        // idempotente, NON sovrascrive le modifiche fatte dall'utente. I gruppi condividono il colore.
        // Gli stati legacy CON/COS/SPED/MOD sono stati assorbiti (v7): CON/COS/SPED→DISP, MOD→RAM
        // (migrazione v39 sui DB esistenti; qui non vengono più seedati).
        c.Execute(@"INSERT IGNORE INTO ddp_statuses (status_key, label, color_bg, color_fg, sort_order) VALUES
            ('ANN',  'ANNULLATO',                                       '#000000', '#FFFFFF', 1),
            ('SOSP', 'SOSPESO',                                         '#000000', '#FFFFFF', 2),
            ('RAM',  'RIMESSO A MAGAZZINO',                             '#000000', '#FFFFFF', 3),
            ('SOST', 'SOSTITUITO',                                      '#000000', '#FFFFFF', 4),
            ('DISP', 'DISPONIBILE / CONSEGNATO',                        '#00B050', '#FFFFFF', 7),
            ('DC',   'DA COSTRUIRE',                                    '#006400', '#FFFFFF', 8),
            ('DO',   'DA ORDINARE',                                     '#FF0000', '#FFFFFF', 9),
            ('ASS',  'ASSEGNATO AL MONTATORE',                          '#B4B4B4', '#000000', 10),
            ('CHEK', 'MAT. CHE NECESSITA CONTROLLO TECNICO/COMMERCIALE','#8B008B', '#FFFFFF', 11),
            ('IO',   'IN ORDINE',                                       '#FFFF00', '#000000', 12),
            ('PAR',  'PARZIALMENTE CONSEGNATO o COSTRUITO',             '#7030A0', '#FFFFFF', 13),
            ('RO',   'RICHIESTA OFFERTA',                               '#FFC000', '#000000', 14),
            ('VER',  'VERIFICARE SE DISPONIBILE A MAG',                 '#00B0F0', '#FFFFFF', 15)");

        // Matrice degli avanzamenti di stato (riga = stato corrente, colonna = stato selezionabile).
        // Un record (from,to) = transizione ammessa; il record sentinella (from,'') marca uno stato
        // "governato" senza uscite (terminale: ANN, SOST). Stati SENZA alcun record = non governati
        // dalla matrice → finestra opzioni completa (retro-compatibilità con stati custom di Conf. DDP).
        // from_key speciale 'INIZIO' = finestra di partenza delle righe senza stato.
        // Seed nella migrazione v39, sdoppiata per tipo dalla v40 (ddp_type COMMERCIAL/OFFICINA:
        // la commerciale non contempla DC — il materiale commerciale si acquista). One-shot:
        // l'editor in Conf. DDP può togliere archi senza vederseli riapparire a ogni avvio.
        // Creata qui nella forma v39 (senza tipo): sui DB nuovi la v40 la porta subito al PK triplo.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_status_transitions (
            from_key VARCHAR(30) NOT NULL,
            to_key VARCHAR(30) NOT NULL DEFAULT '',
            PRIMARY KEY (from_key, to_key)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Trattamenti DDP Officina (anagrafica editabile da Conf. DDP; le righe distinta
        // salvano il trattamento per NOME). Stessa definizione della migrazione v30.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_treatments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL UNIQUE,
            sort_order INT NOT NULL DEFAULT 0,
            is_active TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"INSERT IGNORE INTO ddp_treatments (name, sort_order, is_active) VALUES
            ('ANODIZZATO', 10, 1),
            ('BRUNITO', 20, 1),
            ('ZINCATO', 30, 1),
            ('VERNICIATO', 40, 1),
            ('SABBIATO', 50, 1)");

        // Backfill: i trattamenti già digitati a mano sulle righe officina entrano in
        // anagrafica (UNIQUE(name) case-insensitive dedupa via INSERT IGNORE)
        c.Execute(@"INSERT IGNORE INTO ddp_treatments (name, sort_order, is_active)
            SELECT DISTINCT TRIM(treatment), 100, 1
            FROM ddp_officina_items
            WHERE TRIM(COALESCE(treatment, '')) <> ''");

        // Aggregazioni di stato DDP (matrice Stati × Aggregazioni, editabile da "Aggregazioni DDP").
        // kind: SET=unione stati · ALL=tutti (conteggio per stato) · DATED=stati+data prev. · SUBGROUPS=7 card.
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_aggregations (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(10) NOT NULL UNIQUE,
            name VARCHAR(150) NOT NULL,
            description VARCHAR(500) DEFAULT '',
            kind VARCHAR(20) NOT NULL DEFAULT 'SET',
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_aggregation_states (
            aggregation_id INT NOT NULL,
            status_key VARCHAR(30) NOT NULL,
            PRIMARY KEY (aggregation_id, status_key),
            FOREIGN KEY (aggregation_id) REFERENCES ddp_aggregations(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS material_categories (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            default_markup DECIMAL(5,3) NOT NULL DEFAULT 1.300,
            default_commission_markup DECIMAL(5,3) NOT NULL DEFAULT 1.100,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS cost_section_groups (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 1 — Dipendono da livello 0
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS employee_departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            department_id INT NOT NULL,
            is_responsible BOOLEAN DEFAULT FALSE,
            is_primary BOOLEAN DEFAULT FALSE,
            UNIQUE KEY UQ_EmpDept (employee_id, department_id),
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS employee_competences (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            department_id INT NOT NULL,
            notes VARCHAR(255) DEFAULT '',
            UNIQUE KEY UQ_EmpComp (employee_id, department_id),
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS absences (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            date_from DATE NOT NULL,
            date_to DATE NOT NULL,
            absence_type VARCHAR(20) DEFAULT 'VACATION',
            status VARCHAR(20) DEFAULT 'PENDING',
            approved_by INT NULL,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS cost_section_templates (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            section_type VARCHAR(20) NOT NULL DEFAULT 'IN_SEDE',
            group_id INT NOT NULL,
            is_default_project BOOLEAN NOT NULL DEFAULT TRUE,
            is_default_quote BOOLEAN NOT NULL DEFAULT TRUE,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            default_markup DECIMAL(5,3) NOT NULL DEFAULT 1.450,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (group_id) REFERENCES cost_section_groups(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS cost_section_template_departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            section_template_id INT NOT NULL,
            department_id INT NOT NULL,
            FOREIGN KEY (section_template_id) REFERENCES cost_section_templates(id) ON DELETE CASCADE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS catalog_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(100) NOT NULL,
            description VARCHAR(2000) DEFAULT '',
            category VARCHAR(255) DEFAULT '',
            subcategory VARCHAR(255) DEFAULT '',
            unit VARCHAR(50) DEFAULT 'PZ',
            unit_cost DECIMAL(10,4) DEFAULT 0,
            list_price DECIMAL(10,4) DEFAULT 0,
            supplier_id INT NULL,
            supplier_code VARCHAR(100) DEFAULT '',
            manufacturer VARCHAR(255) DEFAULT '',
            barcode VARCHAR(50) DEFAULT '',
            notes TEXT,
            is_active BOOLEAN DEFAULT TRUE,
            easyfatt_id INT NULL,
            atec_code VARCHAR(15) NULL,
            codex_item_id INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_CatalogItem_Code (code),
            INDEX IX_CatalogItems_Description (description(255)),
            INDEX IX_CatalogItems_AtecCode (atec_code),
            FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE SET NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS notifications (
            id INT AUTO_INCREMENT PRIMARY KEY,
            notification_type VARCHAR(30) NOT NULL,
            severity VARCHAR(10) NOT NULL DEFAULT 'INFO',
            title VARCHAR(200) NOT NULL,
            message VARCHAR(500) NOT NULL DEFAULT '',
            reference_type VARCHAR(20) NOT NULL DEFAULT '',
            reference_id INT NOT NULL DEFAULT 0,
            project_id INT NULL,
            created_by INT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            INDEX idx_type (notification_type),
            INDEX idx_created (created_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS notification_recipients (
            id INT AUTO_INCREMENT PRIMARY KEY,
            notification_id INT NOT NULL,
            employee_id INT NOT NULL,
            is_read BOOLEAN NOT NULL DEFAULT FALSE,
            read_at DATETIME NULL,
            FOREIGN KEY (notification_id) REFERENCES notifications(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            INDEX idx_emp_unread (employee_id, is_read),
            INDEX idx_notif (notification_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 2 — Dipendono da livello 1
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS phase_templates (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            category VARCHAR(50) DEFAULT '',
            department_id INT NULL,
            cost_section_template_id INT NULL,
            sort_order INT DEFAULT 0,
            is_default BOOLEAN DEFAULT TRUE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL,
            FOREIGN KEY (cost_section_template_id) REFERENCES cost_section_templates(id) ON DELETE SET NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 3 — projects
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS projects (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(20) NOT NULL,
            title VARCHAR(300) NOT NULL,
            customer_id INT NOT NULL,
            pm_id INT NOT NULL,
            description TEXT,
            start_date DATE,
            end_date_planned DATE,
            end_date_actual DATE NULL,
            budget_total DECIMAL(12,2) DEFAULT 0,
            budget_hours_total DECIMAL(8,1) DEFAULT 0,
            revenue DECIMAL(12,2) DEFAULT 0,
            actual_travel_cost DECIMAL(12,2) NOT NULL DEFAULT 0,
            status VARCHAR(20) DEFAULT 'DRAFT',
            priority VARCHAR(20) DEFAULT 'MEDIUM',
            server_path VARCHAR(500) DEFAULT '',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            FOREIGN KEY (customer_id) REFERENCES customers(id),
            FOREIGN KEY (pm_id) REFERENCES employees(id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 4 — Dipendono da projects
        // ══════════════════════════════════════════════════════════
        // Tabelle offer_* rimosse (legacy — sostituite da quote_*)

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_phases (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            phase_template_id INT NOT NULL,
            department_id INT NULL,
            custom_name VARCHAR(200) DEFAULT '',
            budget_hours DECIMAL(8,1) DEFAULT 0,
            budget_cost DECIMAL(12,2) DEFAULT 0,
            status VARCHAR(20) DEFAULT 'NOT_STARTED',
            progress_pct INT DEFAULT 0,
            sort_order INT DEFAULT 0,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (phase_template_id) REFERENCES phase_templates(id),
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL,
            INDEX idx_pp_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cashflow (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL UNIQUE,
            payment_amount DECIMAL(12,2) DEFAULT 0,
            month_count INT DEFAULT 13,
            start_date DATE NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cashflow_categories (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            name VARCHAR(200) NOT NULL,
            total_amount DECIMAL(12,2) DEFAULT 0,
            notes VARCHAR(500) DEFAULT '',
            sort_order INT DEFAULT 0,
            linked_source VARCHAR(100) NULL,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_pcc_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cashflow_data (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            data_type VARCHAR(20) NOT NULL,
            ref_id INT DEFAULT 0,
            month_number INT NOT NULL,
            num_value DECIMAL(12,2) DEFAULT 0,
            date_value DATE NULL,
            UNIQUE KEY UQ_CfData (project_id, data_type, ref_id, month_number),
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_chats (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            title VARCHAR(200) NOT NULL,
            created_by INT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (created_by) REFERENCES employees(id),
            INDEX idx_pch_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_chat_participants (
            id INT AUTO_INCREMENT PRIMARY KEY,
            chat_id INT NOT NULL,
            employee_id INT NOT NULL,
            last_read_message_id INT DEFAULT 0,
            added_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_ChatPart (chat_id, employee_id),
            FOREIGN KEY (chat_id) REFERENCES project_chats(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_chat_messages (
            id INT AUTO_INCREMENT PRIMARY KEY,
            chat_id INT NOT NULL,
            employee_id INT NOT NULL,
            message TEXT NOT NULL,
            has_attachment BOOLEAN DEFAULT FALSE,
            attachment_name VARCHAR(300) DEFAULT '',
            attachment_path VARCHAR(500) DEFAULT '',
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (chat_id) REFERENCES project_chats(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id),
            INDEX idx_pcm_chat (chat_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS bom_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            project_phase_id INT NULL,
            catalog_item_id INT NULL,
            part_number VARCHAR(100) DEFAULT '',
            description VARCHAR(300) DEFAULT '',
            unit VARCHAR(50) DEFAULT 'PZ',
            quantity DECIMAL(10,3) DEFAULT 0,
            unit_cost DECIMAL(10,2) DEFAULT 0,
            supplier_id INT NULL,
            manufacturer VARCHAR(200) DEFAULT '',
            item_status VARCHAR(20) DEFAULT 'VER',
            requested_by VARCHAR(100) DEFAULT '',
            danea_ref VARCHAR(100) DEFAULT '',
            danea_order_iddoc INT NULL,
            purchase_order VARCHAR(100) DEFAULT '',
            date_needed DATE,
            date_ordered DATE,
            date_received DATE,
            destination VARCHAR(200) DEFAULT '',
            destination_spec VARCHAR(200) DEFAULT '',
            ddp_type VARCHAR(20) DEFAULT 'COMMERCIAL',
            atec_code VARCHAR(15) NULL,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE SET NULL,
            FOREIGN KEY (catalog_item_id) REFERENCES catalog_items(id) ON DELETE SET NULL,
            INDEX idx_bom_project (project_id),
            INDEX IX_BomItems_AtecCode (atec_code)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Distinta DDP Officina (particolari meccanici, template "Mod. DDP - OFFICINA"): tabella dedicata,
        // schema allineato al Codex (codici 101). NESSUNA FK verso codex_items per scelta: codice/
        // descrizione/fornitore/costo sono uno snapshot denormalizzato al momento della scelta dal picker
        // (la riga DDP non deve cambiare se il Codex viene aggiornato). Stati e destinazioni CONDIVISI
        // con la DDP commerciale (ddp_statuses / ddp_destinations). updated_at = token di concorrenza
        // ottimistica (pattern v5).
        c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_officina_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            part_number VARCHAR(100) DEFAULT '',
            description VARCHAR(300) DEFAULT '',
            quantity DECIMAL(10,3) DEFAULT 0,
            quantity_produced DECIMAL(10,3) NOT NULL DEFAULT 0,
            material VARCHAR(200) DEFAULT '',
            treatment VARCHAR(200) DEFAULT '',
            supplier_name VARCHAR(200) DEFAULT '',
            unit_cost DECIMAL(10,2) DEFAULT 0,
            item_status VARCHAR(20) DEFAULT 'DO',
            requested_by VARCHAR(100) DEFAULT '',
            danea_ref VARCHAR(100) DEFAULT '',
            date_needed DATE,
            order_date DATE NULL,
            destination VARCHAR(200) DEFAULT '',
            destination_spec VARCHAR(200) DEFAULT '',
            notes TEXT,
            parent_officina_item_id INT NULL,
            composition_qty DECIMAL(10,3) NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (parent_officina_item_id) REFERENCES ddp_officina_items(id) ON DELETE SET NULL,
            INDEX idx_ddpoff_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS documents (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            project_phase_id INT NULL,
            title VARCHAR(300) DEFAULT '',
            file_path VARCHAR(500) DEFAULT '',
            file_url VARCHAR(500) DEFAULT '',
            file_type VARCHAR(50) DEFAULT '',
            category VARCHAR(20) DEFAULT 'OTHER',
            uploaded_by VARCHAR(100) DEFAULT '',
            file_size BIGINT DEFAULT 0,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_doc_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS extra_costs (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            project_phase_id INT NULL,
            employee_id INT NULL,
            cost_date DATE,
            category VARCHAR(20) DEFAULT 'OTHER',
            description VARCHAR(300) DEFAULT '',
            amount DECIMAL(10,2) DEFAULT 0,
            receipt_ref VARCHAR(100) DEFAULT '',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            INDEX idx_ec_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cost_sections (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            template_id INT NULL,
            name VARCHAR(200) NOT NULL,
            section_type VARCHAR(20) NOT NULL DEFAULT 'IN_SEDE',
            group_name VARCHAR(100) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
            contingency_pct DECIMAL(7,4) NOT NULL DEFAULT 0,
            margin_pct DECIMAL(7,4) NOT NULL DEFAULT 0,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (template_id) REFERENCES cost_section_templates(id) ON DELETE SET NULL,
            INDEX idx_pcs_project (project_id, is_enabled),
            INDEX idx_pcs_template (template_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cost_section_departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_cost_section_id INT NOT NULL,
            department_id INT NOT NULL,
            FOREIGN KEY (project_cost_section_id) REFERENCES project_cost_sections(id) ON DELETE CASCADE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_cost_resources (
            id INT AUTO_INCREMENT PRIMARY KEY,
            section_id INT NOT NULL,
            employee_id INT NULL,
            resource_name VARCHAR(200) NOT NULL DEFAULT '',
            work_days DECIMAL(8,1) NOT NULL DEFAULT 0,
            hours_per_day DECIMAL(4,1) NOT NULL DEFAULT 8,
            hourly_cost DECIMAL(8,2) NOT NULL DEFAULT 0,
            markup_value DECIMAL(5,3) NOT NULL DEFAULT 1.450,
            num_trips INT NOT NULL DEFAULT 0,
            km_per_trip DECIMAL(8,1) NOT NULL DEFAULT 0,
            cost_per_km DECIMAL(6,3) NOT NULL DEFAULT 0,
            daily_food DECIMAL(8,2) NOT NULL DEFAULT 0,
            daily_hotel DECIMAL(8,2) NOT NULL DEFAULT 0,
            allowance_days INT NOT NULL DEFAULT 0,
            daily_allowance DECIMAL(8,2) NOT NULL DEFAULT 0,
            sort_order INT NOT NULL DEFAULT 0,
            FOREIGN KEY (section_id) REFERENCES project_cost_sections(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE SET NULL,
            INDEX idx_pcr_section (section_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_material_sections (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            category_id INT NULL,
            name VARCHAR(200) NOT NULL,
            markup_value DECIMAL(5,3) NOT NULL DEFAULT 1.300,
            commission_markup DECIMAL(5,3) NOT NULL DEFAULT 1.100,
            sort_order INT NOT NULL DEFAULT 0,
            is_enabled BOOLEAN NOT NULL DEFAULT TRUE,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (category_id) REFERENCES material_categories(id) ON DELETE SET NULL,
            INDEX idx_pms_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_material_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            section_id INT NOT NULL,
            parent_item_id INT NULL,
            description VARCHAR(500) NOT NULL DEFAULT '',
            quantity DECIMAL(10,3) NOT NULL DEFAULT 0,
            unit_cost DECIMAL(10,4) NOT NULL DEFAULT 0,
            markup_value DECIMAL(5,3) NOT NULL DEFAULT 1.300,
            item_type VARCHAR(20) NOT NULL DEFAULT 'MATERIAL',
            sort_order INT NOT NULL DEFAULT 0,
            FOREIGN KEY (section_id) REFERENCES project_material_sections(id) ON DELETE CASCADE,
            FOREIGN KEY (parent_item_id) REFERENCES project_material_items(id) ON DELETE CASCADE,
            INDEX idx_pmi_section (section_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_pricing (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL UNIQUE,
            contingency_pct DECIMAL(7,4) NOT NULL DEFAULT 0.1300,
            negotiation_margin_pct DECIMAL(7,4) NOT NULL DEFAULT 0.0500,
            travel_markup DECIMAL(5,3) NOT NULL DEFAULT 1.000,
            allowance_markup DECIMAL(5,3) NOT NULL DEFAULT 1.000,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // LIVELLO 5 — Dipendono da project_phases
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS phase_assignments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_phase_id INT NOT NULL,
            employee_id INT NOT NULL,
            assign_role VARCHAR(20) DEFAULT 'MEMBER',
            planned_hours DECIMAL(8,1) DEFAULT 0,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_phase_id) REFERENCES project_phases(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id),
            INDEX idx_pa_phase (project_phase_id),
            INDEX idx_pa_employee (employee_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS timesheet_entries (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            project_phase_id INT NOT NULL,
            work_date DATE NOT NULL,
            hours DECIMAL(4,1) DEFAULT 0,
            entry_type VARCHAR(20) DEFAULT 'REGULAR',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (employee_id) REFERENCES employees(id),
            FOREIGN KEY (project_phase_id) REFERENCES project_phases(id),
            INDEX idx_te_phase_date (project_phase_id, work_date),
            INDEX idx_te_employee (employee_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // STANDALONE — nessuna FK verso tabelle app
        // (eccezione: codex_compositions.child_catalog_id → catalog_items, FK aggiunta in v10)
        // ══════════════════════════════════════════════════════════

        // remote_id NULL = codice generato localmente (ConfirmReservation), non ancora presente sul
        // Codex remoto. Il sync (CodexSyncService) fa UPSERT per remote_id: gli id locali sono stabili
        // tra i sync, quindi le FK di codex_compositions/codex_item_references restano valide.
        // codice_nuovo = NUOVA CODIFICA (ampliamento Codex 21/07/2026): di proprietà di ATEC PM,
        // il sync remoto NON la tocca mai; si compila SOLO a mano (ricodifica manuale dei 201xxx,
        // famiglie nuove 201 generici / 211 elettrici / 221 pneumatici, …). NULL = non ricodificato.
        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            remote_id INT NULL,
            codice VARCHAR(15) NOT NULL DEFAULT '',
            codice_nuovo VARCHAR(15) NULL,
            code_forn VARCHAR(200) NOT NULL DEFAULT '',
            fornitore VARCHAR(40) NOT NULL DEFAULT '',
            prezzo_forn DECIMAL(7,2) NOT NULL DEFAULT 0,
            iva VARCHAR(3) NOT NULL DEFAULT '',
            produttore VARCHAR(100) NOT NULL DEFAULT '',
            data DATE NULL,
            descr VARCHAR(200) NOT NULL DEFAULT '',
            note TEXT,
            categoria VARCHAR(200) NOT NULL DEFAULT '',
            barcode VARCHAR(200) NOT NULL DEFAULT '',
            tipologia VARCHAR(200) NOT NULL DEFAULT '',
            extra1 VARCHAR(200) NOT NULL DEFAULT '',
            extra2 VARCHAR(200) NOT NULL DEFAULT '',
            extra3 VARCHAR(200) NOT NULL DEFAULT '',
            code_prod VARCHAR(200) NOT NULL DEFAULT '',
            spec VARCHAR(200) NOT NULL DEFAULT '',
            oper INT NOT NULL DEFAULT 0,
            um VARCHAR(10) NOT NULL DEFAULT '',
            ubicazione VARCHAR(200) NOT NULL DEFAULT '',
            codexforn VARCHAR(200) NOT NULL DEFAULT '',
            synced_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY uq_codex_remote_id (remote_id),
            UNIQUE KEY uq_codex_codice_nuovo (codice_nuovo),
            INDEX idx_codice (codice),
            INDEX idx_fornitore (fornitore),
            INDEX idx_categoria (categoria)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_reservations (
            id INT AUTO_INCREMENT PRIMARY KEY,
            prefix VARCHAR(10) NOT NULL,
            reserved_code VARCHAR(50) NOT NULL,
            reserved_by VARCHAR(100) NOT NULL,
            reserved_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            expires_at DATETIME NOT NULL,
            status ENUM('RESERVED','CONFIRMED','RELEASED') NOT NULL DEFAULT 'RESERVED',
            INDEX idx_prefix_status (prefix, status),
            INDEX idx_expires (expires_at, status)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Figlio = articolo Codex (child_codex_id) OPPURE articolo catalogo (child_catalog_id):
        // esattamente uno dei due è valorizzato. La FK verso catalog_items è aggiunta dalla
        // migrazione v10 perché catalog_items viene creata più avanti (QuoteDbService.InitTables).
        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_compositions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            parent_codex_id INT NOT NULL,
            child_codex_id INT NULL,
            child_catalog_id INT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (parent_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            FOREIGN KEY (child_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            INDEX idx_parent (parent_codex_id),
            INDEX idx_child (child_codex_id),
            INDEX idx_cc_catalog (child_catalog_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_item_references (
            id INT AUTO_INCREMENT PRIMARY KEY,
            source_codex_id INT NOT NULL,
            ref_codex_id INT NOT NULL,
            ref_type VARCHAR(10) NOT NULL COMMENT '201 o 401',
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (source_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            FOREIGN KEY (ref_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            UNIQUE KEY uq_source_ref (source_codex_id, ref_type),
            INDEX idx_source (source_codex_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // TEMPLATE CARTELLE COMMESSE
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_template_folders (
            id INT AUTO_INCREMENT PRIMARY KEY,
            parent_id INT NULL,
            name VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (parent_id) REFERENCES project_template_folders(id) ON DELETE CASCADE,
            INDEX idx_ptf_parent (parent_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_template_files (
            id INT AUTO_INCREMENT PRIMARY KEY,
            folder_id INT NOT NULL,
            file_name VARCHAR(300) NOT NULL,
            disk_path VARCHAR(500) NOT NULL,
            file_size BIGINT NOT NULL DEFAULT 0,
            uploaded_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            uploaded_by INT NULL,
            FOREIGN KEY (folder_id) REFERENCES project_template_folders(id) ON DELETE CASCADE,
            FOREIGN KEY (uploaded_by) REFERENCES employees(id) ON DELETE SET NULL,
            INDEX idx_ptfi_folder (folder_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // SISTEMA PERMESSI A LIVELLI (stile VisiWin7)
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS auth_levels (
            id INT AUTO_INCREMENT PRIMARY KEY,
            level_value INT NOT NULL UNIQUE,
            role_name VARCHAR(30) NOT NULL UNIQUE,
            display_name VARCHAR(50) NOT NULL,
            sort_order INT DEFAULT 0
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS auth_features (
            id INT AUTO_INCREMENT PRIMARY KEY,
            feature_key VARCHAR(100) NOT NULL UNIQUE,
            display_name VARCHAR(100) NOT NULL,
            category VARCHAR(50) DEFAULT 'navigation',
            min_level INT NOT NULL DEFAULT 0,
            behavior VARCHAR(20) DEFAULT 'HIDDEN'
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ══════════════════════════════════════════════════════════
        // SEED DATA
        // ══════════════════════════════════════════════════════════

        // Seed livelli autorizzazione
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM auth_levels") == 0)
        {
            c.Execute(@"INSERT INTO auth_levels (level_value, role_name, display_name, sort_order) VALUES
                (0, 'TECH',          'Tecnico',          0),
                (1, 'RESP_REPARTO',  'Resp. Reparto',    1),
                (2, 'PM',            'Project Manager',  2),
                (3, 'ADMIN',         'Amministratore',   3),
                (4, 'DEVELOPER',     'Developer',        4)");
            Console.WriteLine("[DB] Seed auth_levels completato.");
        }

        // Seed feature con livello minimo
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM auth_features") == 0)
        {
            c.Execute(@"INSERT INTO auth_features (feature_key, display_name, category, min_level, behavior) VALUES
                ('nav.dashboard',         'Dashboard',               'navigation', 0, 'HIDDEN'),
                ('nav.timesheet',         'Timesheet',               'navigation', 0, 'HIDDEN'),
                ('nav.commesse',          'Commesse',                'navigation', 0, 'HIDDEN'),
                ('nav.preventivi',        'Preventivi',              'navigation', 2, 'HIDDEN'),
                ('nav.cat_preventivi',    'Cat. Preventivi',         'navigation', 2, 'HIDDEN'),
                ('nav.clienti',           'Clienti',                 'navigation', 2, 'HIDDEN'),
                ('nav.fornitori',         'Fornitori',               'navigation', 2, 'HIDDEN'),
                ('nav.catalogo',          'Catalogo Articoli',       'navigation', 1, 'HIDDEN'),
                ('nav.codex',             'Codex Articoli',          'navigation', 1, 'HIDDEN'),
                ('nav.codex_composizione','Composizione Codex',      'navigation', 1, 'HIDDEN'),
                ('nav.utenti',            'Utenti',                  'navigation', 3, 'HIDDEN'),
                ('nav.config_sezioni',    'Configurazione Sezioni',  'navigation', 3, 'HIDDEN'),
                ('nav.ddp_destinazioni',  'Destinazioni DDP',        'navigation', 1, 'HIDDEN'),
                ('nav.project_templates', 'Template Commesse',       'navigation', 2, 'HIDDEN'),
                ('nav.backup',            'Backup DB',               'navigation', 3, 'HIDDEN'),
                ('nav.permessi',          'Gestione Permessi',       'navigation', 3, 'HIDDEN'),
                ('nav.digest_email',      'Digest Email',            'navigation', 3, 'HIDDEN'),
                ('nav.anagrafica_attivita','Anagrafica Attività',    'navigation', 2, 'HIDDEN'),
                ('nav.scadenze',          'Scadenze',                'navigation', 2, 'HIDDEN'),
                ('action.create_project', 'Crea Commessa',           'action',     2, 'DISABLED'),
                ('action.edit_project',   'Modifica Commessa',       'action',     2, 'DISABLED'),
                ('action.delete_project', 'Elimina Commessa',        'action',     3, 'HIDDEN'),
                ('data.budget',           'Dati Budget',             'data',       2, 'HIDDEN'),
                ('data.costs',            'Dati Costi',              'data',       2, 'HIDDEN'),
                ('data.revenue',          'Dati Ricavi',             'data',       2, 'HIDDEN'),
                ('data.hourly_cost',      'Costo Orario',            'data',       3, 'HIDDEN'),
                ('resources.edit',        'Modifica Allocazioni Risorse', 'action', 1, 'DISABLED')");
            Console.WriteLine("[DB] Seed auth_features completato.");
        }

        // Seed tariffe trasferta predefinite
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM tariff_options") == 0)
        {
            c.Execute(@"INSERT INTO tariff_options (tariff_type, value) VALUES
                ('COST_PER_KM', 0.900), ('COST_PER_KM', 1.100),
                ('DAILY_FOOD', 25.000), ('DAILY_FOOD', 50.000), ('DAILY_FOOD', 80.000),
                ('DAILY_HOTEL', 80.000), ('DAILY_HOTEL', 100.000), ('DAILY_HOTEL', 120.000),
                ('DAILY_ALLOWANCE', 20.000), ('DAILY_ALLOWANCE', 40.000), ('DAILY_ALLOWANCE', 60.000)");
            Console.WriteLine("[DB] Seed tariff_options completato.");
        }

        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM app_config") == 0)
        {
            c.Execute(@"INSERT INTO app_config (config_key, config_value, description) VALUES
                ('BasePath', 'C:\\ATEC_Commesse', 'Percorso base cartelle commesse')");
        }

        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM employees") == 0)
        {
            string adminHash = BCrypt.Net.BCrypt.HashPassword("admin");
            c.Execute(@"INSERT INTO employees (first_name, last_name, email, username, password_hash, user_role, status)
                VALUES ('Admin', 'ATEC', 'admin@atec.it', 'admin', @Hash, 'ADMIN', 'ACTIVE')",
                new { Hash = adminHash });
            Console.WriteLine("[DB] Utente admin di default creato con bcrypt (user: admin / pwd: admin)");
        }

        // ══════════════════════════════════════════════════════════
        // MIGRAZIONI su tabelle esistenti
        // ══════════════════════════════════════════════════════════
try
        {
            c.Execute(@"CREATE OR REPLACE VIEW v_timesheet_with_section AS
                SELECT
                    te.id              AS entry_id,
                    te.employee_id,
                    te.project_phase_id,
                    te.work_date,
                    te.hours,
                    te.entry_type,
                    pp.project_id,
                    COALESCE(NULLIF(pp.custom_name,''), pt.name) AS phase_name,
                    pt.cost_section_template_id,
                    CONCAT(emp.first_name, ' ', emp.last_name) AS employee_name,
                    COALESCE(d.hourly_cost, 0) AS hourly_cost
                FROM timesheet_entries te
                JOIN project_phases pp   ON pp.id = te.project_phase_id
                JOIN phase_templates pt  ON pt.id = pp.phase_template_id
                JOIN employees emp       ON emp.id = te.employee_id
                LEFT JOIN (
                    SELECT employee_id, MIN(department_id) AS department_id
                    FROM employee_departments
                    GROUP BY employee_id
                ) ed ON ed.employee_id = emp.id
                LEFT JOIN departments d  ON d.id = ed.department_id");
            Console.WriteLine("[DB Migration] View v_timesheet_with_section creata/aggiornata.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB Migration] Warning view: {ex.Message}");
        }

        // Modulo Preventivi/Catalogo
        new QuoteDbService(this).InitTables(c);
        new QuoteDbService(this).ApplyMigrations(c);

        // Modulo Gamma Robot (distinta schede/componenti per robot+quadro)
        new GammaRobotDbService(this).InitTables(c);

        // Modulo MoM (verbali di riunione → action item)
        new MoMDbService(this).InitTables(c);

        // Modulo Check list / Attività (attività per commessa reale o gruppo generico)
        new CheckListDbService(this).InitTables(c);

        // Moduli Anagrafica attività (catalogo globale) + Milestone (pianificazione per-commessa)
        new MilestonesDbService(this).InitTables(c);

        // Modulo Gestione Risorse (allocazioni op/flex/ferie su dipendenti)
        new ResourcesDbService(this).InitTables(c);

        // Modulo SAL / Fatturazione a stati d'avanzamento (tabelle + seed anagrafiche, idempotenti)
        SalDbService salDb = new SalDbService(this);
        salDb.InitTables(c);
        salDb.SeedConditions(c);
        salDb.SeedSapCausali(c);
        salDb.SeedPaymentStates(c);

        // Modulo Lavorazioni meccaniche (work requests, griglia per commessa + pannello globale)
        EnsureWorkRequestsTable(c);

        // Migrazioni versionati (dopo CREATE TABLE idempotente in dev)
        int devVersion = GetSchemaVersion(c);
        ApplyVersionedMigrations(c, devVersion);

        int tableCount = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE()");
        _logger.LogInformation("[InitDatabase] Schema verificato: {TableCount} tabelle presenti", tableCount);
    }

    // ══════════════════════════════════════════════════════════════
    // SCHEMA VERSIONING
    // ══════════════════════════════════════════════════════════════

    private const int LatestSchemaVersion = 51;

    /// <summary>
    /// Tabella del modulo Lavorazioni meccaniche (project_work_requests) — idempotente.
    /// Chiamata sia dal percorso dev (InitDatabase) sia dalla migrazione v20 (produzione).
    /// Le offerte RDO sono denormalizzate in JSON nella colonna `rfqs` (max 4 per riga);
    /// i timestamp delivered_at/treatment_confirmed_at/created_at sono Unix millis (BIGINT).
    /// `row_version` è il concurrency token (pattern MoM/Check list): la PUT lo confronta
    /// e ogni scrittura lo incrementa.
    /// </summary>
    private static void EnsureWorkRequestsTable(MySqlConnection c)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_work_requests (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            request_date DATE NULL,
            description TEXT NULL,
            type VARCHAR(20) NOT NULL DEFAULT '',
            priority TINYINT NOT NULL DEFAULT 2,
            availability_date DATE NULL,
            notes TEXT NULL,
            is_ultra_critical TINYINT(1) NOT NULL DEFAULT 0,
            is_delivered TINYINT(1) NOT NULL DEFAULT 0,
            delivered_at BIGINT NULL,
            is_staging TINYINT(1) NOT NULL DEFAULT 0,
            rfqs TEXT NULL,
            po_supplier VARCHAR(200) NOT NULL DEFAULT '',
            po_number VARCHAR(100) NOT NULL DEFAULT '',
            po_date DATE NULL,
            has_treatment TINYINT(1) NOT NULL DEFAULT 0,
            treatment_date DATE NULL,
            treatment_notes TEXT NULL,
            is_treatment_confirmed TINYINT(1) NOT NULL DEFAULT 0,
            treatment_confirmed_at BIGINT NULL,
            row_version INT NOT NULL DEFAULT 0,
            created_at BIGINT NOT NULL DEFAULT 0,
            ddp_officina_item_id INT NULL,
            CONSTRAINT fk_pwr_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            CONSTRAINT fk_pwr_ddp_officina FOREIGN KEY (ddp_officina_item_id)
                REFERENCES ddp_officina_items(id) ON DELETE SET NULL,
            UNIQUE KEY uq_pwr_ddp_officina (ddp_officina_item_id),
            KEY idx_pwr_project (project_id),
            KEY idx_pwr_staging (is_staging),
            KEY idx_pwr_delivered (is_delivered)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Installazioni dove la tabella è nata senza row_version (v20 già applicata): ALTER idempotente.
        int hasRowVersion = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'project_work_requests' AND COLUMN_NAME = 'row_version'");
        if (hasRowVersion == 0)
            c.Execute("ALTER TABLE project_work_requests ADD COLUMN row_version INT NOT NULL DEFAULT 0 AFTER treatment_confirmed_at");
    }

    private static void EnsureSchemaMigrationsTable(MySqlConnection c)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS schema_migrations (
            version INT NOT NULL PRIMARY KEY,
            description VARCHAR(200) NOT NULL DEFAULT '',
            applied_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }

    private static int GetSchemaVersion(MySqlConnection c)
    {
        return c.ExecuteScalar<int>("SELECT COALESCE(MAX(version), 0) FROM schema_migrations");
    }

    private void ApplyVersionedMigrations(MySqlConnection c, int currentVersion)
    {
        if (currentVersion >= LatestSchemaVersion)
        {
            _logger.LogInformation("[Migrations] Schema aggiornato (v{Version})", currentVersion);
            return;
        }

        // v1: aggiunge project_material_items.is_active (allineamento quote)
        if (currentVersion < 1)
        {
            try
            {
                bool hasColumn = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE()
                      AND table_name = 'project_material_items'
                      AND column_name = 'is_active'") > 0;

                if (!hasColumn)
                {
                    c.Execute("ALTER TABLE project_material_items ADD COLUMN is_active BOOLEAN NOT NULL DEFAULT TRUE");
                    _logger.LogInformation("[Migration v1] Aggiunta colonna project_material_items.is_active");
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (1, 'project_material_items.is_active')");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v1] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v2: rimuove feature keys orfane (nav.preventivi_nuovo, nav.offerte)
        if (currentVersion < 2)
        {
            try
            {
                c.Execute("DELETE FROM auth_features WHERE feature_key IN ('nav.preventivi_nuovo', 'nav.offerte')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (2, 'cleanup orphan feature keys')");
                _logger.LogInformation("[Migration v2] Rimosse feature keys orfane");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v2] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v3: aggiunge feature key nav.project_templates
        if (currentVersion < 3)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.project_templates', 'Template Commesse', 'navigation', 2, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (3, 'nav.project_templates feature key')");
                _logger.LogInformation("[Migration v3] Aggiunta feature key nav.project_templates");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v3] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v4: feature key resources.edit (gating scrittura allocazioni risorse: RESP_REPARTO+ )
        if (currentVersion < 4)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('resources.edit', 'Modifica Allocazioni Risorse', 'action', 1, 'DISABLED')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (4, 'resources.edit feature key')");
                _logger.LogInformation("[Migration v4] Aggiunta feature key resources.edit");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v4] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v5: bom_items.updated_at — concorrenza ottimistica della distinta DDP (real-time + anti lost-update)
        if (currentVersion < 5)
        {
            try
            {
                bool hasColumn = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE()
                      AND table_name = 'bom_items'
                      AND column_name = 'updated_at'") > 0;

                if (!hasColumn)
                {
                    c.Execute("ALTER TABLE bom_items ADD COLUMN updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP");
                    _logger.LogInformation("[Migration v5] Aggiunta colonna bom_items.updated_at");
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (5, 'bom_items.updated_at')");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v5] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v6: causali DDP reali — rimuove le 12 chiavi generiche seedate in precedenza (TO_ORDER, ecc.).
        // Il seed con le 17 causali reali gira ad ogni avvio (INSERT IGNORE) nella creazione tabelle.
        if (currentVersion < 6)
        {
            try
            {
                c.Execute(@"DELETE FROM ddp_statuses WHERE status_key IN
                    ('TO_ORDER','ORDERED','DELIVERED','PARTIAL','TO_BUILD','RFQ',
                     'TO_CHECK','CANCELLED','ASSIGNED','SHIPPED','TECH_CHECK','TO_MODULA')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (6, 'ddp_statuses causali reali')");
                _logger.LogInformation("[Migration v6] Rimosse le causali DDP generiche (sostituite dal set reale)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v6] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v7: seed aggregazioni di stato DDP (matrice dall'Excel V53.1). Una sola volta: dopo, le modifiche
        // dell'utente persistono (NON ri-seedato ad ogni avvio, a differenza di un INSERT IGNORE nella creazione tabelle).
        if (currentVersion < 7)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO ddp_aggregations (code, name, description, kind, sort_order) VALUES
                    ('A1','Conteggio per stato','Tutti gli stati conteggiati singolarmente (base di tutte le viste)','ALL',1),
                    ('A2','Materiale Consegnato','CON+COS+DISP+ASS+MOD','SET',2),
                    ('A3','Mat. Par. Cons.','Parzialmente consegnato/costruito (PAR)','SET',3),
                    ('A4','Materiale in Consegna','Righe con Data prev. e stato NON consegnato (finestra/ritardo)','DATED',4),
                    ('A5','Stati Avanzamento (7 card)','VER · CHEK · DO · RO · IO · DDP Stop(ANN+SOSP+RAM+SOST) · Sped-Mod(SPED+MOD)','SUBGROUPS',5),
                    ('A6','Feedback Acquisti','VER+CHEK+DO+RO+PAR','SET',6),
                    ('A7','Feedback Magazzino','CON+COS+DISP+PAR+MOD','SET',7),
                    ('A8','Esclusione Dati Mancanti','Stati esclusi dall analisi di completezza','SET',8)");

                var seed = new Dictionary<string, string[]>
                {
                    ["A1"] = new[] { "VER", "DISP", "RO", "DO", "IO", "PAR", "CON", "COS", "ASS", "CHEK", "SPED", "MOD", "ANN", "RAM", "SOSP", "SOST", "ND" },
                    ["A2"] = new[] { "CON", "COS", "DISP", "ASS", "MOD" },
                    ["A3"] = new[] { "PAR" },
                    ["A4"] = new[] { "VER", "RO", "DO", "IO", "PAR", "CHEK", "SPED", "ANN", "RAM", "SOSP", "SOST", "ND" },
                    ["A5"] = new[] { "VER", "CHEK", "DO", "RO", "IO", "ANN", "SOSP", "RAM", "SOST", "SPED", "MOD" },
                    ["A6"] = new[] { "VER", "CHEK", "DO", "RO", "PAR" },
                    ["A7"] = new[] { "CON", "COS", "DISP", "PAR", "MOD" },
                    ["A8"] = new[] { "ANN", "SOSP", "RAM", "SOST", "DO", "CHEK", "IO", "RO" }
                };
                foreach (KeyValuePair<string, string[]> kv in seed)
                {
                    int aggId = c.ExecuteScalar<int>("SELECT id FROM ddp_aggregations WHERE code=@C", new { C = kv.Key });
                    foreach (string st in kv.Value)
                        c.Execute("INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A,@S)",
                            new { A = aggId, S = st });
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (7, 'ddp aggregazioni stato')");
                _logger.LogInformation("[Migration v7] Seed aggregazioni di stato DDP (A1-A8)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v7] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v8: tabella dedicata DDP Officina (particolari meccanici, vedi commento CREATE TABLE in InitDatabase).
        if (currentVersion < 8)
        {
            try
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
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (8, 'ddp_officina_items')");
                _logger.LogInformation("[Migration v8] Creata tabella ddp_officina_items (distinta officina)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v8] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v9: sync Codex non distruttivo. remote_id diventa NULL-able (NULL = codice generato
        // localmente, non ancora presente sul Codex remoto) e UNIQUE, così CodexSyncService può fare
        // upsert per remote_id invece di DELETE+reinsert: gli id locali restano stabili tra i sync e
        // le FK ON DELETE CASCADE di codex_compositions/codex_item_references non svuotano più
        // composizioni e riferimenti a ogni sincronizzazione.
        if (currentVersion < 9)
        {
            try
            {
                // Dedup difensivo per remote_id prima dell'indice UNIQUE (esclusi i codici locali,
                // che condividono remote_id=0 e NON sono duplicati). La DELETE con self-join è O(n²)
                // senza indice su remote_id e col timeout default (30s) andava in timeout già a ~18k
                // righe: si esegue solo se servono davvero dedup, e con timeout esteso.
                int dupGroups = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM (
                        SELECT remote_id FROM codex_items
                        WHERE remote_id <> 0
                        GROUP BY remote_id HAVING COUNT(*) > 1) d");
                if (dupGroups > 0)
                    c.Execute(@"DELETE ci FROM codex_items ci
                        JOIN codex_items keep ON keep.remote_id = ci.remote_id AND keep.id < ci.id
                        WHERE ci.remote_id <> 0", commandTimeout: 600);

                c.Execute("ALTER TABLE codex_items MODIFY remote_id INT NULL", commandTimeout: 600);
                c.Execute("UPDATE codex_items SET remote_id = NULL WHERE remote_id = 0", commandTimeout: 600);

                bool hasIndex = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.statistics
                    WHERE table_schema = DATABASE()
                      AND table_name = 'codex_items'
                      AND index_name = 'uq_codex_remote_id'") > 0;
                if (!hasIndex)
                    c.Execute("ALTER TABLE codex_items ADD UNIQUE KEY uq_codex_remote_id (remote_id)", commandTimeout: 600);

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (9, 'codex_items.remote_id NULL + UNIQUE (sync upsert)')");
                _logger.LogInformation("[Migration v9] codex_items.remote_id NULL-able + UNIQUE: il sync Codex ora fa upsert");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v9] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v10: allinea codex_compositions allo schema usato da CodexController (figli da catalogo):
        // child_codex_id NULL-able + colonna child_catalog_id, che mancavano nel CREATE TABLE
        // (un DB nato da zero rompeva l'Editor Composizione). FK verso catalog_items aggiunta qui
        // perché in InitDatabase catalog_items viene creata dopo codex_compositions.
        if (currentVersion < 10)
        {
            try
            {
                bool childNullable = c.ExecuteScalar<string>(@"
                    SELECT IS_NULLABLE FROM information_schema.columns
                    WHERE table_schema = DATABASE()
                      AND table_name = 'codex_compositions'
                      AND column_name = 'child_codex_id'") == "YES";
                if (!childNullable)
                    c.Execute("ALTER TABLE codex_compositions MODIFY child_codex_id INT NULL");

                bool hasCatalogCol = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE()
                      AND table_name = 'codex_compositions'
                      AND column_name = 'child_catalog_id'") > 0;
                if (!hasCatalogCol)
                    c.Execute(@"ALTER TABLE codex_compositions
                        ADD COLUMN child_catalog_id INT NULL AFTER child_codex_id,
                        ADD INDEX idx_cc_catalog (child_catalog_id)");

                bool hasFk = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.table_constraints
                    WHERE table_schema = DATABASE()
                      AND table_name = 'codex_compositions'
                      AND constraint_name = 'fk_cc_catalog'") > 0;
                if (!hasFk)
                {
                    // Pulizia orfani prima della FK (DB esistenti con colonna aggiunta a mano)
                    c.Execute(@"DELETE FROM codex_compositions
                        WHERE child_catalog_id IS NOT NULL
                          AND child_catalog_id NOT IN (SELECT id FROM catalog_items)");
                    c.Execute(@"ALTER TABLE codex_compositions
                        ADD CONSTRAINT fk_cc_catalog FOREIGN KEY (child_catalog_id)
                        REFERENCES catalog_items(id) ON DELETE CASCADE");
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (10, 'codex_compositions.child_catalog_id + child_codex_id NULL')");
                _logger.LogInformation("[Migration v10] codex_compositions allineata (child_catalog_id, child_codex_id NULL-able)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v10] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v11: feature key nav.digest_email (voce di menu "Digest Email" — solo ADMIN)
        if (currentVersion < 11)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.digest_email', 'Digest Email', 'navigation', 3, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (11, 'nav.digest_email feature key')");
                _logger.LogInformation("[Migration v11] Aggiunta feature key nav.digest_email");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v11] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v12: Feedback Acquisti (nota + nascosto per DDP+stato) e Feedback Magazzino (nascosto per riga).
        // Stati tracciati = aggregazioni A6 (acquisti) / A7 (magazzino), già seedate in ddp_aggregations.
        if (currentVersion < 12)
        {
            try
            {
                c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_feedback_acquisti (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    project_id INT NOT NULL,
                    ddp_type VARCHAR(20) NOT NULL,
                    status_key VARCHAR(20) NOT NULL,
                    note TEXT,
                    hidden TINYINT(1) NOT NULL DEFAULT 0,
                    updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
                    UNIQUE KEY uq_ddp_feedback_acquisti (project_id, ddp_type, status_key),
                    FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

                c.Execute(@"CREATE TABLE IF NOT EXISTS ddp_feedback_magazzino_hidden (
                    id INT AUTO_INCREMENT PRIMARY KEY,
                    project_id INT NOT NULL,
                    ddp_type VARCHAR(20) NOT NULL,
                    item_id INT NOT NULL,
                    created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
                    UNIQUE KEY uq_ddp_feedback_magazzino (project_id, ddp_type, item_id),
                    FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
                ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (12, 'ddp_feedback_acquisti + ddp_feedback_magazzino_hidden')");
                _logger.LogInformation("[Migration v12] Create tabelle ddp_feedback_acquisti e ddp_feedback_magazzino_hidden");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v12] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v13: specifica destinazione sulle righe DDP + seed elenco standard destinazioni (demo V1).
        if (currentVersion < 13)
        {
            try
            {
                if (c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'bom_items' AND COLUMN_NAME = 'destination_spec'") == 0)
                {
                    c.Execute("ALTER TABLE bom_items ADD COLUMN destination_spec VARCHAR(200) NOT NULL DEFAULT '' AFTER destination");
                    _logger.LogInformation("[Migration v13] Aggiunta colonna bom_items.destination_spec");
                }

                if (c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'ddp_officina_items' AND COLUMN_NAME = 'destination_spec'") == 0)
                {
                    c.Execute("ALTER TABLE ddp_officina_items ADD COLUMN destination_spec VARCHAR(200) NOT NULL DEFAULT '' AFTER destination");
                    _logger.LogInformation("[Migration v13] Aggiunta colonna ddp_officina_items.destination_spec");
                }

                c.Execute("DELETE FROM ddp_destinations WHERE name IN ('DEMO', 'DUE', 'TRE')");

                for (int i = 0; i < DdpDestinationSeed.Names.Length; i++)
                {
                    string name = DdpDestinationSeed.Names[i];
                    c.Execute(@"
                        INSERT INTO ddp_destinations (name, sort_order, is_active)
                        SELECT @Name, 0, TRUE
                        WHERE NOT EXISTS (SELECT 1 FROM ddp_destinations WHERE name = @Name)",
                        new { Name = name });
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (13, 'ddp destination_spec + seed destinazioni standard')");
                _logger.LogInformation("[Migration v13] destination_spec + {Count} destinazioni standard seedate",
                    DdpDestinationSeed.Names.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v13] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v14: aggregazione A9 «Escluso da totale/conteggi» (membri default ANN/SOSP/RAM/SOST).
        // Gli stati membri di A9 non contano nei totali € né nei conteggi di scadenza. Configurabile
        // dall'admin in «Aggregazioni DDP» come le altre. Idempotente (INSERT IGNORE), non sovrascrive
        // eventuali membri già modificati.
        if (currentVersion < 14)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO ddp_aggregations (code, name, description, kind, sort_order) VALUES
                    ('A9','Escluso da totale/conteggi','Stati esclusi dai totali € e dai conteggi (annullato, sospeso, ecc.)','SET',9)");

                int a9Id = c.ExecuteScalar<int>("SELECT id FROM ddp_aggregations WHERE code='A9'");
                foreach (string st in new[] { "ANN", "SOSP", "RAM", "SOST" })
                    c.Execute("INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A,@S)",
                        new { A = a9Id, S = st });

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (14, 'ddp aggregazione A9 (escluso da totale)')");
                _logger.LogInformation("[Migration v14] Aggregazione A9 (escluso da totale/conteggi) + membri default");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v14] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v15: moduli "Anagrafica attività" (catalogo globale activity_catalog) + "Milestone"
        // (pianificazione per-commessa project_milestones). Crea le tabelle (idempotente), seeda il
        // catalogo standard una-tantum e registra la feature key di navigazione della nuova pagina.
        if (currentVersion < 15)
        {
            try
            {
                var milestones = new MilestonesDbService(this);
                milestones.InitTables(c);
                milestones.SeedCatalog(c);

                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.anagrafica_attivita', 'Anagrafica Attività', 'navigation', 2, 'HIDDEN')");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (15, 'activity_catalog + project_milestones + nav.anagrafica_attivita')");
                _logger.LogInformation("[Migration v15] Tabelle activity_catalog + project_milestones, seed catalogo, feature key nav.anagrafica_attivita");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v15] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v16: modulo "SAL / Fatturazione" (sal_conditions + project_sal + sal_rows) + anagrafica condizioni.
        if (currentVersion < 16)
        {
            try
            {
                var sal = new SalDbService(this);
                sal.InitTables(c);
                sal.SeedConditions(c);
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.sal_condizioni', 'Condizioni Pagamento SAL', 'navigation', 2, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (16, 'sal_conditions + project_sal + sal_rows + nav.sal_condizioni')");
                _logger.LogInformation("[Migration v16] Tabelle SAL + seed condizioni + feature key nav.sal_condizioni");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v16] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v17: feature key per la pagina PM SAL globale
        if (currentVersion < 17)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.sal', 'SAL / Fatturazione', 'navigation', 2, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (17, 'nav.sal feature key')");
                _logger.LogInformation("[Migration v17] feature key nav.sal");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v17] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v18: blocco righe pagate + audit fields + indici
        if (currentVersion < 18)
        {
            try
            {
                // Verifica se la colonna paid_by esiste già in sal_rows
                bool hasPaidBy = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.columns
                    WHERE table_schema = DATABASE()
                      AND table_name = 'sal_rows'
                      AND column_name = 'paid_by'") > 0;
                if (!hasPaidBy)
                {
                    c.Execute("ALTER TABLE sal_rows ADD COLUMN paid_by INT NULL, ADD COLUMN paid_at DATETIME NULL");
                    _logger.LogInformation("[Migration v18] Aggiunte colonne paid_by e paid_at a sal_rows");
                }

                // Verifica se l'indice idx_salrow_stato_data esiste
                bool hasIndex = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.statistics
                    WHERE table_schema = DATABASE()
                      AND table_name = 'sal_rows'
                      AND index_name = 'idx_salrow_stato_data'") > 0;
                if (!hasIndex)
                {
                    c.Execute("ALTER TABLE sal_rows ADD INDEX idx_salrow_stato_data (stato, data_fatt)");
                    _logger.LogInformation("[Migration v18] Aggiunto indice idx_salrow_stato_data a sal_rows");
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (18, 'sal_rows paid_by + paid_at + index')");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v18] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v19: feature key nav.scadenze
        if (currentVersion < 19)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.scadenze', 'Scadenze', 'navigation', 2, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (19, 'nav.scadenze feature key')");
                _logger.LogInformation("[Migration v19] Aggiunta feature key nav.scadenze");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v19] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v20: tabella Lavorazioni meccaniche (project_work_requests) — modulo Gestione Lavorazioni
        if (currentVersion < 20)
        {
            try
            {
                EnsureWorkRequestsTable(c);
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (20, 'Tabella project_work_requests (modulo Lavorazioni)')");
                _logger.LogInformation("[Migration v20] Creata tabella project_work_requests");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v20] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v21: SAL v10 — campi fatturazione/incasso su sal_rows, PO/riferimento offerta su project_sal,
        // anagrafiche causali Conto SAP e stati pagamento, storico controlli periodici del prospetto.
        // Le righe legacy con stato='pagata' diventano stato='emessa' + pagamento='Pagata'
        // (paid_by/paid_at restano come audit dell'incasso).
        if (currentVersion < 21)
        {
            try
            {
                // Tabelle nuove (sal_sap_causali, sal_payment_states, sal_prospetto_checks);
                // no-op sulle tabelle SAL già esistenti (CREATE TABLE IF NOT EXISTS).
                var sal = new SalDbService(this);
                sal.InitTables(c);

                // Nuove colonne sal_rows, una per volta con check di esistenza; la catena AFTER
                // replica l'ordine del CREATE TABLE (tra 'stato' e 'sort_order').
                (string Column, string Definition)[] salRowColumns = new (string, string)[]
                {
                    ("iva_perc",       "INT NULL AFTER stato"),
                    ("gg_saldo",       "INT NULL AFTER iva_perc"),
                    ("n_fatt",         "VARCHAR(50) NOT NULL DEFAULT '' AFTER gg_saldo"),
                    ("conto_sap",      "VARCHAR(200) NOT NULL DEFAULT '' AFTER n_fatt"),
                    ("pagamento",      "VARCHAR(100) NOT NULL DEFAULT '' AFTER conto_sap"),
                    ("data_pagamento", "DATE NULL AFTER pagamento"),
                    ("note",           "VARCHAR(2000) NOT NULL DEFAULT '' AFTER data_pagamento")
                };
                foreach ((string Column, string Definition) col in salRowColumns)
                {
                    bool hasColumn = c.ExecuteScalar<int>(@"
                        SELECT COUNT(*) FROM information_schema.columns
                        WHERE table_schema = DATABASE()
                          AND table_name = 'sal_rows'
                          AND column_name = @Column", new { col.Column }) > 0;
                    if (!hasColumn)
                    {
                        c.Execute($"ALTER TABLE sal_rows ADD COLUMN {col.Column} {col.Definition}");
                        _logger.LogInformation("[Migration v21] Aggiunta colonna sal_rows.{Column}", col.Column);
                    }
                }

                // Indice per warning incasso / prospetto (pagamento + data fattura)
                bool hasIndex = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.statistics
                    WHERE table_schema = DATABASE()
                      AND table_name = 'sal_rows'
                      AND index_name = 'idx_salrow_pag_saldo'") > 0;
                if (!hasIndex)
                {
                    c.Execute("ALTER TABLE sal_rows ADD INDEX idx_salrow_pag_saldo (pagamento, data_fatt)");
                    _logger.LogInformation("[Migration v21] Aggiunto indice idx_salrow_pag_saldo a sal_rows");
                }

                // Header esteso project_sal: PO - Ordine cliente + Riferimento Offerta ATEC
                (string Column, string Definition)[] headerColumns = new (string, string)[]
                {
                    ("po",          "VARCHAR(150) NOT NULL DEFAULT '' AFTER valore"),
                    ("rif_offerta", "VARCHAR(200) NOT NULL DEFAULT '' AFTER po")
                };
                foreach ((string Column, string Definition) col in headerColumns)
                {
                    bool hasColumn = c.ExecuteScalar<int>(@"
                        SELECT COUNT(*) FROM information_schema.columns
                        WHERE table_schema = DATABASE()
                          AND table_name = 'project_sal'
                          AND column_name = @Column", new { col.Column }) > 0;
                    if (!hasColumn)
                    {
                        c.Execute($"ALTER TABLE project_sal ADD COLUMN {col.Column} {col.Definition}");
                        _logger.LogInformation("[Migration v21] Aggiunta colonna project_sal.{Column}", col.Column);
                    }
                }

                // Seed anagrafiche (idempotenti: solo se tabella vuota)
                sal.SeedSapCausali(c);
                sal.SeedPaymentStates(c);

                // Migrazione dati: lo stato legacy 'pagata' si sdoppia in fatturazione 'emessa'
                // + pagamento 'Pagata' (idempotente: al secondo giro non resta nessuna 'pagata').
                int migratedRows = c.Execute("UPDATE sal_rows SET pagamento='Pagata', stato='emessa' WHERE stato='pagata'");
                if (migratedRows > 0)
                    _logger.LogInformation("[Migration v21] {Count} righe sal_rows migrate da stato 'pagata' a emessa + Pagata", migratedRows);

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (21, 'SAL v10: campi fatturazione/incasso su sal_rows, po/rif_offerta su project_sal, anagrafiche causali SAP e stati pagamento, controlli prospetto')");
                _logger.LogInformation("[Migration v21] Schema SAL v10 applicato (colonne, indice, anagrafiche, migrazione stato pagata)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v21] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        if (currentVersion < 22)
        {
            try
            {
                // Regola di business SAL: le righe senza %IVA (legacy, pre-v10) valgono 22%
                // come nel prototipo; le righe nuove nascono già a 22 (default in CreateRow).
                int fixedRows = c.Execute("UPDATE sal_rows SET iva_perc = 22 WHERE iva_perc IS NULL");
                if (fixedRows > 0)
                    _logger.LogInformation("[Migration v22] {Count} righe sal_rows legacy portate a IVA 22%", fixedRows);

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (22, 'SAL v10: default IVA 22% sulle righe legacy senza %IVA')");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v22] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v23: SAL — colori configurabili sugli stati pagamento (sal_payment_states).
        // color_bg/color_fg VARCHAR(9) NULL (#RRGGBB o #RRGGBBAA): NULL = stato neutro senza tinta.
        // Priorità colore riga nel foglio: colore del pagamento selezionato > giallo 'emessa' > nessuno.
        // I colori sono pura estetica: la semantica (lock, incasso, warning, bucket Cash Flow)
        // resta cablata sulle etichette di sistema 'Pagata'/'Parzialmente Pagata'.
        if (currentVersion < 23)
        {
            try
            {
                // Nuove colonne colore, una per volta con check di esistenza (pattern v21)
                (string Column, string Definition)[] colorColumns = new (string, string)[]
                {
                    ("color_bg", "VARCHAR(9) NULL AFTER is_active"),
                    ("color_fg", "VARCHAR(9) NULL AFTER color_bg")
                };
                foreach ((string Column, string Definition) col in colorColumns)
                {
                    bool hasColumn = c.ExecuteScalar<int>(@"
                        SELECT COUNT(*) FROM information_schema.columns
                        WHERE table_schema = DATABASE()
                          AND table_name = 'sal_payment_states'
                          AND column_name = @Column", new { col.Column }) > 0;
                    if (!hasColumn)
                    {
                        c.Execute($"ALTER TABLE sal_payment_states ADD COLUMN {col.Column} {col.Definition}");
                        _logger.LogInformation("[Migration v23] Aggiunta colonna sal_payment_states.{Column}", col.Column);
                    }
                }

                // Seed colori di default delle voci di sistema SOLO se ancora NULL (idempotente,
                // non sovrascrive eventuali personalizzazioni): Pagata = verde pastello (parità con
                // l'attuale riga emerald del foglio v10), Parzialmente Pagata = rosso pastello.
                int seeded = c.Execute(@"UPDATE sal_payment_states
                    SET color_bg='#D1FAE5', color_fg='#065F46'
                    WHERE LOWER(label)='pagata' AND color_bg IS NULL");
                seeded += c.Execute(@"UPDATE sal_payment_states
                    SET color_bg='#FEE2E2', color_fg='#991B1B'
                    WHERE LOWER(label)='parzialmente pagata' AND color_bg IS NULL");
                if (seeded > 0)
                    _logger.LogInformation("[Migration v23] Colori di default impostati su {Count} stati pagamento di sistema", seeded);

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (23, 'SAL: colori configurabili stati pagamento')");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v23] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        if (currentVersion < 24)
        {
            try
            {
                // Regola di business SAL: GG saldo nullo (legacy) → 0 giorni (stesso giorno fattura).
                int fixedRows = c.Execute("UPDATE sal_rows SET gg_saldo = 0 WHERE gg_saldo IS NULL");
                if (fixedRows > 0)
                    _logger.LogInformation("[Migration v24] {Count} righe sal_rows legacy portate a GG saldo 0", fixedRows);

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (24, 'SAL: default GG saldo 0 sulle righe legacy senza valore')");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v24] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v25: stato MIT (Materiale in trattamento, tipico Officina) — presente nella legenda
        // DDP di ATEC ma assente dal seed storico. INSERT IGNORE: chi lo ha già creato/ritoccato
        // da Conf. DDP non viene toccato. MIT entra in A1 (conteggio per stato) e A4 (in consegna).
        if (currentVersion < 25)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO ddp_statuses (status_key, label, color_bg, color_fg, sort_order)
                    VALUES ('MIT', 'MATERIALE IN TRATTAMENTO', '#7FC1B0', '#000000', 18)");

                foreach (string aggCode in new[] { "A1", "A4" })
                {
                    int aggId = c.ExecuteScalar<int>("SELECT COALESCE(MAX(id), 0) FROM ddp_aggregations WHERE code=@C", new { C = aggCode });
                    if (aggId > 0)
                        c.Execute("INSERT IGNORE INTO ddp_aggregation_states (aggregation_id, status_key) VALUES (@A, 'MIT')", new { A = aggId });
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (25, 'ddp: stato MIT + membership aggregazioni A1/A4')");
                _logger.LogInformation("[Migration v25] Stato MIT seedato in ddp_statuses + aggregazioni A1/A4");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v25] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v26: lavorazioni alimentate dalla DDP Officina — colonna di collegamento
        // ddp_officina_item_id (UNIQUE: una lavorazione per riga) + backfill bozze in
        // staging per tutte le righe officina esistenti non ancora collegate.
        if (currentVersion < 26)
        {
            try
            {
                int hasCol = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'project_work_requests'
                      AND COLUMN_NAME = 'ddp_officina_item_id'");
                if (hasCol == 0)
                {
                    c.Execute(@"ALTER TABLE project_work_requests
                        ADD COLUMN ddp_officina_item_id INT NULL,
                        ADD UNIQUE KEY uq_pwr_ddp_officina (ddp_officina_item_id),
                        ADD CONSTRAINT fk_pwr_ddp_officina FOREIGN KEY (ddp_officina_item_id)
                            REFERENCES ddp_officina_items(id) ON DELETE SET NULL");
                }

                List<int> orphans = c.Query<int>(@"
                    SELECT o.id FROM ddp_officina_items o
                    LEFT JOIN project_work_requests wr ON wr.ddp_officina_item_id = o.id
                    WHERE wr.id IS NULL").ToList();
                foreach (int itemId in orphans)
                    WorkRequestDdpSync.Upsert(c, itemId);

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (26, 'lavorazioni: link ddp_officina_item_id + backfill bozze da DDP Officina')");
                _logger.LogInformation("[Migration v26] Link lavorazioni-DDP Officina + {Count} bozze generate", orphans.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v26] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v27: quantità sulle composizioni Codex — una riga per componente con colonna
        // quantity, invece di N righe duplicate (quantità 4 = 4 insert identici).
        // Collassa i duplicati esistenti (stesso padre + stesso figlio) sommando le
        // occorrenze sulla riga con id minimo ed eliminando le altre.
        if (currentVersion < 27)
        {
            try
            {
                int hasCol = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'codex_compositions'
                      AND COLUMN_NAME = 'quantity'");
                if (hasCol == 0)
                {
                    c.Execute("ALTER TABLE codex_compositions ADD COLUMN quantity INT NOT NULL DEFAULT 1");
                }

                c.Execute(@"
                    UPDATE codex_compositions cc
                    JOIN (
                        SELECT MIN(id) AS keep_id, SUM(quantity) AS qty
                        FROM codex_compositions
                        GROUP BY parent_codex_id, child_codex_id, child_catalog_id
                        HAVING COUNT(*) > 1
                    ) g ON cc.id = g.keep_id
                    SET cc.quantity = g.qty");

                // <=> = uguaglianza NULL-safe: uno tra child_codex_id e child_catalog_id è sempre NULL.
                int deleted = c.Execute(@"
                    DELETE cc FROM codex_compositions cc
                    JOIN (
                        SELECT MIN(id) AS keep_id, parent_codex_id, child_codex_id, child_catalog_id
                        FROM codex_compositions
                        GROUP BY parent_codex_id, child_codex_id, child_catalog_id
                        HAVING COUNT(*) > 1
                    ) g ON cc.parent_codex_id = g.parent_codex_id
                       AND cc.child_codex_id <=> g.child_codex_id
                       AND cc.child_catalog_id <=> g.child_catalog_id
                       AND cc.id <> g.keep_id");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (27, 'codex_compositions: colonna quantity + collasso righe duplicate')");
                _logger.LogInformation("[Migration v27] Colonna quantity su codex_compositions, {Deleted} righe duplicate collassate", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v27] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v28: «comanda il padre» — i componenti importati dalla composizione Codex in DDP
        // Officina restano collegati alla riga del padre (parent_officina_item_id) con la
        // quantità unitaria di composizione (composition_qty): al cambio Qtà del padre i
        // figli seguono con delta = composition_qty × ΔQtà. FK ON DELETE SET NULL: eliminato
        // il padre, i figli restano come righe libere (scollegati).
        if (currentVersion < 28)
        {
            try
            {
                int hasCol = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'ddp_officina_items'
                      AND COLUMN_NAME = 'parent_officina_item_id'");
                if (hasCol == 0)
                {
                    c.Execute(@"ALTER TABLE ddp_officina_items
                        ADD COLUMN parent_officina_item_id INT NULL AFTER notes,
                        ADD COLUMN composition_qty DECIMAL(10,3) NULL AFTER parent_officina_item_id,
                        ADD CONSTRAINT fk_ddpoff_parent FOREIGN KEY (parent_officina_item_id)
                            REFERENCES ddp_officina_items(id) ON DELETE SET NULL");
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (28, 'ddp_officina_items: parent_officina_item_id + composition_qty (comanda il padre)')");
                _logger.LogInformation("[Migration v28] Colonne parent_officina_item_id/composition_qty su ddp_officina_items");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v28] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v29: aggiunge supplier_id a ddp_officina_items per supportare la scelta del fornitore da anagrafica
        if (currentVersion < 29)
        {
            try
            {
                int hasCol = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'ddp_officina_items'
                      AND COLUMN_NAME = 'supplier_id'");
                if (hasCol == 0)
                {
                    c.Execute(@"ALTER TABLE ddp_officina_items
                        ADD COLUMN supplier_id INT NULL AFTER treatment,
                        ADD CONSTRAINT fk_ddpoff_supplier FOREIGN KEY (supplier_id)
                            REFERENCES suppliers(id) ON DELETE SET NULL");
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (29, 'ddp_officina_items: colonna supplier_id per fornitore da anagrafica')");
                _logger.LogInformation("[Migration v29] Colonna supplier_id su ddp_officina_items");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v29] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v30: aggiunge tabella ddp_treatments per i trattamenti gestiti da anagrafica
        if (currentVersion < 30)
        {
            try
            {
                c.Execute(@"
                    CREATE TABLE IF NOT EXISTS ddp_treatments (
                        id INT AUTO_INCREMENT PRIMARY KEY,
                        name VARCHAR(200) NOT NULL UNIQUE,
                        sort_order INT NOT NULL DEFAULT 0,
                        is_active TINYINT(1) NOT NULL DEFAULT 1,
                        created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
                    ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;");

                c.Execute(@"
                    INSERT IGNORE INTO ddp_treatments (name, sort_order, is_active) VALUES
                    ('ANODIZZATO', 10, 1),
                    ('BRUNITO', 20, 1),
                    ('ZINCATO', 30, 1),
                    ('VERNICIATO', 40, 1),
                    ('SABBIATO', 50, 1);");

                // Backfill dei trattamenti già digitati a mano sulle righe officina
                c.Execute(@"INSERT IGNORE INTO ddp_treatments (name, sort_order, is_active)
                    SELECT DISTINCT TRIM(treatment), 100, 1
                    FROM ddp_officina_items
                    WHERE TRIM(COALESCE(treatment, '')) <> ''");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (30, 'ddp_treatments: tabella e seed iniziale trattamenti')");
                _logger.LogInformation("[Migration v30] Tabella ddp_treatments creata e popolata");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v30] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v31: Lavorazioni = solo particolari a disegno (prefisso Codex 101).
        // Rimuove le bozze staging nate da righe DDP Officina con altri prefissi
        // (201/301/401/501/…); le promosse restano (nascoste dalle GET).
        if (currentVersion < 31)
        {
            try
            {
                int deleted = c.Execute(@"
                    DELETE wr FROM project_work_requests wr
                    JOIN ddp_officina_items o ON o.id = wr.ddp_officina_item_id
                    WHERE wr.is_staging = 1
                      AND REPLACE(REPLACE(COALESCE(o.part_number, ''), '.', ''), ' ', '') NOT LIKE '101%'");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (31, 'lavorazioni: elimina staging non-101 da DDP Officina')");
                _logger.LogInformation("[Migration v31] Eliminate {Count} bozze lavorazioni non-101", deleted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v31] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v32: bozze lavorazioni — tipo Interna/Esterna one-shot dallo stato DDP Officina.
        // DO/RO/IO → External; DC/COS → Internal (+ fornitore ATEC). Solo se type è ancora vuoto.
        if (currentVersion < 32)
        {
            try
            {
                int updatedExt = c.Execute(@"
                    UPDATE project_work_requests wr
                    JOIN ddp_officina_items o ON o.id = wr.ddp_officina_item_id
                    SET wr.type = 'External',
                        wr.row_version = wr.row_version + 1
                    WHERE wr.is_staging = 1
                      AND TRIM(COALESCE(wr.type, '')) = ''
                      AND UPPER(TRIM(COALESCE(o.item_status, ''))) IN ('DO', 'RO', 'IO')");

                int updatedInt = c.Execute(@"
                    UPDATE project_work_requests wr
                    JOIN ddp_officina_items o ON o.id = wr.ddp_officina_item_id
                    SET wr.type = 'Internal',
                        wr.po_supplier = 'ATEC',
                        wr.po_number = '',
                        wr.row_version = wr.row_version + 1
                    WHERE wr.is_staging = 1
                      AND TRIM(COALESCE(wr.type, '')) = ''
                      AND UPPER(TRIM(COALESCE(o.item_status, ''))) IN ('DC', 'COS')");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (32, 'lavorazioni: tipo Internal/External one-shot da stato DDP')");
                _logger.LogInformation(
                    "[Migration v32] Tipo bozze: {Ext} External, {Int} Internal",
                    updatedExt, updatedInt);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v32] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v33: commessa di sistema INTERNA — contenitore per lavorazioni generiche
        // (bozze staging non legate a una commessa reale). customer_id/pm_id sono NOT NULL.
        if (currentVersion < 33)
        {
            try
            {
                int exists = c.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM projects WHERE code = @Code",
                    new { Code = ATEC.PM.Shared.SystemProjects.InternaCode });
                if (exists == 0)
                {
                    // Cliente di sistema (vat univoco) — riusa se già presente da un run precedente.
                    const string systemVat = "__SYSTEM_INTERNA__";
                    int customerId = c.ExecuteScalar<int>(
                        "SELECT COALESCE(MAX(id), 0) FROM customers WHERE vat_number = @Vat",
                        new { Vat = systemVat });
                    if (customerId == 0)
                    {
                        customerId = c.ExecuteScalar<int>(@"
                            INSERT INTO customers (company_name, vat_number, notes, is_active)
                            VALUES (@Name, @Vat, @Notes, 1);
                            SELECT LAST_INSERT_ID()", new
                        {
                            Name = "ATEC — Sistema",
                            Vat = systemVat,
                            Notes = ATEC.PM.Shared.SystemProjects.InternaNotes,
                        });
                    }

                    int pmId = c.ExecuteScalar<int>(
                        "SELECT COALESCE(MIN(id), 0) FROM employees WHERE status = 'ACTIVE'");
                    if (pmId == 0)
                        pmId = c.ExecuteScalar<int>("SELECT COALESCE(MIN(id), 0) FROM employees");
                    if (pmId == 0)
                        throw new InvalidOperationException(
                            "Impossibile creare progetto INTERNA: nessun employee in anagrafica.");

                    c.Execute(@"
                        INSERT INTO projects
                            (code, title, customer_id, pm_id, description, status, priority, notes)
                        VALUES
                            (@Code, @Title, @CustomerId, @PmId, @Description, 'ACTIVE', 'MEDIUM', @Notes)",
                        new
                        {
                            Code = ATEC.PM.Shared.SystemProjects.InternaCode,
                            Title = ATEC.PM.Shared.SystemProjects.InternaTitle,
                            CustomerId = customerId,
                            PmId = pmId,
                            Description = ATEC.PM.Shared.SystemProjects.InternaTitle,
                            Notes = ATEC.PM.Shared.SystemProjects.InternaNotes,
                        });
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (33, 'progetto sistema INTERNA per lavorazioni generiche')");
                _logger.LogInformation("[Migration v33] Progetto sistema INTERNA assicurato");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v33] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v34: data ordine su DDP Officina (valorizzata in automatico al passaggio a IO).
        if (currentVersion < 34)
        {
            try
            {
                int hasCol = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'ddp_officina_items'
                      AND COLUMN_NAME = 'order_date'");
                if (hasCol == 0)
                {
                    c.Execute(@"ALTER TABLE ddp_officina_items
                        ADD COLUMN order_date DATE NULL AFTER date_needed");
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (34, 'ddp_officina_items: order_date (data In Ordine)')");
                _logger.LogInformation("[Migration v34] Colonna order_date su ddp_officina_items");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v34] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v35: priorità default P2 (più bassa) sulle lavorazioni senza priorità.
        if (currentVersion < 35)
        {
            try
            {
                c.Execute(@"UPDATE project_work_requests SET priority = 2 WHERE priority IS NULL");
                try
                {
                    c.Execute(@"ALTER TABLE project_work_requests
                        MODIFY COLUMN priority TINYINT NOT NULL DEFAULT 2");
                }
                catch (Exception alterEx)
                {
                    _logger.LogWarning("[Migration v35] ALTER priority default: {Message}", alterEx.Message);
                }

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (35, 'work_requests: default priority P2')");
                _logger.LogInformation("[Migration v35] Priorità default P2 su project_work_requests");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v35] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v36: feature key navigazione Gamma Robot (allineata a Preventivi, min_level 2)
        if (currentVersion < 36)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.gamma_robot', 'Gamma Robot', 'navigation', 2, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (36, 'nav.gamma_robot feature key')");
                _logger.LogInformation("[Migration v36] feature key nav.gamma_robot");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v36] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v37: inbox Officina (Responsabile) — visibile da RESP_REPARTO in su
        if (currentVersion < 37)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.officina_inbox', 'Officina — Inbox', 'navigation', 1, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (37, 'nav.officina_inbox feature key')");
                _logger.LogInformation("[Migration v37] feature key nav.officina_inbox");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v37] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v38: pezzi prodotti su distinta Officina (RESP / inbox)
        if (currentVersion < 38)
        {
            try
            {
                if (c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'ddp_officina_items'
                      AND COLUMN_NAME = 'quantity_produced'") == 0)
                {
                    c.Execute(@"ALTER TABLE ddp_officina_items
                        ADD COLUMN quantity_produced DECIMAL(10,3) NOT NULL DEFAULT 0
                        AFTER quantity");
                }
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (38, 'ddp_officina_items: quantity_produced')");
                _logger.LogInformation("[Migration v38] Colonna quantity_produced su ddp_officina_items");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v38] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v39: matrice degli avanzamenti di stato DDP v7 (MATRICE_STATI_DDP_V7 + relazione tecnica
        // DDP-MATRICE-STATI del 20/07/2026). Tre interventi:
        //  1) consolidamento stati legacy sulle righe: CON/COS/SPED → DISP, MOD → RAM
        //     (bom_items + ddp_officina_items) + disattivazione dei 4 stati + etichetta DISP v7;
        //  2) pulizia ddp_aggregation_states dalle chiavi legacy (le righe non le porteranno più);
        //  3) seed one-shot della matrice transizioni (l'editor in Conf. DDP la modifica liberamente).
        if (currentVersion < 39)
        {
            try
            {
                int remapped = 0;
                remapped += c.Execute("UPDATE bom_items SET item_status='DISP', updated_at=NOW() WHERE UPPER(TRIM(COALESCE(item_status,''))) IN ('CON','COS','SPED')");
                remapped += c.Execute("UPDATE bom_items SET item_status='RAM',  updated_at=NOW() WHERE UPPER(TRIM(COALESCE(item_status,''))) = 'MOD'");
                remapped += c.Execute("UPDATE ddp_officina_items SET item_status='DISP', updated_at=NOW() WHERE UPPER(TRIM(COALESCE(item_status,''))) IN ('CON','COS','SPED')");
                remapped += c.Execute("UPDATE ddp_officina_items SET item_status='RAM',  updated_at=NOW() WHERE UPPER(TRIM(COALESCE(item_status,''))) = 'MOD'");

                c.Execute("UPDATE ddp_statuses SET is_active=FALSE WHERE status_key IN ('CON','COS','SPED','MOD')");
                c.Execute("UPDATE ddp_statuses SET label='DISPONIBILE / CONSEGNATO' WHERE status_key='DISP' AND label='DISPONIBILE'");

                c.Execute("DELETE FROM ddp_aggregation_states WHERE status_key IN ('CON','COS','SPED','MOD')");

                // Matrice v7: riga INIZIO (nessuno stato) non è memorizzata → finestra completa.
                // ('ANN','') e ('SOST','') = terminali governati senza uscite.
                c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (from_key, to_key) VALUES
                    ('VER','CHEK'),('VER','RO'),('VER','DO'),('VER','IO'),('VER','DC'),('VER','DISP'),('VER','SOSP'),('VER','ANN'),('VER','SOST'),
                    ('CHEK','RO'),('CHEK','DO'),('CHEK','IO'),('CHEK','DC'),('CHEK','DISP'),('CHEK','SOSP'),('CHEK','ANN'),('CHEK','SOST'),
                    ('RO','CHEK'),('RO','DO'),('RO','IO'),('RO','DC'),('RO','SOSP'),('RO','ANN'),('RO','SOST'),
                    ('DO','IO'),('DO','SOSP'),('DO','ANN'),('DO','SOST'),
                    ('IO','DC'),('IO','MIT'),('IO','PAR'),('IO','DISP'),('IO','RAM'),('IO','ASS'),('IO','SOSP'),('IO','ANN'),('IO','SOST'),
                    ('DC','MIT'),('DC','PAR'),('DC','DISP'),('DC','RAM'),('DC','ASS'),('DC','SOSP'),('DC','ANN'),('DC','SOST'),
                    ('MIT','PAR'),('MIT','DISP'),('MIT','RAM'),('MIT','ASS'),('MIT','SOSP'),('MIT','ANN'),('MIT','SOST'),
                    ('PAR','DISP'),('PAR','RAM'),('PAR','ASS'),('PAR','SOSP'),('PAR','ANN'),('PAR','SOST'),
                    ('DISP','MIT'),('DISP','PAR'),('DISP','RAM'),('DISP','ASS'),('DISP','SOSP'),('DISP','ANN'),('DISP','SOST'),
                    ('RAM','VER'),('RAM','CHEK'),('RAM','DISP'),('RAM','SOSP'),('RAM','ANN'),('RAM','SOST'),
                    ('ASS','RAM'),('ASS','SOSP'),('ASS','ANN'),('ASS','SOST'),
                    ('SOSP','VER'),('SOSP','CHEK'),('SOSP','RO'),('SOSP','DO'),('SOSP','IO'),('SOSP','DC'),('SOSP','MIT'),('SOSP','PAR'),('SOSP','DISP'),('SOSP','ASS'),('SOSP','ANN'),('SOSP','SOST'),
                    ('ANN',''),
                    ('SOST','')");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (39, 'matrice stati DDP v7: transizioni + consolidamento CON/COS/SPED/MOD')");
                _logger.LogInformation("[Migration v39] Matrice stati DDP v7 seedata; {Remapped} righe rimappate (CON/COS/SPED→DISP, MOD→RAM)", remapped);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v39] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v40: matrice transizioni PER TIPO di distinta (regola 20/07/2026: nella DDP commerciale
        // DC non deve esistere — il materiale commerciale si acquista e basta — mentre in officina sì,
        // incluso DO→DC per dirottare alla produzione interna un pezzo "da ordinare").
        // ddp_type ∈ {COMMERCIAL, OFFICINA}; la matrice v39 (senza tipo) viene duplicata nei due tipi.
        // Nuova riga speciale from_key='INIZIO' = finestra di partenza per le righe senza stato
        // (COMMERCIAL senza DC); se assente, fallback permissivo come per gli altri stati.
        if (currentVersion < 40)
        {
            try
            {
                bool hasType = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'ddp_status_transitions'
                      AND COLUMN_NAME = 'ddp_type'") > 0;
                if (!hasType)
                {
                    c.Execute("ALTER TABLE ddp_status_transitions ADD COLUMN ddp_type VARCHAR(20) NOT NULL DEFAULT '' FIRST");
                    c.Execute("ALTER TABLE ddp_status_transitions DROP PRIMARY KEY, ADD PRIMARY KEY (ddp_type, from_key, to_key)");
                }

                // Duplica la matrice "senza tipo" (v39 o pre-esistente) nei due tipi, poi la rimuove.
                c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                            SELECT 'COMMERCIAL', from_key, to_key FROM ddp_status_transitions WHERE ddp_type = ''");
                c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                            SELECT 'OFFICINA', from_key, to_key FROM ddp_status_transitions WHERE ddp_type = ''");
                c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type = ''");

                // Divergenze: commerciale senza DC (né in ingresso né in uscita); officina apre DO→DC.
                c.Execute("DELETE FROM ddp_status_transitions WHERE ddp_type = 'COMMERCIAL' AND (from_key = 'DC' OR to_key = 'DC')");
                c.Execute("INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key) VALUES ('OFFICINA','DO','DC')");

                // Finestra di partenza (riga INIZIO dell'Excel): tutti gli stati attivi,
                // meno DC sulla commerciale.
                c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                            SELECT 'COMMERCIAL', 'INIZIO', status_key FROM ddp_statuses
                            WHERE is_active = TRUE AND status_key <> 'DC'");
                c.Execute(@"INSERT IGNORE INTO ddp_status_transitions (ddp_type, from_key, to_key)
                            SELECT 'OFFICINA', 'INIZIO', status_key FROM ddp_statuses
                            WHERE is_active = TRUE");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (40, 'matrice transizioni per tipo DDP + finestra INIZIO (commerciale senza DC)')");
                _logger.LogInformation("[Migration v40] Matrice transizioni sdoppiata per tipo (COMMERCIAL senza DC, OFFICINA con DO→DC) + righe INIZIO");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v40] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v41: ampliamento Codex — colonna codice_nuovo (nuova codifica, vedi commento CREATE TABLE).
        // Solo schema: nessuna conversione automatica, la ricodifica dei 201xxx è manuale (decisione 21/07/2026).
        if (currentVersion < 41)
        {
            try
            {
                if (c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'codex_items'
                      AND COLUMN_NAME = 'codice_nuovo'") == 0)
                {
                    c.Execute("ALTER TABLE codex_items ADD COLUMN codice_nuovo VARCHAR(15) NULL AFTER codice", commandTimeout: 600);
                    c.Execute("ALTER TABLE codex_items ADD UNIQUE KEY uq_codex_codice_nuovo (codice_nuovo)", commandTimeout: 600);
                }
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (41, 'codex_items: colonna codice_nuovo (nuova codifica manuale)')");
                _logger.LogInformation("[Migration v41] Colonna codice_nuovo su codex_items (nuova codifica)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v41] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v42: mapping Danea ↔ codice ATEC (piano Acquisti, 21/07/2026). Sul catalogo (specchio
        // degli articoli Danea): atec_code = Extra1 dell'articolo (convenzione: SOLO codici nuovi
        // della nuova codifica Codex) + codex_item_id = risoluzione verso la riga Codex.
        if (currentVersion < 42)
        {
            try
            {
                if (c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'catalog_items'
                      AND COLUMN_NAME = 'atec_code'") == 0)
                {
                    c.Execute("ALTER TABLE catalog_items ADD COLUMN atec_code VARCHAR(15) NULL AFTER easyfatt_id", commandTimeout: 600);
                    c.Execute("ALTER TABLE catalog_items ADD COLUMN codex_item_id INT NULL AFTER atec_code", commandTimeout: 600);
                    c.Execute("ALTER TABLE catalog_items ADD INDEX IX_CatalogItems_AtecCode (atec_code)", commandTimeout: 600);
                }
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (42, 'catalog_items: atec_code + codex_item_id (mapping Danea Extra1)')");
                _logger.LogInformation("[Migration v42] Colonne atec_code/codex_item_id su catalog_items");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v42] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v43: snapshot codice ATEC sulle righe distinta commerciale (piano Acquisti Fase 2).
        if (currentVersion < 43)
        {
            try
            {
                if (c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'bom_items'
                      AND COLUMN_NAME = 'atec_code'") == 0)
                {
                    c.Execute("ALTER TABLE bom_items ADD COLUMN atec_code VARCHAR(15) NULL AFTER ddp_type", commandTimeout: 600);
                    c.Execute("ALTER TABLE bom_items ADD INDEX IX_BomItems_AtecCode (atec_code)", commandTimeout: 600);
                }
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (43, 'bom_items: atec_code (snapshot codice ATEC in distinta)')");
                _logger.LogInformation("[Migration v43] Colonna atec_code su bom_items");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v43] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v44: ciclo RDO Acquisti (testata + righe BOM + offerte fornitori).
        if (currentVersion < 44)
        {
            try
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
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (44, 'purchase_rfqs + items + offers (ciclo RDO Acquisti)')");
                _logger.LogInformation("[Migration v44] Tabelle purchase_rfq* (ciclo RDO)");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v44] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v45: feature key nav inbox Acquisti.
        if (currentVersion < 45)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.acquisti_inbox', 'Acquisti — Inbox', 'navigation', 1, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (45, 'nav.acquisti_inbox feature key')");
                _logger.LogInformation("[Migration v45] feature key nav.acquisti_inbox");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v45] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v46: invariante «codice ATEC = codice_nuovo, sempre» — gli articoli NATI nelle
        // famiglie nuove (201/211/221, generatore locale ⇒ remote_id NULL) ricevono
        // codice_nuovo = codice, così mapping Danea / orfani / ricerche non hanno bisogno
        // di predicati sul formato. Da qui in poi ci pensa ConfirmReservation.
        if (currentVersion < 46)
        {
            try
            {
                c.Execute(@"
                    UPDATE codex_items
                    SET codice_nuovo = codice
                    WHERE remote_id IS NULL
                      AND (codice_nuovo IS NULL OR codice_nuovo = '')
                      AND codice REGEXP '^(201|211|221)[0-9]{9}$'");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (46, 'codex_items: codice_nuovo = codice per gli articoli nati nelle famiglie nuove')");
                _logger.LogInformation("[Migration v46] codice_nuovo allineato per gli articoli nati nelle famiglie nuove");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v46] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v47: righe distinta nate col fornitore vuoto perché il catalogo era monco
        // (bug sync fornitori IDAnag, fixato 22/07/2026): ricopia il fornitore
        // dell'articolo di catalogo SOLO dove in riga manca. One-shot, idempotente.
        if (currentVersion < 47)
        {
            try
            {
                int n = c.Execute(@"
                    UPDATE bom_items b
                    JOIN catalog_items ci ON ci.id = b.catalog_item_id
                    SET b.supplier_id = ci.supplier_id
                    WHERE b.supplier_id IS NULL AND ci.supplier_id IS NOT NULL", commandTimeout: 600);
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (47, 'bom_items: backfill supplier_id dal catalogo (righe pre-fix sync fornitori)')");
                _logger.LogInformation("[Migration v47] Fornitore ricopiato dal catalogo su {Count} righe distinta", n);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v47] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v48: feature key nav pagina Trasferimento catalogo Danea (migrazione Atec_PM).
        if (currentVersion < 48)
        {
            try
            {
                c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
                    VALUES ('nav.danea_migration', 'Trasferimento Danea', 'navigation', 1, 'HIDDEN')");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (48, 'nav.danea_migration feature key')");
                _logger.LogInformation("[Migration v48] feature key nav.danea_migration");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v48] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v49: default stato nuove righe DDP commerciale = VER (verificare magazzino).
        // Solo bom_items: la officina resta su DO.
        if (currentVersion < 49)
        {
            try
            {
                c.Execute("ALTER TABLE bom_items ALTER item_status SET DEFAULT 'VER'");
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (49, 'bom_items: default item_status VER (DDP commerciale)')");
                _logger.LogInformation("[Migration v49] bom_items.item_status DEFAULT → VER");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v49] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v50: tracciamento ordine fornitore Danea generato dalla RDO (strada B, 22/07/2026).
        if (currentVersion < 50)
        {
            try
            {
                if (c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'purchase_rfqs'
                      AND COLUMN_NAME = 'danea_order_iddoc'") == 0)
                {
                    c.Execute(@"ALTER TABLE purchase_rfqs
                        ADD COLUMN danea_order_iddoc INT NULL AFTER closed_at,
                        ADD COLUMN danea_order_num INT NULL AFTER danea_order_iddoc");
                }
                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (50, 'purchase_rfqs: riferimento ordine fornitore Danea (iddoc + numero)')");
                _logger.LogInformation("[Migration v50] Colonne ordine Danea su purchase_rfqs");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v50] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        // v51: riferimento ordine Danea sulla singola riga distinta commerciale — la generazione
        // ordine dalla RDO scrive danea_ref (numero) + danea_order_iddoc (chiave per il popup
        // di rendering dell'ordine). Colonna presente anche nel CREATE TABLE (ramo dev).
        if (currentVersion < 51)
        {
            try
            {
                if (c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                      AND TABLE_NAME = 'bom_items'
                      AND COLUMN_NAME = 'danea_order_iddoc'") == 0)
                {
                    c.Execute(@"ALTER TABLE bom_items
                        ADD COLUMN danea_order_iddoc INT NULL AFTER danea_ref");
                }

                // Backfill delle RDO già evase: il riferimento risale dalle righe RDO.
                c.Execute(@"
                    UPDATE bom_items b
                    JOIN purchase_rfq_items i ON i.bom_item_id = b.id
                    JOIN purchase_rfqs r ON r.id = i.rfq_id
                    SET b.danea_order_iddoc = r.danea_order_iddoc,
                        b.danea_ref = CASE WHEN COALESCE(b.danea_ref,'') = ''
                                           THEN CAST(r.danea_order_num AS CHAR)
                                           ELSE b.danea_ref END
                    WHERE r.danea_order_iddoc IS NOT NULL");

                c.Execute("INSERT IGNORE INTO schema_migrations (version, description) VALUES (51, 'bom_items: riferimento ordine Danea di riga (danea_order_iddoc + backfill danea_ref)')");
                _logger.LogInformation("[Migration v51] Colonna danea_order_iddoc su bom_items");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("[Migration v51] Errore (non bloccante): {Message}", ex.Message);
            }
        }

        _logger.LogInformation("[Migrations] Migrazioni applicate fino a v{Version}", LatestSchemaVersion);
    }
}
