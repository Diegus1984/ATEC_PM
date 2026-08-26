namespace ATEC.PM.Server.Services;

/// <summary>In quale DDP finisce una riga: officina (si costruisce) o commerciale (si compra).</summary>
public enum DdpDestinazione
{
    Officina,
    Commerciale,
}

/// <summary>
/// Segnalazione #119: importando un gruppo (5xx) in commessa, i componenti non finiscono più
/// tutti in DDP Officina — i codici commerciali vanno nella DDP Commerciale e quelli d'officina
/// nella DDP Officina, con la stessa intestazione collassabile in tutte e due.
///
/// <para><b>La regola sta SOLO qui.</b> Prima la stessa nozione («cos'è roba d'officina») era
/// sparsa: le etichette del client, <c>ValidateHierarchy</c> nel CodexController e il
/// <c>LIKE '101%'</c> di <see cref="Controllers.WorkRequestsController"/> che decide cos'è una
/// lavorazione. Aggiungerne una quarta copia dentro l'import era il modo sicuro per farle
/// divergere: chi deve smistare chiama questo.</para>
///
/// <para>Si guarda la <b>prima cifra</b> della famiglia, non le tre. È una scelta, non una
/// pigrizia: <c>511</c> «Gruppo custom» è nato il 25/08/2026 come clone esatto del <c>501</c>
/// proprio perché si comportasse identico ovunque, e la gerarchia delle composizioni ragiona
/// già così. Se un giorno due famiglie della stessa decina dovranno andare in DDP diverse,
/// è questo il punto in cui passare a tre cifre — e sarà l'unico.</para>
/// </summary>
public static class DdpSmistamento
{
    /// <summary>
    /// Dove va un codice Codex:
    /// <list type="bullet">
    /// <item><c>1xx</c> particolari a disegno → <b>officina</b> (si costruiscono);</item>
    /// <item><c>2xx</c> commerciale e <c>3xx</c> elementi di fissaggio → <b>commerciale</b>
    /// (si comprano);</item>
    /// <item><c>5xx</c>/<c>6xx</c>/<c>7xx</c> gruppi e assiemi → <b>officina</b>: come figli
    /// sono sotto-gruppi che si montano, come padri sono l'intestazione (che vive in entrambe
    /// le DDP, ma quella la decide l'import, non questa funzione).</item>
    /// </list>
    /// <para>Qualunque altra cosa — codice vuoto, part number scritto a mano, la famiglia
    /// <c>401</c> ritirata — va in <b>officina</b>: è dove finivano tutte le righe prima della
    /// #119, quindi lo sconosciuto non cambia comportamento di sua iniziativa.</para>
    /// </summary>
    public static DdpDestinazione Destinazione(string? codice)
    {
        // I codici viaggiano col punto in DDP (501140621.001) e senza nel Codex: si guarda
        // la prima cifra dopo aver tolto punti e spazi, così le due forme decidono uguale.
        string pulito = (codice ?? "").Replace(".", "").Replace(" ", "").Trim();
        if (pulito.Length == 0) return DdpDestinazione.Officina;

        return pulito[0] switch
        {
            '2' or '3' => DdpDestinazione.Commerciale,
            _ => DdpDestinazione.Officina,
        };
    }

    /// <summary>Comodità per le query e i log: <c>true</c> se la riga è da DDP Commerciale.</summary>
    public static bool VaInCommerciale(string? codice) =>
        Destinazione(codice) == DdpDestinazione.Commerciale;
}
