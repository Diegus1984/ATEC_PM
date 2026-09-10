using System.Globalization;
using System.Text.Json;
using MySqlConnector;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>Una timbratura come arriva dall'API Ecos, prima di ogni elaborazione.</summary>
/// <param name="UpdateDate">Istante di ultima modifica secondo l'orologio DI ECOS: è il
/// campo su cui l'API filtra, quindi è l'unico cursore incrementale sensato (il nostro
/// orologio non è confrontabile con il loro).</param>
/// <param name="Deleted">Cancellazione <b>logica</b>: Ecos non toglie mai un record, lo
/// marca <c>Delete=1</c> e continua a restituirlo (guida §7.8; <c>PeopleStampGetAll</c> non
/// filtra <c>Delete=0</c>). Una riga così è una timbratura da togliere, non da importare.</param>
public record EcosPunch(
    string ExternalId, DateTime PunchedAt, string EmplCode, string Name, string Direction, string? Location,
    DateTime? UpdateDate = null, bool Deleted = false, string EmplId = "");

/// <summary>Un badge/anagrafica Ecos: serve alla mappatura <c>employees.ecos_empl_code</c>.</summary>
public record EcosBadge(string EmplCode, string Name, bool IsActive, string EmplId = "");

/// <summary>Una richiesta di assenza come arriva dall'API Ecos.</summary>
/// <param name="EmplId">🪤 <c>PeopleAbsenceRequestGetAll</c> NON manda l'<c>EmplCode</c>: la
/// persona si riconosce solo da qui (l'id interno stabile di Ecos, guida §11).</param>
public record EcosAbsenceRequest(
    string AbsenceRequestId, string EmplCode, string Name, string CategoryCode,
    string CategoryDesc, string StatusCode, DateTime DateBegin, DateTime DateEnd,
    bool FullDay, string? HourBegin, string? HourEnd, decimal? Duration,
    DateTime? UpdateDate = null, bool Deleted = false, string EmplId = "");

/// <summary>
/// Un <b>tratto</b> di un giorno di assenza come lo spezza Ecos
/// (<c>PeopleAbsenceRequestRefineWorkAll</c>): una giornata intera sono due righe, mattina e
/// pomeriggio, ciascuna con i suoi orari; un permesso di tre quarti d'ora è una riga sola.
/// <paramref name="RefineId"/> è il progressivo del tratto <b>dentro il giorno</b> (1 =
/// mattina, 2 = pomeriggio): 🪤 si ripete per ogni giorno di una richiesta a più giorni, la
/// chiave è (richiesta, giorno, tratto).
/// <paramref name="Minutes"/> = fine − inizio; null se Ecos non dà gli orari.
/// </summary>
public record EcosAbsenceDay(
    string AbsenceRequestId, string RefineId, string EmplCode, string Name, DateTime Date,
    string? HourBegin, string? HourEnd, int? Minutes, string CategoryCode, string CategoryDesc,
    string StatusCode, string SourceCode, DateTime? UpdateDate = null, string EmplId = "");

/// <summary>L'API Ecos ha risposto ma con un errore suo (CODE ≠ OK) o in una forma inattesa.</summary>
public sealed class EcosApiException : Exception
{
    public EcosApiException(string message) : base(message) { }

    /// <summary>
    /// true = errore di trasporto (timeout, rete): la richiesta PUÒ essere stata eseguita.
    /// Per un inserimento vuol dire «non ritentare alla cieca» (guida §4.3).
    /// </summary>
    public bool EsitoIncerto { get; init; }
}

/// <summary>Il record che Ecos restituisce dopo un inserimento di timbratura (ReturnAllPostedRecord=1).</summary>
public record EcosStampInserted(string StampId, string EmplId, string EmplCode, DateTime? StampDateTime);

/// <summary>La richiesta creata su Ecos: la chiave, la persona a cui è finita e lo stato con cui è nata.</summary>
public record EcosAbsenceRequestInserted(string AbsenceRequestId, string EmplId, string StatusCode);

/// <summary>
/// Client dell'API EcosAgile («eTime»). Port fedele di <c>Api/EcosApiManager.vb</c> del
/// progetto Timbrature (PIANO-HR-PRESENZE.md §4-§5), con una differenza voluta: gli errori
/// API <b>sollevano eccezione</b> invece di restituire dati parziali in silenzio — l'import
/// non deve avanzare il cursore su una pagina fallita a metà.
///
/// <para>Protocollo: un solo dispatcher <c>…/api.pm?ApiName=</c>, l'operazione è il valore
/// di <c>ApiName</c>. Autenticazione: <c>TokenGet</c> (POST con Userid/Password/ClientID)
/// → <c>AuthToken</c>, che viaggia in query string. Risposta:
/// <c>ECOSAGILE_TABLE_DATA.ECOSAGILE_DATA.ECOSAGILE_DATA_ROW</c> (array O oggetto singolo),
/// esito in <c>ECOSAGILE_ERROR_MESSAGE.CODE</c>, paginazione con
/// <c>PageNumber</c>/<c>RowsPerPage</c>/<c>DF=1</c> e flag <c>LASTPAGE</c>.</para>
///
/// <para>Credenziali nella sezione <c>Ecos</c> di appsettings (in produzione vanno
/// nell'appsettings.json che vive solo sul server, come per DaneaSync). Senza credenziali
/// <see cref="Configured"/> è false e nessuno chiama l'API.</para>
/// </summary>
public class EcosClient
{
    private const int RowsPerPage = 500;

    /// <summary>Tetto anti-loop: se LASTPAGE non arriva mai qualcosa è rotto lato API.</summary>
    private const int MaxPages = 2000;

    /// <summary>
    /// «Tutto quello che c'è»: l'API vuole comunque un <c>UpdateDate</c>, e questa data lo
    /// rende innocuo. Serve alle anagrafiche, dove le righe vecchie contano quanto le nuove.
    /// </summary>
    private static readonly DateTime DallInizio = new(1900, 1, 1);

    private static readonly string[] PunchFields =
    {
        "StampID", "StampDateTime", "EmplID", "EmplCode", "NameComplete",
        "VersusCode", "StampLocationName", "YearMonth", "UpdateDate", "StatusCode", "Delete",
    };

    private static readonly string[] CampiBadge =
    {
        "EmplID", "EmplCode", "NameComplete", "BadgeCode", "InForce", "StatusCode",
    };

    private static readonly string[] AbsenceFields =
    {
        "AbsenceRequestID", "EmplID", "EmplCode", "NameComplete",
        "CategoryCode", "CategoryDescShort", "StatusCode",
        "DateBegin", "DateEnd", "FullDay", "HourBegin", "HourEnd", "Duration", "UpdateDate", "Delete",
    };

    private static readonly string[] AbsenceDayFields =
    {
        "AbsenceRequestID", "AbsenceRequestRefineID", "EmplID", "EmplCode", "NameComplete",
        "TSDate", "HourBegin", "HourEnd", "CategoryCode", "CategoryDescShort", "StatusCode",
        "SourceCode", "UpdateDate",
    };

    private readonly HttpClient _http;
    private readonly ILogger<EcosClient> _logger;
    private readonly IConfiguration _config;
    private readonly ResourcesDbService? _rdb;

    public EcosClient(IConfiguration config, ILogger<EcosClient> logger, ResourcesDbService? rdb = null)
        : this(config, logger, new HttpClient(), rdb) { }

    /// <summary>Costruttore per i test: l'HttpClient (con handler finto) arriva da fuori.</summary>
    internal EcosClient(
        IConfiguration config, ILogger<EcosClient> logger, HttpClient http, ResourcesDbService? rdb = null)
    {
        _logger = logger;
        _http = http;
        _config = config;
        _rdb = rdb;
    }

    // ── CREDENZIALI ───────────────────────────────────────────────────────────
    //
    // Nel programma «Timbrature» le credenziali Ecos si mettono da dentro l'applicazione
    // (dialogo «Configurazione Credenziali», password cifrata con DPAPI). Qui è uguale:
    // stanno in `res_settings` con chiavi `ecos.*` come quelle SMTP, e si scrivono dalla
    // pagina Timbrature. L'appsettings del server resta come RIPIEGO: chi le ha già messe
    // là continua a funzionare, e se il database non risponde il modulo non si blocca.
    //
    // Si rileggono a ogni uso, non una volta all'avvio: cambiare la password non deve
    // richiedere il riavvio del servizio.

    /// <summary>Le credenziali in vigore e da dove arrivano.</summary>
    internal sealed record Credenziali(string BaseUrl, string UserId, string Password, string ClientId, string Source);

    private const string BaseUrlPredefinito = "https://ha.ecosagile.com/dd/api.pm?ApiName=";

    internal Credenziali ResolveCredenziali()
    {
        Dictionary<string, string> righe = LeggiImpostazioni();

        string Get(string chiave, string ripiego) =>
            righe.TryGetValue(chiave, out string? v) && !string.IsNullOrEmpty(v) ? v : ripiego;

        string password = righe.TryGetValue("ecos.password", out string? cifrata) && !string.IsNullOrEmpty(cifrata)
            ? DecifraPassword(cifrata) ?? ""
            : _config["Ecos:Password"] ?? "";

        bool dalDatabase = righe.ContainsKey("ecos.userid") || righe.ContainsKey("ecos.password");

        return new Credenziali(
            Get("ecos.baseurl", _config["Ecos:BaseUrl"] ?? BaseUrlPredefinito),
            Get("ecos.userid", _config["Ecos:UserId"] ?? ""),
            password,
            Get("ecos.clientid", _config["Ecos:ClientId"] ?? ""),
            dalDatabase ? "DATABASE" : "APPSETTINGS");
    }

    private Dictionary<string, string> LeggiImpostazioni()
    {
        if (_rdb == null) return new Dictionary<string, string>();
        try
        {
            using MySqlConnection c = _rdb.Open();
            return c.Query<(string SettingKey, string SettingValue)>(
                "SELECT `key` AS SettingKey, `value` AS SettingValue FROM res_settings WHERE `key` LIKE 'ecos.%'")
                .ToDictionary(r => r.SettingKey, r => r.SettingValue);
        }
        catch (Exception ex)
        {
            // Database irraggiungibile: si ripiega su appsettings invece di dichiarare
            // «non configurato», che manderebbe l'import a riposo per un guasto passeggero.
            _logger.LogWarning(ex, "[Ecos] Impostazioni non leggibili dal database: uso appsettings.");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>Salva le credenziali. La password si aggiorna SOLO se ne arriva una nuova.</summary>
    public void SalvaCredenziali(HrEcosSettingsDto dto)
    {
        if (_rdb == null) throw new InvalidOperationException("Impostazioni Ecos non disponibili senza database.");

        using MySqlConnection c = _rdb.Open();
        void Set(string chiave, string valore) => c.Execute(
            "INSERT INTO res_settings (`key`, `value`) VALUES (@K, @V) " +
            "ON DUPLICATE KEY UPDATE `value` = VALUES(`value`)",
            new { K = chiave, V = valore ?? "" });

        Set("ecos.baseurl", string.IsNullOrWhiteSpace(dto.BaseUrl) ? BaseUrlPredefinito : dto.BaseUrl.Trim());
        Set("ecos.userid", dto.UserId?.Trim() ?? "");
        Set("ecos.clientid", dto.ClientId?.Trim() ?? "");

        // Write-only, come la password SMTP: chi riapre la pagina non se la ritrova a video
        // e salvando senza toccarla non la cancella.
        if (!string.IsNullOrEmpty(dto.Password))
            Set("ecos.password", Segreti.Cifra(dto.Password));
    }

    /// <summary>Le impostazioni per la pagina: la password non esce mai, esce se c'è.</summary>
    public HrEcosSettingsDto LeggiCredenziali()
    {
        Credenziali cred = ResolveCredenziali();
        return new HrEcosSettingsDto
        {
            BaseUrl = cred.BaseUrl,
            UserId = cred.UserId,
            ClientId = cred.ClientId,
            HasPassword = !string.IsNullOrEmpty(cred.Password),
            Source = cred.Source,
            Configured = Configured,
        };
    }

    private static string? DecifraPassword(string cifrataBase64)
    {
        try
        {
            return Segreti.Decifra(cifrataBase64);
        }
        catch
        {
            return null; // cifrata da un'altra macchina: va riscritta
        }
    }

    /// <summary>true = le credenziali ci sono; false = il modulo import resta a riposo.</summary>
    public bool Configured
    {
        get
        {
            Credenziali c = ResolveCredenziali();
            return !string.IsNullOrWhiteSpace(c.UserId)
                && !string.IsNullOrWhiteSpace(c.Password)
                && !string.IsNullOrWhiteSpace(c.ClientId);
        }
    }

    // ── TOKEN ─────────────────────────────────────────────────────────────────

    public async Task<string> TokenAsync(CancellationToken ct = default)
    {
        Credenziali cred = ResolveCredenziali();
        if (string.IsNullOrWhiteSpace(cred.UserId) || string.IsNullOrWhiteSpace(cred.Password)
            || string.IsNullOrWhiteSpace(cred.ClientId))
        {
            throw new EcosApiException(
                "Credenziali Ecos non configurate: si mettono dalla pagina Timbrature, «Credenziali Ecos».");
        }

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Userid"] = cred.UserId,
            ["Password"] = cred.Password,
            ["ClientID"] = cred.ClientId,
        });

        string body = await PostAsync(cred.BaseUrl + "TokenGet", form, ct);
        string? token = EstraiToken(body);
        if (string.IsNullOrEmpty(token))
            throw new EcosApiException("TokenGet: token non presente nella risposta (credenziali errate?).");
        return token;
    }

    /// <summary>Estrae l'AuthToken; null se assente. Statico per i test.</summary>
    internal static string? EstraiToken(string json)
    {
        using JsonDocument doc = ParseDocumento(json, "TokenGet");
        if (!doc.RootElement.TryGetProperty("ECOSAGILE_TABLE_DATA", out var tabella)) return null;
        if (!tabella.TryGetProperty("ECOSAGILE_DATA", out var data)
            || data.ValueKind != JsonValueKind.Object) return null;
        if (!data.TryGetProperty("ECOSAGILE_DATA_ROW", out var row)
            || row.ValueKind != JsonValueKind.Object) return null;
        return row.TryGetProperty("AuthToken", out var t) ? t.GetString() : null;
    }

    // ── OPERAZIONI TIPIZZATE ──────────────────────────────────────────────────

    /// <summary>
    /// Tutte le timbrature con <c>UpdateDate &gt;= updateDa</c> (null = dal 2020, cioè tutto:
    /// il primo import è full per costruzione). L'incrementale funziona perché Ecos
    /// filtra su UpdateDate, quindi arrivano anche le timbrature <b>corrette</b> dopo il fatto.
    /// </summary>
    public async Task<List<EcosPunch>> GetPunchesAsync(
        string token, DateTime? updateDa, CancellationToken ct = default, Action<string>? log = null)
    {
        List<Dictionary<string, string>> righe =
            await FetchTutteLePagineAsync("PeopleStampGetAll", token, PunchFields, updateDa, ct, log: log);

        return LeggiTimbrature(righe);
    }

    /// <summary>
    /// Le timbrature di <b>un mese di calendario</b>, chieste col filtro <c>YearMonth</c> —
    /// lo stesso che usava <c>FetchStampsForEmployee</c> nel VB.
    ///
    /// <para>Serve alla risincronizzazione mirata (voci 2 e 5 del port): dentro quel mese si
    /// riceve tutto quello che Ecos ha, non solo ciò che è cambiato dal cursore, e quindi si
    /// ha la <b>fotografia completa</b> da cui capire cosa è stato cancellato là. Per questo
    /// il vincolo su <c>UpdateDate</c> parte dall'inizio dei tempi: qui non è un incrementale.</para>
    /// </summary>
    public async Task<List<EcosPunch>> GetPunchesMonthAsync(
        string token, int year, int month, CancellationToken ct = default, Action<string>? log = null)
    {
        string yearMonth = $"{year:D4}{month:D2}";
        List<Dictionary<string, string>> righe = await FetchTutteLePagineAsync(
            "PeopleStampGetAll", token, PunchFields, DallInizio, ct,
            filtro: ("YearMonth", $"='{yearMonth}'"), log: log);

        return LeggiTimbrature(righe);
    }

    private List<EcosPunch> LeggiTimbrature(List<Dictionary<string, string>> righe)
    {
        var risultato = new List<EcosPunch>(righe.Count);
        foreach (Dictionary<string, string> r in righe)
        {
            // Una timbratura senza orario o senza id non è importabile: si scarta e si
            // logga, non si inventa.
            if (!ProvaData(r.GetValueOrDefault("StampDateTime", ""), out DateTime punchedAt)
                || string.IsNullOrWhiteSpace(r.GetValueOrDefault("StampID")))
            {
                _logger.LogWarning("[Ecos] Timbratura scartata (StampID='{Id}', StampDateTime='{Dt}')",
                    r.GetValueOrDefault("StampID"), r.GetValueOrDefault("StampDateTime"));
                continue;
            }

            risultato.Add(new EcosPunch(
                ExternalId: r["StampID"].Trim(),
                PunchedAt: punchedAt,
                EmplCode: r.GetValueOrDefault("EmplCode", "").Trim(),
                Name: r.GetValueOrDefault("NameComplete", "").Trim(),
                Direction: r.GetValueOrDefault("VersusCode", "").Trim(),
                Location: ValoreOpzionale(r, "StampLocationName"),
                UpdateDate: ProvaData(r.GetValueOrDefault("UpdateDate", ""), out DateTime agg)
                    ? agg
                    : null,
                Deleted: Vero(r.GetValueOrDefault("Delete")),
                EmplId: r.GetValueOrDefault("EmplID", "").Trim()));
        }
        return risultato;
    }

    /// <summary>
    /// Un booleano di Ecos, in qualunque veste arrivi: <c>true</c> JSON (che <see cref="Testo"/>
    /// rende <c>"TRUE"</c>), <c>"TRUE"</c>, <c>"1"</c>, <c>"-1"</c>. Tutto il resto è falso.
    /// </summary>
    internal static bool Vero(string? valore) =>
        valore is not null
        && (valore.Equals("TRUE", StringComparison.OrdinalIgnoreCase) || valore == "1" || valore == "-1");

    /// <summary>
    /// Anagrafica badge: alimenta i suggerimenti della pagina di mappatura.
    ///
    /// <para>🪤 Qui si chiede <b>dall'inizio dei tempi</b>, non dal 2020 come per timbrature e
    /// assenze. L'API filtra su <c>UpdateDate</c>, e un badge non si aggiorna più dal giorno
    /// in cui è stato assegnato: col ripiego al 2020-01-01 mancavano all'appello quattordici
    /// persone assegnate nel 2019 — fra cui Carretta, Chiantia, Larganà, Tomasi e Vinardi —
    /// e la pagina di mappatura le dava per «senza badge», costringendo a scrivere il codice
    /// a mano. Un elenco di anagrafica si chiede intero: non è un incrementale.</para>
    /// </summary>
    public async Task<List<EcosBadge>> BadgesAsync(string token, CancellationToken ct = default)
    {
        List<Dictionary<string, string>> righe =
            await FetchTutteLePagineAsync("PeopleBadgeGetAll", token, CampiBadge, DallInizio, ct);

        return righe
            .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("EmplCode")))
            .Select(r => new EcosBadge(
                EmplCode: r["EmplCode"].Trim(),
                Name: r.GetValueOrDefault("NameComplete", "").Trim(),
                IsActive: string.Equals(r.GetValueOrDefault("InForce"), "TRUE",
                    StringComparison.OrdinalIgnoreCase),
                // I badge portano EmplCode ed EmplID insieme: è da qui che si impara l'id di chi
                // non timbra (PeopleAbsenceRequestGetAll manda solo l'EmplID).
                EmplId: r.GetValueOrDefault("EmplID", "").Trim()))
            .GroupBy(b => b.EmplCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(b => b.IsActive).ThenByDescending(b => b.Name).First())
            .ToList();
    }

    // ── SCRITTURA: PeopleStampPost ──────────────────────────────────────────────
    //
    // Provata sul tenant l'08/09/2026 (manuale §4.2): Edit=true in query string, nel corpo la
    // chiave StampID, il nuovo StampDateTime e UserTZ (i minuti di scarto dall'UTC col segno
    // di JavaScript: -120 d'estate). Senza UserTZ Ecos risponde -11 «StampDateTimeTZOffSet».
    // È un update parziale: gli altri campi restano; Ecos NON tiene l'ora precedente.

    /// <summary>Il fuso dell'azienda: le timbrature sono in ora italiana.</summary>
    private static readonly TimeZoneInfo FusoItalia = TrovaFuso();

    private static TimeZoneInfo TrovaFuso()
    {
        foreach (string id in new[] { "Europe/Rome", "W. Europe Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { }
            catch (InvalidTimeZoneException) { }
        }
        return TimeZoneInfo.Local;
    }

    /// <summary>
    /// <c>UserTZ</c> come lo manda l'app di Ecos: il <c>getTimezoneOffset()</c> di JavaScript,
    /// cioè UTC meno ora locale in minuti (-120 con l'ora legale, -60 con quella solare).
    /// </summary>
    internal static int UserTz(DateTime oraLocale) =>
        -(int)FusoItalia.GetUtcOffset(DateTime.SpecifyKind(oraLocale, DateTimeKind.Unspecified)).TotalMinutes;

    /// <summary>
    /// Sposta l'orario di una timbratura che su Ecos esiste già (<c>Edit=true</c> + <c>StampID</c>).
    /// Idempotente: rimandare lo stesso orario non cambia niente. Torna il <c>MESSAGE</c> di
    /// Ecos («Correct Record Update»); ogni errore è una <see cref="EcosApiException"/>.
    /// </summary>
    public async Task<string> UpdateStampTimeAsync(
        string token, string stampId, DateTime nuovoOrario, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stampId))
            throw new EcosApiException("PeopleStampPost: senza StampID sarebbe un inserimento, non una modifica.");

        string url = $"{ResolveCredenziali().BaseUrl}PeopleStampPost&Edit=true&DF=1&AppCode=ATEC_PM" +
                     $"&AuthToken={Uri.EscapeDataString(token)}";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["StampID"] = stampId.Trim(),
            ["StampDateTime"] = nuovoOrario.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["UserTZ"] = UserTz(nuovoOrario).ToString(CultureInfo.InvariantCulture),
        });
        string body = await PostAsync(url, form, ct);
        return EsitoScrittura(body, "PeopleStampPost");
    }

    /// <summary>
    /// Cambia il VERSO di una timbratura già su Ecos (entrata ↔ uscita): stessa
    /// <c>PeopleStampPost</c> con <c>Edit=true</c> della modifica d'orario, con
    /// <c>VersusCode</c> al posto di <c>StampDateTime</c>. Serve quando il lettore ha
    /// registrato il gesto al contrario (Diego, 10/09/2026).
    /// </summary>
    public async Task<string> UpdateStampDirectionAsync(
        string token, string stampId, string verso, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(stampId))
            throw new EcosApiException("PeopleStampPost: senza StampID sarebbe un inserimento, non una modifica.");

        string url = $"{ResolveCredenziali().BaseUrl}PeopleStampPost&Edit=true&DF=1&AppCode=ATEC_PM" +
                     $"&AuthToken={Uri.EscapeDataString(token)}";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["StampID"] = stampId.Trim(),
            ["VersusCode"] = NightShift.IsEntry(verso) ? "IN" : "OUT",
        });
        string body = await PostAsync(url, form, ct);
        return EsitoScrittura(body, "PeopleStampPost");
    }

    /// <summary>
    /// La busta di una Post: <c>CODE</c> OK, oppure errore con <c>ERROR_CODE</c>. Torna il
    /// <c>MESSAGE</c>. Trappola: <c>Edit=true</c> a corpo vuoto risponde VUOTO, non -17: anche
    /// quello è un errore (lo intercetta <see cref="ParseDocumento"/>). Statico per i test.
    /// </summary>
    internal static string EsitoScrittura(string json, string apiName)
    {
        using JsonDocument doc = ParseDocumento(json, apiName);
        if (!doc.RootElement.TryGetProperty("ECOSAGILE_TABLE_DATA", out JsonElement tabella)
            || !tabella.TryGetProperty("ECOSAGILE_ERROR_MESSAGE", out JsonElement errore)
            || errore.ValueKind != JsonValueKind.Object)
            throw new EcosApiException($"{apiName}: risposta senza esito ({Accorcia(json)}).");

        string codice = Testo(errore, "CODE");
        string messaggio = Testo(errore, "MESSAGE");
        string codiceErrore = Testo(errore, "ERROR_CODE");
        if (string.Equals(codice, "OK", StringComparison.OrdinalIgnoreCase)) return messaggio;

        string spiegazione = codiceErrore switch
        {
            "-1" => " Token Ecos scaduto (ERROR_CODE -1): serve un TokenGet nuovo.",
            "-2" => " Diritto mancante sul servizio (ERROR_CODE -2).",
            "-17" => " Chiave mancante nel corpo (ERROR_CODE -17).",
            "" => "",
            _ => $" (ERROR_CODE {codiceErrore})",
        };
        throw new EcosApiException($"{apiName}: {messaggio} (CODE={codice}).{spiegazione}");
    }

    /// <summary>
    /// Il badge ATTIVO di una persona: è così che <c>PeopleStampPost</c> in inserimento
    /// identifica il dipendente (provato l'08/09/2026: <c>EmplID</c> nel corpo viene ignorato e
    /// nasce un record senza persona, invisibile). Null se la persona non ha un badge attivo.
    /// </summary>
    public async Task<string?> ActiveBadgeCodeAsync(string token, int emplId, CancellationToken ct = default)
    {
        List<Dictionary<string, string>> righe = await FetchTutteLePagineAsync(
            "PeopleBadgeGetAll", token, CampiBadge, DallInizio, ct, ("EmplID", $"={emplId}"));
        return righe
            .Where(r => string.Equals(r.GetValueOrDefault("StatusCode"), "A", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.GetValueOrDefault("BadgeCode", "").Trim())
            .FirstOrDefault(b => b.Length > 0);
    }

    /// <summary>
    /// Inserisce una timbratura NUOVA su Ecos (<c>PeopleStampPost</c> senza <c>Edit</c>).
    /// 🪤 Non idempotente: un timeout dopo la scrittura ha già creato il record
    /// (<see cref="EcosApiException.EsitoIncerto"/>): mai ritentare alla cieca. La risposta
    /// porta il record intero: la chiave e la persona a cui Ecos l'ha attaccata, da verificare.
    /// </summary>
    public async Task<EcosStampInserted> InsertStampAsync(
        string token, string badgeCode, DateTime quando, string direction, string? note, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(badgeCode))
            throw new EcosApiException("PeopleStampPost: senza BadgeCode la timbratura nascerebbe senza persona.");

        string url = $"{ResolveCredenziali().BaseUrl}PeopleStampPost&ReturnAllPostedRecord=1&DF=1&AppCode=ATEC_PM" +
                     $"&AuthToken={Uri.EscapeDataString(token)}";
        var campi = new Dictionary<string, string>
        {
            ["BadgeCode"] = badgeCode.Trim(),
            ["StampDateTime"] = quando.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["VersusCode"] = NightShift.IsEntry(direction) ? "IN" : "OUT",
            ["StatusCode"] = "A",
            ["UserTZ"] = UserTz(quando).ToString(CultureInfo.InvariantCulture),
        };
        if (!string.IsNullOrWhiteSpace(note)) campi["Note"] = note.Length > 200 ? note[..200] : note;
        using var form = new FormUrlEncodedContent(campi);
        string body = await PostAsync(url, form, ct);
        Dictionary<string, string> riga = RigaScrittura(body, "PeopleStampPost");
        string stampId = riga.GetValueOrDefault("StampID", "").Trim();
        if (stampId.Length == 0)
            throw new EcosApiException("PeopleStampPost: inserimento riuscito ma senza StampID nella risposta.") { EsitoIncerto = true };
        return new EcosStampInserted(
            stampId, riga.GetValueOrDefault("EmplID", "").Trim(), riga.GetValueOrDefault("EmplCode", "").Trim(),
            ProvaData(riga.GetValueOrDefault("StampDateTime", ""), out DateTime d) ? d : null);
    }

    /// <summary>Cancellazione logica (<c>Edit=true</c> + <c>Delete=1</c>): il record resta, marcato.</summary>
    public async Task DeleteStampAsync(string token, string stampId, CancellationToken ct = default)
    {
        string url = $"{ResolveCredenziali().BaseUrl}PeopleStampPost&Edit=true&DF=1&AppCode=ATEC_PM" +
                     $"&AuthToken={Uri.EscapeDataString(token)}";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["StampID"] = stampId.Trim(),
            ["Delete"] = "1",
        });
        EsitoScrittura(await PostAsync(url, form, ct), "PeopleStampPost");
    }

    /// <summary>La prima riga di dati di una Post riuscita (con <c>ReturnAllPostedRecord=1</c>).</summary>
    internal static Dictionary<string, string> RigaScrittura(string json, string apiName)
    {
        EsitoScrittura(json, apiName);
        using JsonDocument doc = ParseDocumento(json, apiName);
        var vuota = new Dictionary<string, string>();
        if (!doc.RootElement.TryGetProperty("ECOSAGILE_TABLE_DATA", out JsonElement tabella)
            || !tabella.TryGetProperty("ECOSAGILE_DATA", out JsonElement data)
            || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("ECOSAGILE_DATA_ROW", out JsonElement rows))
            return vuota;
        JsonElement row = rows.ValueKind == JsonValueKind.Array
            ? (rows.GetArrayLength() > 0 ? rows[0] : default)
            : rows;
        if (row.ValueKind != JsonValueKind.Object) return vuota;
        var esito = new Dictionary<string, string>();
        foreach (JsonProperty p in row.EnumerateObject()) esito[p.Name] = Testo(row, p.Name);
        return esito;
    }

    // ── SCRITTURA: PeopleAbsenceRequestPost (abilitata ad api.it l'08/09/2026, #151) ────
    //
    // Provata su Diego (richiesta 136492): qui EmplID È onorato; StatusCode accettato anche in
    // insert (ACCEPTED/REQUEST/REJECT); la richiesta compare subito in GetAll; Edit=true cambia
    // lo stato; Delete=1 la cancella logicamente. CategoryID va SEMPRE letto da
    // AnagTSCategoryGetAll e mappato per CategoryCode: gli id non si cablano (manuale §9.9).

    private static readonly string[] CampiCategoria = { "CategoryID", "CategoryCode", "DescShort", "StatusCode", "isAbsence" };

    /// <summary>Le causali attive di Ecos: <c>CategoryCode</c> → <c>CategoryID</c>.</summary>
    public async Task<Dictionary<string, int>> AbsenceCategoriesAsync(string token, CancellationToken ct = default)
    {
        List<Dictionary<string, string>> righe =
            await FetchTutteLePagineAsync("AnagTSCategoryGetAll", token, CampiCategoria, DallInizio, ct);
        var mappa = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (Dictionary<string, string> r in righe)
        {
            if (!string.Equals(r.GetValueOrDefault("StatusCode"), "A", StringComparison.OrdinalIgnoreCase)) continue;
            string codice = r.GetValueOrDefault("CategoryCode", "").Trim();
            if (codice.Length > 0 && int.TryParse(r.GetValueOrDefault("CategoryID"), out int id)) mappa[codice] = id;
        }
        return mappa;
    }

    /// <summary>
    /// Crea una richiesta di assenza su Ecos. Non idempotente: la chiave restituita si salva
    /// subito (<c>hr_absences.ecos_absence_id</c>) e non si ritenta alla cieca.
    /// </summary>
    public async Task<EcosAbsenceRequestInserted> InsertAbsenceRequestAsync(
        string token, int emplId, DateTime dateBegin, DateTime? dateEnd, bool fullDay,
        TimeSpan? hourBegin, TimeSpan? hourEnd, int categoryId, string statusCode, string? note,
        CancellationToken ct = default)
    {
        if (!fullDay && (hourBegin == null || hourEnd == null))
            throw new EcosApiException("PeopleAbsenceRequestPost: una richiesta a ore vuole la fascia oraria (HourBegin/HourEnd).");

        string url = $"{ResolveCredenziali().BaseUrl}PeopleAbsenceRequestPost&ReturnAllPostedRecord=1&DF=1&AppCode=ATEC_PM" +
                     $"&AuthToken={Uri.EscapeDataString(token)}";
        var campi = new Dictionary<string, string>
        {
            ["EmplID"] = emplId.ToString(CultureInfo.InvariantCulture),
            ["DateBegin"] = dateBegin.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture),
            ["FullDay"] = fullDay ? "1" : "0",
            ["CategoryID"] = categoryId.ToString(CultureInfo.InvariantCulture),
            ["StatusCode"] = statusCode,
        };
        if (dateEnd is { } fine && fine.Date != dateBegin.Date)
            campi["DateEnd"] = fine.ToString("yyyy-MM-dd 00:00:00", CultureInfo.InvariantCulture);
        if (!fullDay)
        {
            campi["HourBegin"] = hourBegin!.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            campi["HourEnd"] = hourEnd!.Value.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
        }
        if (!string.IsNullOrWhiteSpace(note)) campi["Note"] = note.Length > 200 ? note[..200] : note;

        using var form = new FormUrlEncodedContent(campi);
        string body = await PostAsync(url, form, ct);
        Dictionary<string, string> riga = RigaScrittura(body, "PeopleAbsenceRequestPost");
        string id = riga.GetValueOrDefault("AbsenceRequestID", "").Trim();
        if (id.Length == 0)
            throw new EcosApiException("PeopleAbsenceRequestPost: inserimento riuscito ma senza AbsenceRequestID nella risposta.") { EsitoIncerto = true };
        return new EcosAbsenceRequestInserted(id, riga.GetValueOrDefault("EmplID", "").Trim(),
            riga.GetValueOrDefault("StatusCode", "").Trim().ToUpperInvariant());
    }

    /// <summary>Cambia lo stato di una richiesta (<c>ACCEPTED</c> / <c>REJECT</c>); il motivo del rifiuto in <c>ApproveReply</c>.</summary>
    public async Task<string> SetAbsenceRequestStatusAsync(
        string token, string absenceRequestId, string statusCode, string? approveReply, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(absenceRequestId))
            throw new EcosApiException("PeopleAbsenceRequestPost: senza AbsenceRequestID sarebbe un inserimento.");
        string url = $"{ResolveCredenziali().BaseUrl}PeopleAbsenceRequestPost&Edit=true&DF=1&AppCode=ATEC_PM" +
                     $"&AuthToken={Uri.EscapeDataString(token)}";
        var campi = new Dictionary<string, string>
        {
            ["AbsenceRequestID"] = absenceRequestId.Trim(),
            ["StatusCode"] = statusCode,
        };
        if (!string.IsNullOrWhiteSpace(approveReply))
            campi["ApproveReply"] = approveReply.Length > 200 ? approveReply[..200] : approveReply;
        using var form = new FormUrlEncodedContent(campi);
        return EsitoScrittura(await PostAsync(url, form, ct), "PeopleAbsenceRequestPost");
    }

    /// <summary>Cancellazione logica di una richiesta (<c>Edit=true</c> + <c>Delete=1</c>).</summary>
    public async Task DeleteAbsenceRequestAsync(string token, string absenceRequestId, CancellationToken ct = default)
    {
        string url = $"{ResolveCredenziali().BaseUrl}PeopleAbsenceRequestPost&Edit=true&DF=1&AppCode=ATEC_PM" +
                     $"&AuthToken={Uri.EscapeDataString(token)}";
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["AbsenceRequestID"] = absenceRequestId.Trim(),
            ["Delete"] = "1",
        });
        EsitoScrittura(await PostAsync(url, form, ct), "PeopleAbsenceRequestPost");
    }

    /// <summary>Richieste di assenza / ferie / permessi da Ecos.</summary>
    public async Task<List<EcosAbsenceRequest>> GetAbsenceRequestsAsync(
        string token, DateTime? updateDa, CancellationToken ct = default)
    {
        List<Dictionary<string, string>> righe =
            await FetchTutteLePagineAsync("PeopleAbsenceRequestGetAll", token, AbsenceFields, updateDa, ct);

        var risultato = new List<EcosAbsenceRequest>(righe.Count);
        foreach (Dictionary<string, string> r in righe)
        {
            if (string.IsNullOrWhiteSpace(r.GetValueOrDefault("AbsenceRequestID"))
                || !ProvaData(r.GetValueOrDefault("DateBegin", ""), out DateTime dateBegin))
            {
                continue;
            }

            DateTime dateEnd = dateBegin;
            if (ProvaData(r.GetValueOrDefault("DateEnd", ""), out DateTime dtEnd))
                dateEnd = dtEnd;

            bool fullDay = string.Equals(r.GetValueOrDefault("FullDay", ""), "TRUE", StringComparison.OrdinalIgnoreCase);

            decimal? duration = null;
            string durStr = r.GetValueOrDefault("Duration", "").Replace(',', '.');
            if (decimal.TryParse(durStr, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal dur))
                duration = dur;

            risultato.Add(new EcosAbsenceRequest(
                AbsenceRequestId: r["AbsenceRequestID"].Trim(),
                EmplCode: r.GetValueOrDefault("EmplCode", "").Trim(),
                Name: r.GetValueOrDefault("NameComplete", "").Trim(),
                CategoryCode: r.GetValueOrDefault("CategoryCode", "").Trim(),
                CategoryDesc: r.GetValueOrDefault("CategoryDescShort", "").Trim(),
                StatusCode: r.GetValueOrDefault("StatusCode", "").Trim().ToUpperInvariant(),
                DateBegin: dateBegin.Date,
                DateEnd: dateEnd.Date,
                FullDay: fullDay,
                HourBegin: ValoreOpzionale(r, "HourBegin"),
                HourEnd: ValoreOpzionale(r, "HourEnd"),
                Duration: duration,
                UpdateDate: ProvaData(r.GetValueOrDefault("UpdateDate", ""), out DateTime agg) ? agg : null,
                Deleted: Vero(r.GetValueOrDefault("Delete")),
                EmplId: r.GetValueOrDefault("EmplID", "").Trim()));
        }
        return risultato;
    }

    /// <summary>
    /// I giorni di assenza fra <paramref name="dal"/> e <paramref name="al"/> come li spezza
    /// Ecos (<c>PeopleAbsenceRequestRefineWorkAll</c>, catalogo). Filtro su <c>TSDate</c> con
    /// l'intervallo nello stesso valore («&gt;='…' AND TSDate&lt;='…'», guida §6.5); nessun
    /// criterio implicito lato server, quindi risponde anche per i mesi vecchi. Le righe senza
    /// data o senza chiave si scartano e si loggano.
    /// </summary>
    public async Task<List<EcosAbsenceDay>> GetAbsenceDaysAsync(
        string token, DateTime dal, DateTime al, CancellationToken ct = default)
    {
        string intervallo = $">='{dal:yyyy-MM-dd}' AND TSDate<='{al:yyyy-MM-dd}'";
        List<Dictionary<string, string>> righe = await FetchTutteLePagineAsync(
            "PeopleAbsenceRequestRefineWorkAll", token, AbsenceDayFields, DallInizio, ct,
            filtro: ("TSDate", intervallo));

        var risultato = new List<EcosAbsenceDay>(righe.Count);
        foreach (Dictionary<string, string> r in righe)
        {
            if (string.IsNullOrWhiteSpace(r.GetValueOrDefault("AbsenceRequestID"))
                || !ProvaData(r.GetValueOrDefault("TSDate", ""), out DateTime giorno))
            {
                _logger.LogWarning("[Ecos] Giorno di assenza scartato (AbsenceRequestID='{Id}', TSDate='{Dt}')",
                    r.GetValueOrDefault("AbsenceRequestID"), r.GetValueOrDefault("TSDate"));
                continue;
            }

            string? inizio = ValoreOpzionale(r, "HourBegin");
            string? fine = ValoreOpzionale(r, "HourEnd");
            risultato.Add(new EcosAbsenceDay(
                AbsenceRequestId: r["AbsenceRequestID"].Trim(),
                RefineId: r.GetValueOrDefault("AbsenceRequestRefineID", "").Trim() is { Length: > 0 } rid ? rid : "1",
                EmplCode: r.GetValueOrDefault("EmplCode", "").Trim(),
                Name: r.GetValueOrDefault("NameComplete", "").Trim(),
                Date: giorno.Date,
                HourBegin: inizio,
                HourEnd: fine,
                Minutes: MinutiFra(inizio, fine),
                CategoryCode: r.GetValueOrDefault("CategoryCode", "").Trim(),
                CategoryDesc: r.GetValueOrDefault("CategoryDescShort", "").Trim(),
                StatusCode: r.GetValueOrDefault("StatusCode", "").Trim().ToUpperInvariant(),
                SourceCode: r.GetValueOrDefault("SourceCode", "").Trim(),
                UpdateDate: ProvaData(r.GetValueOrDefault("UpdateDate", ""), out DateTime agg) ? agg : null,
                EmplId: r.GetValueOrDefault("EmplID", "").Trim()));
        }
        return risultato;
    }

    /// <summary>
    /// I minuti fra due orari «HH:mm:ss» (o «HH:mm») di Ecos; null se uno manca, non si legge,
    /// o la fine viene prima dell'inizio (un tratto a cavallo di mezzanotte non esiste qui).
    /// </summary>
    internal static int? MinutiFra(string? inizio, string? fine)
    {
        if (!ProvaOra(inizio, out TimeSpan a) || !ProvaOra(fine, out TimeSpan b)) return null;
        if (b < a) return null;
        return (int)Math.Round((b - a).TotalMinutes);
    }

    internal static bool ProvaOra(string? valore, out TimeSpan ora) =>
        TimeSpan.TryParseExact(valore?.Trim() ?? "", new[] { "hh\\:mm\\:ss", "hh\\:mm", "h\\:mm\\:ss", "h\\:mm" },
            CultureInfo.InvariantCulture, out ora);

    // ── PAGINAZIONE ───────────────────────────────────────────────────────────

    /// <param name="filtro">
    /// Filtro aggiuntivo sul campo indicato, nella forma che vuole Ecos (es.
    /// <c>("YearMonth", "='202608'")</c>). Null = solo il vincolo su <c>UpdateDate</c>.
    /// </param>
    /// <param name="log">
    /// Se valorizzato riceve una riga per pagina scaricata: è il <c>txtLog</c> di
    /// <c>SyncEcosPage</c> che l'utente guarda mentre l'import gira.
    /// </param>
    private async Task<List<Dictionary<string, string>>> FetchTutteLePagineAsync(
        string apiName, string token, string[] campi, DateTime? updateDa, CancellationToken ct,
        (string Key, string Value)? filtro = null, Action<string>? log = null)
    {
        var tutte = new List<Dictionary<string, string>>();
        // Senza una data di partenza si riparte dal 2020: vale per i dati che scorrono
        // (timbrature, richieste). Per le anagrafiche si passa <see cref="DallInizio"/>.
        string updateFrom = (updateDa ?? new DateTime(2020, 1, 1))
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        string baseUrl = ResolveCredenziali().BaseUrl;

        for (int pagina = 1; pagina <= MaxPages; pagina++)
        {
            // Il token va in query string ed è dato altrui: senza escape un '&' lo
            // troncherebbe e l'errore arriverebbe travestito da «privilegi insufficienti».
            string url = $"{baseUrl}{apiName}&PageNumber={pagina}&RowsPerPage={RowsPerPage}" +
                         $"&DF=1&AuthToken={Uri.EscapeDataString(token)}";
            var campiPost = new Dictionary<string, string>
            {
                ["UpdateDate"] = $">='{updateFrom}'",
            };
            if (filtro is { } f) campiPost[f.Key] = f.Value;
            using var form = new FormUrlEncodedContent(campiPost);

            string body = await PostAsync(url, form, ct);
            (List<Dictionary<string, string>> righe, bool? ultima) = EstraiPagina(body, campi, apiName);
            tutte.AddRange(righe);
            log?.Invoke($"  pagina {pagina}: {righe.Count} righe");

            // LASTPAGE dichiarato: si crede all'API. Non dichiarato: è l'ultima solo se
            // la pagina non è piena — una pagina piena può sempre avere un seguito.
            if (ultima == true || righe.Count == 0) return tutte;
            if (ultima == null && righe.Count < RowsPerPage) return tutte;
        }

        throw new EcosApiException(
            $"{apiName}: superate {MaxPages} pagine senza LASTPAGE — risposta API anomala.");
    }

    /// <summary>
    /// Una pagina di risposta → (righe, èL'ultima). <c>UltimaPagina</c> è null quando l'API
    /// non manda LASTPAGE: lì decide il chiamante contando le righe. Statico per i test.
    /// CODE ≠ OK solleva <see cref="EcosApiException"/>: un errore a pagina N non deve
    /// passare per «fine dati».
    /// </summary>
    internal static (List<Dictionary<string, string>> Righe, bool? UltimaPagina) EstraiPagina(
        string json, string[] campi, string apiName)
    {
        using JsonDocument doc = ParseDocumento(json, apiName);
        if (!doc.RootElement.TryGetProperty("ECOSAGILE_TABLE_DATA", out var tabella))
            throw new EcosApiException($"{apiName}: risposta senza ECOSAGILE_TABLE_DATA.");

        // 🪤 `ultima` è un booleano a TRE stati: null = l'API non l'ha dichiarato, e in
        // quel caso decide il chiamante contando le righe. Col default a `true` una
        // risposta senza LASTPAGE troncava lo scarico alla prima pagina, e l'import lo
        // dichiarava riuscito: esattamente la perdita silenziosa che questa classe
        // esiste per impedire.
        string codice = "", messaggio = "", codiceErrore = "";
        bool? ultima = null;
        if (tabella.TryGetProperty("ECOSAGILE_ERROR_MESSAGE", out var errore)
            && errore.ValueKind == JsonValueKind.Object)
        {
            codice = Testo(errore, "CODE");
            messaggio = Testo(errore, "MESSAGE");
            codiceErrore = Testo(errore, "ERROR_CODE");
            if (errore.TryGetProperty("LASTPAGE", out _))
                ultima = string.Equals(Testo(errore, "LASTPAGE"), "TRUE", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(codice, "OK", StringComparison.OrdinalIgnoreCase))
        {
            // ERROR_CODE -1 = token scaduto (60 s di inattività, guida §4.4): chi legge il log
            // deve capire che serve un TokenGet nuovo, non cercare un guasto di rete o di diritti.
            string spiegazione = codiceErrore == "-1"
                ? " Token Ecos scaduto (ERROR_CODE -1): serve un TokenGet nuovo prima di questa chiamata."
                : "";
            throw new EcosApiException($"{apiName}: errore API — {messaggio} (CODE={codice}).{spiegazione}");
        }

        var righe = new List<Dictionary<string, string>>();

        // ECOSAGILE_DATA può essere: oggetto con le righe, stringa vuota (nessun dato),
        // o mancare del tutto. E ECOSAGILE_DATA_ROW può essere array O oggetto singolo.
        if (tabella.TryGetProperty("ECOSAGILE_DATA", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("ECOSAGILE_DATA_ROW", out var rows))
        {
            if (rows.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement row in rows.EnumerateArray())
                    righe.Add(EstraiCampi(row, campi));
            }
            else if (rows.ValueKind == JsonValueKind.Object)
            {
                righe.Add(EstraiCampi(rows, campi));
            }
        }

        return (righe, ultima);
    }

    private static Dictionary<string, string> EstraiCampi(JsonElement row, string[] campi)
    {
        var valori = new Dictionary<string, string>(campi.Length);
        foreach (string campo in campi)
        {
            valori[campo] = row.ValueKind == JsonValueKind.Object
                ? Testo(row, campo)
                : "";
        }
        return valori;
    }

    /// <summary>
    /// Il valore di una proprietà come testo, qualunque tipo JSON abbia. Ecos manda i
    /// numeri a volte quotati e a volte no, e i booleani ora come <c>"TRUE"</c> ora come
    /// <c>true</c>: leggerli con <c>GetString()</c> vorrebbe dire un'eccezione sui
    /// booleani e un campo vuoto sui numeri.
    /// </summary>
    private static string Testo(JsonElement oggetto, string campo)
    {
        if (!oggetto.TryGetProperty(campo, out JsonElement v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.True => "TRUE",
            JsonValueKind.False => "FALSE",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        };
    }

    private static JsonDocument ParseDocumento(string json, string apiName)
    {
        // Ecos su certi errori risponde HTML: il messaggio deve dirlo, non degenerare
        // in una JsonException criptica.
        if (string.IsNullOrWhiteSpace(json) || !json.TrimStart().StartsWith('{'))
            throw new EcosApiException(
                $"{apiName}: risposta non JSON ({Accorcia(json)}).");
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new EcosApiException($"{apiName}: JSON malformato — {ex.Message}.");
        }
    }

    private async Task<string> PostAsync(string url, HttpContent content, CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage risposta = await _http.PostAsync(url, content, ct);
            // L'API segnala gli errori nel JSON, non nello status HTTP: si legge sempre il corpo.
            return await risposta.Content.ReadAsStringAsync(ct);
        }
        // 🪤 Il `when` sulla cancellazione è necessario: senza, lo spegnimento del servizio
        // durante l'import notturno finirebbe a log come «Ecos non raggiungibile», e in
        // produzione si andrebbe a cercare un guasto di rete che non c'è.
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            throw new EcosApiException($"Ecos non raggiungibile: {ex.Message}") { EsitoIncerto = true };
        }
    }

    /// <summary>
    /// «yyyy-MM-dd HH:mm:ss», il formato osservato sul campo, più poche varianti ISO.
    ///
    /// <para>🪤 <b>Solo formati espliciti.</b> Il ripiego su <c>DateTime.TryParse</c>
    /// invariante leggeva «05/02/2026» come <b>2 maggio</b>: se un domani Ecos passasse al
    /// formato italiano, le timbrature finirebbero nel giorno sbagliato <i>in silenzio</i>,
    /// sballando due cartellini. Meglio scartare e loggare che indovinare.</para>
    /// </summary>
    internal static bool ProvaData(string valore, out DateTime risultato) =>
        DateTime.TryParseExact(
            valore,
            new[]
            {
                "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm",
                "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-dd",
            },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out risultato);

    private static string? ValoreOpzionale(Dictionary<string, string> r, string campo)
    {
        string v = r.GetValueOrDefault(campo, "").Trim();
        return v.Length == 0 ? null : v;
    }

    private static string Accorcia(string testo) =>
        string.IsNullOrEmpty(testo) ? "risposta vuota" : testo[..Math.Min(160, testo.Length)];
}
