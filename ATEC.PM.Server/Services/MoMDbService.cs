using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services;

// Service del modulo MoM (verbali di riunione): verbale → action item.
// Si aggancia alle anagrafiche esistenti: projects (commesse) ed employees (responsabili).
public class MoMDbService
{
    private readonly DbService _db;
    private readonly ILogger<MoMDbService>? _logger;

    public MoMDbService(DbService db, ILogger<MoMDbService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public MySqlConnection Open() => _db.Open();

    /// <summary>
    /// Chiamato da DbService.InitDatabase() — crea le tabelle MoM (idempotente).
    /// </summary>
    public void InitTables(MySqlConnection c)
    {
        // mom_records — il verbale (di commessa o riunione generica)
        c.Execute(@"CREATE TABLE IF NOT EXISTS mom_records (
            id INT AUTO_INCREMENT PRIMARY KEY,
            tipo VARCHAR(20) NOT NULL DEFAULT 'RIUNIONE',
            project_id INT NULL,
            title VARCHAR(300) NOT NULL,
            meeting_date DATE NULL,
            in_dashboard TINYINT(1) NOT NULL DEFAULT 1,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_mom_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE SET NULL,
            KEY idx_mom_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // mom_action_items — le righe del verbale (attività/azioni con responsabili e scadenze).
        // Responsabili = 3 colonne FK a employees (max 3 slot, niente tabella di legame).
        c.Execute(@"CREATE TABLE IF NOT EXISTS mom_action_items (
            id INT AUTO_INCREMENT PRIMARY KEY,
            mom_id INT NOT NULL,
            attivita VARCHAR(500) NOT NULL,
            descrizione VARCHAR(1000) NULL,
            azione VARCHAR(1000) NULL,
            priorita TINYINT NOT NULL DEFAULT 2,
            status VARCHAR(20) NOT NULL DEFAULT 'OPEN',
            is_critical TINYINT(1) NOT NULL DEFAULT 0,
            resp1_id INT NULL,
            resp2_id INT NULL,
            resp3_id INT NULL,
            data_check DATE NULL,
            data_close DATE NULL,
            sort_order INT NOT NULL DEFAULT 0,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_mai_mom FOREIGN KEY (mom_id) REFERENCES mom_records(id) ON DELETE CASCADE,
            CONSTRAINT fk_mai_r1 FOREIGN KEY (resp1_id) REFERENCES employees(id) ON DELETE SET NULL,
            CONSTRAINT fk_mai_r2 FOREIGN KEY (resp2_id) REFERENCES employees(id) ON DELETE SET NULL,
            CONSTRAINT fk_mai_r3 FOREIGN KEY (resp3_id) REFERENCES employees(id) ON DELETE SET NULL,
            KEY idx_mai_mom (mom_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Migrazione difensiva: garantisce le 3 colonne responsabile anche se la tabella
        // esisteva già con uno schema diverso (CREATE TABLE IF NOT EXISTS non altera).
        AddColumnIfMissing(c, "mom_action_items", "resp1_id", "INT NULL");
        AddColumnIfMissing(c, "mom_action_items", "resp2_id", "INT NULL");
        AddColumnIfMissing(c, "mom_action_items", "resp3_id", "INT NULL");

        // N responsabili per action item (oltre alle 3 colonne legacy resp1/2/3).
        c.Execute(@"CREATE TABLE IF NOT EXISTS mom_action_item_responsibles (
            id INT AUTO_INCREMENT PRIMARY KEY,
            action_item_id INT NOT NULL,
            employee_id INT NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            CONSTRAINT fk_mair_item FOREIGN KEY (action_item_id) REFERENCES mom_action_items(id) ON DELETE CASCADE,
            CONSTRAINT fk_mair_emp FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            UNIQUE KEY uk_mair_item_emp (action_item_id, employee_id),
            KEY idx_mair_item (action_item_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        AddColumnIfMissing(c, "mom_action_items", "sort_order", "INT NOT NULL DEFAULT 0");
        AddColumnIfMissing(c, "mom_action_item_responsibles", "sort_order", "INT NOT NULL DEFAULT 0");

        // ── Gestione v9 (prototipo Gestione_MoM_v9) ──
        // Revisione del verbale: numero corrente sul record + storico dei cambi data riunione.
        AddColumnIfMissing(c, "mom_records", "rev", "INT NOT NULL DEFAULT 0");
        c.Execute(@"CREATE TABLE IF NOT EXISTS mom_revisions (
            id INT AUTO_INCREMENT PRIMARY KEY,
            mom_id INT NOT NULL,
            rev INT NOT NULL,
            meeting_date DATE NULL,
            prev_date DATE NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_mrev_mom FOREIGN KEY (mom_id) REFERENCES mom_records(id) ON DELETE CASCADE,
            KEY idx_mrev_mom (mom_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Note MoM (acquisizione rapida): staging personale per dipendente, la nota
        // vive qui finché non viene assegnata a un verbale (poi viene eliminata).
        c.Execute(@"CREATE TABLE IF NOT EXISTS mom_notes (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            note VARCHAR(2000) NOT NULL DEFAULT '',
            target_mom_id INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            CONSTRAINT fk_mnote_emp FOREIGN KEY (employee_id) REFERENCES employees(id) ON DELETE CASCADE,
            CONSTRAINT fk_mnote_mom FOREIGN KEY (target_mom_id) REFERENCES mom_records(id) ON DELETE SET NULL,
            KEY idx_mnote_emp (employee_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Token di concorrenza ottimistica sul foglio condiviso (regola ambiente multi-utente).
        AddColumnIfMissing(c, "mom_action_items", "row_version", "INT NOT NULL DEFAULT 0");

        MigrateLegacyResponsibles(c);

        EnsureWildcardEmployees(c);

        _logger?.LogInformation("[InitTables] Tabelle MoM verificate/create.");
    }

    /// <summary>
    /// Garantisce un dipendente "wildcard" per reparto MoM ([PM] Generico, [UTM] Generico, …).
    /// Usato dal client per pre-assegnare Resp.1 quando si aggiunge un'azione con filtro reparto attivo.
    /// </summary>
    public void EnsureWildcardEmployees(MySqlConnection c)
    {
        string[] deptCodes = { "PM", "UTM", "UTE", "MEC", "INS", "PLC", "ROB", "ACQ", "AMM" };
        foreach (string code in deptCodes)
        {
            string firstName = $"[{code}]";
            int exists = c.ExecuteScalar<int>(@"
                SELECT COUNT(*) FROM employees
                WHERE first_name = @FirstName AND last_name = 'Generico'",
                new { FirstName = firstName });
            if (exists > 0) continue;

            int empId = c.ExecuteScalar<int>(@"
                INSERT INTO employees (first_name, last_name, email, emp_type, status, user_role)
                VALUES (@FirstName, 'Generico', '', 'INTERNAL', 'ACTIVE', 'TECH');
                SELECT LAST_INSERT_ID()",
                new { FirstName = firstName });

            int? deptId = c.ExecuteScalar<int?>(
                "SELECT id FROM departments WHERE code = @Code LIMIT 1",
                new { Code = code });
            if (deptId.HasValue)
            {
                int linkExists = c.ExecuteScalar<int>(@"
                    SELECT COUNT(*) FROM employee_departments
                    WHERE employee_id = @EmpId AND department_id = @DeptId",
                    new { EmpId = empId, DeptId = deptId.Value });
                if (linkExists == 0)
                {
                    c.Execute(@"
                        INSERT INTO employee_departments (employee_id, department_id)
                        VALUES (@EmpId, @DeptId)",
                        new { EmpId = empId, DeptId = deptId.Value });
                }
            }

            _logger?.LogInformation("[InitTables] Wildcard MoM creato: {Name} (id={Id})", $"{firstName} Generico", empId);
        }
    }

    /// <summary>Salva l'elenco completo responsabili (N) e sincronizza resp1/2/3 legacy.</summary>
    public void SaveItemResponsibles(MySqlConnection c, int itemId, IReadOnlyList<int> employeeIds)
    {
        c.Execute("DELETE FROM mom_action_item_responsibles WHERE action_item_id=@Id", new { Id = itemId });

        List<int> ordered = new List<int>();
        foreach (int empId in employeeIds)
        {
            if (empId <= 0 || ordered.Contains(empId)) continue;
            ordered.Add(empId);
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            c.Execute(@"
                INSERT INTO mom_action_item_responsibles (action_item_id, employee_id, sort_order)
                VALUES (@ItemId, @EmpId, @Ord)",
                new { ItemId = itemId, EmpId = ordered[i], Ord = i });
        }

        c.Execute(@"
            UPDATE mom_action_items SET resp1_id=@R1, resp2_id=@R2, resp3_id=@R3 WHERE id=@Id",
            new
            {
                Id = itemId,
                R1 = ordered.Count > 0 ? ordered[0] : (int?)null,
                R2 = ordered.Count > 1 ? ordered[1] : (int?)null,
                R3 = ordered.Count > 2 ? ordered[2] : (int?)null
            });
    }

    /// <summary>Popola ResponsibleIds/Names sui DTO caricati dal DB.</summary>
    public void AttachResponsibles(MySqlConnection c, IList<MoMActionItemDto> items)
    {
        if (items.Count == 0) return;

        List<int> itemIds = items.Select(i => i.Id).ToList();
        List<MoMResponsibleLinkRow> rows = c.Query<MoMResponsibleLinkRow>(@"
            SELECT r.action_item_id AS ActionItemId, r.employee_id AS EmployeeId,
                   CONCAT_WS(' ', e.first_name, e.last_name) AS Name, r.sort_order AS SortOrder
            FROM mom_action_item_responsibles r
            INNER JOIN employees e ON e.id = r.employee_id
            WHERE r.action_item_id IN @Ids
            ORDER BY r.action_item_id, r.sort_order, r.employee_id",
            new { Ids = itemIds }).ToList();

        foreach (MoMActionItemDto item in items)
        {
            List<MoMResponsibleLinkRow> mine = rows.Where(r => r.ActionItemId == item.Id).ToList();
            if (mine.Count == 0)
            {
                // Fallback dati legacy resp1/2/3
                if (item.Resp1Id.HasValue) { item.ResponsibleIds.Add(item.Resp1Id.Value); item.ResponsibleNames.Add(item.Resp1Name ?? ""); }
                if (item.Resp2Id.HasValue && item.Resp2Id != item.Resp1Id) { item.ResponsibleIds.Add(item.Resp2Id.Value); item.ResponsibleNames.Add(item.Resp2Name ?? ""); }
                if (item.Resp3Id.HasValue && !item.ResponsibleIds.Contains(item.Resp3Id.Value)) { item.ResponsibleIds.Add(item.Resp3Id.Value); item.ResponsibleNames.Add(item.Resp3Name ?? ""); }
                continue;
            }

            item.ResponsibleIds = mine.Select(r => r.EmployeeId).ToList();
            item.ResponsibleNames = mine.Select(r => r.Name).ToList();
            if (item.ResponsibleIds.Count > 0) { item.Resp1Id = item.ResponsibleIds[0]; item.Resp1Name = item.ResponsibleNames[0]; }
            if (item.ResponsibleIds.Count > 1) { item.Resp2Id = item.ResponsibleIds[1]; item.Resp2Name = item.ResponsibleNames[1]; }
            if (item.ResponsibleIds.Count > 2) { item.Resp3Id = item.ResponsibleIds[2]; item.Resp3Name = item.ResponsibleNames[2]; }
        }
    }

    private void MigrateLegacyResponsibles(MySqlConnection c)
    {
        List<LegacyRespRow> legacy = c.Query<LegacyRespRow>(@"
            SELECT id AS Id, resp1_id AS R1, resp2_id AS R2, resp3_id AS R3
            FROM mom_action_items").ToList();

        foreach (LegacyRespRow row in legacy)
        {
            int linkCount = c.ExecuteScalar<int>(
                "SELECT COUNT(*) FROM mom_action_item_responsibles WHERE action_item_id=@Id",
                new { Id = row.Id });
            if (linkCount > 0) continue;

            List<int> ids = new List<int>();
            if (row.R1.HasValue) ids.Add(row.R1.Value);
            if (row.R2.HasValue && row.R2 != row.R1) ids.Add(row.R2.Value);
            if (row.R3.HasValue && !ids.Contains(row.R3.Value)) ids.Add(row.R3.Value);
            if (ids.Count > 0) SaveItemResponsibles(c, row.Id, ids);
        }
    }

    private sealed class MoMResponsibleLinkRow
    {
        public int ActionItemId { get; set; }
        public int EmployeeId { get; set; }
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
    }

    private sealed class LegacyRespRow
    {
        public int Id { get; set; }
        public int? R1 { get; set; }
        public int? R2 { get; set; }
        public int? R3 { get; set; }
    }

    /// <summary>Aggiunge una colonna solo se assente (idempotente).</summary>
    private static void AddColumnIfMissing(MySqlConnection c, string table, string column, string definition)
    {
        int exists = c.ExecuteScalar<int>($@"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'");
        if (exists == 0)
            c.Execute($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
    }
}
