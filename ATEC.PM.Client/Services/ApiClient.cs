using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Windows;
using ATEC.PM.Client.Views;

namespace ATEC.PM.Client.Services;

public static class ApiClient
{
    private static readonly HttpClient _http = new();

    // ── HELPER: gestione 401 (token scaduto) ────────────────────

    private static async Task<string> HandleResponse(HttpResponseMessage resp)
    {
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            App.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show("Sessione scaduta. Effettua nuovamente il login.",
                    "Sessione Scaduta", MessageBoxButton.OK, MessageBoxImage.Warning);
                App.Token = "";
                new LoginWindow().Show();
                foreach (Window w in App.Current.Windows)
                    if (w is not LoginWindow) w.Close();
            });
            return "{}";
        }
        return await resp.Content.ReadAsStringAsync();
    }

    private static void AddAuth(HttpRequestMessage req)
    {
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
    }

    // ── LOGIN (senza token) ─────────────────────────────────────

    public static async Task<string> PostLogin(string user, string pass)
    {
        var json = $"{{\"username\":\"{user}\",\"password\":\"{pass}\"}}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{App.ApiBaseUrl}/api/auth/login", content);
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
        var resp = await _http.SendAsync(req);
        if (resp.StatusCode == HttpStatusCode.Unauthorized)
        {
            await HandleResponse(resp);
            return null;
        }
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsByteArrayAsync();
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

        var resp = await _http.SendAsync(req);
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

        var resp = await _http.SendAsync(req);
        return await HandleResponse(resp);
    }
}
