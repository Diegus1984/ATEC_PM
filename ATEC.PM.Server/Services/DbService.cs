using MySqlConnector;
using Dapper;

namespace ATEC.PM.Server.Services;

public class DbService
{
    private readonly string _cs;

    public DbService(IConfiguration config)
    {
        _cs = config.GetConnectionString("Default")!;
    }

    public MySqlConnection Open()
    {
        var conn = new MySqlConnection(_cs);
        conn.Open();
        return conn;
    }

    public string GetConfig(string key, string defaultValue = "")
    {
        using var c = Open();
        return c.ExecuteScalar<string?>(
            "SELECT config_value FROM app_config WHERE config_key=@Key", new { Key = key }) ?? defaultValue;
    }

    public void InitDatabase()
    {
        using var c = Open();

        c.Execute(@"CREATE TABLE IF NOT EXISTS app_config (
            config_key VARCHAR(100) PRIMARY KEY,
            config_value VARCHAR(500) DEFAULT '',
            description VARCHAR(200) DEFAULT '',
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Seed configurazioni default
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM app_config") == 0)
        {
            c.Execute(@"INSERT INTO app_config (config_key, config_value, description) VALUES
        ('BasePath', 'C:\\ATEC_Commesse', 'Percorso base cartelle commesse'),
        ('TemplatePath', 'C:\\ATEC_Commesse\\MASTER_TEMPLATE', 'Percorso cartella template')");
        }

        c.Execute(@"CREATE TABLE IF NOT EXISTS employees (
            id INT AUTO_INCREMENT PRIMARY KEY,
            badge_number VARCHAR(50),
            first_name VARCHAR(100) NOT NULL,
            last_name VARCHAR(100) NOT NULL,
            email VARCHAR(200) DEFAULT '',
            phone VARCHAR(100) DEFAULT '',
            emp_type VARCHAR(20) DEFAULT 'INTERNAL',
            supplier_id INT NULL,
            hourly_cost DECIMAL(10,2) DEFAULT 0,
            weekly_hours DECIMAL(4,1) DEFAULT 40,
            hire_date DATE,
            end_date DATE NULL,
            status VARCHAR(20) DEFAULT 'ACTIVE',
            username VARCHAR(50),
            password_hash VARCHAR(255) DEFAULT '',
            user_role VARCHAR(20) DEFAULT 'TECH',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
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
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
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
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS skills (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            category VARCHAR(50) DEFAULT ''
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS employee_skills (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            skill_id INT NOT NULL,
            skill_level VARCHAR(20) DEFAULT 'MID',
            FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            FOREIGN KEY (skill_id) REFERENCES skills(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS phase_templates (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(100) NOT NULL,
            category VARCHAR(50) DEFAULT '',
            department_id INT NULL,
            sort_order INT DEFAULT 0,
            is_default BOOLEAN DEFAULT TRUE,
            FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Aggiungi department_id a phase_templates se non esiste già
        int hasPhTmplDeptCol = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='phase_templates' AND COLUMN_NAME='department_id'");
        if (hasPhTmplDeptCol == 0)
            c.Execute("ALTER TABLE phase_templates ADD COLUMN department_id INT NULL, ADD CONSTRAINT FK_PhTmpl_Dept FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL");

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
            status VARCHAR(20) DEFAULT 'DRAFT',
            priority VARCHAR(20) DEFAULT 'MEDIUM',
            server_path VARCHAR(500) DEFAULT '',
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            FOREIGN KEY (customer_id) REFERENCES customers(id),
            FOREIGN KEY (pm_id) REFERENCES employees(id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS project_phases (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            phase_template_id INT NOT NULL,
            custom_name VARCHAR(200) DEFAULT '',
            budget_hours DECIMAL(8,1) DEFAULT 0,
            budget_cost DECIMAL(12,2) DEFAULT 0,
            status VARCHAR(20) DEFAULT 'NOT_STARTED',
            progress_pct INT DEFAULT 0,
            sort_order INT DEFAULT 0,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (phase_template_id) REFERENCES phase_templates(id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS phase_assignments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_phase_id INT NOT NULL,
            employee_id INT NOT NULL,
            assign_role VARCHAR(20) DEFAULT 'MEMBER',
            planned_hours DECIMAL(8,1) DEFAULT 0,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_phase_id) REFERENCES project_phases(id) ON DELETE CASCADE,
            FOREIGN KEY (employee_id) REFERENCES employees(id)
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
            FOREIGN KEY (project_phase_id) REFERENCES project_phases(id)
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
            item_status VARCHAR(20) DEFAULT 'TO_ORDER',
            purchase_order VARCHAR(100) DEFAULT '',
            date_needed DATE,
            date_ordered DATE,
            date_received DATE,
            notes TEXT,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            FOREIGN KEY (supplier_id) REFERENCES suppliers(id) ON DELETE SET NULL,
            FOREIGN KEY (catalog_item_id) REFERENCES catalog_items(id) ON DELETE SET NULL
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
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
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
            FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
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

        c.Execute(@"CREATE TABLE IF NOT EXISTS holidays (
            id INT AUTO_INCREMENT PRIMARY KEY,
            holiday_date DATE NOT NULL,
            description VARCHAR(100) DEFAULT '',
            year INT
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ── REPARTI ──────────────────────────────────────────────────
        c.Execute(@"CREATE TABLE IF NOT EXISTS departments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            code VARCHAR(10) NOT NULL UNIQUE,
            name VARCHAR(100) NOT NULL,
            sort_order INT DEFAULT 0,
            is_active BOOLEAN DEFAULT TRUE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

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

        // Aggiungi department_id a project_phases se non esiste già
        int hasDeptCol = c.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME='project_phases' AND COLUMN_NAME='department_id'");
        if (hasDeptCol == 0)
            c.Execute("ALTER TABLE project_phases ADD COLUMN department_id INT NULL, ADD CONSTRAINT FK_Phase_Dept FOREIGN KEY (department_id) REFERENCES departments(id) ON DELETE SET NULL");

        // Seed reparti
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM departments") == 0)
        {
            c.Execute(@"INSERT INTO departments (code, name, sort_order) VALUES
                ('MEC','Meccanico',1),
                ('ELE','Elettrico',2),
                ('PLC','Software PLC',3),
                ('ROB','Software Robot',4),
                ('AMM','Contabilità',5),
                ('ACQ','Ufficio Acquisti',6),
                ('UTC','Ufficio Tecnico',7)");
        }

        // Seed admin
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM employees") == 0)
        {
            c.Execute("INSERT INTO employees (badge_number,first_name,last_name,email,emp_type,hourly_cost,weekly_hours,hire_date,status,username,password_hash,user_role) VALUES ('ADMIN','Admin','ATEC','admin@atec.it','INTERNAL',0,40,CURDATE(),'ACTIVE','admin',SHA2('admin',256),'ADMIN')");
        }

        // Seed phase templates — fasi reali ATEC
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM phase_templates") == 0)
        {
            // Recupera gli id dei reparti per il seed
            int dEle = c.ExecuteScalar<int>("SELECT id FROM departments WHERE code='ELE'");
            int dMec = c.ExecuteScalar<int>("SELECT id FROM departments WHERE code='MEC'");
            int dPlc = c.ExecuteScalar<int>("SELECT id FROM departments WHERE code='PLC'");
            int dRob = c.ExecuteScalar<int>("SELECT id FROM departments WHERE code='ROB'");
            int dUtc = c.ExecuteScalar<int>("SELECT id FROM departments WHERE code='UTC'");
            int dAcq = c.ExecuteScalar<int>("SELECT id FROM departments WHERE code='ACQ'");

            // Fasi ELE
            c.Execute(@"INSERT INTO phase_templates (name, category, department_id, sort_order, is_default) VALUES
                ('Progettazione Elettrica',             'ELE', @d, 10, 1),
                ('Cablaggio quadro elettrico',          'ELE', @d, 11, 1),
                ('Montaggio elettrico IN ATEC',         'ELE', @d, 12, 1),
                ('Preinstallazione elettrica IN ATEC',  'ELE', @d, 13, 1),
                ('Installazione elettrica in CANTIERE', 'ELE', @d, 14, 1),
                ('Collaudo Hardware',                   'ELE', @d, 15, 1),
                ('Allestimento Robot',                  'ELE', @d, 16, 0)", new { d = dEle });

            // Fasi MEC
            c.Execute(@"INSERT INTO phase_templates (name, category, department_id, sort_order, is_default) VALUES
                ('Progettazione Meccanica',                     'MEC', @d, 20, 1),
                ('Montaggio meccanico IN ATEC',                 'MEC', @d, 21, 1),
                ('Preinstallazione meccanica IN ATEC',          'MEC', @d, 22, 1),
                ('Installazione meccanica in CANTIERE',         'MEC', @d, 23, 1),
                ('Lavorazione officina meccanica',              'MEC', @d, 24, 0),
                ('Lavorazione carpenteria',                     'MEC', @d, 25, 0),
                ('Stampa 3D',                                   'MEC', @d, 26, 0),
                ('Attività di cantiere/montaggio/modifiche',    'MEC', @d, 27, 0)", new { d = dMec });

            // Fasi PLC
            c.Execute(@"INSERT INTO phase_templates (name, category, department_id, sort_order, is_default) VALUES
                ('Programmazione PLC IN ATEC',  'PLC', @d, 30, 1),
                ('Commissioning PLC',           'PLC', @d, 31, 1),
                ('Interno commissioning',       'PLC', @d, 32, 0),
                ('Sviluppo SW PC',              'PLC', @d, 33, 0)", new { d = dPlc });

            // Fasi ROB
            c.Execute(@"INSERT INTO phase_templates (name, category, department_id, sort_order, is_default) VALUES
                ('Programmazione Robot IN ATEC',    'ROB', @d, 40, 1),
                ('Commissioning Robot',             'ROB', @d, 41, 1),
                ('Simulazione Robot',               'ROB', @d, 42, 0)", new { d = dRob });

            // Fasi UTC
            c.Execute(@"INSERT INTO phase_templates (name, category, department_id, sort_order, is_default) VALUES
                ('Gestione Commessa',           'UTC', @d, 50, 1),
                ('Sviluppo avanprogetto',       'UTC', @d, 51, 0),
                ('Qualità e documentazione',    'UTC', @d, 52, 0),
                ('Riunione Ufficio Tecnico',    'UTC', @d, 53, 0),
                ('Riunione con PM',             'UTC', @d, 54, 0)", new { d = dUtc });

            // Fasi ACQ
            c.Execute(@"INSERT INTO phase_templates (name, category, department_id, sort_order, is_default) VALUES
                ('Incontro fornitori', 'ACQ', @d, 60, 0)", new { d = dAcq });

            // Fasi TRASVERSALI (nessun reparto)
            c.Execute(@"INSERT INTO phase_templates (name, category, department_id, sort_order, is_default) VALUES
                ('Viaggio',                         'TRASV', NULL, 70, 1),
                ('Formazione',                      'TRASV', NULL, 71, 0),
                ('Call/Riunione',                   'TRASV', NULL, 72, 0),
                ('Sopralluogo Cliente',              'TRASV', NULL, 73, 0),
                ('Assistenza Clienti',              'TRASV', NULL, 74, 0),
                ('Assistenza al commerciale',       'TRASV', NULL, 75, 0),
                ('Assistenza al montaggio ATEC',    'TRASV', NULL, 76, 0),
                ('Assistenza alla produzione',      'TRASV', NULL, 77, 0),
                ('Supporto Cantiere Cliente',       'TRASV', NULL, 78, 0),
                ('Prove nuove applicazioni',        'TRASV', NULL, 79, 0),
                ('Ripresa non conformità pezzi',    'TRASV', NULL, 80, 0)");
        }

        // Seed skills
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM skills") == 0)
        {
            c.Execute(@"INSERT INTO skills (name,category) VALUES
                ('Progettista Elettrico','DESIGN'),('Progettista Meccanico','DESIGN'),('Layout / CAD','DESIGN'),
                ('Programmatore PLC','DEV'),('Programmatore HMI','DEV'),('Programmatore Robot','DEV'),
                ('Sviluppatore SW','DEV'),('Cablatore','PRODUCTION'),('Assemblatore Meccanico','PRODUCTION'),
                ('Installatore','INSTALL'),('Commissioning','INSTALL'),('Collaudatore','TEST')");
        }

        // Seed holidays 2026
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM holidays WHERE year=2026") == 0)
        {
            c.Execute(@"INSERT INTO holidays (holiday_date,description,year) VALUES
                ('2026-01-01','Capodanno',2026),('2026-01-06','Epifania',2026),
                ('2026-04-05','Pasqua',2026),('2026-04-06','Lunedi Angelo',2026),
                ('2026-04-25','Liberazione',2026),('2026-05-01','Festa Lavoratori',2026),
                ('2026-06-02','Festa Repubblica',2026),('2026-06-24','San Giovanni',2026),
                ('2026-08-15','Ferragosto',2026),('2026-11-01','Ognissanti',2026),
                ('2026-12-08','Immacolata',2026),('2026-12-25','Natale',2026),('2026-12-26','Santo Stefano',2026)");
        }

        Console.WriteLine("[DB] Inizializzato.");
    }
}
