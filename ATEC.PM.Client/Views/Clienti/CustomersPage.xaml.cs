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

public partial class CustomersPage : Page
{
    private List<CustomerListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;

    public CustomersPage()
    {
        InitializeComponent();
        syncBar.SyncCompleted += async () => await Load();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/customers");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var response = JsonSerializer.Deserialize<ApiResponse<List<CustomerListItem>>>(json, options);

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

        string fName = _filterBoxes.GetValueOrDefault("Name")?.Text.Trim().ToLower() ?? "";
        string fContact = _filterBoxes.GetValueOrDefault("Contact")?.Text.Trim().ToLower() ?? "";
        string fVat = _filterBoxes.GetValueOrDefault("Vat")?.Text.Trim().ToLower() ?? "";
        string fEmail = _filterBoxes.GetValueOrDefault("Email")?.Text.Trim().ToLower() ?? "";

        var filtered = _allItems.Where(c =>
            Match(c.CompanyName, fName) &&
            Match(c.ContactName, fContact) &&
            Match(c.VatNumber, fVat) &&
            Match(c.Email, fEmail)
        ).ToList();

        dgCustomers.ItemsSource = filtered;
        txtStatus.Text = $"{filtered.Count} clienti trovati su {_allItems.Count}";
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tb in _filterBoxes.Values) tb.Clear();
        ApplyFilter();
    }

    private void Dg_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        btnEdit.IsEnabled = btnDelete.IsEnabled = dgCustomers.SelectedItem != null;
    }

    private void Dg_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => BtnEdit_Click(sender, e);

    private void BtnNew_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new CustomerDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }

    private void BtnEdit_Click(object sender, RoutedEventArgs e)
    {
        if (dgCustomers.SelectedItem is CustomerListItem c)
        {
            var dlg = new CustomerDialog(c.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) _ = Load();
        }
    }

    private async void BtnDelete_Click(object sender, RoutedEventArgs e)
    {
        if (dgCustomers.SelectedItem is CustomerListItem c &&
            MessageBox.Show($"Disattivare {c.CompanyName}?", "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/customers/{c.Id}");
            await Load();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load();

    private void BtnImportEasyfatt_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new EasyfattCustomersImportDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = Load();
    }
}
