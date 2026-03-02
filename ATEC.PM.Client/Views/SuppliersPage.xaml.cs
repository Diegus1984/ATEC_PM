using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class SuppliersPage : Page
{
    private List<SupplierListItem> _allSuppliers = new();
    private List<SupplierListItem> _suppliers = new();

    public SuppliersPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            var json = await ApiClient.GetAsync("/api/suppliers");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _allSuppliers = JsonSerializer.Deserialize<List<SupplierListItem>>(
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
        List<SupplierListItem> filtered = string.IsNullOrEmpty(filter)
            ? _allSuppliers
            : _allSuppliers.Where(s =>
                (s.CompanyName?.ToLower().Contains(filter) ?? false) ||
                (s.ContactName?.ToLower().Contains(filter) ?? false) ||
                (s.Email?.ToLower().Contains(filter) ?? false) ||
                (s.Phone?.ToLower().Contains(filter) ?? false) ||
                (s.VatNumber?.ToLower().Contains(filter) ?? false) ||
                (s.FiscalCode?.ToLower().Contains(filter) ?? false)
            ).ToList();

        _suppliers = filtered;
        dgSuppliers.ItemsSource = _suppliers;
        txtStatus.Text = $"{_suppliers.Count} fornitori" + (string.IsNullOrEmpty(filter) ? "" : $" (filtrati da {_allSuppliers.Count})");
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text)
            ? Visibility.Visible : Visibility.Collapsed;
        if (_allSuppliers.Count > 0) ApplyFilter();
    }

    private void Dg_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        btnEdit.IsEnabled = btnDelete.IsEnabled = dgSuppliers.SelectedItem != null;
    }

    private void Dg_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => BtnEdit_Click(sender, e);

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SupplierDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (dgSuppliers.SelectedItem is SupplierListItem s)
        {
            var dlg = new SupplierDialog(s.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _ = Load();
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgSuppliers.SelectedItem is SupplierListItem s &&
            MessageBox.Show($"Disattivare {s.CompanyName}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/suppliers/{s.Id}");
            await Load();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load();

    private void BtnImportEasyfatt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new EasyfattImportDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }
}
