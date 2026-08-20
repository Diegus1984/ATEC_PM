using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v80 — CAUSALE «EXTRA LAVORO» sulle ore di commessa (segnalazione #39).
// Il PM guarda le imputazioni una per una e può spostarne alcune su «Extra Lavoro»:
// da quel momento quelle ore NON pesano più sui costi della commessa. Nella pagina
// Extra Lavoro ogni riga si può rimettere dentro, per vedere quanto cambierebbe la
// redditività se quel lavoro venisse caricato.
// Tabella LATERALE, non una colonna su timesheet_entries: le ore restano quelle che
// la persona ha scritto — non si riscrive il timesheet di nessuno per una decisione
// di contabilità. Chi ha spostato e quando resta scritto.
// La vista v_timesheet_with_section espone `is_extra` e `counts_in_project`: il
// CREATE OR REPLACE va rifatto QUI perché quello di InitDatabase gira solo in
// sviluppo — è la trappola che è costata la v69.
public sealed class M080_ExtraLavoro : IMigrazione
{
    public int Versione => 80;

    public string Descrizione => "timesheet_extra_work + vista con is_extra/counts_in_project: ore spostate su Extra Lavoro fuori dai costi";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS timesheet_extra_work (
            entry_id INT NOT NULL PRIMARY KEY,
            counts_in_project TINYINT(1) NOT NULL DEFAULT 0,
            moved_by INT NULL,
            moved_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            note VARCHAR(300) NOT NULL DEFAULT '',
            CONSTRAINT fk_tew_entry FOREIGN KEY (entry_id)
                REFERENCES timesheet_entries(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // La vista v_timesheet_with_section la riallinea EnsureViews, a ogni avvio e in tutti
        // gli ambienti (blocco A2, 15/08/2026). Qui c'era un `c.Execute(TimesheetSectionViewSql)`:
        // eseguiva la definizione di OGGI dentro una migrazione vecchia, e su un database
        // ancora indietro (ripristino di un backup di mesi fa) falliva, perché quella
        // definizione nomina tabelle e colonne nate da migrazioni successive.

        // ⚠️ Feature NON registrata = pagina visibile a tutti (trappola già pagata con
        // i permessi di navigazione). Livello 2 = PM: la pagina Ore Commessa e l'Extra
        // Lavoro sono suoi, come chiede Paolo («solo il PM»).
        c.Execute(@"INSERT IGNORE INTO auth_features (feature_key, display_name, category, min_level, behavior)
            VALUES ('nav.ore_commessa', 'Ore Commessa', 'navigation', 2, 'HIDDEN')");

        log.LogInformation("[Migration v80] Extra Lavoro pronto: nessuna riga spostata (tutte le ore continuano a contare come prima).");
    }
}
