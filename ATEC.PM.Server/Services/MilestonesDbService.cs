using MySqlConnector;
using Dapper;
using Microsoft.Extensions.Logging;
using ATEC.PM.Server.Data;

namespace ATEC.PM.Server.Services;

// Service dei moduli "Anagrafica attività" (catalogo globale) e "Milestone" (pianificazione per-commessa).
//
// activity_catalog  → elenco globale delle voci-attività standard di progetto. È il catalogo da cui si
//   precaricano le milestone alla creazione di una commessa. Copia PER VALORE: una milestone NON
//   referenzia il catalogo (source_catalog_id è solo tracciabilità, senza FK), quindi modifiche o
//   cancellazioni nel catalogo NON toccano le commesse già create.
//
// project_milestones → le milestone/righe di pianificazione di UNA commessa (project_id, CASCADE).
//   W.Inizio/W.Fine/W.Tot e lo stato/colore NON sono persistiti: sono derivati lato client dalle date
//   (settimana ISO 8601 e confronto con la data odierna). "spento" ed "evidenza" (hl) sono colonne
//   della riga (nel prototipo lo "spento" viveva nel registro Gantt, qui è promosso a colonna).
public class MilestonesDbService
{
    private readonly DbService _db;
    private readonly ILogger<MilestonesDbService>? _logger;

    public MilestonesDbService(DbService db, ILogger<MilestonesDbService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public MySqlConnection Open() => _db.Open();

    /// <summary>
    /// Chiamato da DbService.InitDatabase() — crea le tabelle (idempotente).
    /// Il SEED del catalogo avviene una-tantum nella migrazione v15 (vedi <see cref="SeedCatalog"/>),
    /// così le voci eliminate dall'utente non ricompaiono ad ogni avvio.
    /// </summary>
    public void InitTables(MySqlConnection c)
    {
        // activity_catalog — catalogo globale delle voci attività standard (nessuna FK).
        c.Execute(@"CREATE TABLE IF NOT EXISTS activity_catalog (
            id INT AUTO_INCREMENT PRIMARY KEY,
            label VARCHAR(300) NOT NULL,
            sort_order INT NOT NULL DEFAULT 0,
            is_active BOOLEAN NOT NULL DEFAULT TRUE,
            created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // project_milestones — righe di pianificazione (milestone) di una commessa.
        // ON DELETE CASCADE: eliminando la commessa spariscono le sue milestone.
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_milestones (
            id INT AUTO_INCREMENT PRIMARY KEY,
            project_id INT NOT NULL,
            descrizione VARCHAR(1000) NOT NULL DEFAULT '',
            data_inizio DATE NULL,
            data_fine DATE NULL,
            avanzamento TINYINT NULL,
            note VARCHAR(2000) NOT NULL DEFAULT '',
            evidenza TINYINT(1) NOT NULL DEFAULT 0,
            spento TINYINT(1) NOT NULL DEFAULT 0,
            sort_order INT NOT NULL DEFAULT 0,
            row_version INT NOT NULL DEFAULT 0,
            source_catalog_id INT NULL,
            created_by INT NULL,
            created_at DATETIME DEFAULT CURRENT_TIMESTAMP,
            updated_at DATETIME NULL,
            CONSTRAINT fk_pms_project FOREIGN KEY (project_id) REFERENCES projects(id) ON DELETE CASCADE,
            KEY idx_pms_project (project_id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        _logger?.LogInformation("[InitTables] Tabelle Anagrafica attività / Milestone verificate/create.");
    }

    /// <summary>
    /// Seed una-tantum del catalogo attività con l'elenco standard. Non fa nulla se il catalogo
    /// contiene già delle voci (già seedato o già personalizzato). Pensato per la migrazione v15.
    /// </summary>
    public void SeedCatalog(MySqlConnection c)
    {
        int count = c.ExecuteScalar<int>("SELECT COUNT(*) FROM activity_catalog");
        if (count > 0) return;

        int order = 1;
        foreach (string label in ActivityCatalogSeed.Labels)
            c.Execute("INSERT INTO activity_catalog (label, sort_order, is_active) VALUES (@Label, @Sort, TRUE)",
                new { Label = label, Sort = order++ });
    }
}
