using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Una migrazione dello schema. Una classe per versione, un file per versione.
///
/// <para><b>Perché una classe C# e non un file .sql.</b> Buona parte delle 87 migrazioni non è
/// SQL puro: usano <c>AddColumnIfMissing</c>, controlli su <c>information_schema</c>, cicli,
/// conversioni di dati. Estrarle in <c>.sql</c> perderebbe quell'idempotenza, e 18 di esse
/// mettono DDL e conversione dati sotto lo stesso numero di versione — non sono separabili in
/// due file senza cambiarne il significato.</para>
///
/// <para><b>Chi la implementa non deve preoccuparsi di registrarsi</b>: la riga in
/// <c>schema_migrations</c> la scrive il <see cref="MigrationRunner"/> dopo che
/// <see cref="Applica"/> è tornato senza eccezioni. È il contrario di com'era prima, dove ogni
/// blocco scriveva la propria riga: se se ne dimenticava una, quel blocco tornava a girare a
/// ogni avvio per sempre.</para>
/// </summary>
public interface IMigrazione
{
    /// <summary>Numero di versione. Unico: due migrazioni con lo stesso numero fermano l'avvio.</summary>
    int Versione { get; }

    /// <summary>Cosa fa, in una riga. Finisce in <c>schema_migrations.description</c>.</summary>
    string Descrizione { get; }

    /// <summary>
    /// <c>true</c> solo per le <b>pulizie di dati</b> (righe orfane, resti di versioni vecchie):
    /// se falliscono, l'avvio prosegue lo stesso.
    ///
    /// <para>La regola normale è l'opposta — un fallimento ferma il server, perché lavorare su
    /// uno schema incompleto produce dati sbagliati per giorni prima che qualcuno se ne accorga.
    /// Ma per una pulizia quella cura è peggiore del male: il gestionale funziona benissimo con
    /// qualche riga orfana in giro, e tenere ferma l'azienda per quella non ha senso.</para>
    ///
    /// <para><b>Non marcare facoltativa una migrazione da cui dipende qualcosa dopo</b>: una
    /// pulizia che precede una <c>UNIQUE KEY</c>, o un <c>DELETE</c> che serve a far passare una
    /// migrazione successiva, è strutturale anche se sembra una pulizia. La versione fallita
    /// resta comunque pendente e viene ritentata a ogni avvio, con un warning nel log e la riga
    /// <c>success = 0</c> in <c>schema_migrations</c>: non sparisce, si vede.</para>
    /// </summary>
    bool Facoltativa => false;

    /// <summary>
    /// Il lavoro. Se solleva un'eccezione, la migrazione risulta <b>non applicata</b> e (salvo
    /// <c>Migrations:StopOnError=false</c>) l'avvio si interrompe: nessuna riga viene scritta e
    /// al riavvio successivo si riprova.
    /// <para>Va scritta <b>idempotente</b> dove è possibile (<c>IF NOT EXISTS</c>,
    /// <c>INSERT IGNORE</c>, guardie su <c>information_schema</c>): un fallimento a metà lascia
    /// comunque il lavoro già fatto, e il ritentativo deve poterci passare sopra.</para>
    /// </summary>
    void Applica(MySqlConnection c, ILogger log);
}
