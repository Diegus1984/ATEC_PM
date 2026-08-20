using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// ── v69 — v_timesheet_with_section allineata alle FASI LOCALI ──────────────────
// In produzione era in vigore la definizione di InitDatabase, con `JOIN phase_templates`
// INNER: le ore imputate su una fase LOCALE (phase_template_id NULL, creata dentro la
// commessa e non da template) sparivano dalla vista, quindi dal consuntivo «Risorse
// Atec» del Bilancio e dalla pagina /bilancio — senza nessun errore, solo un costo più
// basso del vero. La versione corretta esisteva solo in `migrate_view_timesheet.py`,
// uno script da lanciare a mano che in produzione non è mai passato.
// Il ramo dev e questo ora leggono la STESSA costante: non possono più divergere.
public sealed class M069_VistaTimesheetFasiLocali : IMigrazione
{
    public int Versione => 69;

    public string Descrizione => "v_timesheet_with_section: LEFT JOIN sui template + fallback sulle fasi locali";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // La vista v_timesheet_with_section la riallinea EnsureViews, a ogni avvio e in tutti
        // gli ambienti (blocco A2, 15/08/2026). Qui c'era un `c.Execute(TimesheetSectionViewSql)`:
        // eseguiva la definizione di OGGI dentro una migrazione vecchia, e su un database
        // ancora indietro (ripristino di un backup di mesi fa) falliva, perché quella
        // definizione nomina tabelle e colonne nate da migrazioni successive.
        int recuperate = c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            WHERE pp.phase_template_id IS NULL");

        log.LogInformation(
            "[Migration v69] Fasi locali dentro il consuntivo: {Recuperate} imputazioni ore rientrano nel costo (la vista la riallinea EnsureViews all'avvio).",
            recuperate);
    }
}
