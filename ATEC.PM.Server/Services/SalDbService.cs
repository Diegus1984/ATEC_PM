using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;
using ATEC.PM.Server.Data;

namespace ATEC.PM.Server.Services;

// Service per la gestione dei SAL (Stato Avanzamento Lavori) e delle Fatturazioni associate.
// Gestisce tabelle sal_conditions, project_sal e sal_rows.
public class SalDbService
{
    private readonly DbService _db;
    private readonly ILogger<SalDbService>? _logger;

    public SalDbService(DbService db, ILogger<SalDbService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public MySqlConnection Open() => _db.Open();

    /// <summary>
    /// Crea le tabelle relative al modulo SAL se non esistono.
    /// </summary>
    public void InitTables(MySqlConnection c)
    {
        // sal_conditions: Anagrafica globale delle condizioni di pagamento.
        c.Execute(@"CREATE TABLE IF NOT EXISTS sal_conditions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            label VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // project_sal: Header SAL per commessa (cliente + valore).
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_sal (
            project_id INT NOT NULL PRIMARY KEY,
            cliente VARCHAR(300) NOT NULL DEFAULT '',
            valore DECIMAL(14,2) NULL,
            row_version INT NOT NULL DEFAULT 0,
            updated_at DATETIME NULL,
            CONSTRAINT fk_psal_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        // sal_rows: Step/righe di pagamento per commessa.
        c.Execute(@"CREATE TABLE IF NOT EXISTS sal_rows (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            step VARCHAR(1000) NOT NULL DEFAULT '',
            perc DECIMAL(6,3) NULL,
            condizione VARCHAR(200) NOT NULL DEFAULT '',
            data_fatt DATE NULL,
            stato VARCHAR(10) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_by INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL,
            CONSTRAINT fk_salrow_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            KEY idx_salrow_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;");

        _logger?.LogInformation("[InitTables] Tabelle SAL (sal_conditions, project_sal, sal_rows) verificate/create.");
    }

    /// <summary>
    /// Esegue il seed delle condizioni di pagamento standard se vuoto.
    /// </summary>
    public void SeedConditions(MySqlConnection c)
    {
        int count = c.ExecuteScalar<int>("SELECT COUNT(*) FROM sal_conditions");
        if (count > 0) return;

        string[] standardConditions = new[] { "A Vista", "30 gg. dffm.", "60 gg. dffm.", "90 gg. dffm." };
        int order = 1;
        foreach (string cond in standardConditions)
        {
            c.Execute("INSERT INTO sal_conditions (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                new { Label = cond, Sort = order++ });
        }
        _logger?.LogInformation("[SeedConditions] Condizioni di pagamento SAL standard inserite.");
    }
}
