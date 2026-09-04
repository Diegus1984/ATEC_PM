using Microsoft.Extensions.Logging.Abstractions;

namespace ATEC.PM.Server.Hubs;

/// <summary>
/// Le notifiche SignalR partono «senza aspettare»: la risposta HTTP non deve attendere il
/// push, e un hub spento non deve far fallire un salvataggio riuscito. Fino al 04/09/2026
/// però erano 60 chiamate a <c>SendAsync</c> scartate in <c>_</c>: se il push falliva l'eccezione spariva e
/// la pagina di qualcuno restava vecchia senza che nessuno lo sapesse (blocco F3 del piano
/// tecnico). Qui il fallimento finisce nel log — non si garantisce la consegna (il client
/// rilegge comunque, <c>staleTime: 0</c>), si garantisce di saperlo.
/// </summary>
public static class HubNotifica
{
    /// <summary>
    /// Impostato una volta all'avvio (<c>Program.cs</c>): i 16 controller che notificano
    /// non devono iniettare un logger solo per questo.
    /// </summary>
    public static ILogger Log { get; set; } = NullLogger.Instance;

    /// <summary>
    /// Segue il push in sottofondo e scrive nel log se fallisce. Va al posto dello scarto
    /// in <c>_</c>: un test controlla che non ne restino di nudi.
    /// </summary>
    public static void SenzaAttesa(this Task invio, string evento)
    {
        _ = invio.ContinueWith(
            t => Log.LogWarning(t.Exception?.GetBaseException(),
                "[SignalR] Notifica {Evento} non consegnata", evento),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
    }
}
