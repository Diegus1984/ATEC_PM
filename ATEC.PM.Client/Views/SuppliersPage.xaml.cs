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

public partial class SuppliersPage : Page
{
    private List<SupplierListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;

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
            string json = await ApiClient.GetAsync("/api/suppliers");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<ApiResponse<List<SupplierListItem>>>(json, options);

            if (response != null && response.Success)
            {
                _allItems = response.Data ?? new();
                ApplyFilter();
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

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

        string fName = _filterBoxes.GetValueOrDefault("Name")?.Text.Trim().ToLower() ?? "";
        string fContact = _filterBoxes.GetValueOrDefault("Contact")?.Text.Trim().ToLower() ?? "";
        string fVat = _filterBoxes.GetValueOrDefault("Vat")?.Text.Trim().ToLower() ?? "";

        var filtered = _allItems.Where(s =>
            (string.IsNullOrEmpty(fName) || (s.CompanyName?.ToLower().Contains(fName) ?? false)) &&
            (string.IsNullOrEmpty(fContact) || (s.ContactName?.ToLower().Contains(fContact) ?? false)) &&
            (string.IsNullOrEmpty(fVat) || (s.VatNumber?.ToLower().Contains(fVat) ?? false))
        ).ToList();

        dgSuppliers.ItemsSource = filtered;
        txtStatus.Text = $"{filtered.Count} fornitori trovati su {_allItems.Count}";
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tb in _filterBoxes.Values) tb.Clear();
        ApplyFilter();
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