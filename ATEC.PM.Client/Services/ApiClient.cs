using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace ATEC.PM.Client.Services;

public static class ApiClient
{
    private static readonly HttpClient _http = new();

    public static async Task<string> PostLogin(string user, string pass)
    {
        var json = $"{{\"username\":\"{user}\",\"password\":\"{pass}\"}}";
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        var resp = await _http.PostAsync($"{App.ApiBaseUrl}/api/auth/login", content);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<string> GetAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}{endpoint}");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
        var resp = await _http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<string> PostAsync(string endpoint, string jsonBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{App.ApiBaseUrl}{endpoint}");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
        var resp = await _http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<string> PutAsync(string endpoint, string jsonBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Put, $"{App.ApiBaseUrl}{endpoint}");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
        var resp = await _http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<string> PatchAsync(string endpoint, string jsonBody)
    {
        var req = new HttpRequestMessage(HttpMethod.Patch, $"{App.ApiBaseUrl}{endpoint}");
        req.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
        var resp = await _http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<byte[]?> DownloadAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}{endpoint}");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadAsByteArrayAsync();
    }

    public static async Task<string> DeleteAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"{App.ApiBaseUrl}{endpoint}");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
        var resp = await _http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<string> GetRawAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}{endpoint}");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<string> UploadFileAsync(string endpoint, string filePath)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{App.ApiBaseUrl}{endpoint}");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);

        var content = new MultipartFormDataContent();
        var fileBytes = System.IO.File.ReadAllBytes(filePath);
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", System.IO.Path.GetFileName(filePath));
        req.Content = content;

        var resp = await _http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<string> UploadFilesAsync(string endpoint, string[] filePaths)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"{App.ApiBaseUrl}{endpoint}");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);

        var content = new MultipartFormDataContent();
        foreach (string path in filePaths)
        {
            var fileBytes = System.IO.File.ReadAllBytes(path);
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            content.Add(fileContent, "files", System.IO.Path.GetFileName(path));
        }
        req.Content = content;

        var resp = await _http.SendAsync(req);
        return await resp.Content.ReadAsStringAsync();
    }

    public static async Task<byte[]> GetBytesAsync(string endpoint)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"{App.ApiBaseUrl}{endpoint}");
        if (!string.IsNullOrEmpty(App.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Token);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsByteArrayAsync();
    }
}
