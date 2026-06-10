using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class DdpDestinationsPage : Page
{
    private List<DdpDestinationItem> _allItems = new();
    private List<DdpStatusItem> _statuses = new();

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
            _allItems = await ApiClient.GetListAsync<DdpDestinationItem>("/api/ddp-destinations");
            dgDestinations.ItemsSource = _allItems;

            _statuses = await ApiClient.GetListAsync<DdpStatusItem>("/api/ddp-statuses");
            dgStatuses.ItemsSource = _statuses;

            txtCount.Text = $"({_allItems.Count} destinazioni · {_statuses.Count} stati)";
            txtStatus.Text = $"{_allItems.Count} destinazioni, {_statuses.Count} stati caricati";
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
    }

    // ── Destinazioni ──
    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new DdpDestinationDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await Load();
    }

    private async void DgDest_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgDestinations.SelectedItem is DdpDestinationItem item)
        {
            var dlg = new DdpDestinationDialog(item.Id) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await Load();
        }
    }

    // ── Stati / Causali (crea + modifica etichetta/colore) ──
    private async void BtnAddStatus_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new DdpStatusDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) await Load();
    }

    private async void DgStatus_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgStatuses.SelectedItem is DdpStatusItem item)
        {
            var dlg = new DdpStatusDialog(item) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true) await Load();
        }
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e) => await Load();
}
