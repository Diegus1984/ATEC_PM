using System.Text;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Services;

/// <summary>URL e caricamento paginato via ApiClient tipizzato.</summary>
public static class PagedApiHelper
{
    public static string BuildUrl(string basePath, int page, int pageSize, IReadOnlyDictionary<string, string?>? query = null)
    {
        StringBuilder sb = new(basePath);
        sb.Append(basePath.Contains('?') ? '&' : '?');
        sb.Append($"page={page}&pageSize={pageSize}");
        if (query == null) return sb.ToString();
        foreach (KeyValuePair<string, string?> kv in query)
        {
            if (string.IsNullOrWhiteSpace(kv.Value)) continue;
            sb.Append('&').Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
        }
        return sb.ToString();
    }

    /// <summary>GET paginato: restituisce Data (PagedResult) se success.</summary>
    public static async Task<PagedResult<T>?> GetPageAsync<T>(string url)
    {
        ApiResponse<PagedResult<T>>? response = await ApiClient.GetApiAsync<PagedResult<T>>(url);
        if (response?.Success == true && response.Data != null)
            return response.Data;
        return null;
    }
}
