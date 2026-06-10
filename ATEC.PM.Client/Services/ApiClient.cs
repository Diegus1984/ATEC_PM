using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ATEC.PM.Client.Views;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Services;

public static class ApiClient
{
    // NB: i timeout DEVONO essere dichiarati prima di _http
    // (static readonly inizializzati in ordine di dichiarazione)
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LongTimeout = TimeSpan.FromMinutes(5);

    private static readonly HttpClient _http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        HttpClientHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip
                | DecompressionMethods.Deflate
                | DecompressionMethods.Brotli
        };
        return new HttpClient(handler) { Timeout = DefaultTimeout };
    }

    /// <summary>Opzioni JSON condivise (property case-insensitive).</summary>
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // ── HELPER: gestione 401 (token scaduto) ────────────────────

    // Guard contro 401 concorrenti: se più chiamate API falliscono insieme
    // (token scaduto), solo la prima fa partire il flusso di re-login.
    private static int _handlingUnauthorized;

    private static async Task<string> HandleResponse(HttpResponseMessage resp)
    {
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            TriggerSessionExpiredUi();
            return "{}";
        }
        return await resp.Content.ReadAsStringAsync();
    }

    private static void TriggerSessionExpiredUi()
    {
        // Interlocked: la prima 401 vince, le successive escono senza riaprire dialog/finestre.
        if (Interlocked.CompareExchange(ref _handlingUnauthorized, 1, 0) != 0)
            return;

        Dispatcher dispatcher = App.Current.Dispatcher;
        // BeginInvoke (non Invoke): se HandleResponse viene awaitato dal thread UI,
        // Invoke andrebbe in deadlock. BeginInvoke accoda e ritorna subito.
        if (dispatcher.CheckAccess())
            ShowSessionExpiredUi();
        else
            dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(ShowSessionExpiredUi));
    }

    private static void ShowSessionExpiredUi()
    {
        App.Token = "";
        new LoginWindow().Show();
        // Chiude le altre finestre forzando il re-login. NB: eventuali modali
        // con lavoro non salvato vengono chiusi senza conferma (limite noto).
        foreach (Window w in App.Current.Windows)
            if (w is not LoginWindow) w.Close();

        ATEC.PM.Client.Controls.ShadcnMessageBox.Show("Sessione scaduta. Effettua nuovamente il login.",
            "Sessione Scaduta", MessageBoxButton.OK, MessageBoxImage.Warning);

        // Riarma il guard per il prossimo ciclo di sessione.
        Interlocked.Exchange(ref _handlingUnauthorized, 0);
    }

    private static void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
    }

    // ── LOGIN (senza token) ─────────────────────────────────────

    public static async Task<string> PostLogin(string user, string pass)
    {
        // JSON serializzato correttamente: username/password con " o \ verrebbero
        // altrimenti malformati (o sfruttabili per injection nel body).
        string json = JsonSerializer.Serialize(new { username = user, password = pass });
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{App.ApiBaseUrl}/api/auth/login", content);
        return await resp.Content.ReadAsStringAsync();
    }

    /// <summary>POST senza token (es. cambio password da schermata login).</summary>
    public static async Task<string> PostAnonymousAsync(string endpoint, string jsonBody)
    {
        StringContent content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        HttpResponseMessage resp = await _http.PostAsync($"{App.ApiBaseUrl}{endpoint}", content);
        return await resp.Content.ReadAsStringAsync();
    }

    // ── GET ─────────────────────────────────────────────────────

    public static async Task<string> GetAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}{endpoint}");
        AddAuth(req);
        var resp = await _http.SendAsync(req);
        return await HandleResponse(resp);
    }

    /// <summary>GET tipizzato → ApiResponse&lt;T&gt; (null se JSON non valido).</summary>
    public static async Task<ApiResponse<T>?> GetApiAsync<T>(string endpoint)
    {
        string json = await GetAsync(endpoint);
        return ParseApiResponse<T>(json);
    }

    /// <summary>GET → <c>Data</c> se <c>Success</c>, altrimenti default.</summary>
    public static async Task<T?> GetDataAsync<T>(string endpoint)
    {
        ApiResponse<T>? response = await GetApiAsync<T>(endpoint);
        if (response == null || !response.Success)
            return default;
        return response.Data;
    }

    /// <summary>Deserializza JSON envelope <c>{ success, data, message }</c>.</summary>
    public static ApiResponse<T>? ParseApiResponse<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
            return null;
        try
        {
            return JsonSerializer.Deserialize<ApiResponse<T>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsApiSuccess(string json, out string message)
    {
        message = "";
        ApiResponse<JsonElement>? response = ParseApiResponse<JsonElement>(json);
        if (response == null)
            return false;
        message = response.Message;
        return response.Success;
    }

    public static bool TryGetApiData<T>(string json, out T? data, out string message)
    {
        data = default;
        message = "";
        ApiResponse<T>? response = ParseApiResponse<T>(json);
        if (response == null)
            return false;
        message = response.Message;
        if (!response.Success)
            return false;
        data = response.Data;
        return true;
    }

    /// <summary>GET lista — mai null, array vuoto se errore.</summary>
    public static async Task<List<T>> GetListAsync<T>(string endpoint)
    {
        return await GetDataAsync<List<T>>(endpoint) ?? new List<T>();
    }

    public static async Task<byte[]> GetBytesAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}{endpoint}");
        AddAuth(req);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }

    // ── POST ────────────────────────────────────────────────────

    public static async Task<string> PostAsync(string endpoint, string jsonBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{App.ApiBaseUrl}{endpoint}");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        AddAuth(req);
        var resp = await _http.SendAsync(req);
        return await HandleResponse(resp);
    }

    // ── PUT ─────────────────────────────────────────────────────

    public static async Task<string> PutAsync(string endpoint, string jsonBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"{App.ApiBaseUrl}{endpoint}");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        AddAuth(req);
        var resp = await _http.SendAsync(req);
        return await HandleResponse(resp);
    }

    // ── PATCH ───────────────────────────────────────────────────

    public static async Task<string> PatchAsync(string endpoint, string jsonBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, $"{App.ApiBaseUrl}{endpoint}");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        AddAuth(req);
        var resp = await _http.SendAsync(req);
        return await HandleResponse(resp);
    }

    // ── DELETE ──────────────────────────────────────────────────

    public static async Task<string> DeleteAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"{App.ApiBaseUrl}{endpoint}");
        AddAuth(req);
        var resp = await _http.SendAsync(req);
        return await HandleResponse(resp);
    }

    // ── GET RAW ─────────────────────────────────────────────────

    public static async Task<string> GetRawAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}{endpoint}");
        AddAuth(req);
        var resp = await _http.SendAsync(req);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            await HandleResponse(resp);
            throw new UnauthorizedAccessException("Sessione scaduta");
        }
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    // ── DOWNLOAD (bytes) ────────────────────────────────────────

    public static async Task<byte[]?> DownloadAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}{endpoint}");
        AddAuth(req);
        using CancellationTokenSource cts = new(LongTimeout);
        var resp = await _http.SendAsync(req, cts.Token);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            await HandleResponse(resp);
            return null;
        }
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsByteArrayAsync(cts.Token);
    }

    // ── UPLOAD FILE ─────────────────────────────────────────────

    public static async Task<string> UploadFileAsync(string endpoint, string filePath)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{App.ApiBaseUrl}{endpoint}");
        AddAuth(req);

        var content = new MultipartFormDataContent();
        var fileBytes = System.IO.File.ReadAllBytes(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", System.IO.Path.GetFileName(filePath));
        req.Content = content;

        using CancellationTokenSource cts = new(LongTimeout);
        var resp = await _http.SendAsync(req, cts.Token);
        return await HandleResponse(resp);
    }

    // ── UPLOAD FILES (multipli) ─────────────────────────────────

    public static async Task<string> UploadFilesAsync(string endpoint, string[] filePaths)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{App.ApiBaseUrl}{endpoint}");
        AddAuth(req);

        var content = new MultipartFormDataContent();
        foreach (string path in filePaths)
        {
            var fileBytes = System.IO.File.ReadAllBytes(path);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "files", System.IO.Path.GetFileName(path));
        }
        req.Content = content;

        using CancellationTokenSource cts = new(LongTimeout);
        var resp = await _http.SendAsync(req, cts.Token);
        return await HandleResponse(resp);
    }
}
