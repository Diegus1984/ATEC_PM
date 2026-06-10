using System;
using System.Collections.ObjectModel;
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
    private const int PageSize = 50;

    private ObservableCollection<CodexListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;
    private DispatcherTimer? _syncTimer;
    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    private int _loadedPage;
    private int _totalCount;
    private bool _hasMore;
    private bool _loading;
    private InfiniteScrollHelper? _infiniteScroll;

    private static readonly Dictionary<string, string> CodexFilterQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Codice"] = "codice",
        ["Descr"] = "descr",
        ["CodeForn"] = "codeForn",
        ["Fornitore"] = "fornitore",
        ["PrezzoForn"] = "prezzoForn",
        ["Iva"] = "iva",
        ["Produttore"] = "produttore",
        ["Data"] = "data",
        ["Categoria"] = "categoria",
        ["Barcode"] = "barcode",
        ["Tipologia"] = "tipologia",
        ["Extra1"] = "extra1",
        ["Extra2"] = "extra2",
        ["Extra3"] = "extra3",
        ["CodeProd"] = "codeProd",
        ["Spec"] = "spec",
        ["Oper"] = "oper",
        ["Um"] = "um",
        ["Ubicazione"] = "ubicazione",
        ["Codexforn"] = "codexforn",
        ["Note"] = "note"
    };

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
        _infiniteScroll = new InfiniteScrollHelper(
            () => _hasMore && !_loading,
            () => Load(append: true));
        _infiniteScroll.Attach(dgCodex);
        InitColumnDefs();
        LoadColumnSettings();
        BuildColumnCheckboxes();

        // Ricerca lazy nelle combo riferimento 201/401
        cmbRef201.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => FilterRefCombo(cmbRef201, "2")));
        cmbRef401.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new TextChangedEventHandler((_, _) => FilterRefCombo(cmbRef401, "4")));

        Loaded += async (_, _) =>
        {
            // Colonna azioni e pulsante genera solo per admin
            bool isAdmin = App.CurrentUser.IsAdmin;
            colActions.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
            btnGenerate.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

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
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading column settings: {ex}");
        }

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
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error saving column settings: {ex}");
        }
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

    private async Task Load(bool append = false)
    {
        if (_loading) return;
        _loading = true;
        if (!append)
        {
            _loadedPage = 0;
            _allItems.Clear();
        }

        int page = append ? _loadedPage + 1 : 1;
        string url = PagedApiHelper.BuildUrl("/api/codex", page, PageSize, BuildCodexQuery());
        txtStatus.Text = append ? "Caricamento altri articoli..." : "Caricamento...";
        try
        {
            PagedResult<CodexListItem>? pageData = await PagedApiHelper.GetPageAsync<CodexListItem>(url);
            if (pageData != null)
            {
                _loadedPage = pageData.Page;
                _totalCount = pageData.TotalCount;
                _hasMore = pageData.HasMore;
                MergePageItems(pageData.Items, append);
                UpdateListStatus();
                _infiniteScroll?.NotifyContentUpdated(dgCodex);
            }
            else
                txtStatus.Text = "Nessun dato — eseguire una sincronizzazione";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally
        {
            _loading = false;
        }
    }

    private Dictionary<string, string?> BuildCodexQuery()
    {
        Dictionary<string, string?> query = new();
        foreach (KeyValuePair<string, string> kv in CodexFilterQueryKeys)
        {
            string val = F(kv.Key);
            if (!string.IsNullOrEmpty(val))
                query[kv.Value] = val;
        }
        return query;
    }

    private void UpdateListStatus()
    {
        if (_totalCount <= 0)
            txtStatus.Text = "Nessun articolo";
        else if (_allItems.Count >= _totalCount)
            txtStatus.Text = $"{_totalCount:N0} articoli";
        else
            txtStatus.Text = $"{_allItems.Count:N0} di {_totalCount:N0} articoli";
    }

    /// <summary>Aggiorna la griglia con notifiche CollectionChanged (no AddRange su List).</summary>
    private void MergePageItems(IReadOnlyList<CodexListItem> items, bool append)
    {
        if (!append)
            _allItems.Clear();
        foreach (CodexListItem item in items)
            _allItems.Add(item);
        if (dgCodex.ItemsSource != _allItems)
            dgCodex.ItemsSource = _allItems;
    }

    private async Task LoadSyncStatus()
    {
        try
        {
            CodexSyncStatus? status = await ApiClient.GetDataAsync<CodexSyncStatus>("/api/codex/sync-status");
            if (status != null)
                UpdateSyncStatusUI(status);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error loading sync status: {ex}");
        }
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
            await Load(append: false);
        }
        catch (TaskCanceledException) { }
    }

    private string F(string tag) =>
        _filterBoxes.GetValueOrDefault(tag)?.Text.Trim().ToLower() ?? "";

    private static bool Match(string? value, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        string v = value?.ToLower() ?? "";
        bool startsWild = filter.StartsWith('*');
        bool endsWild = filter.EndsWith('*');
        if (startsWild && endsWild) return v.Contains(filter.Trim('*'));
        if (endsWild) return v.StartsWith(filter.TrimEnd('*'));
        if (startsWild) return v.EndsWith(filter.TrimStart('*'));
        return v.Contains(filter);
    }

    // ── GENERA CODICE INLINE ──────────────────────────────────────

    private int? _currentReservationId;
    private string _currentReservedCode = "";

    private async void BtnGenerate_Click(object sender, RoutedEventArgs e)
    {
        pnlGenerate.Visibility = Visibility.Visible;
        btnGenerate.IsEnabled = false;
        txtGenerateError.Text = "";
        txtDescrizione.Text = "";
        cmbPrefisso.SelectedIndex = -1;
        brdPreview.Visibility = Visibility.Collapsed;
        btnGenerateConfirm.IsEnabled = false;
        await LoadPrefixes();
    }

    private async Task LoadPrefixes()
    {
        try
        {
            List<CodexPrefix> prefixes = await ApiClient.GetListAsync<CodexPrefix>("/api/codex/prefixes");
            if (prefixes.Count > 0)
            {
                cmbPrefisso.ItemsSource = prefixes;
                cmbPrefisso.DisplayMemberPath = "Display";
                cmbPrefisso.SelectedValuePath = "Codice";
            }
        }
        catch (Exception ex) { txtGenerateError.Text = $"Errore caricamento prefissi: {ex.Message}"; }
    }

    private async void CmbPrefisso_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        txtGenerateError.Text = "";
        await ReleaseCurrentReservation();

        if (cmbPrefisso.SelectedValue == null)
        {
            brdPreview.Visibility = Visibility.Collapsed;
            btnGenerateConfirm.IsEnabled = false;
            pnlReferences.Visibility = Visibility.Collapsed;
            return;
        }

        // Mostra campi riferimento solo per prefisso 101
        string selectedPrefix = cmbPrefisso.SelectedValue.ToString() ?? "";
        bool is101 = selectedPrefix == "101";
        pnlReferences.Visibility = is101 ? Visibility.Visible : Visibility.Collapsed;

        if (is101)
        {
            // Combo vuote — si popolano digitando (filtro lazy)
            cmbRef201.ItemsSource = null;
            cmbRef201.SelectedIndex = -1;
            cmbRef201.Text = "";
            cmbRef401.ItemsSource = null;
            cmbRef401.SelectedIndex = -1;
            cmbRef401.Text = "";
        }

        try
        {
            string prefisso = selectedPrefix;
            var req = new CodexReserveRequest { Prefisso = prefisso };
            string jsonBody = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync("/api/codex/reserve", jsonBody);

            var response = JsonSerializer.Deserialize<ApiResponse<CodexReservationResult>>(result, _jsonOpt);
            if (response?.Success == true && response.Data != null)
            {
                _currentReservationId = response.Data.ReservationId;
                _currentReservedCode = response.Data.Codice;
                txtPreviewCode.Text = _currentReservedCode;
                brdPreview.Visibility = Visibility.Visible;
                btnGenerateConfirm.IsEnabled = true;
                txtDescrizione.Focus();
            }
            else
            {
                txtGenerateError.Text = response?.Message ?? "Errore prenotazione";
                brdPreview.Visibility = Visibility.Collapsed;
                btnGenerateConfirm.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            txtGenerateError.Text = $"Errore: {ex.Message}";
            brdPreview.Visibility = Visibility.Collapsed;
            btnGenerateConfirm.IsEnabled = false;
        }
    }

    private async void BtnGenerateConfirm_Click(object sender, RoutedEventArgs e)
    {
        txtGenerateError.Text = "";

        if (_currentReservationId == null)
        {
            txtGenerateError.Text = "Nessun codice prenotato";
            return;
        }

        if (string.IsNullOrWhiteSpace(txtDescrizione.Text))
        {
            txtGenerateError.Text = "Inserisci una descrizione";
            return;
        }

        btnGenerateConfirm.IsEnabled = false;
        try
        {
            var req = new CodexConfirmRequest
            {
                ReservationId = _currentReservationId.Value,
                Descrizione = txtDescrizione.Text.Trim()
            };

            string jsonBody = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PostAsync("/api/codex/confirm", jsonBody);

            var response = JsonSerializer.Deserialize<ApiResponse<CodexGeneratedCode>>(result, _jsonOpt);
            if (response?.Success == true && response.Data != null)
            {
                string code = response.Data.Codice;
                int id = response.Data.Id;
                _currentReservationId = null;

                // Salva riferimenti 201/401 se presenti (solo per 101)
                await SaveReferencesIfNeeded(id);

                // Chiudi pannello e mostra successo
                CloseGeneratePanel();
                txtStatus.Text = $"✓ Codice {code} generato con successo (ID: {id})";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12B76A"));
                await Load();
            }
            else
            {
                txtGenerateError.Text = response?.Message ?? "Errore nella conferma";
                btnGenerateConfirm.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            txtGenerateError.Text = $"Errore: {ex.Message}";
            btnGenerateConfirm.IsEnabled = true;
        }
    }

    private async void BtnGenerateCancel_Click(object sender, RoutedEventArgs e)
    {
        await ReleaseCurrentReservation();
        CloseGeneratePanel();
    }

    private void CloseGeneratePanel()
    {
        pnlGenerate.Visibility = Visibility.Collapsed;
        btnGenerate.IsEnabled = true;
    }

    private async Task ReleaseCurrentReservation()
    {
        if (_currentReservationId == null) return;
        try { await ApiClient.PostAsync($"/api/codex/release/{_currentReservationId.Value}", "{}"); }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Error releasing current reservation: {ex}");
        }
        _currentReservationId = null;
        _currentReservedCode = "";
    }

    private bool _suppressRefFilter;

    private void FilterRefCombo(ComboBox cmb, string prefix)
    {
        if (_suppressRefFilter) return;
        if (cmb.SelectedItem != null) return;

        var tb = cmb.Template.FindName("PART_EditableTextBox", cmb) as System.Windows.Controls.TextBox;
        string search = tb?.Text?.Trim().ToLower() ?? "";
        int caretPos = tb?.CaretIndex ?? 0;

        if (search.Length < 2)
        {
            _suppressRefFilter = true;
            cmb.ItemsSource = null;
            cmb.IsDropDownOpen = false;
            _suppressRefFilter = false;
            return;
        }

        var filtered = _allItems
            .Where(i => i.Codice.StartsWith(prefix))
            .Where(i => Match(i.Codice, search) || Match(i.Descr, search))
            .Take(50)
            .Select(i => new { i.Id, Display = $"{i.Codice} — {i.Descr}" })
            .ToList();

        _suppressRefFilter = true;
        cmb.ItemsSource = filtered;
        cmb.IsDropDownOpen = filtered.Count > 0;

        // Ripristina testo e cursore
        if (tb != null)
        {
            tb.Text = search;
            tb.CaretIndex = caretPos;
        }
        _suppressRefFilter = false;
    }

    private async Task SaveReferencesIfNeeded(int codexId)
    {
        var jsonOpts = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        // Rif. 201 (commerciale)
        if (cmbRef201.SelectedItem != null)
        {
            dynamic sel201 = cmbRef201.SelectedItem;
            var req201 = new AddCodexReferenceRequest
            {
                SourceCodexId = codexId,
                RefCodexId = (int)sel201.Id,
                RefType = "201"
            };
            try { await ApiClient.PostAsync("/api/codex/references", JsonSerializer.Serialize(req201, jsonOpts)); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error saving Codex 201 reference: {ex}");
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore durante il salvataggio del riferimento 201: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Rif. 401 (materia prima)
        if (cmbRef401.SelectedItem != null)
        {
            dynamic sel401 = cmbRef401.SelectedItem;
            var req401 = new AddCodexReferenceRequest
            {
                SourceCodexId = codexId,
                RefCodexId = (int)sel401.Id,
                RefType = "401"
            };
            try { await ApiClient.PostAsync("/api/codex/references", JsonSerializer.Serialize(req401, jsonOpts)); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"Error saving Codex 401 reference: {ex}");
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore durante il salvataggio del riferimento 401: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

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
        foreach (TextBox tb in _filterBoxes.Values) tb.Clear();
        _ = Load(append: false);
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadSyncStatus();
        await Load(append: false);
    }

    // ── MODIFICA / ELIMINA PER RIGA ─────────────────────────────

    private int _editId;

    private void BtnEditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CodexListItem item) return;

        _editId = item.Id;
        txtEditCode.Text = item.Codice;
        txtEditDescr.Text = item.Descr;
        txtEditError.Text = "";
        pnlEdit.Visibility = Visibility.Visible;
        txtEditDescr.Focus();
    }

    private async void BtnSaveEdit_Click(object sender, RoutedEventArgs e)
    {
        txtEditError.Text = "";

        if (string.IsNullOrWhiteSpace(txtEditDescr.Text))
        {
            txtEditError.Text = "La descrizione non può essere vuota";
            return;
        }

        btnSaveEdit.IsEnabled = false;
        try
        {
            var req = new CodexUpdateRequest { Descrizione = txtEditDescr.Text.Trim() };
            string jsonBody = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string result = await ApiClient.PutAsync($"/api/codex/{_editId}", jsonBody);

            var response = JsonSerializer.Deserialize<ApiResponse<int>>(result, _jsonOpt);
            if (response?.Success == true)
            {
                pnlEdit.Visibility = Visibility.Collapsed;
                txtStatus.Text = $"✓ Articolo {txtEditCode.Text} aggiornato";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12B76A"));
                await Load();
            }
            else
            {
                txtEditError.Text = response?.Message ?? "Errore nel salvataggio";
            }
        }
        catch (Exception ex)
        {
            txtEditError.Text = $"Errore: {ex.Message}";
        }
        btnSaveEdit.IsEnabled = true;
    }

    private void BtnCancelEdit_Click(object sender, RoutedEventArgs e)
    {
        pnlEdit.Visibility = Visibility.Collapsed;
    }

    private async void BtnDeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not CodexListItem item) return;

        var result = ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
            $"Eliminare definitivamente l'articolo {item.Codice}?\n\n\"{item.Descr}\"\n\nQuesta operazione non è reversibile.",
            "Conferma eliminazione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            string json = await ApiClient.DeleteAsync($"/api/codex/{item.Id}");
            var response = JsonSerializer.Deserialize<ApiResponse<bool>>(json, _jsonOpt);
            if (response?.Success == true)
            {
                txtStatus.Text = $"✓ Articolo {item.Codice} eliminato";
                txtStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12B76A"));
                await Load();
            }
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(response?.Message ?? "Errore", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
