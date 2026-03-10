using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CodexPage : Page
{
    private List<CodexListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;
    private DispatcherTimer? _syncTimer;
    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    public CodexPage()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            await LoadSyncStatus();
            await Load();
        };
        Unloaded += (_, _) => _syncTimer?.Stop();
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/codex");
            var response = JsonSerializer.Deserialize<ApiResponse<List<CodexListItem>>>(json, _jsonOpt);

            if (response != null && response.Success)
            {
                _allItems = response.Data ?? new();
                ApplyFilter();
            }
            else
            {
                txtStatus.Text = "Nessun dato — eseguire una sincronizzazione";
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadSyncStatus()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/codex/sync-status");
            var response = JsonSerializer.Deserialize<ApiResponse<CodexSyncStatus>>(json, _jsonOpt);
            if (response?.Success == true && response.Data != null)
            {
                UpdateSyncStatusUI(response.Data);
            }
        }
        catch { /* ignora */ }
    }

    private void UpdateSyncStatusUI(CodexSyncStatus s)
    {
        if (s.IsSyncing)
        {
            txtSyncStatus.Text = "⟳ Sincronizzazione in corso...";
            txtSyncStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F79009"));
            btnSync.IsEnabled = false;
            StartPolling();
        }
        else
        {
            btnSync.IsEnabled = true;
            _syncTimer?.Stop();

            if (!string.IsNullOrEmpty(s.LastError))
            {
                txtSyncStatus.Text = $"✗ Errore: {s.LastError}";
                txtSyncStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F04438"));
            }
            else if (s.LastSync.HasValue)
            {
                txtSyncStatus.Text = $"✓ Ultimo sync: {s.LastSync.Value:dd/MM/yyyy HH:mm} — {s.TotalRows:N0} articoli";
                txtSyncStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12B76A"));
            }
            else
            {
                txtSyncStatus.Text = "Mai sincronizzato";
                txtSyncStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#667085"));
            }
        }
    }

    private void StartPolling()
    {
        if (_syncTimer != null) return;
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _syncTimer.Tick += async (_, _) =>
        {
            await LoadSyncStatus();
            // Se non sta più sincronizzando, ricarica i dati
            if (btnSync.IsEnabled)
            {
                _syncTimer.Stop();
                _syncTimer = null;
                await Load();
            }
        };
        _syncTimer.Start();
    }

    // ── FILTRI ────────────────────────────────────────────────────

    private void Filter_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag != null)
            _filterBoxes[tb.Tag.ToString()!] = tb;
    }

    private async void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, _filterCts.Token);
            ApplyFilter();
        }
        catch (TaskCanceledException) { }
    }

    private void ApplyFilter()
    {
        if (_allItems == null) return;

        string fCodice = _filterBoxes.GetValueOrDefault("Codice")?.Text.Trim().ToLower() ?? "";
        string fDescr = _filterBoxes.GetValueOrDefault("Descr")?.Text.Trim().ToLower() ?? "";
        string fForn = _filterBoxes.GetValueOrDefault("Fornitore")?.Text.Trim().ToLower() ?? "";
        string fProd = _filterBoxes.GetValueOrDefault("Produttore")?.Text.Trim().ToLower() ?? "";
        string fCat = _filterBoxes.GetValueOrDefault("Categoria")?.Text.Trim().ToLower() ?? "";

        var filtered = _allItems.Where(r =>
            (string.IsNullOrEmpty(fCodice) || (r.Codice?.ToLower().Contains(fCodice) ?? false)) &&
            (string.IsNullOrEmpty(fDescr) || (r.Descr?.ToLower().Contains(fDescr) ?? false)) &&
            (string.IsNullOrEmpty(fForn) || (r.Fornitore?.ToLower().Contains(fForn) ?? false)) &&
            (string.IsNullOrEmpty(fProd) || (r.Produttore?.ToLower().Contains(fProd) ?? false)) &&
            (string.IsNullOrEmpty(fCat) || (r.Categoria?.ToLower().Contains(fCat) ?? false))
        ).ToList();

        dgCodex.ItemsSource = filtered;
        txtStatus.Text = $"{filtered.Count:N0} articoli trovati su {_allItems.Count:N0}";
    }

    // ── AZIONI ────────────────────────────────────────────────────

    private async void BtnSync_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            btnSync.IsEnabled = false;
            txtSyncStatus.Text = "⟳ Avvio sincronizzazione...";
            string json = await ApiClient.PostAsync("/api/codex/sync", "{}");
            var response = JsonSerializer.Deserialize<ApiResponse<string>>(json, _jsonOpt);

            if (response?.Success == true)
                StartPolling();
            else
            {
                txtSyncStatus.Text = response?.Message ?? "Errore";
                btnSync.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            txtSyncStatus.Text = $"Errore: {ex.Message}";
            btnSync.IsEnabled = true;
        }
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tb in _filterBoxes.Values) tb.Clear();
        ApplyFilter();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadSyncStatus();
        await Load();
    }
}
