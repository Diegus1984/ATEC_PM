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
}
