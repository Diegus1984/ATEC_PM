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

namespace ATEC.PM.Client.Views.Quotes;

public partial class AddQuoteItemDialog : Window
{
    private int _quoteId;
    private List<CatalogPickItem> _allItems = new();
    private CancellationTokenSource? _searchCts;
    private bool _added = false;

    public AddQuoteItemDialog(int quoteId)
    {
        InitializeComponent();
        _quoteId = quoteId;
        Loaded += async (_, _) => await LoadCatalog();
    }

    private async Task LoadCatalog()
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        // Carica gruppi
        try
        {
            string json = await ApiClient.GetAsync("/api/quote-catalog/groups");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var groups = JsonSerializer.Deserialize<List<QuoteGroupDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(), opts) ?? new();
                var all = new List<QuoteGroupDto> { new() { Id = 0, Name = "Tutti i gruppi" } };
                all.AddRange(groups);
                cmbGroup.ItemsSource = all;
                cmbGroup.SelectedIndex = 0;
            }
        }
        catch { }
    }

    private async void CmbGroup_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (cmbGroup.SelectedValue is int groupId)
        {
            // Carica categorie per gruppo
            if (groupId > 0)
            {
                try
                {
                    string json = await ApiClient.GetAsync($"/api/quote-catalog/categories?groupId={groupId}");
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.GetProperty("success").GetBoolean())
                    {
                        var cats = JsonSerializer.Deserialize<List<QuoteCategoryDto>>(
                            doc.RootElement.GetProperty("data").GetRawText(),
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                        var all = new List<QuoteCategoryDto> { new() { Id = 0, Name = "Tutte le categorie" } };
                        all.AddRange(cats);
                        cmbCategory.ItemsSource = all;
                        cmbCategory.SelectedIndex = 0;
                    }
                }
                catch { }
            }
            else
            {
                cmbCategory.ItemsSource = null;
            }

            await LoadProducts();
        }
    }

    private async void CmbCategory_Changed(object sender, SelectionChangedEventArgs e)
    {
        await LoadProducts();
    }

    private async Task LoadProducts()
    {
        int? groupId = cmbGroup.SelectedValue is int g && g > 0 ? g : null;
        int? catId = cmbCategory?.SelectedValue is int c && c > 0 ? c : null;

        string url = "/api/quote-catalog/products?";
        if (catId.HasValue) url += $"categoryId={catId}";
        else if (groupId.HasValue) url += $"groupId={groupId}";

        try
        {
            string json = await ApiClient.GetAsync(url);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var products = JsonSerializer.Deserialize<List<QuoteProductDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

                // Flatten: 1 riga per variante
                _allItems = new();
                foreach (var p in products)
                {
                    if (p.Variants.Count == 0)
                    {
                        _allItems.Add(new CatalogPickItem
                        {
                            ProductId = p.Id,
                            ItemType = p.ItemType,
                            Code = p.Code,
                            Name = p.Name,
                            DescriptionRtf = p.DescriptionRtf,
                            VariantName = "—",
                            CostPrice = 0, SellPrice = 0,
                            Unit = "nr.", DefaultQty = 1, VatPct = 22
                        });
                    }
                    else
                    {
                        foreach (var v in p.Variants)
                        {
                            _allItems.Add(new CatalogPickItem
                            {
                                ProductId = p.Id,
                                VariantId = v.Id,
                                ItemType = p.ItemType,
                                Code = v.Code,
                                Name = p.Name,
                                DescriptionRtf = p.DescriptionRtf,
                                VariantName = v.Name,
                                CostPrice = v.CostPrice,
                                SellPrice = v.SellPrice,
                                DiscountPct = v.DiscountPct,
                                VatPct = v.VatPct,
                                Unit = v.Unit,
                                DefaultQty = v.DefaultQty
                            });
                        }
                    }
                }

                ApplySearch();
            }
        }
        catch { }
    }

    private async void TxtSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        try { await Task.Delay(300, _searchCts.Token); ApplySearch(); }
        catch (TaskCanceledException) { }
    }

    private void ApplySearch()
    {
        string s = txtSearch.Text.Trim().ToLower();
        var filtered = string.IsNullOrEmpty(s)
            ? _allItems
            : _allItems.Where(i =>
                i.Code.ToLower().Contains(s) ||
                i.Name.ToLower().Contains(s) ||
                i.VariantName.ToLower().Contains(s)).ToList();

        dgProducts.ItemsSource = filtered;
        txtInfo.Text = $"{filtered.Count} voci disponibili";
    }

    private void DgProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        btnAdd.IsEnabled = dgProducts.SelectedItem != null;
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        if (dgProducts.SelectedItem is not CatalogPickItem item) return;

        var dto = new QuoteItemSaveDto
        {
            ProductId = item.ProductId,
            VariantId = item.VariantId,
            ItemType = item.ItemType,
            Code = item.Code,
            Name = string.IsNullOrEmpty(item.VariantName) || item.VariantName == "—"
                ? item.Name : item.VariantName,
            DescriptionRtf = item.DescriptionRtf,
            Unit = item.Unit,
            Quantity = item.DefaultQty,
            CostPrice = item.CostPrice,
            SellPrice = item.SellPrice,
            DiscountPct = item.DiscountPct,
            VatPct = item.VatPct
        };

        try
        {
            string body = JsonSerializer.Serialize(dto);
            string json = await ApiClient.PostAsync($"/api/quotes/{_quoteId}/items", body);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _added = true;
                txtInfo.Text = $"✓ Aggiunto: {dto.Name}";
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _added;
        Close();
    }
}

public class CatalogPickItem
{
    public int ProductId { get; set; }
    public int? VariantId { get; set; }
    public string ItemType { get; set; } = "product";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string DescriptionRtf { get; set; } = "";
    public string VariantName { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal VatPct { get; set; } = 22;
    public string Unit { get; set; } = "nr.";
    public decimal DefaultQty { get; set; } = 1;
}
