using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Blocco E2 del piano tecnico — i primi indici mirati, quelli che non hanno bisogno della
/// settimana di misura perché il pattern della query è certo e sta scritto nel codice.
///
/// <para><b>Ore (timesheet_entries)</b>. Tre endpoint del Timesheet filtrano
/// <c>employee_id = @Emp AND work_date BETWEEN @Start AND @End</c> — è l'apertura della
/// settimana, l'operazione più frequente del gestionale. C'era solo <c>idx_te_employee</c>
/// sul solo <c>employee_id</c>: MySQL prendeva <b>tutte</b> le righe di quella persona (che
/// crescono per sempre, ~1.500 l'anno) e scartava le date una per una. Il composito legge
/// solo la settimana chiesta. Quello vecchio viene tolto perché è il prefisso del nuovo:
/// non fa guadagnare niente in lettura e si paga a ogni ora registrata.</para>
///
/// <para><b>Ore, aggregati globali.</b> La home somma le ore del mese, della settimana e degli
/// ultimi 90 giorni <b>di tutti</b>: lì <c>employee_id</c> non filtra, e l'indice composito non
/// serve (la sua prima colonna non è nella query). Serve l'indice sulla sola data. Fino al
/// 15/08/2026 quelle somme erano scritte con <c>YEAR(work_date)=…</c> e
/// <c>YEARWEEK(work_date,1)=…</c>: una funzione sulla colonna rende <b>qualunque</b> indice
/// inutilizzabile, quindi questo indice da solo non sarebbe servito a niente — le query sono
/// state riscritte a intervallo di date nello stesso lavoro.</para>
///
/// <para><b>Notifiche.</b> Ogni controllo di scadenza, per <b>ogni</b> riga trovata, chiede
/// «l'ho già segnalata oggi?» con <c>notification_type + reference_type + reference_id</c> e la
/// data. Esisteva solo <c>idx_type</c> sul tipo — che da solo non seleziona quasi niente, visto
/// che il tipo è lo stesso per tutte le righe di quel controllo. Anche qui il vecchio indice è
/// il prefisso del nuovo e viene tolto.</para>
///
/// <para>Gli altri indici (DDP, SAL, Codex, Bilancio) restano fuori <b>apposta</b>: si decidono
/// con i numeri di E1, non a intuito. Un indice inutile non si vede — rallenta le scritture e
/// occupa spazio, in silenzio.</para>
///
/// <para><b>NON è <c>Facoltativa</c>, ed è una scelta, non una dimenticanza.</b> Le sette
/// facoltative del progetto sono tutte pulizie di <i>dati</i>. Qui si tratta dello <i>schema</i>:
/// i tre indici nuovi nascono da questa migrazione anche su un database nuovo, quindi lasciarla
/// saltare vorrebbe dire permettere alla produzione di restare con un assetto diverso da quello
/// che i test certificano — con la suite tutta verde. È la trappola della v69 (due definizioni
/// divergenti, in produzione girava quella sbagliata, senza un solo errore). Costo del rischio
/// opposto: misurata su 300.000 righe, la costruzione dei tre indici dura ~1,2 secondi.</para>
/// </summary>
public sealed class M091_IndiciOreENotifiche : IMigrazione
{
    public int Versione => 91;

    public string Descrizione =>
        "E2: indici su timesheet_entries (employee+data, data) e notifications (dedup)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // ── Ore ───────────────────────────────────────────────────────────────
        if (AiutiMigrazione.CreaIndiceSeManca(c, "timesheet_entries", "ix_te_employee_date",
                "`employee_id`, `work_date`"))
            log.LogInformation("[v91] timesheet_entries: creato ix_te_employee_date.");

        if (AiutiMigrazione.CreaIndiceSeManca(c, "timesheet_entries", "ix_te_work_date",
                "`work_date`"))
            log.LogInformation("[v91] timesheet_entries: creato ix_te_work_date.");

        // DOPO i due CREATE: employee_id ha una chiave esterna e MySQL non lascia togliere
        // l'ultimo indice che la serve. Con il composito davanti, il drop è consentito.
        if (AiutiMigrazione.EliminaIndiceSePresente(c, "timesheet_entries", "idx_te_employee"))
            log.LogInformation("[v91] timesheet_entries: tolto idx_te_employee (prefisso del nuovo).");

        // ── Notifiche ─────────────────────────────────────────────────────────
        if (AiutiMigrazione.CreaIndiceSeManca(c, "notifications", "ix_notif_dedup",
                "`notification_type`, `reference_type`, `reference_id`, `created_at`"))
            log.LogInformation("[v91] notifications: creato ix_notif_dedup.");

        if (AiutiMigrazione.EliminaIndiceSePresente(c, "notifications", "idx_type"))
            log.LogInformation("[v91] notifications: tolto idx_type (prefisso del nuovo).");
    }
}
