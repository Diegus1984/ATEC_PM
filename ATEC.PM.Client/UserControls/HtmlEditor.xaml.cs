using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ATEC.PM.Client.UserControls;

public partial class HtmlEditor : UserControl
{
    private bool _isReady;
    private bool _isInitializing;
    private string _pendingContent = "";
    private Task? _initTask;
    private WebView2? _webView;

    /// <summary>Evento scatenato quando il contenuto HTML cambia nell'editor.</summary>
    public event Action<string>? ContentChanged;

    public HtmlEditor()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        Unloaded += (_, _) => TeardownWebView();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && !_isReady && !_isInitializing)
            _ = EnsureInitializedAsync();
    }

    /// <summary>
    /// Crea WebView2 e carica TinyMCE — solo al primo utilizzo (dialog visibile o SetContent/GetContent).
    /// </summary>
    public Task EnsureInitializedAsync()
    {
        if (_isReady)
            return Task.CompletedTask;

        if (_initTask != null)
            return _initTask;

        _initTask = InitWebViewCoreAsync();
        return _initTask;
    }

    private async Task InitWebViewCoreAsync()
    {
        if (_isReady || _isInitializing)
            return;

        _isInitializing = true;
        txtLoading.Visibility = Visibility.Visible;

        try
        {
            CoreWebView2Environment env = await WebView2Host.GetEnvironmentAsync().ConfigureAwait(true);

            WebView2 webView = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.White
            };
            editorHost.Children.Insert(0, webView);
            _webView = webView;

            await webView.EnsureCoreWebView2Async(env).ConfigureAwait(true);

            webView.CoreWebView2.WebMessageReceived += OnWebMessage;

            string htmlPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "Assets", "tinymce", "editor.html");

            if (!File.Exists(htmlPath))
            {
                txtLoading.Text = $"File editor non trovato: {htmlPath}";
                return;
            }

            long version = File.GetLastWriteTimeUtc(htmlPath).Ticks;
            string cacheBust = $"?v={version}";
            webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri + cacheBust);

            TaskCompletionSource<bool> navigationDone = new TaskCompletionSource<bool>();
            EventHandler<CoreWebView2NavigationCompletedEventArgs>? onNavCompleted = null;
            onNavCompleted = async (_, args) =>
            {
                if (webView.CoreWebView2 != null && onNavCompleted != null)
                    webView.CoreWebView2.NavigationCompleted -= onNavCompleted;

                if (!args.IsSuccess)
                {
                    txtLoading.Text = "Errore caricamento editor HTML.";
                    navigationDone.TrySetResult(false);
                    return;
                }

                CoreWebView2 core = webView.CoreWebView2
                    ?? throw new InvalidOperationException("WebView2 non inizializzato.");
                string escaped = JsonSerializer.Serialize(_pendingContent);
                string apiUrl = JsonSerializer.Serialize(App.ApiBaseUrl ?? "");
                string token = JsonSerializer.Serialize(App.Token ?? "");
                await core.ExecuteScriptAsync(
                    $"initEditor({escaped}, {apiUrl}, {token})").ConfigureAwait(true);

                _isReady = true;
                txtLoading.Visibility = Visibility.Collapsed;
                navigationDone.TrySetResult(true);
            };
            webView.CoreWebView2.NavigationCompleted += onNavCompleted;

            await navigationDone.Task.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            txtLoading.Text = $"Errore WebView2: {ex.Message}";
        }
        finally
        {
            _isInitializing = false;
        }
    }

    private void TeardownWebView()
    {
        _isReady = false;
        _isInitializing = false;
        _initTask = null;

        if (_webView == null)
            return;

        try
        {
            if (_webView.CoreWebView2 != null)
                _webView.CoreWebView2.WebMessageReceived -= OnWebMessage;
        }
        catch
        {
            // ignore during shutdown
        }

        editorHost.Children.Remove(_webView);
        _webView.Dispose();
        _webView = null;

        txtLoading.Visibility = Visibility.Visible;
        txtLoading.Text = "Caricamento editor...";
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            string json = e.WebMessageAsJson;
            JsonDocument doc = JsonDocument.Parse(json);
            string type = doc.RootElement.GetProperty("type").GetString() ?? "";

            if (type == "contentChanged")
            {
                string html = doc.RootElement.GetProperty("html").GetString() ?? "";
                ContentChanged?.Invoke(html);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[HtmlEditor] Errore parsing messaggio web: {ex.Message}");
        }
    }

    /// <summary>Imposta il contenuto HTML nell'editor.</summary>
    public void SetContent(string html)
    {
        _pendingContent = html ?? "";
        if (_isReady && _webView?.CoreWebView2 != null)
            _ = ApplyContentAsync();
        else if (IsVisible)
            _ = EnsureInitializedAsync();
    }

    private async Task ApplyContentAsync()
    {
        if (!_isReady || _webView?.CoreWebView2 == null)
            return;

        string escaped = JsonSerializer.Serialize(_pendingContent);
        await _webView.CoreWebView2.ExecuteScriptAsync($"setContent({escaped})").ConfigureAwait(true);
    }

    /// <summary>Ottiene il contenuto HTML corrente dall'editor.</summary>
    public async Task<string> GetContentAsync()
    {
        if (IsVisible && !_isReady)
            await EnsureInitializedAsync().ConfigureAwait(true);

        if (!_isReady || _webView?.CoreWebView2 == null)
            return _pendingContent;

        string result = await _webView.CoreWebView2.ExecuteScriptAsync("getContent()").ConfigureAwait(true);
        return JsonSerializer.Deserialize<string>(result) ?? "";
    }
}
