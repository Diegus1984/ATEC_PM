using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CatalogPage : Page
{
    private List<CatalogItemListItem> _allItems = new();
    private List<CatalogItemListItem> _items = new();

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
            JsonDocument doc = JsonDocument.Parse(json);
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

    private void ApplyFilter()
    {
        string filter = txtSearch.Text.Trim().ToLower();
        List<CatalogItemListItem> filtered = string.IsNullOrEmpty(filter)
            ? _allItems
            : _allItems.Where(i =>
                (i.Code?.ToLower().Contains(filter) ?? false) ||
                (i.Description?.ToLower().Contains(filter) ?? false) ||
                (i.Category?.ToLower().Contains(filter) ?? false) ||
                (i.SupplierName?.ToLower().Contains(filter) ?? false) ||
                (i.Manufacturer?.ToLower().Contains(filter) ?? false)
            ).ToList();

        _items = filtered;
        dgCatalog.ItemsSource = _items;
        txtStatus.Text = $"{_items.Count} articoli" + (string.IsNullOrEmpty(filter) ? "" : $" (filtrati da {_allItems.Count})");
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        if (_allItems.Count > 0) ApplyFilter();
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
