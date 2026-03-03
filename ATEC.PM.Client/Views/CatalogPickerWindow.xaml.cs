using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CatalogPickerWindow : Window
{
    private List<CatalogItemListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;

    private readonly int _projectId;
    private readonly string _ddpType;
    private readonly string _requestedBy;
    private int _addedCount;
    public event Action? ItemAdded;

    public CatalogPickerWindow(int projectId, string ddpType, string requestedBy)
    {
        InitializeComponent();
        _projectId = projectId;
        _ddpType = ddpType;
        _requestedBy = requestedBy;
        Loaded += async (_, _) => await Load();
    }

    private async Task Load()
    {
        txtStatus.Text = "Caricamento catalogo...";
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
        string fCode = _filterBoxes.GetValueOrDefault("Code")?.Text.Trim().ToLower() ?? "";
        string fDesc = _filterBoxes.GetValueOrDefault("Desc")?.Text.Trim().ToLower() ?? "";
        string fSupp = _filterBoxes.GetValueOrDefault("Supp")?.Text.Trim().ToLower() ?? "";
        string fMan = _filterBoxes.GetValueOrDefault("Man")?.Text.Trim().ToLower() ?? "";

        var filtered = _allItems.Where(i =>
            (string.IsNullOrEmpty(fCode) || (i.Code?.ToLower().Contains(fCode) ?? false)) &&
            (string.IsNullOrEmpty(fDesc) || (i.Description?.ToLower().Contains(fDesc) ?? false)) &&
            (string.IsNullOrEmpty(fSupp) || (i.SupplierName?.ToLower().Contains(fSupp) ?? false)) &&
            (string.IsNullOrEmpty(fMan) || (i.Manufacturer?.ToLower().Contains(fMan) ?? false))
        ).ToList();

        dgCatalog.ItemsSource = filtered;
        txtStatus.Text = $"{filtered.Count} articoli su {_allItems.Count}";
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (var tb in _filterBoxes.Values) tb.Clear();
        ApplyFilter();
    }

    private async void Dg_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (dgCatalog.SelectedItem is not CatalogItemListItem item) return;

        try
        {
            var req = new BomItemSaveRequest
            {
                ProjectId = _projectId,
                CatalogItemId = item.Id,
                PartNumber = item.Code,
                Description = item.Description,
                Unit = item.Unit,
                Quantity = 1,
                UnitCost = item.UnitCost,
                SupplierId = item.SupplierId,
                Manufacturer = item.Manufacturer,
                ItemStatus = "TO_ORDER",
                RequestedBy = _requestedBy,
                DdpType = _ddpType
            };

            string body = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string json = await ApiClient.PostAsync($"/api/projects/{_projectId}/ddp", body);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _addedCount++;
                txtAdded.Text = $"✓ {_addedCount} articol{(_addedCount == 1 ? "o" : "i")} aggiunti";
                ItemAdded?.Invoke();
            }
            else
            {
                MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore", "Errore");
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    public bool HasAdded => _addedCount > 0;
}