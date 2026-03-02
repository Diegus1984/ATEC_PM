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

    public void InitDatabase()
    {
        using var c = Open();

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
            phone VARCHAR(100) DEFAULT '',
            address VARCHAR(300) DEFAULT '',
            vat_number VARCHAR(50) DEFAULT '',
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
            sort_order INT DEFAULT 0,
            is_default BOOLEAN DEFAULT TRUE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

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

        // Seed admin
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM employees") == 0)
        {
            c.Execute("INSERT INTO employees (badge_number,first_name,last_name,email,emp_type,hourly_cost,weekly_hours,hire_date,status,username,password_hash,user_role) VALUES ('ADMIN','Admin','ATEC','admin@atec.it','INTERNAL',0,40,CURDATE(),'ACTIVE','admin',SHA2('admin',256),'ADMIN')");
        }

        // Seed phase templates
        if (c.ExecuteScalar<int>("SELECT COUNT(*) FROM phase_templates") == 0)
        {
            c.Execute(@"INSERT INTO phase_templates (name,category,sort_order,is_default) VALUES
                ('Offerta / Preventivo','DESIGN',1,1),('Progettazione Elettrica','DESIGN',2,1),
                ('Progettazione Meccanica','DESIGN',3,1),('Progettazione Pneumatica','DESIGN',4,0),
                ('Sviluppo SW PLC','DEV',5,1),('Sviluppo SW HMI','DEV',6,1),('Sviluppo SW Robot','DEV',7,0),
                ('Approvvigionamento Materiali','PRODUCTION',8,1),('Cablaggio Quadri','PRODUCTION',9,1),
                ('Assemblaggio Meccanico','PRODUCTION',10,0),('Prefabbricazione','PRODUCTION',11,0),
                ('Installazione On-Site','INSTALL',12,1),('Commissioning','INSTALL',13,1),
                ('Test / Collaudo FAT','TEST',14,1),('Collaudo SAT','TEST',15,1),
                ('Documentazione As-Built','DESIGN',16,0),('Garanzia / Assistenza','TEST',17,0)");
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
