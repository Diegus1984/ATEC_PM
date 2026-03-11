using System;
using System.Collections.Generic;
using System.IO;
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

    // Mappa colonna → (x:Name, label per checkbox)
    private readonly List<(string Key, string Label, DataGridColumn Column)> _columnDefs = new();
    private Dictionary<string, bool> _columnVisibility = new();
    private bool _suppressSave = false;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ATEC_PM", "codex_columns.json");

    public CodexPage()
    {
        InitializeComponent();
        InitColumnDefs();
        LoadColumnSettings();
        BuildColumnCheckboxes();

        Loaded += async (_, _) =>
        {
            await LoadSyncStatus();
            await Load();
            btnColumnToggle.Unchecked += (_, _) => { }; // Popup si chiude da solo con StaysOpen=False
        };
        Unloaded += (_, _) => _syncTimer?.Stop();
    }

    // ── COLUMN VISIBILITY ─────────────────────────────────────────

    private void InitColumnDefs()
    {
        _columnDefs.Add(("Codice", "Codice", colCodice));
        _columnDefs.Add(("Descr", "Descrizione", colDescr));
        _columnDefs.Add(("CodeForn", "Cod. Forn.", colCodeForn));
        _columnDefs.Add(("Fornitore", "Fornitore", colFornitore));
        _columnDefs.Add(("PrezzoForn", "Prezzo €", colPrezzoForn));
        _columnDefs.Add(("Iva", "IVA", colIva));
        _columnDefs.Add(("Produttore", "Produttore", colProduttore));
        _columnDefs.Add(("Data", "Data", colData));
        _columnDefs.Add(("Categoria", "Categoria", colCategoria));
        _columnDefs.Add(("Barcode", "Barcode", colBarcode));
        _columnDefs.Add(("Tipologia", "Tipologia", colTipologia));
        _columnDefs.Add(("Extra1", "Extra1", colExtra1));
        _columnDefs.Add(("Extra2", "Extra2", colExtra2));
        _columnDefs.Add(("Extra3", "Extra3", colExtra3));
        _columnDefs.Add(("CodeProd", "Cod. Prod.", colCodeProd));
        _columnDefs.Add(("Spec", "Spec", colSpec));
        _columnDefs.Add(("Oper", "Oper", colOper));
        _columnDefs.Add(("Um", "UM", colUm));
        _columnDefs.Add(("Ubicazione", "Ubicazione", colUbicazione));
        _columnDefs.Add(("Codexforn", "Codex Forn.", colCodexforn));
        _columnDefs.Add(("Note", "Note", colNote));
    }

    private void LoadColumnSettings()
    {
        // Default: colonne principali visibili, extra nascoste
        HashSet<string> defaultHidden = new()
        {
            "Barcode", "Extra1", "Extra2", "Extra3", "Spec", "Oper", "Codexforn"
        };

        _columnVisibility = _columnDefs.ToDictionary(
            c => c.Key,
            c => !defaultHidden.Contains(c.Key));

        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var saved = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                if (saved != null)
                {
                    foreach (var kv in saved)
                    {
                        if (_columnVisibility.ContainsKey(kv.Key))
                            _columnVisibility[kv.Key] = kv.Value;
                    }
                }
            }
        }
        catch { /* ignora file corrotto, usa default */ }

        ApplyColumnVisibility();
    }

    private void SaveColumnSettings()
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsPath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(_columnVisibility, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { /* silenzioso */ }
    }

    private void BuildColumnCheckboxes()
    {
        _suppressSave = true;
        wpColumns.Children.Clear();

        foreach (var (key, label, _) in _columnDefs)
        {
            var cb = new CheckBox
            {
                Content = label,
                IsChecked = _columnVisibility.GetValueOrDefault(key, true),
                Tag = key,
                Style = (Style)FindResource("ColumnCheckBox")
            };
            cb.Checked += ColumnCheckbox_Changed;
            cb.Unchecked += ColumnCheckbox_Changed;
            wpColumns.Children.Add(cb);
        }

        _suppressSave = false;
    }

    private void ColumnCheckbox_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressSave) return;
        if (sender is CheckBox cb && cb.Tag is string key)
        {
            _columnVisibility[key] = cb.IsChecked == true;
            ApplyColumnVisibility();
            SaveColumnSettings();
        }
    }

    private void ApplyColumnVisibility()
    {
        foreach (var (key, _, col) in _columnDefs)
        {
            bool visible = _columnVisibility.GetValueOrDefault(key, true);
            col.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // ── LOAD DATA ─────────────────────────────────────────────────

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
                UpdateSyncStatusUI(response.Data);
        }
        catch { }
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

    private string F(string tag) =>
        _filterBoxes.GetValueOrDefault(tag)?.Text.Trim().ToLower() ?? "";

    private static bool Match(string? value, string filter) =>
        string.IsNullOrEmpty(filter) || (value?.ToLower().Contains(filter) ?? false);

    private void ApplyFilter()
    {
        if (_allItems == null) return;

        string fCodice = F("Codice");
        string fDescr = F("Descr");
        string fCodeForn = F("CodeForn");
        string fFornitore = F("Fornitore");
        string fPrezzoForn = F("PrezzoForn");
        string fIva = F("Iva");
        string fProduttore = F("Produttore");
        string fData = F("Data");
        string fCategoria = F("Categoria");
        string fBarcode = F("Barcode");
        string fTipologia = F("Tipologia");
        string fExtra1 = F("Extra1");
        string fExtra2 = F("Extra2");
        string fExtra3 = F("Extra3");
        string fCodeProd = F("CodeProd");
        string fSpec = F("Spec");
        string fOper = F("Oper");
        string fUm = F("Um");
        string fUbicazione = F("Ubicazione");
        string fCodexforn = F("Codexforn");
        string fNote = F("Note");

        var filtered = _allItems.Where(r =>
            Match(r.Codice, fCodice) &&
            Match(r.Descr, fDescr) &&
            Match(r.CodeForn, fCodeForn) &&
            Match(r.Fornitore, fFornitore) &&
            Match(r.PrezzoForn.ToString("N2"), fPrezzoForn) &&
            Match(r.Iva, fIva) &&
            Match(r.Produttore, fProduttore) &&
            Match(r.Data.ToString("dd/MM/yyyy"), fData) &&
            Match(r.Categoria, fCategoria) &&
            Match(r.Barcode, fBarcode) &&
            Match(r.Tipologia, fTipologia) &&
            Match(r.Extra1, fExtra1) &&
            Match(r.Extra2, fExtra2) &&
            Match(r.Extra3, fExtra3) &&
            Match(r.CodeProd, fCodeProd) &&
            Match(r.Spec, fSpec) &&
            Match(r.Oper.ToString(), fOper) &&
            Match(r.Um, fUm) &&
            Match(r.Ubicazione, fUbicazione) &&
            Match(r.Codexforn, fCodexforn) &&
            Match(r.Note, fNote)
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
