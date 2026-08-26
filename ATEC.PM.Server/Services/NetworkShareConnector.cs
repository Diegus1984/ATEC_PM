using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace ATEC.PM.Server.Services;

/// <summary>
/// Apre una sessione SMB <b>autenticata</b> verso una share di rete prima che il programma
/// la legga o ci scriva con le normali API Directory/File.
///
/// <para><b>Perché serve</b> (cartella immagini Danea, 25/08/2026): il servizio AtecPmServer
/// gira come account LOCALE <c>.\atec</c> di ATEC-FC; ATEC-FC e Server-maga sono in WORKGROUP,
/// quindi Server-maga <b>non conosce</b> quell'utente. L'SMB ripiega su <c>guest</c>, che
/// Windows blocca (<c>AllowInsecureGuestAuth</c> = 0 di default), e ogni accesso a
/// <c>\\Server-maga\...</c> risponde «accesso negato» anche in sola lettura. Il database
/// Danea invece funziona, perché ci si connette in TCP con credenziali esplicite: qui si fa
/// la stessa identica cosa per i file.</para>
///
/// <para><c>WNetAddConnection2</c> stabilisce la sessione a nome dell'utente indicato. La
/// connessione è «deviceless» (nessuna lettera di unità occupata) e vale per l'intero
/// processo: da lì in poi Directory/File sulla share funzionano. Non serve il profilo utente
/// del servizio, quindi regge ai riavvii — a differenza di <c>cmdkey</c>, che vive nel
/// profilo e i servizi non lo caricano sempre.</para>
///
/// <para><b>Senza credenziali configurate non fa nulla</b> e il comportamento resta quello
/// storico: sui PC di sviluppo la share è già raggiungibile con le credenziali di Windows.</para>
/// </summary>
public class NetworkShareConnector
{
    private readonly ILogger<NetworkShareConnector> _log;

    /// <summary>Ultima connessione riuscita, per radice <c>\\server\share</c>.</summary>
    private readonly ConcurrentDictionary<string, DateTime> _connesse = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per quanto ci si fida di una sessione già aperta senza rifarla.</summary>
    private static readonly TimeSpan Validita = TimeSpan.FromMinutes(5);

    public NetworkShareConnector(ILogger<NetworkShareConnector> log) => _log = log;

    /// <summary>
    /// Radice <c>\\server\share</c> di un percorso UNC; <c>null</c> se il percorso è locale
    /// (allora non c'è nessuna sessione da autenticare).
    /// </summary>
    public static string? ShareRoot(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        string p = path.Trim().Replace('/', '\\');
        if (!p.StartsWith(@"\\")) return null;
        string[] parti = p.Substring(2).Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parti.Length < 2) return null;              // solo \\server: non è una share
        return @"\\" + parti[0] + @"\" + parti[1];
    }

    /// <summary>
    /// Assicura una sessione autenticata verso la share che contiene <paramref name="path"/>.
    /// Restituisce <c>null</c> se è tutto a posto (o se non c'è nulla da fare: percorso locale,
    /// nessuna credenziale configurata, sistema non Windows), altrimenti il messaggio d'errore
    /// già in italiano, pronto da mostrare.
    /// </summary>
    public string? Connect(string? path, string? utente, string? password)
    {
        if (!OperatingSystem.IsWindows()) return null;
        string? root = ShareRoot(path);
        if (root == null) return null;
        if (string.IsNullOrWhiteSpace(utente)) return null;

        if (_connesse.TryGetValue(root, out DateTime quando) &&
            DateTime.UtcNow - quando < Validita &&
            Directory.Exists(root))
            return null;

        int esito = Aggiungi(root, utente, password);

        // 1219 = verso quel server esiste già una sessione con credenziali diverse (tipicamente
        // proprio quella «guest» fallita). Windows rifiuta di aprirne una seconda: si chiude la
        // vecchia e si riprova una sola volta.
        if (esito == ErroreConflittoCredenziali)
        {
            Cancella(root);
            Cancella(@"\\" + NomeServer(root));
            esito = Aggiungi(root, utente, password);
        }

        if (esito == NessunErrore || esito == ErroreGiaAssegnata)
        {
            _connesse[root] = DateTime.UtcNow;
            _log.LogInformation("[Share] Sessione autenticata verso {Root} come {Utente}.", root, utente);
            return null;
        }

        _connesse.TryRemove(root, out _);
        string msg = Messaggio(esito, root, utente);
        _log.LogWarning("[Share] {Msg}", msg);
        return msg;
    }

    private static string NomeServer(string root) => root.Substring(2).Split('\\')[0];

    /// <summary>Chiude la sessione (serve solo dopo un conflitto di credenziali).</summary>
    private void Cancella(string nome)
    {
        try { WNetCancelConnection2(nome, 0, true); }
        catch (Exception ex) { _log.LogDebug("[Share] Chiusura sessione {Nome}: {Msg}", nome, ex.Message); }
    }

    private static int Aggiungi(string root, string utente, string? password)
    {
        var risorsa = new NETRESOURCE
        {
            dwType = ResourceTypeDisk,
            lpLocalName = null,          // deviceless: nessuna lettera di unità occupata
            lpRemoteName = root,
            lpProvider = null,
        };
        return WNetAddConnection2(ref risorsa, password ?? "", utente, 0);
    }

    private static string Messaggio(int codice, string root, string utente) => codice switch
    {
        ErroreAccessoNegato =>
            $"{root}: accesso negato all'utente {utente} (permessi di condivisione o NTFS insufficienti).",
        ErrorePercorsoRete or ErroreNomeRete =>
            $"{root}: condivisione inesistente o server non raggiungibile.",
        ErrorePasswordNonValida or ErroreLogonFallito =>
            $"{root}: utente o password non validi per {utente}.",
        ErroreConflittoCredenziali =>
            $"{root}: verso quel server esiste già una sessione con credenziali diverse.",
        ErroreAccountLimitato =>
            $"{root}: l'account {utente} non è ammesso in rete (password vuota o restrizioni sull'account).",
        ErroreLogonNonConcesso =>
            $"{root}: a {utente} manca il diritto di accedere dalla rete a quel server.",
        ErroreReteAssente =>
            $"{root}: rete non disponibile.",
        _ => $"{root}: connessione fallita (errore Windows {codice}).",
    };

    // ── P/Invoke ──────────────────────────────────────────────────────────

    private const int NessunErrore = 0;
    private const int ErroreAccessoNegato = 5;
    private const int ErrorePercorsoRete = 53;
    private const int ErroreNomeRete = 67;
    private const int ErroreGiaAssegnata = 85;
    private const int ErrorePasswordNonValida = 86;
    private const int ErroreConflittoCredenziali = 1219;
    private const int ErroreReteAssente = 1222;
    private const int ErroreLogonFallito = 1326;
    private const int ErroreAccountLimitato = 1327;
    private const int ErroreLogonNonConcesso = 1385;

    private const int ResourceTypeDisk = 1;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NETRESOURCE
    {
        public int dwScope;
        public int dwType;
        public int dwDisplayType;
        public int dwUsage;
        public string? lpLocalName;
        public string? lpRemoteName;
        public string? lpComment;
        public string? lpProvider;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(
        ref NETRESOURCE netResource, string? password, string? username, int flags);

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetCancelConnection2(string name, int flags, bool force);
}
