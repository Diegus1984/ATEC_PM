using Dapper;
using MySqlConnector;
using static ATEC.PM.Server.Migrations.AiutiMigrazione;

namespace ATEC.PM.Server.Migrations;

// v84 — UNA SOLA TABELLA DEI PERMESSI, SULLA PERSONA (PIANO-PERMESSI.md, Fase A).
//
// Fin qui i permessi arrivavano da TRE motori sovrapposti: la scala dei livelli
// (auth_levels.level_value contro auth_features.min_level), le liste bianche di ruolo
// (auth_role_features) e l'elenco cablato del reparto Contabilità dentro
// FeatureAccessService. Tre modi di rispondere alla stessa domanda, che si contraddicevano
// a vicenda e che nessuno poteva leggere tutti insieme per sapere «cosa vede Tizio».
//
// Da qui in avanti la risposta sta in UNA riga: (persona, funzione) → READ | FULL.
// Riga assente = non vede. `origin` ricorda CHI l'ha scritta — il pacchetto della classe
// (CLASSE) o una mano umana (MANO) — così ri-applicare una classe a venti persone non
// cancella in silenzio le eccezioni messe apposta.
//
// ⚠️ Questa migrazione crea SOLO le tabelle. Non toglie niente a nessuno: il motore
// continua a leggere il vecchio modello finché lo seed non è stato verificato col diff
// (PIANO-PERMESSI.md §10, punti 2-4). L'inversione del fallback è il passo dopo.
public sealed class M084_PermessiSullaPersona : IMigrazione
{
    public int Versione => 84;

    public string Descrizione => "employee_feature_access + log + permissions_version: permessi sulla persona (Fase A)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // La chiave jolly '*' vale «tutto» e ce l'ha solo chi amministra: serve perché,
        // una volta invertito il fallback, una funzione NUOVA nasce invisibile a chiunque
        // — compreso chi dovrebbe concederla. Senza jolly ogni deploy che aggiunge una
        // pagina diventerebbe una migrazione dati, e il primo ad accorgersene sarebbe
        // l'utente. Non è un livello mascherato: è una riga come le altre, si vede sulla
        // scheda della persona e si può togliere (con l'unico limite dell'ultimo admin).
        c.Execute(@"CREATE TABLE IF NOT EXISTS employee_feature_access (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            feature_key VARCHAR(100) NOT NULL,
            access VARCHAR(10) NOT NULL DEFAULT 'FULL',
            origin VARCHAR(10) NOT NULL DEFAULT 'CLASSE',
            updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
            UNIQUE KEY uk_employee_feature (employee_id, feature_key),
            KEY ix_efa_employee (employee_id),
            CONSTRAINT fk_efa_employee FOREIGN KEY (employee_id)
                REFERENCES employees(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Registro: chi ha tolto cosa a chi, e quando. È la prima domanda dopo il primo
        // incidente, e senza queste righe non ha risposta. Stessa idea di ddp_item_events.
        // `access_before`/`access_after` NULL = la riga non c'era / è stata tolta.
        c.Execute(@"CREATE TABLE IF NOT EXISTS employee_feature_access_log (
            id INT AUTO_INCREMENT PRIMARY KEY,
            employee_id INT NOT NULL,
            feature_key VARCHAR(100) NOT NULL,
            access_before VARCHAR(10) NULL,
            access_after VARCHAR(10) NULL,
            origin VARCHAR(10) NOT NULL DEFAULT 'MANO',
            changed_by INT NULL,
            changed_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
            KEY ix_efal_employee (employee_id, changed_at),
            CONSTRAINT fk_efal_employee FOREIGN KEY (employee_id)
                REFERENCES employees(id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Contatore di versione: quando i permessi di una persona cambiano, il client se
        // ne deve accorgere SENZA aspettare che scada il token (8 ore). Oggi i permessi
        // arrivano solo al login, quindi «tolto il permesso» stringeva l'API ma lasciava
        // il menu com'era fino al primo F5 — e «Disattiva» non buttava fuori nessuno.
        AddColumnIfMissing(c, "employees", "permissions_version", "INT NOT NULL DEFAULT 0");

        log.LogInformation(
            "[Migration v84] Tabelle dei permessi per persona create. Nessun permesso è cambiato: il motore legge ancora il modello vecchio.");
    }
}
