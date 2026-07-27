using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Services;

// Service del modulo Check list / Attività.
// Un'attività (checklist_items) appartiene a UNA commessa reale (project_id) OPPURE a un
// gruppo generico (group_id → checklist_groups: DIREZIONE, VARIE, custom). L'inbox
// "Fissa attività" (checklist_inbox) è lo staging personale per dipendente.
public class CheckListDbService
{
    private readonly DbService _db;
    private readonly ILogger<CheckListDbService>? _logger;

    public CheckListDbService(DbService db, ILogger<CheckListDbService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public MySqlConnection Open() => _db.Open();

    /// <summary>
    /// Chiamato da DbService.InitDatabase() — crea le tabelle Check list (idempotente).
    /// </summary>
    public void InitTables(MySqlConnection c)
    {
        // checklist_groups — gruppi generici (non commessa): DIREZIONE, VARIE, custom.
        c.Execute(@"CREATE TABLE IF NOT EXISTS checklist_groups (
            id INT AUTO_INCREMENT PRIMARY KEY,
            name VARCHAR(200) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // checklist_items — le attività. Vincolo applicativo: project_id XOR group_id.
        // ON DELETE CASCADE su entrambe le FK: eliminando una commessa o un gruppo
        // spariscono le sue attività.
        c.Execute(@"CREATE TABLE IF NOT EXISTS checklist_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NULL,
            group_id INT NULL,
            description VARCHAR(1000) NOT NULL DEFAULT '',
            priority TINYINT NOT NULL DEFAULT 2,
            due_date DATE NULL,
            is_critical TINYINT(1) NOT NULL DEFAULT 0,
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            created_by INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_cli_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            CONSTRAINT fk_cli_group FOREIGN KEY (group_id) REFERENCES checklist_groups(id) ON DELETE CASCADE,
            KEY idx_cli_project (project_id),
            KEY idx_cli_group (group_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        EnsureColumn(c, "checklist_items", "created_at", "DATETIME DEFAULT CURRENT_TIMESTAMP");

        // Stato attività (allineato a mom_action_items.status): OPEN · STANDBY · CLOSED.
        // Un'attività CLOSED è considerata gestita: non genera notifiche di scadenza né
        // viene conteggiata come scaduta. data_close registra quando è stata chiusa.
        EnsureColumn(c, "checklist_items", "status", "VARCHAR(20) NOT NULL DEFAULT 'OPEN'");
        EnsureColumn(c, "checklist_items", "data_close", "DATE NULL");

        // checklist_inbox — "Fissa attività": staging personale per dipendente. La nota
        // vive qui finché non viene assegnata a una commessa/gruppo (poi viene eliminata).
        c.Execute(@"CREATE TABLE IF NOT EXISTS checklist_inbox (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            note VARCHAR(2000) NOT NULL DEFAULT '',
            sort_order INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_clinbox_emp FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            KEY idx_clinbox_emp (employee_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        _logger?.LogInformation("[InitTables] Tabelle Check list verificate/create.");
    }

    private static void EnsureColumn(MySqlConnection c, string table, string column, string definition)
    {
        int exists = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @Table AND COLUMN_NAME = @Column",
            new { Table = table, Column = column });
        if (exists == 0)
            c.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }
}
