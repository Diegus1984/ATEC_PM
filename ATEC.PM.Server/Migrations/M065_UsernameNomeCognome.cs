using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

// v65: formato username dipendenti nome.cognome (invece di n.cognome).
public sealed class M065_UsernameNomeCognome : IMigrazione
{
    public int Versione => 65;

    public string Descrizione => "Username dipendenti in formato nome.cognome";

    /// <summary>Pulizia di dati: se fallisce, l'avvio prosegue (vedi <see cref="IMigrazione.Facoltativa"/>).</summary>
    // Se non riesce, gli username restano quelli di prima e si entra lo stesso: il login accetta
    // anche la forma calcolata da nome e cognome. Il caso di fallimento realistico è proprio
    // quello per cui NON deve fermare il server: due dipendenti omonimi, l'UPDATE che sbatte
    // sull'unicità dello username — tenere ferma l'azienda per il formato di un nome utente
    // sarebbe una cura peggiore del male.
    public bool Facoltativa => true;

    public void Applica(MySqlConnection c, ILogger log)
    {
        int updated = c.Execute(@"
            UPDATE employees
            SET username = LOWER(CONCAT(first_name, '.', last_name))
            WHERE first_name NOT LIKE '[%'
              AND username IS NOT NULL
              AND username <> ''
              AND username <> 'admin'");

        log.LogInformation(
            "[Migration v65] Aggiornati {Count} username dipendenti al formato nome.cognome", updated);
    }
}
