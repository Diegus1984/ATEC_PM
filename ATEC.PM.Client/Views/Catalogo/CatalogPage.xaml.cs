using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CatalogPage : Page
{
    private const int PageSize = 50;

    private ObservableCollection<CatalogItemListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private Dictionary<string, ComboBox> _comboFilterBoxes = new();
    private CancellationTokenSource? _filterCts;
    private int _loadedPage;
    private int _totalCount;
    private bool _hasMore;
    private bool _loading;
    private bool _filterMetaLoaded;
    private InfiniteScrollHelper? _infiniteScroll;

    private readonly List<(string Key, string Label, DataGridColumn Column)> _columnDefs = new();
    private Dictionary<string, bool> _columnVisibility = new();
    private bool _suppressSave = false;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ATEC_PM", "catalog_columns.json");

    public CatalogPage()
    {
        InitializeComponent();
        _infiniteScroll = new InfiniteScrollHelper(
            () => _hasMore && !_loading,
            () => Load(append: true));
        _infiniteScroll.Attach(dgCatalog);
        InitColumnDefs();
        LoadColumnSettings();
        BuildColumnCheckboxes();
        syncBar.SyncCompleted += async () => await Load(append: false);
        Loaded += async (_, _) => await Load(append: false);
    }

    // ── COLUMN VISIBILITY ─────────────────────────────────────────

    private void InitColumnDefs()
    {
        _columnDefs.Add(("Code", "Codice", colCode));
        _columnDefs.Add(("Description", "Descrizione", colDescription));
        _columnDefs.Add(("Supplier", "Fornitore", colSupplier));
        _columnDefs.Add(("Manufacturer", "Produttore", colManufacturer));
        _columnDefs.Add(("Category", "Categoria", colCategory));
        _columnDefs.Add(("Unit", "UdM", colUnit));
        _columnDefs.Add(("UnitCost", "Acquisto", colUnitCost));
        _columnDefs.Add(("ListPrice", "Listino", colListPrice));
    }

    private void LoadColumnSettings()
    {
        _columnVisibility = _columnDefs.ToDictionary(c => c.Key, c => true);

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
        Dictionary<string, string?> query = BuildCatalogQuery();
        string url = PagedApiHelper.BuildUrl("/api/catalog", page, PageSize, query);

        txtStatus.Text = append ? "Caricamento altri articoli..." : "Caricamento...";
        try
        {
            if (!_filterMetaLoaded)
                await LoadFilterMetaAsync();

            PagedResult<CatalogItemListItem>? pageData = await PagedApiHelper.GetPageAsync<CatalogItemListItem>(url);
            if (pageData != null)
            {
                _loadedPage = pageData.Page;
                _totalCount = pageData.TotalCount;
                _hasMore = pageData.HasMore;
                if (!append)
                    _allItems.Clear();
                foreach (CatalogItemListItem item in pageData.Items)
                    _allItems.Add(item);
                if (dgCatalog.ItemsSource != _allItems)
                    dgCatalog.ItemsSource = _allItems;
                UpdateListStatus();
                _infiniteScroll?.NotifyContentUpdated(dgCatalog);
            }
            else
                txtStatus.Text = "Errore caricamento";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally
        {
            _loading = false;
        }
    }

    private Dictionary<string, string?> BuildCatalogQuery()
    {
        string supp = CF("Supp");
        if (supp == "tutti") supp = "";
        string man = CF("Man");
        if (man == "tutti") man = "";
        string cat = CF("Cat");
        if (cat == "tutti") cat = "";

        return new Dictionary<string, string?>
        {
            ["code"] = F("Code"),
            ["description"] = F("Desc"),
            ["supplier"] = string.IsNullOrEmpty(supp) ? null : supp,
            ["manufacturer"] = string.IsNullOrEmpty(man) ? null : man,
            ["category"] = string.IsNullOrEmpty(cat) ? null : cat
        };
    }

    private async Task LoadFilterMetaAsync()
    {
        try
        {
            CatalogFilterMetaDto? meta = await ApiClient.GetDataAsync<CatalogFilterMetaDto>("/api/catalog/filter-meta");
            if (meta == null) return;
            PopulateCombo("Supp", meta.Suppliers);
            PopulateCombo("Man", meta.Manufacturers);
            PopulateCombo("Cat", meta.Categories);
            _filterMetaLoaded = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Catalog filter-meta: {ex.Message}");
        }
    }

    private static IEnumerable<string?> ReadStringArray(JsonElement data, string prop)
    {
        if (!data.TryGetProperty(prop, out JsonElement arr)) yield break;
        foreach (JsonElement el in arr.EnumerateArray())
            yield return el.GetString();
    }

    private void UpdateListStatus()
    {
        if (_totalCount <= 0)
            txtStatus.Text = "Nessun articolo";
        else if (_allItems.Count >= _totalCount)
            txtStatus.Text = $"{_totalCount} articoli";
        else
            txtStatus.Text = $"{_allItems.Count} di {_totalCount} articoli";
    }

    // ── FILTRI ─────────────────────────────────────────────────────

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

    private void ComboFilter_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox cb && cb.Tag != null)
            _comboFilterBoxes[cb.Tag.ToString()!] = cb;
    }

    private void ComboFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        Dispatcher.InvokeAsync(() => _ = Load(append: false),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void PopulateCombo(string tag, IEnumerable<string?> values)
    {
        if (!_comboFilterBoxes.TryGetValue(tag, out var cb)) return;

        var prev = cb.SelectedItem as string;
        cb.SelectionChanged -= ComboFilter_Changed;

        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(v => v)
            .ToList();

        distinct.Insert(0, "Tutti");
        cb.ItemsSource = distinct;
        cb.SelectedIndex = prev != null && distinct.Contains(prev) ? distinct.IndexOf(prev) : 0;

        cb.SelectionChanged += ComboFilter_Changed;
    }

    private string CF(string tag)
    {
        if (!_comboFilterBoxes.TryGetValue(tag, out var cb)) return "";
        var sel = cb.SelectedItem as string;
        if (sel == "Tutti" || string.IsNullOrEmpty(sel)) return "";
        if (cb.IsEditable && !string.IsNullOrEmpty(cb.Text) && cb.Text != sel)
            return cb.Text.Trim().ToLower();
        return sel.ToLower();
    }

    private string F(string tag) =>
        _filterBoxes.GetValueOrDefault(tag)?.Text.Trim().ToLower() ?? "";

    // ── AZIONI ─────────────────────────────────────────────────────

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (TextBox tb in _filterBoxes.Values) tb.Clear();
        foreach (ComboBox cb in _comboFilterBoxes.Values) cb.SelectedIndex = 0;
        _ = Load(append: false);
    }

    private void Dg_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgCatalog.SelectedItem is CatalogItemListItem item)
        {
            var dlg = new CatalogItemDialog(item.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _ = Load();
        }
    }

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CatalogItemDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CatalogItemListItem item)
        {
            var dlg = new CatalogItemDialog(item.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _ = Load();
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is CatalogItemListItem item &&
            ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Disattivare {item.Code} - {item.Description}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/catalog/{item.Id}");
            await Load();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load(append: false);

    private void BtnImportEasyfatt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new EasyfattArticlesImportDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }
}
