using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using ATEC.PM.Client.Services;

namespace ATEC.PM.Client.UserControls;

public partial class HtmlEditor : UserControl
{
    private bool _isReady;
    private string _pendingContent = "";

    /// <summary>Evento scatenato quando il contenuto HTML cambia nell'editor.</summary>
    public event Action<string>? ContentChanged;

    public HtmlEditor()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitWebView();
    }

    private async System.Threading.Tasks.Task InitWebView()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(
                userDataFolder: Path.Combine(Path.GetTempPath(), "ATEC_PM_WebView2"));
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.WebMessageReceived += OnWebMessage;

            // Carica l'HTML dell'editor
            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tinymce", "editor.html");
            if (File.Exists(htmlPath))
            {
                webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
                webView.CoreWebView2.NavigationCompleted += async (_, _) =>
                {
                    // Inizializza TinyMCE con il contenuto pendente
                    string escaped = JsonSerializer.Serialize(_pendingContent);
                    await webView.CoreWebView2.ExecuteScriptAsync($"initEditor({escaped})");
                    _isReady = true;
                    txtLoading.Visibility = Visibility.Collapsed;
                };
            }
            else
            {
                txtLoading.Text = $"File editor non trovato: {htmlPath}";
            }
        }
        catch (Exception ex)
        {
            txtLoading.Text = $"Errore WebView2: {ex.Message}";
        }
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            var doc = JsonDocument.Parse(json);
            string type = doc.RootElement.GetProperty("type").GetString() ?? "";

            if (type == "contentChanged")
            {
                string html = doc.RootElement.GetProperty("html").GetString() ?? "";
                ContentChanged?.Invoke(html);
            }
            else if (type == "imageUpload")
            {
                string uploadId = doc.RootElement.GetProperty("uploadId").GetString() ?? "";
                string fileName = doc.RootElement.GetProperty("fileName").GetString() ?? "image.png";
                string base64 = doc.RootElement.GetProperty("base64").GetString() ?? "";
                _ = HandleImageUpload(uploadId, fileName, base64);
            }
        }
        catch { }
    }

    private async System.Threading.Tasks.Task HandleImageUpload(string uploadId, string fileName, string base64)
    {
        try
        {
            // Salva base64 come file temporaneo
            byte[] bytes = Convert.FromBase64String(base64);
            string tempDir = Path.Combine(Path.GetTempPath(), "ATEC_PM_Uploads");
            Directory.CreateDirectory(tempDir);
            string tempPath = Path.Combine(tempDir, fileName);
            await File.WriteAllBytesAsync(tempPath, bytes);

            // Upload sul server
            string json = await ApiClient.UploadFileAsync("/api/quote-catalog/products/upload", tempPath);
            var response = JsonSerializer.Deserialize<JsonElement>(json);

            bool success = response.TryGetProperty("success", out var sp) && sp.GetBoolean();
            if (success && response.TryGetProperty("data", out var dp))
            {
                // Costruisci URL completo per l'immagine
                string relativePath = dp.GetString() ?? "";
                string fullUrl = $"{App.ApiBaseUrl}{relativePath}";

                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"onImageUploaded({JsonSerializer.Serialize(uploadId)}, {JsonSerializer.Serialize(fullUrl)})");
            }
            else
            {
                string error = response.TryGetProperty("message", out var mp) ? mp.GetString() ?? "Errore upload" : "Errore upload";
                await webView.CoreWebView2.ExecuteScriptAsync(
                    $"onImageUploadFailed({JsonSerializer.Serialize(uploadId)}, {JsonSerializer.Serialize(error)})");
            }

            // Pulisci file temporaneo
            try { File.Delete(tempPath); } catch { }
        }
        catch (Exception ex)
        {
            await webView.CoreWebView2.ExecuteScriptAsync(
                $"onImageUploadFailed({JsonSerializer.Serialize(uploadId)}, {JsonSerializer.Serialize(ex.Message)})");
        }
    }

    /// <summary>Imposta il contenuto HTML nell'editor.</summary>
    public async void SetContent(string html)
    {
        _pendingContent = html ?? "";
        if (_isReady)
        {
            string escaped = JsonSerializer.Serialize(_pendingContent);
            await webView.CoreWebView2.ExecuteScriptAsync($"setContent({escaped})");
        }
    }

    /// <summary>Ottiene il contenuto HTML corrente dall'editor.</summary>
    public async System.Threading.Tasks.Task<string> GetContentAsync()
    {
        if (!_isReady) return _pendingContent;
        string result = await webView.CoreWebView2.ExecuteScriptAsync("getContent()");
        // Il risultato è una stringa JSON-escaped
        return JsonSerializer.Deserialize<string>(result) ?? "";
    }
}
