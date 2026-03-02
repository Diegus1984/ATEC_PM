using System;
using System.Collections.Generic;
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
    private CancellationTokenSource? _filterCts;

    public CatalogPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

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
                ApplyFilter();
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void Filter_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag != null)
        {
            _filterBoxes[tb.Tag.ToString()!] = tb;
        }
    }

    // Evento Change con Debounce
    private async void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        // Cancella il timer precedente se l'utente sta ancora scrivendo
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();

        try
        {
            // Aspetta 300 millisecondi prima di filtrare
            await Task.Delay(300, _filterCts.Token);
            ApplyFilter();
        }
        catch (TaskCanceledException)
        {
            // L'utente ha scritto un altro carattere, non fare nulla
        }
    }

    private void ApplyFilter()
    {
        if (_allItems == null) return;

        string fCode = _filterBoxes.GetValueOrDefault("Code")?.Text.Trim().ToLower() ?? "";
        string fDesc = _filterBoxes.GetValueOrDefault("Desc")?.Text.Trim().ToLower() ?? "";
        string fSupp = _filterBoxes.GetValueOrDefault("Supp")?.Text.Trim().ToLower() ?? "";
        string fMan = _filterBoxes.GetValueOrDefault("Man")?.Text.Trim().ToLower() ?? "";

        // Eseguiamo il filtraggio
        var filtered = _allItems.Where(i =>
            (string.IsNullOrEmpty(fCode) || (i.Code?.ToLower().Contains(fCode) ?? false)) &&
            (string.IsNullOrEmpty(fDesc) || (i.Description?.ToLower().Contains(fDesc) ?? false)) &&
            (string.IsNullOrEmpty(fSupp) || (i.SupplierName?.ToLower().Contains(fSupp) ?? false)) &&
            (string.IsNullOrEmpty(fMan) || (i.Manufacturer?.ToLower().Contains(fMan) ?? false))
        ).ToList();

        dgCatalog.ItemsSource = filtered;
        txtStatus.Text = $"{filtered.Count} articoli trovati su {_allItems.Count}";
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tb in _filterBoxes.Values) tb.Clear();
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