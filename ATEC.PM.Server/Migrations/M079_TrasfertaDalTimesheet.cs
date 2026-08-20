using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v79 — la TRASFERTA SI COMPILA DAL TIMESHEET (segnalazioni #37 + #52).
// Oggi gli step di trasferta si aprono a mano. Da qui in poi le ore scaricate su una
// fase la cui SEZIONE DI COSTO è marcata DA_CLIENTE fanno nascere da sole lo step
// (= la fase) e la riga (= persona + giorno). Quello che resta a mano è la spesa:
// alloggio, vitto, indennità, auto, treno.
//
// Le colonne aggiunte, e il perché di ognuna:
//  · travel_steps.project_phase_id  → lo step È la fase, ed è la chiave per ritrovarlo
//    senza doverlo cercare per descrizione (che l'utente può riscrivere).
//  · travel_step_rows.work_date     → il GIORNO della trasferta, che secondo la #52
//    sostituisce inizio/fine. Le righe scritte a mano restano su start/end: non si
//    tocca niente di quello che c'è già.
//  · travel_step_rows.source        → MANUAL / TIMESHEET. È la riga di confine: il
//    motore riscrive SOLO le righe TIMESHEET e non tocca mai quelle a mano.
//  · travel_step_rows.travel_days   → giorni imputati. Deciso l'08/08/2026: se una
//    persona in un giorno lavora su DUE fasi di cantiere nascono due righe, ma il
//    giorno di trasferta è UNO: 1 sulla prima riga, 0 sulle altre, altrimenti
//    l'indennità verrebbe pagata due volte. Resta correggibile a mano.
//  · travel_step_rows.hours_missing → le ore dietro alla riga non ci sono più
//    (cancellate o spostate). Deciso l'08/08/2026: la riga NON si cancella, si
//    segnala — dentro può esserci un albergo da 300 € imputato a mano.
// La chiave unica (step, persona, giorno) è ciò che rende la rigenerazione idempotenda:
// si può rilanciare quante volte si vuole senza creare doppioni. Le righe manuali
// hanno work_date NULL e in MySQL più NULL non collidono mai: restano fuori dal vincolo.
public sealed class M079_TrasfertaDalTimesheet : IMigrazione
{
    public int Versione => 79;

    public string Descrizione => "trasferta derivata dal timesheet: fase sullo step, giorno/provenienza/giorni/ore-mancanti sulla riga";

    public void Applica(MySqlConnection c, ILogger log)
    {
        bool HaColonna(string tabella, string colonna) => c.ExecuteScalar<int>(@"
            SELECT COUNT(*) FROM information_schema.COLUMNS
            WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @T AND COLUMN_NAME = @C",
            new { T = tabella, C = colonna }) > 0;

        if (!HaColonna("travel_steps", "project_phase_id"))
        {
            c.Execute("ALTER TABLE travel_steps ADD COLUMN project_phase_id INT NULL AFTER project_id");
            c.Execute("ALTER TABLE travel_steps ADD INDEX idx_ts_phase (project_phase_id)");
        }
        if (!HaColonna("travel_step_rows", "work_date"))
        {
            c.Execute(@"ALTER TABLE travel_step_rows
                ADD COLUMN work_date DATE NULL AFTER person_name,
                ADD COLUMN source VARCHAR(20) NOT NULL DEFAULT 'MANUAL' AFTER work_date,
                ADD COLUMN travel_days INT NULL AFTER source,
                ADD COLUMN hours_missing TINYINT(1) NOT NULL DEFAULT 0 AFTER travel_days");
            c.Execute("ALTER TABLE travel_step_rows ADD UNIQUE KEY uq_tsr_derivata (step_id, employee_id, work_date)");
        }

        log.LogInformation("[Migration v79] Trasferta pronta a ricevere le righe dal Timesheet (nessuna riga esistente toccata: restano tutte MANUAL).");
    }
}
