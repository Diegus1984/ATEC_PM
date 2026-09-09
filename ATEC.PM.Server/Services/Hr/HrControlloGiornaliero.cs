namespace ATEC.PM.Server.Services.Hr;

/// <summary>
/// I giorni che la pagina «Controllo di ieri» mette a video (09/09/2026, richiesta di Diego: «le
/// timbrature di ieri di tutti i dipendenti, così HR ci mette poco a controllarle; se è lunedì
/// facciamo apparire anche venerdì, sabato e domenica»).
///
/// <para>La regola sta qui, in una copia sola: il blocco <b>finisce nel giorno scelto</b> (di norma
/// ieri) e torna indietro sui giorni di riposo — sabato, domenica, festivi — fino a comprendere
/// <b>l'ultimo giorno lavorativo</b>. Un martedì mostra il lunedì; un lunedì mostra venerdì, sabato e
/// domenica (il venerdì è l'ultimo giorno ancora da controllare, il fine settimana serve a vedere chi
/// ha lavorato lo stesso); il giorno dopo una festa infrasettimanale mostra la festa e il giorno
/// lavorativo prima. I festivi sono quelli di <see cref="TimesheetRules.IsHoliday"/>: la pagina non
/// se ne tiene un elenco suo.</para>
/// </summary>
public static class HrControlloGiornaliero
{
    /// <summary>
    /// Freno di sicurezza sui cicli: fra due giorni lavorativi non passano mai più di quattro
    /// giorni di riposo (sabato, domenica e una festa doppia), quattordici non si toccano mai.
    /// </summary>
    private const int MassimoPassi = 14;

    /// <summary>Giorno in cui si timbra: dal lunedì al venerdì, festivi esclusi.</summary>
    public static bool GiornoLavorativo(DateTime giorno) =>
        giorno.DayOfWeek != DayOfWeek.Saturday && !TimesheetRules.IsHoliday(giorno);

    /// <summary>Il giorno da controllare quando la pagina si apre: ieri.</summary>
    public static DateTime Predefinito(DateTime oggi) => oggi.Date.AddDays(-1);

    /// <summary>
    /// Il blocco che finisce in <paramref name="fine"/>: se è un giorno lavorativo è lui solo,
    /// altrimenti si torna indietro sui riposi fino all'ultimo giorno lavorativo compreso.
    /// </summary>
    public static (DateTime Da, DateTime A) Intervallo(DateTime fine)
    {
        DateTime a = fine.Date;
        DateTime da = a;
        for (int passi = 0; passi < MassimoPassi && !GiornoLavorativo(da); passi++)
            da = da.AddDays(-1);
        return (da, a);
    }

    /// <summary>Il giorno da chiedere per vedere il blocco prima: quello che precede l'inizio.</summary>
    public static DateTime Precedente(DateTime da) => da.Date.AddDays(-1);

    /// <summary>
    /// Il giorno da chiedere per il blocco dopo: il giorno che segue la fine, esteso sui riposi che
    /// gli vengono dietro — così da giovedì si passa a «venerdì, sabato e domenica», non a un venerdì
    /// che il passo dopo si rivedrebbe. Non supera oggi; null quando la fine è già oggi.
    /// </summary>
    public static DateTime? Successivo(DateTime a, DateTime oggi)
    {
        DateTime limite = oggi.Date;
        if (a.Date >= limite) return null;

        DateTime prossimo = a.Date.AddDays(1);
        for (int passi = 0;
             passi < MassimoPassi && prossimo < limite && !GiornoLavorativo(prossimo.AddDays(1));
             passi++)
        {
            prossimo = prossimo.AddDays(1);
        }
        return prossimo;
    }
}
