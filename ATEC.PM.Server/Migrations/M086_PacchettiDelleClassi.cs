using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v86 — I PACCHETTI DELLE CLASSI (PIANO-PERMESSI.md §4.1, Fase B).
//
// Le quattro classi — Tecnico, Responsabile, PM, Admin — non sono ruoli che concedono
// qualcosa: sono PACCHETTI di righe da scrivere sulla persona con un clic, dopo di che
// si staccano e il dato vero resta sulle combo (`origin = CLASSE` vs `MANO`). Il nome
// della classe è quello che sta già in `employees.user_role`.
//
// ⚠️ NON si riusa `auth_role_features`: quella è la lista bianca del motore VECCHIO, che
// `FeatureAccessService` legge ancora e che deve restare intatta finché l'interruttore
// `PermissionsEngine` può tornare indietro. Dargli un secondo significato vorrebbe dire
// che tornare al motore vecchio concede cose mai concesse.
//
// 🔑 Il seed riproduce la situazione di OGGI, non quella che si vuole domani: pacchetto
// della classe = le funzioni che quel livello vede adesso. Così «Applica classe» su una
// persona che non è mai stata toccata non cambia niente, ed è la garanzia che rende
// sicuro premere quel pulsante il primo giorno. Il taglio vero — Tecnico senza Dashboard,
// MoM e Milestone in lettura — è la Fase D, e da lì in avanti si fa modificando QUESTA
// tabella, senza un deploy.
public sealed class M086_PacchettiDelleClassi : IMigrazione
{
    public int Versione => 86;

    public string Descrizione => "auth_class_features: i pacchetti delle 4 classi (Fase B), seminati sulla situazione attuale";

    public void Applica(MySqlConnection c, ILogger log)
    {
        c.Execute(@"CREATE TABLE IF NOT EXISTS auth_class_features (
            id INT AUTO_INCREMENT PRIMARY KEY,
            class_name VARCHAR(30) NOT NULL,
            feature_key VARCHAR(100) NOT NULL,
            access VARCHAR(10) NOT NULL DEFAULT 'FULL',
            UNIQUE KEY uk_class_feature (class_name, feature_key)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4");

        // Le tre classi non-Admin: tutto ciò che il loro livello vede oggi, in scrittura.
        // (`access_mode = 'LEVEL'` esclude eventuali ruoli di reparto residui, che non
        // sono classi e non hanno un pacchetto.)
        int righe = c.Execute(@"
            INSERT IGNORE INTO auth_class_features (class_name, feature_key, access)
            SELECT l.role_name, f.feature_key, 'FULL'
            FROM auth_levels l
            JOIN auth_features f ON f.min_level <= l.level_value
            WHERE UPPER(l.access_mode) = 'LEVEL'
              AND l.level_value < (SELECT MAX(level_value) FROM auth_levels WHERE UPPER(access_mode)='LEVEL')");

        // L'Admin prende il JOLLY e basta. Non è una scorciatoia: è ciò che gli permette
        // di vedere anche le funzioni che NON ESISTONO ANCORA. Col fallback invertito una
        // chiave nuova nasce invisibile a chiunque, e senza jolly il primo deploy che
        // aggiunge una pagina la nasconderebbe anche a chi deve concederla. In più tiene
        // leggibile la sua scheda: una riga invece di settanta.
        righe += c.Execute(@"
            INSERT IGNORE INTO auth_class_features (class_name, feature_key, access)
            SELECT l.role_name, '*', 'FULL'
            FROM auth_levels l
            WHERE UPPER(l.access_mode) = 'LEVEL'
              AND l.level_value = (SELECT MAX(level_value) FROM auth_levels WHERE UPPER(access_mode)='LEVEL')");

        log.LogInformation(
            "[Migration v86] Pacchetti delle classi creati ({Righe} righe). Riproducono i permessi di oggi: applicarli non cambia niente per nessuno.",
            righe);
    }
}
