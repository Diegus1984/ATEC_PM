using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private ObservableCollection<CatalogItemListItem> _allItems = new();
    private Dictionary<string, TextBox> _filterBoxes = new();
    private CancellationTokenSource? _filterCts;
    private bool _hasMore;
    private bool _loading;
    private int _loadedPage;
    private InfiniteScrollHelper? _infiniteScroll;

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
        _infiniteScroll = new InfiniteScrollHelper(
            () => _hasMore && !_loading,
            () => Load(append: true));
        _infiniteScroll.Attach(dgCatalog);
        Loaded += async (_, _) => await Load(append: false);
    }

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
        Dictionary<string, string?> query = new();
        string fCode = _filterBoxes.GetValueOrDefault("Code")?.Text.Trim() ?? "";
        string fDesc = _filterBoxes.GetValueOrDefault("Desc")?.Text.Trim() ?? "";
        if (!string.IsNullOrEmpty(fCode)) query["code"] = fCode;
        if (!string.IsNullOrEmpty(fDesc)) query["description"] = fDesc;

        txtStatus.Text = "Caricamento catalogo...";
        try
        {
            string url = PagedApiHelper.BuildUrl("/api/catalog", page, 50, query);
            PagedResult<CatalogItemListItem>? pageData = await PagedApiHelper.GetPageAsync<CatalogItemListItem>(url);
            if (pageData != null)
            {
                _loadedPage = pageData.Page;
                _hasMore = pageData.HasMore;
                foreach (CatalogItemListItem item in pageData.Items)
                    _allItems.Add(item);
                if (dgCatalog.ItemsSource != _allItems)
                    dgCatalog.ItemsSource = _allItems;
                txtStatus.Text = pageData.HasMore
                    ? $"{_allItems.Count} di {pageData.TotalCount} articoli"
                    : $"{_allItems.Count} articoli";
                _infiniteScroll?.NotifyContentUpdated(dgCatalog);
            }
        }
        catch (Exception ex) { txtStatus.Text = $"Errore: {ex.Message}"; }
        finally
        {
            _loading = false;
        }
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
            await Load(append: false);
        }
        catch (TaskCanceledException) { }
    }

    private void BtnClearFilters_Click(object sender, RoutedEventArgs e)
    {
        foreach (TextBox tb in _filterBoxes.Values) tb.Clear();
        _ = Load(append: false);
    }

    private async void Dg_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (dgCatalog.SelectedItem is not CatalogItemListItem item) return;

        try
        {
            // Controlla se esiste già nella DDP
            List<BomItemListItem> existing = await ApiClient.GetListAsync<BomItemListItem>(
                $"/api/projects/{_projectId}/ddp?type={_ddpType}");
            BomItemListItem? duplicate = existing.FirstOrDefault(x => x.CatalogItemId == item.Id);
            if (duplicate != null)
            {
                    var result = ATEC.PM.Client.Controls.ShadcnMessageBox.Show(
                        $"L'articolo {item.Code} è già presente nella DDP (Qtà attuale: {duplicate.Quantity}).\n\nVuoi aggiungere +1 alla quantità?",
                        "Articolo già presente",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        var updateReq = new BomItemSaveRequest
                        {
                            Id = duplicate.Id,
                            ProjectId = _projectId,
                            Quantity = duplicate.Quantity + 1,
                            ItemStatus = duplicate.ItemStatus,
                            DaneaRef = duplicate.DaneaRef,
                            DateNeeded = duplicate.DateNeeded,
                            Destination = duplicate.Destination,
                            Notes = duplicate.Notes
                        };
                        string updateBody = JsonSerializer.Serialize(updateReq, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                        await ApiClient.PutAsync($"/api/projects/{_projectId}/ddp/{duplicate.Id}", updateBody);
                        _addedCount++;
                        txtAdded.Text = $"✓ Qtà aggiornata per {item.Code}";
                        ItemAdded?.Invoke();
                    }
                return;
            }

            // Inserimento nuovo
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
                ItemStatus = "DO",   // DA ORDINARE (causale DDP di default)
                RequestedBy = _requestedBy,
                DdpType = _ddpType
            };

            string body = JsonSerializer.Serialize(req, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string json = await ApiClient.PostAsync($"/api/projects/{_projectId}/ddp", body);
            if (ApiClient.IsApiSuccess(json, out string msg))
            {
                _addedCount++;
                txtAdded.Text = $"✓ {_addedCount} articol{(_addedCount == 1 ? "o" : "i")} aggiunti";
                ItemAdded?.Invoke();
            }
            else
            {
                ATEC.PM.Client.Controls.ShadcnMessageBox.Show(msg ?? "Errore", "Errore");
            }
        }
        catch (Exception ex) { ATEC.PM.Client.Controls.ShadcnMessageBox.Show($"Errore: {ex.Message}"); }
    }

    public bool HasAdded => _addedCount > 0;
}