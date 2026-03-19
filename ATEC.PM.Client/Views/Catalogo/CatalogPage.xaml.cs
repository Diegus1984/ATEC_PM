using System;
using System.Collections.Generic;
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
    private List<CatalogItemListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private Dictionary<string, ComboBox> _comboFilterBoxes = new();
    private CancellationTokenSource? _filterCts;

    private readonly List<(string Key, string Label, DataGridColumn Column)> _columnDefs = new();
    private Dictionary<string, bool> _columnVisibility = new();
    private bool _suppressSave = false;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ATEC_PM", "catalog_columns.json");

    public CatalogPage()
    {
        InitializeComponent();
        InitColumnDefs();
        LoadColumnSettings();
        BuildColumnCheckboxes();
        syncBar.SyncCompleted += async () => await Load();
        Loaded += async (_, _) => await Load();
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
        catch { }

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
        catch { }
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
            string json = await ApiClient.GetAsync("/api/catalog");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _allItems = JsonSerializer.Deserialize<List<CatalogItemListItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                RefreshComboFilters();
                ApplyFilter();
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
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
            ApplyFilter();
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
        // Posticipa al prossimo ciclo UI così SelectedItem/Text sono allineati
        Dispatcher.InvokeAsync(ApplyFilter, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void RefreshComboFilters()
    {
        PopulateCombo("Supp", _allItems.Select(i => i.SupplierName));
        PopulateCombo("Man", _allItems.Select(i => i.Manufacturer));
        PopulateCombo("Cat", _allItems.Select(i => i.Category));
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

    private static bool Match(string? value, string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        var v = value?.ToLower() ?? "";

        bool startsWild = filter.StartsWith('*');
        bool endsWild = filter.EndsWith('*');

        if (startsWild && endsWild)
            return v.Contains(filter.Trim('*'));
        if (endsWild)
            return v.StartsWith(filter.TrimEnd('*'));
        if (startsWild)
            return v.EndsWith(filter.TrimStart('*'));

        return v.Contains(filter);
    }

    private void ApplyFilter()
    {
        if (_allItems == null) return;

        string fCode = F("Code");
        string fDesc = F("Desc");
        string fSupp = CF("Supp");
        string fMan = CF("Man");
        string fCat = CF("Cat");

        var filtered = _allItems.Where(i =>
            Match(i.Code, fCode) &&
            Match(i.Description, fDesc) &&
            Match(i.SupplierName, fSupp) &&
            Match(i.Manufacturer, fMan) &&
            Match(i.Category, fCat)
        ).ToList();

        dgCatalog.ItemsSource = filtered;
        txtStatus.Text = $"{filtered.Count} articoli trovati su {_allItems.Count}";
    }

    // ── AZIONI ─────────────────────────────────────────────────────

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tb in _filterBoxes.Values) tb.Clear();
        foreach (var cb in _comboFilterBoxes.Values) cb.SelectedIndex = 0; // "Tutti"
        ApplyFilter();
    }

    private void Dg_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        btnEdit.IsEnabled = btnDelete.IsEnabled = dgCatalog.SelectedItem != null;
    }

    private void Dg_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => BtnEdit_Click(sender, e);

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CatalogItemDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (dgCatalog.SelectedItem is CatalogItemListItem item)
        {
            var dlg = new CatalogItemDialog(item.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _ = Load();
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgCatalog.SelectedItem is CatalogItemListItem item &&
            MessageBox.Show($"Disattivare {item.Code} - {item.Description}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/catalog/{item.Id}");
            await Load();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load();

    private void BtnImportEasyfatt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new EasyfattArticlesImportDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }
}
