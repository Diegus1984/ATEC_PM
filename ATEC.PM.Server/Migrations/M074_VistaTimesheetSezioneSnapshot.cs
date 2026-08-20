using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// ── v74 — via il ripiego sul template nella vista del Bilancio ────────────────
// `v_timesheet_with_section` risolveva la sezione con
// COALESCE(pp.cost_section_template_id, pt.cost_section_template_id). Con le fasi
// multi-sezione (v73) quel ripiego è diventato **pericoloso**: la colonna sul template
// contiene UNA sola delle sezioni della fase, quindi le ore di una riga senza snapshot
// sarebbero finite in una sezione a caso del Bilancio, senza nessun errore.
// La v73c ha congelato lo snapshot su tutte le righe che ne avevano diritto, e da lì in
// poi ogni inserimento lo scrive: il ripiego non serve più e mente. Qui si applica la
// vista nuova — **serve una migrazione**, perché il CREATE OR REPLACE di InitDatabase
// gira solo in sviluppo (è la trappola già costata la v69).
// Il log confronta le ore attribuite prima e dopo: se un numero del Bilancio si muove,
// si vede qui invece che in una riunione.
public sealed class M074_VistaTimesheetSezioneSnapshot : IMigrazione
{
    public int Versione => 74;

    public string Descrizione => "v_timesheet_with_section: la sezione e solo lo snapshot sulla fase di commessa, niente piu ripiego sul template";

    public void Applica(MySqlConnection c, ILogger log)
    {
        int oreSenzaSezione = c.ExecuteScalar<int>(@"
            SELECT COUNT(*)
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            LEFT JOIN phase_templates pt ON pt.id = pp.phase_template_id
            WHERE pp.cost_section_template_id IS NULL");
        int oreCheCambiano = c.ExecuteScalar<int>(@"
            SELECT COUNT(*)
            FROM timesheet_entries te
            JOIN project_phases pp ON pp.id = te.project_phase_id
            JOIN phase_templates pt ON pt.id = pp.phase_template_id
            WHERE pp.cost_section_template_id IS NULL
              AND pt.cost_section_template_id IS NOT NULL");

        // La vista v_timesheet_with_section la riallinea EnsureViews, a ogni avvio e in tutti
        // gli ambienti (blocco A2, 15/08/2026). Qui c'era un `c.Execute(TimesheetSectionViewSql)`:
        // eseguiva la definizione di OGGI dentro una migrazione vecchia, e su un database
        // ancora indietro (ripristino di un backup di mesi fa) falliva, perché quella
        // definizione nomina tabelle e colonne nate da migrazioni successive.
        log.LogInformation(
            "[Migration v74] Niente più ripiego sul template: {SenzaSezione} imputazioni ore restano senza sezione, di cui {CheCambiano} prima ne prendevano una dal template — quelle cambiano sezione nel Bilancio (la vista la riallinea EnsureViews all'avvio).",
            oreSenzaSezione, oreCheCambiano);
    }
}
