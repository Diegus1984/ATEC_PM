using System.Reflection;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Quale versione del SERVER sta girando.
///
/// <para>Esiste perché il numero che si legge nell'applicazione (<c>version.json</c>) è
/// quello del <b>client web</b>, e un aggiornamento di solo C# non lo tocca: lo script di
/// deploy riusa la build del client quando è identica, ed è giusto così — altrimenti a
/// tutti comparirebbe la barra «Aggiorna adesso» per qualcosa che nel browser non cambia.
/// Il rovescio è che dall'applicazione non si vedeva più se il server era aggiornato: per
/// saperlo bisognava guardare la data della DLL da dentro il server.</para>
///
/// <para>Il valore è la data di installazione del file che sta girando, nella stessa forma
/// del build id del client (<c>yyyyMMdd-HHmm</c>): il deploy scrive i binari, e quel
/// momento È la versione. Si calcola una volta sola all'avvio.</para>
/// </summary>
public static class VersioneServer
{
    /// <summary>«20260828-1126», oppure vuoto se il file non è raggiungibile.</summary>
    public static string Build { get; } = Calcola();

    private static string Calcola()
    {
        try
        {
            // In un publish self-contained `Location` è valorizzato; se un domani si
            // passasse al single-file diventa vuoto, e allora vale l'eseguibile.
            string percorso = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(percorso)) percorso = Environment.ProcessPath ?? "";
            if (string.IsNullOrEmpty(percorso) || !File.Exists(percorso)) return "";

            return File.GetLastWriteTime(percorso).ToString("yyyyMMdd-HHmm");
        }
        catch
        {
            // Sapere la versione non vale un avvio fallito.
            return "";
        }
    }
}
