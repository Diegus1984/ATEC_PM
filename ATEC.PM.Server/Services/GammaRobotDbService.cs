using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Services;

// Service del sottosistema Gamma Robot: distinta schede/componenti per robot+quadro.
// L'anagrafica componenti è quote_products (listino "Gamma Ricambi", gestito dal modulo Catalogo);
// qui vivono solo le tabelle della composizione: gamma_robot → gamma_quadro → gamma_distinta.
public class GammaRobotDbService
{
    private readonly DbService _db;
    private readonly ILogger<GammaRobotDbService>? _logger;

    public GammaRobotDbService(DbService db, ILogger<GammaRobotDbService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public MySqlConnection Open() => _db.Open();

    /// <summary>
    /// Chiamato da DbService.InitDatabase() — crea le tabelle Gamma Robot (idempotente).
    /// </summary>
    public void InitTables(MySqlConnection c)
    {
        // gamma_robot — il modello/nome (IRB 1600 Type A, ...)
        c.Execute(@"CREATE TABLE IF NOT EXISTS gamma_robot (
            id INT AUTO_INCREMENT PRIMARY KEY,
            modello VARCHAR(150) NOT NULL,
            serie VARCHAR(100) NULL,
            brand VARCHAR(50) NOT NULL DEFAULT 'ABB',
            note VARCHAR(500) NULL,
            is_active TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            UNIQUE KEY uq_modello (modello)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // gamma_quadro — la configurazione (controllore=quadro + payload + OS)
        c.Execute(@"CREATE TABLE IF NOT EXISTS gamma_quadro (
            id INT AUTO_INCREMENT PRIMARY KEY,
            robot_id INT NOT NULL,
            controllore VARCHAR(50) NULL,
            generazione VARCHAR(20) NULL,
            payload VARCHAR(30) NULL,
            area_lavoro VARCHAR(30) NULL,
            os_version VARCHAR(50) NULL,
            system_key VARCHAR(120) NULL,
            source_row INT NULL,
            note VARCHAR(500) NULL,
            is_active TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_quadro_robot FOREIGN KEY (robot_id) REFERENCES gamma_robot(id) ON DELETE CASCADE,
            KEY idx_robot (robot_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // gamma_distinta — i componenti che compongono un quadro (FK a quote_products)
        c.Execute(@"CREATE TABLE IF NOT EXISTS gamma_distinta (
            id INT AUTO_INCREMENT PRIMARY KEY,
            quadro_id INT NOT NULL,
            product_id INT NULL,
            sezione VARCHAR(60) NULL,
            slot VARCHAR(90) NULL,
            code_raw VARCHAR(120) NULL,
            qty INT NOT NULL DEFAULT 1,
            is_alternate TINYINT(1) NOT NULL DEFAULT 0,
            raw_text VARCHAR(1000) NULL,
            note VARCHAR(300) NULL,
            source_row INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_dist_quadro FOREIGN KEY (quadro_id) REFERENCES gamma_quadro(id) ON DELETE CASCADE,
            CONSTRAINT fk_dist_product FOREIGN KEY (product_id) REFERENCES quote_products(id) ON DELETE SET NULL,
            KEY idx_quadro (quadro_id),
            KEY idx_product (product_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Migrazione: is_optional su gamma_distinta (componente principale / alternativa / opzione).
        // CREATE TABLE IF NOT EXISTS non aggiunge colonne a tabelle già esistenti → ALTER guidato.
        AddColumnIfMissing(c, "gamma_distinta", "is_optional", "TINYINT(1) NOT NULL DEFAULT 0 AFTER is_alternate");

        _logger?.LogInformation("[InitTables] Tabelle Gamma Robot verificate/create.");
    }

    /// <summary>Aggiunge una colonna solo se assente (idempotente). Mirror di QuoteDbService.</summary>
    private static void AddColumnIfMissing(MySqlConnection c, string table, string column, string definition)
    {
        int exists = c.ExecuteScalar<int>($@"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'");
        if (exists == 0)
            c.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }
}
