using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Services;

// Service del modulo Segnalazioni (bug e migliorie sul gestionale).
//
// bug_reports             → la segnalazione. Visibile a tutti in lettura; la modifica del
//   contenuto è dell'autore (o di un ADMIN), il cambio di stato solo di un ADMIN.
// bug_report_attachments  → screenshot/file allegati. Sul disco stanno FUORI dalla cartella
//   servita staticamente: si scaricano solo dall'endpoint autenticato del controller.
public class BugReportsDbService
{
    private readonly DbService _db;
    private readonly ILogger<BugReportsDbService>? _logger;

    public BugReportsDbService(DbService db, ILogger<BugReportsDbService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public MySqlConnection Open() => _db.Open();

    /// <summary>Chiamato da DbService.InitDatabase() — crea le tabelle (idempotente).</summary>
    public void InitTables(MySqlConnection c)
    {
        // ON DELETE SET NULL su created_by: se un dipendente viene cancellato la segnalazione
        // resta (serve la storia dei bug), semplicemente senza autore.
        c.Execute(@"CREATE TABLE IF NOT EXISTS bug_reports (
            id INT AUTO_INCREMENT PRIMARY KEY,
            kind VARCHAR(20) NOT NULL DEFAULT 'BUG',
            title VARCHAR(300) NOT NULL,
            description TEXT NOT NULL,
            area VARCHAR(200) NOT NULL DEFAULT '',
            severity VARCHAR(20) NOT NULL DEFAULT 'MEDIUM',
            status VARCHAR(20) NOT NULL DEFAULT 'OPEN',
            admin_note TEXT NULL,
            context TEXT NULL,
            fixed_in_build VARCHAR(40) NULL,
            archived_at DATETIME NULL,
            created_by INT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL,
            resolved_at DATETIME NULL,
            row_version INT NOT NULL DEFAULT 0,
            CONSTRAINT fk_bug_employee FOREIGN KEY (created_by)
                REFERENCES employees(id) ON DELETE SET NULL,
            KEY idx_bug_status (status),
            KEY idx_bug_author (created_by),
            KEY idx_bug_archived (archived_at)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS bug_report_attachments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            bug_id INT NOT NULL,
            file_name VARCHAR(300) NOT NULL,
            stored_name VARCHAR(300) NOT NULL,
            content_type VARCHAR(150) NOT NULL DEFAULT '',
            size_bytes BIGINT NOT NULL DEFAULT 0,
            is_reply TINYINT(1) NOT NULL DEFAULT 0,
            created_by INT NULL,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_bugatt_bug FOREIGN KEY (bug_id)
                REFERENCES bug_reports(id) ON DELETE CASCADE,
            KEY idx_bugatt_bug (bug_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        _logger?.LogInformation("[InitTables] Tabelle Segnalazioni verificate/create.");
    }
}
