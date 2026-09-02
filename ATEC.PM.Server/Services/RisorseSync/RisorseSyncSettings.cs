using System.Net;
using System.Net.Sockets;
using System.Text;
using ATEC.PM.Shared.DTOs;
using Dapper;
using MySqlConnector;

namespace ATEC.PM.Server.Services.RisorseSync;

/// <summary>
/// Le impostazioni in vigore del motore di sincronizzazione Risorse. La password è in chiaro
/// SOLO qui, in memoria: sul database sta cifrata (DPAPI) e al client non esce mai.
/// </summary>
public sealed record RisorseSyncSettings(
    bool Enabled,
    string BaseUrl,
    string Username,
    string Password,
    string? LastRun,
    string? LastEsito,
    string? LastError)
{
    /// <summary>true = si può parlare col VPS: acceso E con indirizzo, utente e password.</summary>
    public bool IsConfigured =>
        Enabled && HasCredentials;

    /// <summary>Indirizzo, utente e password ci sono (a prescindere dall'interruttore): serve al «Prova».</summary>
    public bool HasCredentials =>
        !string.IsNullOrWhiteSpace(BaseUrl)
        && !string.IsNullOrWhiteSpace(Username)
        && !string.IsNullOrWhiteSpace(Password);

    /// <summary>BaseUrl senza slash finale: i percorsi si attaccano con «/api/...».</summary>
    public string BaseUrlNormalizzato => (BaseUrl ?? "").Trim().TrimEnd('/');

    /// <summary>
    /// La regola sull'indirizzo del VPS, una sola per salvataggio e «Prova»: URL assoluto
    /// http(s); <c>http</c> nudo è ammesso SOLO verso loopback o reti private (localhost,
    /// 127.x, 10.x, 192.168.x, 172.16-31.x) — verso Internet la password di servizio viaggia
    /// solo su https. Ritorna il messaggio d'errore, null se l'indirizzo va bene. Vuoto = va
    /// bene (l'obbligo di averlo lo decide chi chiama, in base all'interruttore).
    /// </summary>
    public static string? ErroreIndirizzo(string? baseUrl)
    {
        string testo = (baseUrl ?? "").Trim();
        if (testo.Length == 0) return null;

        if (!Uri.TryCreate(testo, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "Indirizzo del VPS non valido: serve un URL http(s) completo (es. https://nome-del-server)";

        if (uri.Scheme == Uri.UriSchemeHttp && !EReteLocale(uri))
            return "Verso Internet serve https";

        return null;
    }

    /// <summary>localhost / 127.x / ::1 oppure un IPv4 privato (10.x, 192.168.x, 172.16-31.x).</summary>
    private static bool EReteLocale(Uri uri)
    {
        if (uri.IsLoopback) return true;
        if (uri.HostNameType != UriHostNameType.IPv4 || !IPAddress.TryParse(uri.Host, out IPAddress? ip)
            || ip.AddressFamily != AddressFamily.InterNetwork)
            return false;
        byte[] b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || b[0] == 127;
    }

    /// <summary>Il ToString del record stampa tutti i membri: la password no, mai (finirebbe nei log).</summary>
    private bool PrintMembers(StringBuilder b)
    {
        b.Append("Enabled = ").Append(Enabled)
         .Append(", BaseUrl = ").Append(BaseUrl)
         .Append(", Username = ").Append(Username)
         .Append(", Password = ***")
         .Append(", LastRun = ").Append(LastRun)
         .Append(", LastEsito = ").Append(LastEsito)
         .Append(", LastError = ").Append(LastError);
        return true;
    }
}

/// <summary>
/// Lettura/scrittura delle impostazioni in <c>res_settings</c> (chiavi <c>sync.*</c>), stesso
/// pattern della configurazione SMTP di <see cref="EmailService"/> e delle credenziali Ecos:
/// il database comanda, l'<c>appsettings.json</c> (sezione <c>RisorseSync</c>) resta come
/// RIPIEGO per il primo avvio, la password è cifrata a riposo con
/// <see cref="ProtectedConfigHelper"/> ed è write-only (salvare a vuoto non la cancella).
///
/// <para>Si rilegge a ogni uso, non una volta all'avvio: cambiare indirizzo o password dal
/// pannello non deve richiedere il riavvio del servizio.</para>
/// </summary>
public sealed class RisorseSyncSettingsStore
{
    private readonly ResourcesDbService _rdb;
    private readonly IConfiguration _config;
    private readonly ILogger _logger;

    public RisorseSyncSettingsStore(ResourcesDbService rdb, IConfiguration config, ILogger logger)
    {
        _rdb = rdb;
        _config = config;
        _logger = logger;
    }

    // ── Lettura ──────────────────────────────────────────────────

    public RisorseSyncSettings Leggi()
    {
        Dictionary<string, string> righe = LeggiRighe();

        string Get(string chiave, string ripiego) =>
            righe.TryGetValue(chiave, out string? v) && !string.IsNullOrEmpty(v) ? v : ripiego;

        string? GetOpzionale(string chiave) =>
            righe.TryGetValue(chiave, out string? v) && !string.IsNullOrEmpty(v) ? v : null;

        // Password: dal database (cifrata) se c'è, altrimenti dal file di configurazione.
        string password = righe.TryGetValue("sync.password", out string? cifrata) && !string.IsNullOrEmpty(cifrata)
            ? Decifra(cifrata) ?? ""
            : _config["RisorseSync:Password"] ?? "";

        return new RisorseSyncSettings(
            Enabled: EnabledDa(righe),
            BaseUrl: Get("sync.baseurl", _config["RisorseSync:BaseUrl"] ?? "").Trim(),
            Username: Get("sync.username", _config["RisorseSync:Username"] ?? "").Trim(),
            Password: password,
            LastRun: GetOpzionale("sync.last_run"),
            LastEsito: GetOpzionale("sync.last_esito"),
            LastError: GetOpzionale("sync.last_error"));
    }

    private bool EnabledDa(Dictionary<string, string> righe)
    {
        string raw = righe.TryGetValue("sync.enabled", out string? v) && !string.IsNullOrEmpty(v)
            ? v
            : _config["RisorseSync:Enabled"] ?? "false";
        return raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Le impostazioni per il pannello: la password non esce mai, esce se c'è.</summary>
    public RisorseSyncSettingsDto LeggiDto()
    {
        RisorseSyncSettings s = Leggi();
        return new RisorseSyncSettingsDto
        {
            Enabled = s.Enabled,
            BaseUrl = s.BaseUrl,
            Username = s.Username,
            Password = null,
            HasPassword = !string.IsNullOrEmpty(s.Password),
            LastRun = s.LastRun,
            LastEsito = s.LastEsito,
            LastError = s.LastError,
        };
    }

    private Dictionary<string, string> LeggiRighe()
    {
        try
        {
            using MySqlConnection c = _rdb.Open();
            return c.Query<(string SettingKey, string SettingValue)>(
                "SELECT `key` AS SettingKey, `value` AS SettingValue FROM res_settings WHERE `key` LIKE 'sync.%'")
                .ToDictionary(r => r.SettingKey, r => r.SettingValue ?? "");
        }
        catch (Exception ex)
        {
            // Database irraggiungibile: si ripiega su appsettings invece di dichiarare
            // «non configurato» per un guasto passeggero.
            _logger.LogWarning(ex, "[RisorseSync] Impostazioni non leggibili dal database: uso appsettings.");
            return new Dictionary<string, string>();
        }
    }

    // ── Scrittura ────────────────────────────────────────────────

    /// <summary>Salva le impostazioni. La password si aggiorna SOLO se ne arriva una nuova.</summary>
    public void Salva(RisorseSyncSettingsDto dto)
    {
        using MySqlConnection c = _rdb.Open();
        Set(c, "sync.enabled", dto.Enabled ? "1" : "0");
        Set(c, "sync.baseurl", (dto.BaseUrl ?? "").Trim().TrimEnd('/'));
        Set(c, "sync.username", (dto.Username ?? "").Trim());

        // Write-only, come la password SMTP: chi riapre la pagina non se la ritrova a video
        // e salvando senza toccarla non la cancella.
        if (!string.IsNullOrEmpty(dto.Password))
            Set(c, "sync.password", ProtectedConfigHelper.Encrypt(dto.Password));
    }

    /// <summary>Esito dell'ultimo giro (lo scrive il motore, non il pannello).</summary>
    public void ScriviEsito(DateTime runUtc, string esito, string? errore)
    {
        using MySqlConnection c = _rdb.Open();
        Set(c, "sync.last_run", runUtc.ToString("yyyy-MM-dd HH:mm:ss"));
        Set(c, "sync.last_esito", esito);
        // `value` è VARCHAR(500): un messaggio d'errore lungo (stack di HttpRequestException)
        // farebbe fallire proprio la scrittura che doveva raccontarlo.
        Set(c, "sync.last_error", Tronca(errore, 480) ?? "");
    }

    /// <summary>
    /// Una chiave <c>sync.*</c> qualsiasi (es. <c>sync.anagrafiche_full_at</c>,
    /// <c>sync.hash.reparti</c>): il motore ci tiene i suoi segnalibri. null se manca o vuota.
    /// </summary>
    public string? LeggiChiave(string chiave)
    {
        using MySqlConnection c = _rdb.Open();
        string? v = c.ExecuteScalar<string>("SELECT `value` FROM res_settings WHERE `key` = @K", new { K = chiave });
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public void ScriviChiave(string chiave, string valore)
    {
        using MySqlConnection c = _rdb.Open();
        Set(c, chiave, valore);
    }

    private static void Set(MySqlConnection c, string chiave, string valore) => c.Execute(
        "INSERT INTO res_settings (`key`, `value`) VALUES (@K, @V) " +
        "ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
        new { K = chiave, V = valore ?? "" });

    private static string? Tronca(string? s, int max) =>
        s == null || s.Length <= max ? s : s[..max] + "…";

    private static string? Decifra(string cifrataBase64)
    {
        try
        {
            return ProtectedConfigHelper.Decrypt(cifrataBase64);
        }
        catch
        {
            return null; // cifrata da un'altra macchina/utente: va riscritta dal pannello
        }
    }
}
