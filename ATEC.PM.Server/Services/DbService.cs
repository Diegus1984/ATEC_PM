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

        // Seed delle causali DDP reali (legenda ATEC). INSERT IGNORE su status_key:
        // idempotente, NON sovrascrive le modifiche fatte dall'utente. I gruppi condividono il colore.
        c.Execute(@"INSERT IGNORE INTO ddp_statuses (status_key, label, color_bg, color_fg, sort_order) VALUES
            ('ANN',  'ANNULLATO',                                       '#000000', '#FFFFFF', 1),
            ('SOSP', 'SOSPESO',                                         '#000000', '#FFFFFF', 2),
            ('RAM',  'RIMESSO A MAGAZZINO',                             '#000000', '#FFFFFF', 3),
            ('SOST', 'SOSTITUITO',                                      '#000000', '#FFFFFF', 4),
            ('CON',  'CONSEGNATO',                                      '#00B050', '#FFFFFF', 5),
            ('COS',  'COSTRUITO',                                       '#00B050', '#FFFFFF', 6),
            ('DISP', 'DISPONIBILE',                                     '#00B050', '#FFFFFF', 7),
            ('DC',   'DA COSTRUIRE',                                    '#006400', '#FFFFFF', 8),
            ('DO',   'DA ORDINARE',                                     '#FF0000', '#FFFFFF', 9),
            ('ASS',  'ASSEGNATO AL MONTATORE',                          '#B4B4B4', '#000000', 10),
            ('CHEK', 'MAT. CHE NECESSITA CONTROLLO TECNICO/COMMERCIALE','#8B008B', '#FFFFFF', 11),
            ('IO',   'IN ORDINE',                                       '#FFFF00', '#000000', 12),
            ('PAR',  'PARZIALMENTE CONSEGNATO o COSTRUITO',             '#7030A0', '#FFFFFF', 13),
            ('RO',   'RICHIESTA OFFERTA',                               '#FFC000', '#000000', 14),
            ('VER',  'VERIFICARE SE DISPONIBILE A MAG',                 '#00B0F0', '#FFFFFF', 15),
            ('SPED', 'SPEDITO AL CLIENTE O AL FORNITORE DI SERVIZI',    '#D9D9D9', '#000000', 16),
            ('MOD',  'INVIATO A MODULA - MAG',                          '#ADD8E6', '#000000', 17)");

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
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY UQ_CatalogItem_Code (code),
            INDEX IX_CatalogItems_Description (description(255)),
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
            item_status VARCHAR(20) DEFAULT 'DO',
            requested_by VARCHAR(100) DEFAULT '',
            danea_ref VARCHAR(100) DEFAULT '',
            purchase_order VARCHAR(100) DEFAULT '',
            date_needed DATE,
            date_ordered DATE,
            date_received DATE,
            destination VARCHAR(200) DEFAULT '',
            ddp_type VARCHAR(20) DEFAULT 'COMMERCIAL',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE SET NULL,
            FOREIGN KEY (catalog_item_id) REFERENCES catalog_items(id) ON DELETE SET NULL,
            INDEX idx_bom_project (project_id)
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
        // ══════════════════════════════════════════════════════════

        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            remote_id INT NOT NULL,
            codice VARCHAR(15) NOT NULL DEFAULT '',
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

        c.Execute(@"CREATE TABLE IF NOT EXISTS codex_compositions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            parent_codex_id INT NOT NULL,
            child_codex_id INT NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (parent_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            FOREIGN KEY (child_codex_id) REFERENCES codex_items(id) ON DELETE CASCADE,
            INDEX idx_parent (parent_codex_id),
            INDEX idx_child (child_codex_id)
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

        // Modulo Gestione Risorse (allocazioni op/flex/ferie su dipendenti)
        new ResourcesDbService(this).InitTables(c);

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

    private const int LatestSchemaVersion = 7;

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

        _logger.LogInformation("[Migrations] Migrazioni applicate fino a v{Version}", LatestSchemaVersion);
    }
}
