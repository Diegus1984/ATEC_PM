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
    private int _addedCount = 0;
    public event Action? ItemAdded;

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

                // Una riga per prodotto (padre), non per variante
                _allItems = new();
                foreach (var p in products)
                {
                    int varCount = p.Variants.Count;
                    string priceRange = "";
                    if (varCount > 0)
                    {
                        decimal minPrice = p.Variants.Min(v => v.SellPrice);
                        decimal maxPrice = p.Variants.Max(v => v.SellPrice);
                        priceRange = minPrice == maxPrice
                            ? $"{minPrice:N2}€"
                            : $"{minPrice:N2}€ – {maxPrice:N2}€";
                    }

                    _allItems.Add(new CatalogPickItem
                    {
                        ProductId = p.Id,
                        ItemType = p.ItemType,
                        Code = p.Code,
                        Name = p.Name,
                        VariantCount = varCount,
                        PriceRange = priceRange
                    });
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
                i.Name.ToLower().Contains(s)).ToList();

        dgProducts.ItemsSource = filtered;
        txtInfo.Text = $"{filtered.Count} voci disponibili";
    }

    private void DgProducts_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async void DgProducts_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (dgProducts.SelectedItem is not CatalogPickItem item) return;

        try
        {
            // Controlla se il prodotto è già nel preventivo
            string checkJson = await ApiClient.GetAsync($"/api/quotes/{_quoteId}");
            var checkDoc = JsonDocument.Parse(checkJson);
            if (checkDoc.RootElement.GetProperty("success").GetBoolean())
            {
                var quoteData = checkDoc.RootElement.GetProperty("data");
                var existingItems = JsonSerializer.Deserialize<QuoteDto>(
                    quoteData.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })?.Items ?? new();

                var duplicate = existingItems.FirstOrDefault(x => x.ProductId == item.ProductId);
                if (duplicate != null)
                {
                    MessageBox.Show(
                        $"'{item.Name}' è già presente nel preventivo.",
                        "Prodotto già presente",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }
            }

            // Aggiunge il prodotto con tutte le varianti
            string json = await ApiClient.PostAsync($"/api/quotes/{_quoteId}/items/product/{item.ProductId}", "{}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _addedCount++;
                ItemAdded?.Invoke();
                _added = true;
                string msg = item.VariantCount > 0
                    ? $"✓ {item.Name} aggiunto con {item.VariantCount} varianti"
                    : $"✓ {item.Name} aggiunto";
                txtAdded.Text = msg;
            }
            else
            {
                string errMsg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() ?? "Errore" : "Errore";
                MessageBox.Show(errMsg, "Errore", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
    }

    private async void BtnAdd_Click(object sender, RoutedEventArgs e) { }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _added;
        Close();
    }
}

public class CatalogPickItem
{
    public int ProductId { get; set; }
    public string ItemType { get; set; } = "product";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public int VariantCount { get; set; }
    public string PriceRange { get; set; } = "";
}
