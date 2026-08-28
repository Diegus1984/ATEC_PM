using System.Globalization;
using System.Text.Json;
using MySqlConnector;

namespace ATEC.PM.Server.Services.Hr;

/// <summary>Una timbratura come arriva dall'API Ecos, prima di ogni elaborazione.</summary>
/// <param name="UpdateDate">Istante di ultima modifica secondo l'orologio DI ECOS: è il
/// campo su cui l'API filtra, quindi è l'unico cursore incrementale sensato (il nostro
/// orologio non è confrontabile con il loro).</param>
public record EcosPunch(
    string ExternalId, DateTime PunchedAt, string EmplCode, string Name, string Direction, string? Location,
    DateTime? UpdateDate = null);

/// <summary>Un badge/anagrafica Ecos: serve alla mappatura <c>employees.ecos_empl_code</c>.</summary>
public record EcosBadge(string EmplCode, string Name, bool IsActive);

/// <summary>Una richiesta di assenza come arriva dall'API Ecos.</summary>
public record EcosAbsenceRequest(
    string AbsenceRequestId, string EmplCode, string Name, string CategoryCode,
    string CategoryDesc, string StatusCode, DateTime DateBegin, DateTime DateEnd,
    bool FullDay, string? HourBegin, string? HourEnd, decimal? Duration,
    DateTime? UpdateDate = null);

/// <summary>L'API Ecos ha risposto ma con un errore suo (CODE ≠ OK) o in una forma inattesa.</summary>
public sealed class EcosApiException : Exception
{
    public EcosApiException(string message) : base(message) { }
}

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
        "VersusCode", "StampLocationName", "YearMonth", "UpdateDate", "StatusCode",
    };

    private static readonly string[] CampiBadge =
    {
        "EmplID", "EmplCode", "NameComplete", "BadgeCode", "InForce", "StatusCode",
    };

    private static readonly string[] AbsenceFields =
    {
        "AbsenceRequestID", "EmplID", "EmplCode", "NameComplete",
        "CategoryCode", "CategoryDescShort", "StatusCode",
        "DateBegin", "DateEnd", "FullDay", "HourBegin", "HourEnd", "Duration", "UpdateDate"
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
            Set("ecos.password", ProtectedConfigHelper.Encrypt(dto.Password));
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
            return ProtectedConfigHelper.Decrypt(cifrataBase64);
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
        string token, DateTime? updateDa, CancellationToken ct = default)
    {
        List<Dictionary<string, string>> righe =
            await FetchTutteLePagineAsync("PeopleStampGetAll", token, PunchFields, updateDa, ct);

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
                    : null));
        }
        return risultato;
    }

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
                    StringComparison.OrdinalIgnoreCase)))
            .ToList();
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
                UpdateDate: ProvaData(r.GetValueOrDefault("UpdateDate", ""), out DateTime agg) ? agg : null));
        }
        return risultato;
    }

    // ── PAGINAZIONE ───────────────────────────────────────────────────────────

    private async Task<List<Dictionary<string, string>>> FetchTutteLePagineAsync(
        string apiName, string token, string[] campi, DateTime? updateDa, CancellationToken ct)
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
            using var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["UpdateDate"] = $">='{updateFrom}'",
            });

            string body = await PostAsync(url, form, ct);
            (List<Dictionary<string, string>> righe, bool? ultima) = EstraiPagina(body, campi, apiName);
            tutte.AddRange(righe);

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
        string codice = "", messaggio = "";
        bool? ultima = null;
        if (tabella.TryGetProperty("ECOSAGILE_ERROR_MESSAGE", out var errore)
            && errore.ValueKind == JsonValueKind.Object)
        {
            codice = Testo(errore, "CODE");
            messaggio = Testo(errore, "MESSAGE");
            if (errore.TryGetProperty("LASTPAGE", out _))
                ultima = string.Equals(Testo(errore, "LASTPAGE"), "TRUE", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.Equals(codice, "OK", StringComparison.OrdinalIgnoreCase))
            throw new EcosApiException($"{apiName}: errore API — {messaggio} (CODE={codice}).");

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
            throw new EcosApiException($"Ecos non raggiungibile: {ex.Message}");
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
