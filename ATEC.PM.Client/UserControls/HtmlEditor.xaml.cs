using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

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
            string wvDataFolder = Path.Combine(Path.GetTempPath(), "ATEC_PM_WebView2");
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: wvDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            // Pulisci cache per forzare reload di editor.html
            await webView.CoreWebView2.Profile.ClearBrowsingDataAsync(
                CoreWebView2BrowsingDataKinds.DiskCache | CoreWebView2BrowsingDataKinds.CacheStorage);

            webView.CoreWebView2.WebMessageReceived += OnWebMessage;

            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "tinymce", "editor.html");
            if (File.Exists(htmlPath))
            {
                string cacheBust = $"?v={DateTime.Now.Ticks}";
                webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri + cacheBust);
                webView.CoreWebView2.NavigationCompleted += async (_, _) =>
                {
                    // Passa contenuto, API base URL e token a TinyMCE
                    string escaped = JsonSerializer.Serialize(_pendingContent);
                    string apiUrl = JsonSerializer.Serialize(App.ApiBaseUrl ?? "");
                    string token = JsonSerializer.Serialize(App.Token ?? "");
                    await webView.CoreWebView2.ExecuteScriptAsync(
                        $"initEditor({escaped}, {apiUrl}, {token})");
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
        }
        catch { }
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
        return JsonSerializer.Deserialize<string>(result) ?? "";
    }
}
