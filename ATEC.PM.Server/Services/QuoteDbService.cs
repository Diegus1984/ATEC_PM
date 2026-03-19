using MySqlConnector;
using Dapper;

namespace ATEC.PM.Server.Services;

public class QuoteDbService
{
    private readonly DbService _db;

    public QuoteDbService(DbService db) => _db = db;

    public MySqlConnection Open() => _db.Open();

    /// <summary>
    /// Chiamato da DbService.InitDatabase() — crea le tabelle del modulo Preventivi/Catalogo.
    /// </summary>
    public void InitTables(MySqlConnection c)
    {
        // ──────────────────────────────────────────────────
        // QUOTE CATALOG — Listini → Gruppi → Categorie → Prodotti → Varianti
        // ──────────────────────────────────────────────────

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_price_lists (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            currency VARCHAR(10) DEFAULT 'EUR',
            locale VARCHAR(10) DEFAULT 'it',
            is_active TINYINT(1) DEFAULT 1,
            sort_order INT DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_groups (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            description TEXT,
            sort_order INT DEFAULT 0,
            is_active TINYINT(1) DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            INDEX idx_active (is_active)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_categories (
            id INT AUTO_INCREMENT PRIMARY KEY,
            group_id INT NOT NULL,
            name VARCHAR(200) NOT NULL,
            description TEXT,
            sort_order INT DEFAULT 0,
            is_active TINYINT(1) DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            FOREIGN KEY (group_id) REFERENCES quote_groups(id) ON DELETE CASCADE,
            INDEX idx_group (group_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_products (
            id INT AUTO_INCREMENT PRIMARY KEY,
            category_id INT NOT NULL,
            item_type ENUM('product','content') NOT NULL DEFAULT 'product',
            code VARCHAR(100) DEFAULT '',
            name VARCHAR(300) NOT NULL,
            description_rtf TEXT,
            image_path VARCHAR(500) DEFAULT '',
            attachment_path VARCHAR(500) DEFAULT '',
            auto_include TINYINT(1) DEFAULT 0,
            sort_order INT DEFAULT 0,
            is_active TINYINT(1) DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            FOREIGN KEY (category_id) REFERENCES quote_categories(id) ON DELETE CASCADE,
            INDEX idx_category (category_id),
            INDEX idx_type (item_type)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_product_variants (
            id INT AUTO_INCREMENT PRIMARY KEY,
            product_id INT NOT NULL,
            code VARCHAR(100) DEFAULT '',
            name VARCHAR(300) NOT NULL,
            cost_price DECIMAL(12,2) DEFAULT 0,
            sell_price DECIMAL(12,2) DEFAULT 0,
            discount_pct DECIMAL(5,2) DEFAULT 0,
            vat_pct DECIMAL(5,2) DEFAULT 22.00,
            unit VARCHAR(50) DEFAULT 'nr.',
            default_qty DECIMAL(10,2) DEFAULT 1,
            sort_order INT DEFAULT 0,
            FOREIGN KEY (product_id) REFERENCES quote_products(id) ON DELETE CASCADE,
            INDEX idx_product (product_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // ──────────────────────────────────────────────────
        // QUOTES — Preventivi
        // ──────────────────────────────────────────────────

        c.Execute(@"CREATE TABLE IF NOT EXISTS quotes (
            id INT AUTO_INCREMENT PRIMARY KEY,
            quote_number VARCHAR(50) NOT NULL UNIQUE,
            title VARCHAR(300) NOT NULL,
            customer_id INT NOT NULL,
            contact_name1 VARCHAR(200) DEFAULT '',
            contact_name2 VARCHAR(200) DEFAULT '',
            contact_name3 VARCHAR(200) DEFAULT '',
            delivery_days INT DEFAULT 0,
            validity_days INT DEFAULT 60,
            payment_type VARCHAR(200) DEFAULT '',
            language VARCHAR(10) DEFAULT 'it',
            status ENUM('draft','sent','negotiation','accepted',
                         'rejected','expired','converted') DEFAULT 'draft',
            revision INT DEFAULT 0,
            group_id INT,
            subtotal DECIMAL(14,2) DEFAULT 0,
            discount_pct DECIMAL(5,2) DEFAULT 0,
            discount_abs DECIMAL(12,2) DEFAULT 0,
            vat_total DECIMAL(14,2) DEFAULT 0,
            total DECIMAL(14,2) DEFAULT 0,
            total_with_vat DECIMAL(14,2) DEFAULT 0,
            cost_total DECIMAL(14,2) DEFAULT 0,
            profit DECIMAL(14,2) DEFAULT 0,
            show_item_prices TINYINT(1) DEFAULT 1,
            show_summary TINYINT(1) DEFAULT 1,
            show_summary_prices TINYINT(1) DEFAULT 1,
            notes_internal TEXT,
            notes_quote TEXT,
            project_id INT,
            assigned_to INT,
            created_by INT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            sent_at DATETIME,
            accepted_at DATETIME,
            converted_at DATETIME,
            FOREIGN KEY (customer_id) REFERENCES customers(id),
            FOREIGN KEY (group_id) REFERENCES quote_groups(id),
            FOREIGN KEY (project_id) REFERENCES projects(id),
            FOREIGN KEY (assigned_to) REFERENCES employees(id),
            FOREIGN KEY (created_by) REFERENCES employees(id),
            INDEX idx_status (status),
            INDEX idx_customer (customer_id),
            INDEX idx_created (created_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            quote_id INT NOT NULL,
            product_id INT,
            variant_id INT,
            item_type ENUM('product','content') NOT NULL DEFAULT 'product',
            code VARCHAR(100) DEFAULT '',
            name VARCHAR(300) NOT NULL,
            description_rtf TEXT,
            unit VARCHAR(50) DEFAULT 'nr.',
            quantity DECIMAL(10,2) DEFAULT 1,
            cost_price DECIMAL(12,2) DEFAULT 0,
            sell_price DECIMAL(12,2) DEFAULT 0,
            discount_pct DECIMAL(5,2) DEFAULT 0,
            vat_pct DECIMAL(5,2) DEFAULT 22.00,
            line_total DECIMAL(14,2) DEFAULT 0,
            line_profit DECIMAL(14,2) DEFAULT 0,
            sort_order INT DEFAULT 0,
            FOREIGN KEY (quote_id) REFERENCES quotes(id) ON DELETE CASCADE,
            FOREIGN KEY (product_id) REFERENCES quote_products(id) ON DELETE SET NULL,
            FOREIGN KEY (variant_id) REFERENCES quote_product_variants(id) ON DELETE SET NULL,
            INDEX idx_quote (quote_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_revisions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            quote_id INT NOT NULL,
            revision INT NOT NULL,
            snapshot_json JSON NOT NULL,
            change_notes TEXT,
            created_by INT NOT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (quote_id) REFERENCES quotes(id) ON DELETE CASCADE,
            FOREIGN KEY (created_by) REFERENCES employees(id),
            UNIQUE KEY uk_quote_rev (quote_id, revision)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_documents (
            id INT AUTO_INCREMENT PRIMARY KEY,
            quote_id INT NOT NULL,
            file_name VARCHAR(300) NOT NULL,
            file_path VARCHAR(500) NOT NULL,
            file_size BIGINT DEFAULT 0,
            mime_type VARCHAR(100) DEFAULT '',
            uploaded_by INT NOT NULL,
            uploaded_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            FOREIGN KEY (quote_id) REFERENCES quotes(id) ON DELETE CASCADE,
            FOREIGN KEY (uploaded_by) REFERENCES employees(id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS quote_status_log (
            id INT AUTO_INCREMENT PRIMARY KEY,
            quote_id INT NOT NULL,
            old_status VARCHAR(20) DEFAULT '',
            new_status VARCHAR(20) NOT NULL,
            changed_by INT NOT NULL,
            changed_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            notes TEXT,
            FOREIGN KEY (quote_id) REFERENCES quotes(id) ON DELETE CASCADE,
            FOREIGN KEY (changed_by) REFERENCES employees(id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        Console.WriteLine("[QuoteDB] Tabelle modulo Preventivi inizializzate.");
    }

    public void ApplyMigrations(MySqlConnection c)
    {
        // Listino: FK su quote_groups e quotes
        AddColumnIfMissing(c, "quote_groups", "price_list_id", "INT NULL AFTER id");
        AddColumnIfMissing(c, "quotes", "price_list_id", "INT NULL AFTER group_id");

        // Varianti nel preventivo: toggle attiva/conferma + raggruppamento
        AddColumnIfMissing(c, "quote_items", "is_active", "TINYINT(1) DEFAULT 1 AFTER sort_order");
        AddColumnIfMissing(c, "quote_items", "is_confirmed", "TINYINT(1) DEFAULT 0 AFTER is_active");
        AddColumnIfMissing(c, "quote_items", "parent_item_id", "INT NULL AFTER is_confirmed");
    }

    private static void AddColumnIfMissing(MySqlConnection c, string table, string column, string definition)
    {
        int exists = c.ExecuteScalar<int>($@"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'");
        if (exists == 0)
            c.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }
}
