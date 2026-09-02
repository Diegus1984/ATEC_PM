using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Server.Services.RisorseSync;

/// <summary>Il VPS ha risposto ma ha detto di no (ApiResponse.Success = false), o ha risposto in una forma inattesa.</summary>
public sealed class RisorseSyncException : Exception
{
    public RisorseSyncException(string message) : base(message) { }
}

/// <summary>
/// Client HTTP verso <c>{BaseUrl}/api/sync</c> di ATEC Risorse (PIANO-SYNC-RISORSE.md §4.4).
///
/// <para>Autenticazione: <c>POST /api/auth/login</c> con l'account di servizio (ruolo SYNC sul
/// VPS) → JWT (8 ore) tenuto in memoria e messo come Bearer su ogni chiamata; dopo 7 ore il
/// login si rifà da solo, prima che scada. Su un 401 il login viene rifatto UNA volta e la
/// chiamata ripetuta; se fallisce ancora, l'errore sale al chiamante.</para>
///
/// <para>Freno sul lockout: se il login riceve 401 (credenziali rifiutate) si alza
/// <see cref="CredenzialiRifiutate"/> e da lì in poi NESSUNA chiamata tocca più il VPS — ogni
/// tentativo solleva subito un errore VISIBILE nel pannello. Il flag muore col client: salvare
/// impostazioni nuove ne crea uno nuovo (vedi <c>RisorseSyncService.AssicuraClient</c>).</para>
///
/// <para>Ogni risposta è <c>ApiResponse&lt;T&gt;</c>: se <c>Success</c> è false si solleva
/// <see cref="RisorseSyncException"/> col <c>Message</c> del VPS. JSON camelCase in uscita,
/// lettura case-insensitive: le stesse regole di Program.cs, così i DTO del contratto
/// viaggiano identici nei due versi.</para>
///
/// <para>Un solo <see cref="HttpClient"/> statico condiviso (in Program.cs non c'è
/// <c>IHttpClientFactory</c>); nei test si passa un <see cref="HttpClient"/> con handler finto,
/// come fa <c>EcosClient</c>. Il BaseUrl NON è il BaseAddress: può cambiare dal pannello senza
/// riavvio, quindi gli URL si compongono per intero a ogni chiamata.</para>
/// </summary>
public sealed class RisorseSyncClient
{
    /// <summary>Le stesse opzioni JSON dell'API (camelCase, case-insensitive): un posto solo.</summary>
    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HttpClient HttpCondiviso = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _baseUrl;
    private readonly string _username;
    private readonly string _password;

    /// <summary>Il JWT del VPS dura 8 ore: lo si rinnova a 7, prima che un giro parta con un token in scadenza.</summary>
    private static readonly TimeSpan DurataToken = TimeSpan.FromHours(7);

    private const string MsgCredenzialiRifiutate =
        "Credenziali rifiutate dal VPS: correggere utente o password nella scheda Sincronizzazione";

    private string? _token;
    private DateTime _tokenAlle;                 // UTC: quando è stato ottenuto _token
    private readonly SemaphoreSlim _loginLock = new(1, 1);

    public RisorseSyncClient(RisorseSyncSettings settings, ILogger logger, HttpClient? http = null)
    {
        _http = http ?? HttpCondiviso;
        _logger = logger;
        _baseUrl = settings.BaseUrlNormalizzato;
        _username = settings.Username;
        _password = settings.Password;
    }

    public string BaseUrl => _baseUrl;

    /// <summary>Il JWT in mano (null se non si è ancora fatto login). Serve anche al client SignalR.</summary>
    public string? Token => _token;

    /// <summary>
    /// true dopo un 401 al login: utente o password sbagliati. Da qui in poi il client non
    /// chiama più il VPS (un tentativo ogni 60 s farebbe scattare il blocco del VPS sull'utente di servizio: 5 tentativi in 5 minuti, poi 429):
    /// si corregge dal pannello, che crea un client nuovo.
    /// </summary>
    public bool CredenzialiRifiutate { get; private set; }

    /// <summary>true se questo client è stato costruito con le stesse credenziali: se cambiano, se ne fa uno nuovo.</summary>
    public bool StesseImpostazioni(RisorseSyncSettings s) =>
        _baseUrl == s.BaseUrlNormalizzato && _username == s.Username && _password == s.Password;

    // ── Login ────────────────────────────────────────────────────

    /// <summary>POST /api/auth/login → token. Sostituisce quello in memoria.</summary>
    public async Task<string> LoginAsync(CancellationToken ct = default)
    {
        if (CredenzialiRifiutate)
            throw new RisorseSyncException(MsgCredenzialiRifiutate);

        await _loginLock.WaitAsync(ct);
        try
        {
            if (CredenzialiRifiutate)
                throw new RisorseSyncException(MsgCredenzialiRifiutate);

            const string percorso = "/api/auth/login";
            using HttpResponseMessage risposta = await _http.PostAsJsonAsync(
                $"{_baseUrl}{percorso}",
                new LoginRequest { Username = _username, Password = _password },
                JsonOptions, ct);

            if (risposta.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Freno: da qui in poi niente più chiamate finché non cambiano le impostazioni.
                CredenzialiRifiutate = true;
                _logger.LogWarning("[RisorseSync] Login sul VPS rifiutato ({User}): il motore si ferma finché non si correggono le credenziali.", _username);
                throw new RisorseSyncException(MsgCredenzialiRifiutate);
            }

            ApiResponse<LoginResponse>? api = await LeggiAsync<ApiResponse<LoginResponse>>(risposta, percorso, ct);
            if (api == null || !api.Success || api.Data == null || string.IsNullOrEmpty(api.Data.Token))
                throw new RisorseSyncException(
                    string.IsNullOrWhiteSpace(api?.Message) ? "Login sul VPS fallito." : $"Login sul VPS fallito: {api.Message}");

            _token = api.Data.Token;
            _tokenAlle = DateTime.UtcNow;
            _logger.LogInformation("[RisorseSync] Login sul VPS riuscito ({User}).", _username);
            return _token;
        }
        finally
        {
            _loginLock.Release();
        }
    }

    /// <summary>Il token, facendo login se non c'è ancora o se ha più di 7 ore (scade a 8).</summary>
    public async Task<string> TokenAsync(CancellationToken ct = default)
    {
        if (CredenzialiRifiutate)
            throw new RisorseSyncException(MsgCredenzialiRifiutate);
        string? token = _token;
        if (token != null && DateTime.UtcNow - _tokenAlle < DurataToken)
            return token;
        return await LoginAsync(ct);
    }

    // ── Chiamate del contratto ───────────────────────────────────

    public Task<SyncStatusDto> GetStatusAsync(CancellationToken ct = default) =>
        ChiamaAsync<SyncStatusDto>(HttpMethod.Get, "/api/sync/status", null, ct);

    public Task<List<SyncAssignmentDto>> GetAssignmentsAsync(CancellationToken ct = default) =>
        ChiamaAsync<List<SyncAssignmentDto>>(HttpMethod.Get, "/api/sync/assignments", null, ct);

    public Task<List<SyncUpsertResultDto>> UpsertAssignmentsAsync(List<SyncAssignmentUpsertDto> righe, CancellationToken ct = default) =>
        ChiamaAsync<List<SyncUpsertResultDto>>(HttpMethod.Post, "/api/sync/assignments", righe, ct);

    public Task<List<SyncUpsertResultDto>> DeleteAssignmentsAsync(SyncDeleteRequest req, CancellationToken ct = default) =>
        ChiamaAsync<List<SyncUpsertResultDto>>(HttpMethod.Post, "/api/sync/assignments/delete", req, ct);

    // Le tre letture delle anagrafiche del VPS (Fase 1): servono al seme della mappa
    // dipendenti e ai controlli. Tornano TUTTO (anche i cessati e gli account «[…]»);
    // PasswordHash è sempre null: le credenziali dal VPS non escono mai.
    public Task<List<SyncEmployeeDto>> GetEmployeesAsync(CancellationToken ct = default) =>
        ChiamaAsync<List<SyncEmployeeDto>>(HttpMethod.Get, "/api/sync/employees", null, ct);

    public Task<List<SyncProjectDto>> GetProjectsAsync(CancellationToken ct = default) =>
        ChiamaAsync<List<SyncProjectDto>>(HttpMethod.Get, "/api/sync/projects", null, ct);

    public Task<SyncDepartmentsRequest> GetDepartmentsAsync(CancellationToken ct = default) =>
        ChiamaAsync<SyncDepartmentsRequest>(HttpMethod.Get, "/api/sync/departments", null, ct);

    public Task<List<SyncUpsertResultDto>> UpsertEmployeesAsync(List<SyncEmployeeDto> righe, CancellationToken ct = default) =>
        ChiamaAsync<List<SyncUpsertResultDto>>(HttpMethod.Put, "/api/sync/employees", righe, ct);

    public Task<SyncCountsDto> UpsertDepartmentsAsync(SyncDepartmentsRequest req, CancellationToken ct = default) =>
        ChiamaAsync<SyncCountsDto>(HttpMethod.Put, "/api/sync/departments", req, ct);

    public Task<List<SyncUpsertResultDto>> UpsertProjectsAsync(List<SyncProjectDto> righe, CancellationToken ct = default) =>
        ChiamaAsync<List<SyncUpsertResultDto>>(HttpMethod.Put, "/api/sync/projects", righe, ct);

    // ── Il giro di una chiamata: Bearer, 401 → login e riprova una volta ──

    private async Task<T> ChiamaAsync<T>(HttpMethod metodo, string percorso, object? corpo, CancellationToken ct)
    {
        string token = await TokenAsync(ct);

        using HttpResponseMessage prima = await InviaAsync(metodo, percorso, corpo, token, ct);
        if (prima.StatusCode != HttpStatusCode.Unauthorized)
            return await EstraiAsync<T>(prima, percorso, ct);

        // Token scaduto (o chiave JWT del VPS cambiata): un login nuovo e un solo altro tentativo.
        _logger.LogInformation("[RisorseSync] 401 su {Percorso}: rifaccio il login.", percorso);
        _token = null;
        token = await LoginAsync(ct);

        using HttpResponseMessage seconda = await InviaAsync(metodo, percorso, corpo, token, ct);
        if (seconda.StatusCode == HttpStatusCode.Unauthorized)
            throw new RisorseSyncException($"Il VPS rifiuta il token anche dopo un nuovo login ({percorso}).");
        return await EstraiAsync<T>(seconda, percorso, ct);
    }

    private async Task<HttpResponseMessage> InviaAsync(
        HttpMethod metodo, string percorso, object? corpo, string token, CancellationToken ct)
    {
        using var richiesta = new HttpRequestMessage(metodo, $"{_baseUrl}{percorso}");
        richiesta.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo != null)
            richiesta.Content = JsonContent.Create(corpo, options: JsonOptions);
        return await _http.SendAsync(richiesta, ct);
    }

    private static async Task<T> EstraiAsync<T>(HttpResponseMessage risposta, string percorso, CancellationToken ct)
    {
        // Prima il corpo, poi lo stato: il VPS risponde ApiResponse.Fail anche con 200, e su
        // un 4xx/5xx il messaggio dentro vale più del codice.
        ApiResponse<T>? api = await LeggiAsync<ApiResponse<T>>(risposta, percorso, ct);
        if (api == null)
            throw new RisorseSyncException(
                $"Risposta non leggibile dal VPS su {percorso} (HTTP {(int)risposta.StatusCode}).");
        if (!api.Success)
            throw new RisorseSyncException(
                string.IsNullOrWhiteSpace(api.Message) ? $"Il VPS ha risposto con un errore su {percorso}." : api.Message);
        if (api.Data == null)
            throw new RisorseSyncException($"Il VPS non ha restituito dati su {percorso}.");
        return api.Data;
    }

    /// <summary>
    /// Legge il corpo JSON. Se non è JSON (corpo vuoto, pagina HTML di un proxy, 502 di nginx,
    /// indirizzo che punta a un sito qualunque…) traduce lo stato HTTP in una frase per il
    /// pannello: la causa è quasi sempre indirizzo, credenziali o ruolo sbagliati.
    /// </summary>
    private static async Task<T?> LeggiAsync<T>(HttpResponseMessage risposta, string percorso, CancellationToken ct)
    {
        string? mediaType = risposta.Content.Headers.ContentType?.MediaType;
        bool eJson = mediaType != null
            && (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
        if (!eJson)
            throw new RisorseSyncException(MessaggioSenzaJson(risposta.StatusCode, percorso));

        try
        {
            return await risposta.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Dice JSON ma non lo è (o è un JSON che non è il nostro): stessa traduzione.
            throw new RisorseSyncException(MessaggioSenzaJson(risposta.StatusCode, percorso));
        }
    }

    /// <summary>La frase per una risposta senza JSON, in base allo stato HTTP.</summary>
    internal static string MessaggioSenzaJson(HttpStatusCode stato, string percorso) => stato switch
    {
        HttpStatusCode.Unauthorized => "Credenziali rifiutate dal VPS",
        HttpStatusCode.Forbidden => "L'account di servizio sul VPS non ha il ruolo richiesto (SYNC o ADMIN)",
        HttpStatusCode.NotFound => $"Percorso non trovato sul VPS ({percorso}): controllare l'indirizzo",
        HttpStatusCode.OK => "L'indirizzo del VPS non risponde con l'API (controllare l'indirizzo)",
        _ => $"Il VPS ha risposto HTTP {(int)stato} senza JSON su {percorso}",
    };
}
