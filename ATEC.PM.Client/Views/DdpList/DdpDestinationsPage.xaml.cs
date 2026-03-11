using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class DdpDestinationsPage : Page
{
    private List<DdpDestinationItem> _allItems = new();
    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    public DdpDestinationsPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync("/api/ddp-destinations");
            var response = JsonSerializer.Deserialize<ApiResponse<List<DdpDestinationItem>>>(json, _jsonOpt);
            if (response?.Success == true)
            {
                _allItems = response.Data ?? new();
                dgDestinations.ItemsSource = _allItems;
                txtCount.Text = $"({_allItems.Count} voci)";
                txtStatus.Text = $"{_allItems.Count} destinazioni caricate";
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new DdpDestinationDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await Load();
    }

    private async void Dg_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgDestinations.SelectedItem is DdpDestinationItem item)
        {
            var dlg = new DdpDestinationDialog(item.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await Load();
        }
    }

    private void Dg_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load();
}
