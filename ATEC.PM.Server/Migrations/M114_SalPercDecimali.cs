using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Migrations;

/// <summary>
/// Segnalazione #130 — la <b>% SAL</b> deve poter arrivare a <b>10 cifre decimali</b>.
///
/// <para>Il piano di fatturazione non nasce da percentuali tonde: nasce da un importo di
/// fattura concordato col cliente, e la percentuale è quello che serve per riprodurlo
/// esattamente. Sulla commessa 203 la riga da <c>11.599,38 €</c> su un ordine da
/// <c>142.625,00 €</c> vale <c>8,13278177%</c>: con la colonna a <c>DECIMAL(6,3)</c> il
/// valore veniva arrotondato a <c>8,133</c> e la fattura usciva da <c>11.599,38</c> a
/// <c>11.600,45</c>. Non è un capriccio di precisione: è la cifra che va in fattura.</para>
///
/// <para><c>DECIMAL(13,10)</c> = 3 cifre intere (fino a 100, e oltre per i casi sballati che
/// l'avviso «supera il 100%» deve poter mostrare) + le 10 decimali richieste.</para>
///
/// <para>🪤 <b>Allargare non perde niente</b> — i valori già scritti restano quelli che sono,
/// perché <c>DECIMAL(6,3)</c> è interamente contenuto in <c>DECIMAL(13,10)</c>. È il verso
/// opposto (restringere) quello che troncherebbe: se un giorno si volesse tornare indietro,
/// prima si guarda cosa c'è dentro.</para>
///
/// <para>Non è facoltativa: senza la colonna larga il client manderebbe 10 decimali e MySQL
/// li arrotonderebbe <b>in silenzio</b>, esattamente il difetto che questa migrazione chiude.</para>
/// </summary>
public sealed class M114_SalPercDecimali : IMigrazione
{
    public int Versione => 114;

    public string Descrizione =>
        "#130: sal_rows.perc da DECIMAL(6,3) a DECIMAL(13,10) (% SAL con 10 decimali)";

    public void Applica(MySqlConnection c, ILogger log)
    {
        // Idempotente sulla scala: se la colonna ha già 10 decimali non si tocca niente.
        // NUMERIC_SCALE torna null per le colonne non numeriche → -1 e si prosegue.
        int scala = c.ExecuteScalar<int?>(@"
            SELECT NUMERIC_SCALE FROM information_schema.columns
            WHERE table_schema = DATABASE() AND table_name = 'sal_rows' AND column_name = 'perc'") ?? -1;

        if (scala >= 10)
        {
            log.LogInformation("[v114] sal_rows.perc ha già {Scala} decimali: niente da fare.", scala);
            return;
        }

        c.Execute("ALTER TABLE sal_rows MODIFY perc DECIMAL(13,10) NULL", commandTimeout: 600);
        log.LogInformation("[v114] sal_rows.perc allargata da scala {Scala} a DECIMAL(13,10).", scala);
    }
}
