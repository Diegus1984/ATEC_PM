using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;

namespace ATEC.PM.Server.Services;

// Service del modulo Gestione Risorse: allocazioni (op/flex/ferie) su dipendenti.
// Agganci alle anagrafiche esistenti: employees (risorse) e projects (commesse).
// Anagrafiche nuove dedicate: res_services (Syncorgest), res_other_activities.
public class ResourcesDbService
{
    private readonly DbService _db;
    private readonly ILogger<ResourcesDbService>? _logger;

    public ResourcesDbService(DbService db, ILogger<ResourcesDbService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public MySqlConnection Open() => _db.Open();

    /// <summary>Chiamato da DbService.InitDatabase() — crea le tabelle Risorse (idempotente).</summary>
    public void InitTables(MySqlConnection c)
    {
        // res_services — anagrafica Service Syncorgest (codice + cliente)
        c.Execute(@"CREATE TABLE IF NOT EXISTS res_services (
            id INT AUTO_INCREMENT PRIMARY KEY,
            cod VARCHAR(60) NOT NULL,
            cliente VARCHAR(200) NULL,
            is_active TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // res_other_activities — voci ricorrenti di "altra attività"
        c.Execute(@"CREATE TABLE IF NOT EXISTS res_other_activities (
            id INT AUTO_INCREMENT PRIMARY KEY,
            descrizione VARCHAR(200) NOT NULL,
            is_active TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // res_assignments — allocazioni risorsa↔periodo (con commessa/service/altra opzionali)
        c.Execute(@"CREATE TABLE IF NOT EXISTS res_assignments (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            tipo VARCHAR(10) NOT NULL DEFAULT 'OP',
            data_inizio DATE NOT NULL,
            data_fine DATE NOT NULL,
            project_id INT NULL,
            service_id INT NULL,
            other_activity_id INT NULL,
            descrizione VARCHAR(500) NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_resa_emp FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            CONSTRAINT fk_resa_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE SET NULL,
            CONSTRAINT fk_resa_service FOREIGN KEY (service_id) REFERENCES res_services(id) ON DELETE SET NULL,
            CONSTRAINT fk_resa_other FOREIGN KEY (other_activity_id) REFERENCES res_other_activities(id) ON DELETE SET NULL,
            KEY idx_resa_emp (employee_id),
            KEY idx_resa_periodo (data_inizio, data_fine)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Migrazione idempotente: le FERIE non hanno commessa/service/altra attività.
        // Ripulisce eventuali allocazioni ferie create prima dell'invariante (dato storico).
        int cleaned = c.Execute(@"
            UPDATE res_assignments
               SET project_id = NULL, service_id = NULL, other_activity_id = NULL
             WHERE tipo = 'FERIE'
               AND (project_id IS NOT NULL OR service_id IS NOT NULL OR other_activity_id IS NOT NULL)");
        if (cleaned > 0)
            _logger?.LogInformation("[InitTables] Ferie ripulite da commessa/service/attività: {Count} righe.", cleaned);

        // Migrazione idempotente: audit "ultima modifica" per la collaborazione multi-utente.
        // updated_by = employees.id dell'autore (no FK rigida: se il dipendente è rimosso il dato storico resta).
        // updated_at abilita anche la concorrenza ottimistica (PUT confronta la versione vista).
        AddColumnIfMissing(c, "res_assignments", "updated_by", "INT NULL");
        AddColumnIfMissing(c, "res_assignments", "updated_at", "DATETIME NULL");

        InitDigestTables(c);

        _logger?.LogInformation("[InitTables] Tabelle Gestione Risorse verificate/create.");
    }

    /// <summary>
    /// Tabelle del digest email (riepilogo giornaliero modifiche piano): traccia chi ha
    /// cancellato un'allocazione, istantanee del piano per il confronto, config SMTP
    /// (chiave-valore) e registro delle esecuzioni. Vedi PlanNotificationService/EmailService.
    /// </summary>
    private void InitDigestTables(MySqlConnection c)
    {
        // res_notify_pending — chi ha cancellato un'allocazione (creazioni/modifiche restano
        // su res_assignments.updated_by; la DELETE perde la riga, quindi va registrata a parte).
        c.Execute(@"CREATE TABLE IF NOT EXISTS res_notify_pending (
            assignment_id INT PRIMARY KEY,
            made_by INT NULL,
            action VARCHAR(10) NOT NULL DEFAULT 'delete',
            orig_employee_id INT NULL,
            orig_tipo VARCHAR(10) NULL,
            orig_data_inizio DATE NULL,
            orig_data_fine DATE NULL,
            orig_project_id INT NULL,
            orig_service_id INT NULL,
            orig_other_activity_id INT NULL,
            orig_descrizione VARCHAR(500) NULL,
            touched_at DATETIME DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // res_plan_snapshot_batches / res_plan_snapshots — "fotografia" del piano al momento
        // dell'ultimo digest; il confronto con lo stato attuale produce le variazioni da notificare.
        c.Execute(@"CREATE TABLE IF NOT EXISTS res_plan_snapshot_batches (
            id INT AUTO_INCREMENT PRIMARY KEY,
            created_utc DATETIME NOT NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        c.Execute(@"CREATE TABLE IF NOT EXISTS res_plan_snapshots (
            id INT AUTO_INCREMENT PRIMARY KEY,
            batch_id INT NOT NULL,
            assignment_id INT NOT NULL,
            employee_id INT NOT NULL,
            tipo VARCHAR(10) NOT NULL,
            data_inizio DATE NOT NULL,
            data_fine DATE NOT NULL,
            project_id INT NULL,
            service_id INT NULL,
            other_activity_id INT NULL,
            descrizione VARCHAR(500) NULL,
            UNIQUE KEY uq_snap_batch_assignment (batch_id, assignment_id),
            KEY idx_snap_batch (batch_id),
            KEY idx_snap_assignment (assignment_id),
            CONSTRAINT fk_snap_batch FOREIGN KEY (batch_id) REFERENCES res_plan_snapshot_batches(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // res_settings — chiave/valore: config SMTP (email.*) + impostazioni digest (digest_*).
        c.Execute(@"CREATE TABLE IF NOT EXISTS res_settings (
            `key` VARCHAR(60) PRIMARY KEY,
            `value` VARCHAR(500) NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // res_digest_log — storico esecuzioni digest (pannello admin).
        c.Execute(@"CREATE TABLE IF NOT EXISTS res_digest_log (
            id INT AUTO_INCREMENT PRIMARY KEY,
            run_utc DATETIME NOT NULL,
            trigger_kind VARCHAR(20) NOT NULL,
            email_inviate INT NOT NULL DEFAULT 0,
            senza_email INT NOT NULL DEFAULT 0,
            esito VARCHAR(500) NULL
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");
    }

    /// <summary>ALTER TABLE … ADD COLUMN idempotente (verifica information_schema prima).</summary>
    private void AddColumnIfMissing(MySqlConnection c, string table, string column, string definition)
    {
        bool exists = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = @Table AND column_name = @Column",
            new { Table = table, Column = column }) > 0;
        if (exists) return;
        c.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
        _logger?.LogInformation("[InitTables] Aggiunta colonna {Table}.{Column}.", table, column);
    }
}
