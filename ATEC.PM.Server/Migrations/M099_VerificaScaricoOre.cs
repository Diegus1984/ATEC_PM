using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v99 — «VERIFICA EFFETTUATA» sullo scarico ore (segnalazioni #102 e #109).
// Quando i colleghi scaricano ore in Timesheet, il PM non ha modo di sapere che cosa
// gli è arrivato di nuovo: se le guarda tutte le volte perde tempo, se non le guarda
// se ne accorge a fine mese. Da qui in poi ogni commessa porta una FILIGRANA per
// argomento: l'ultima riga di timesheet che il PM ha dichiarato di aver visto.
// Sopra la filigrana = ore nuove, card rossa e voce di menu accesa; premendo
// «Verifica effettuata» la filigrana sale all'ultima riga e l'allarme si spegne, fino
// al prossimo scarico.
//
// Due argomenti sulla stessa commessa, quindi due righe possibili:
//  · 'HOURS'  → pagina Ore Commessa (#109): tutte le ore della commessa;
//  · 'TRAVEL' → pagina Trasferta (#102): solo le ore su fasi «da cliente», che sono
//    le uniche che generano righe di trasferta.
// Verificare la trasferta non spegne le ore e viceversa: sono due letture diverse.
//
// La filigrana è l'ID e non una data: `timesheet_entries` ha `created_at` ma NON un
// `updated_at`, quindi una data non saprebbe distinguere «riga nuova» da «riga di ieri
// ritoccata». Gli id crescono sempre, e una riga cancellata e rifatta ne prende uno
// nuovo — che è esattamente il caso in cui il PM deve tornare a guardare.
public sealed class M099_VerificaScaricoOre : IMigrazione
{
    public int Versione => 99;

    public string Descrizione =>
        "project_hours_checks: filigrana «verifica effettuata» dello scarico ore, per commessa e per argomento (ore / trasferta)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS project_hours_checks (
            project_id INT NOT NULL,
            scope VARCHAR(10) NOT NULL,
            last_entry_id INT NOT NULL DEFAULT 0,
            verified_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            verified_by INT NULL,
            PRIMARY KEY (project_id, scope),
            CONSTRAINT fk_phc_project FOREIGN KEY (project_id)
                REFERENCES projects(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Nessun backfill di proposito: la tabella nasce vuota, quindi al primo avvio
        // tutte le commesse con ore risultano «da verificare». È il comportamento
        // giusto — nessuno le ha ancora guardate con questo strumento — e si azzera in
        // un clic per commessa. Riempirla con l'ultimo id di oggi vorrebbe dire
        // dichiarare verificato per conto del PM del lavoro che non ha visto.
        log.LogInformation(
            "[Migration v99] Verifica scarico ore pronta: nessuna commessa risulta ancora verificata (si azzera dal pulsante «Verifica effettuata»).");
    }
}
