using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Costing;

/// <summary>Group of variants for a single product (shown as card with header + indented variants)</summary>
internal class PickerProductGroup
{
    public string ProductName { get; set; } = "";
    public ObservableCollection<PickerVariantItem> Variants { get; set; } = new();
}

internal class PickerVariantItem : INotifyPropertyChanged
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int VariantId { get; set; }
    public string VariantCode { get; set; } = "";
    public string VariantName { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal MarkupValue { get; set; } = 1.300m;
    public decimal SellPrice => CostPrice * MarkupValue;
    public decimal Quantity { get; set; } = 1;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class CatalogPickerDialog : Window
{
    private static readonly JsonSerializerOptions _jopt = new() { PropertyNameCaseInsensitive = true };

    internal List<PickerVariantItem> SelectedItems { get; private set; } = new();

    // Expose selected data as a simple public DTO list for external consumers
    public List<(int ProductId, string ProductName, int VariantId, string VariantCode,
        string VariantName, decimal CostPrice, decimal SellPrice, decimal Quantity)> SelectedVariants
        => SelectedItems.Select(i => (i.ProductId, i.ProductName, i.VariantId,
            i.VariantCode, i.VariantName, i.CostPrice, i.SellPrice, i.Quantity)).ToList();

    public CatalogPickerDialog()
    {
        InitializeComponent();
        icCart.ItemsSource = _cart;
        _searchTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            if (_treeData.HasValue)
                RebuildTreeFiltered(_treeData.Value, txtSearch.Text.Trim().ToLowerInvariant());
        };
        Loaded += async (_, _) =>
        {
            await LoadPriceLists();
            await LoadTree();
        };
    }

    // ══════════════════════════════════════════════════
    // PRICE LISTS
    // ══════════════════════════════════════════════════

    private async Task LoadPriceLists()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/quote-catalog/price-lists");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var priceLists = JsonSerializer.Deserialize<List<QuotePriceListDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jopt) ?? new();

                var items = new List<QuotePriceListDto> { new() { Id = 0, Name = "Tutti i listini" } };
                items.AddRange(priceLists);
                cmbPriceList.ItemsSource = items;
                cmbPriceList.SelectedIndex = 0;
            }
        }
        catch (Exception ex) { txtTreeStatus.Text = $"Errore listini: {ex.Message}"; }
    }

    private void CmbPriceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = LoadTree();
    }

    // ══════════════════════════════════════════════════
    // TREE
    // ══════════════════════════════════════════════════

    private int? SelectedPriceListId =>
        cmbPriceList?.SelectedItem is QuotePriceListDto pl && pl.Id > 0 ? pl.Id : null;

    private async Task LoadTree()
    {
        txtTreeStatus.Text = "Caricamento...";
        try
        {
            string url = "/api/quote-catalog/tree";
            if (SelectedPriceListId.HasValue)
                url += $"?priceListId={SelectedPriceListId.Value}";

            string json = await ApiClient.GetAsync(url);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var data = doc.RootElement.GetProperty("data");
                BuildTree(data);
            }
        }
        catch (Exception ex) { txtTreeStatus.Text = $"Errore: {ex.Message}"; }
    }

    private JsonElement? _treeData;
    private readonly System.Windows.Threading.DispatcherTimer _searchTimer;

    private void BuildTree(JsonElement data)
    {
        _treeData = data;
        string search = txtSearch.Text.Trim().ToLowerInvariant();
        RebuildTreeFiltered(data, search);
    }

    private void RebuildTreeFiltered(JsonElement data, string search)
    {
        treeCatalog.Items.Clear();
        int groupCount = 0, productCount = 0;
        bool hasFilter = !string.IsNullOrEmpty(search);

        if (!data.TryGetProperty("groups", out var groups)) return;

        foreach (var group in groups.EnumerateArray())
        {
            string gName = group.GetProperty("name").GetString() ?? "";
            int gId = group.GetProperty("id").GetInt32();

            var groupNode = new TreeViewItem
            {
                Tag = $"group|{gId}",
                FontSize = 13
            };

            int childCount = 0;
            if (group.TryGetProperty("categories", out var cats))
                childCount = BuildCategoryNodes(groupNode, cats, search, ref productCount);

            if (hasFilter && childCount == 0) continue; // skip empty groups when searching

            groupNode.Header = BuildCountHeader(gName, childCount, FontWeights.SemiBold, "#1A1D26");
            groupNode.IsExpanded = true;
            treeCatalog.Items.Add(groupNode);
            groupCount++;
        }

        txtTreeStatus.Text = $"{groupCount} gruppi, {productCount} prodotti";
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private int BuildCategoryNodes(TreeViewItem parentNode, JsonElement cats, string search, ref int productCount)
    {
        int totalItems = 0;
        bool hasFilter = !string.IsNullOrEmpty(search);

        foreach (var cat in cats.EnumerateArray())
        {
            string cName = cat.GetProperty("name").GetString() ?? "";
            int cId = cat.GetProperty("id").GetInt32();

            var catNode = new TreeViewItem { Tag = $"category|{cId}|{cName}", FontSize = 13 };
            int catItems = 0;

            // Add subcategories recursively
            if (cat.TryGetProperty("children", out var children))
                catItems += BuildCategoryNodes(catNode, children, search, ref productCount);

            // Add products
            if (cat.TryGetProperty("products", out var products))
            {
                foreach (var prod in products.EnumerateArray())
                {
                    string pName = prod.GetProperty("name").GetString() ?? "";
                    int pId = prod.GetProperty("id").GetInt32();
                    int varCount = prod.TryGetProperty("variants", out var vars) ? vars.GetArrayLength() : 0;

                    if (hasFilter && !pName.ToLowerInvariant().Contains(search)
                        && !cName.ToLowerInvariant().Contains(search))
                        continue;

                    var prodNode = new TreeViewItem
                    {
                        Header = BuildCountHeader(pName, varCount, FontWeights.Normal, "#6B7280"),
                        Tag = $"product|{pId}|{pName}",
                        FontSize = 12
                    };
                    catNode.Items.Add(prodNode);
                    catItems++;
                    productCount++;
                }
            }

            if (hasFilter && catItems == 0 && !cName.ToLowerInvariant().Contains(search)) continue;

            catNode.Header = BuildCountHeader(cName, catItems, FontWeights.Normal, "#374151");
            catNode.IsExpanded = hasFilter; // auto-expand when searching
            parentNode.Items.Add(catNode);
            totalItems += catItems;
        }

        return totalItems;
    }

    private static StackPanel BuildCountHeader(string text, int count, FontWeight weight, string color)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = weight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
        });
        if (count > 0)
        {
            sp.Children.Add(new TextBlock
            {
                Text = $" ({count})",
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        return sp;
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        txtSearchPlaceholder.Visibility = string.IsNullOrEmpty(txtSearch.Text) ? Visibility.Visible : Visibility.Collapsed;
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    // ══════════════════════════════════════════════════
    // TREE SELECTION -> LOAD VARIANTS
    // ══════════════════════════════════════════════════

    private async void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (treeCatalog.SelectedItem is not TreeViewItem tvi) return;
        string? tag = tvi.Tag as string;
        if (string.IsNullOrEmpty(tag)) return;

        if (tag.StartsWith("product|"))
        {
            var parts = tag.Split('|');
            if (parts.Length >= 3 && int.TryParse(parts[1], out int prodId))
            {
                string prodName = parts[2];
                txtProductName.Text = prodName;
                await LoadProductVariants(prodId);
            }
        }
        else if (tag.StartsWith("category|"))
        {
            var parts = tag.Split('|');
            if (parts.Length >= 3 && int.TryParse(parts[1], out int catId))
            {
                string catName = parts[2];
                txtProductName.Text = catName;
                await LoadCategoryProducts(catId);
            }
        }
    }

    private async Task LoadProductVariants(int productId)
    {
        try
        {
            string json = await ApiClient.GetAsync($"/api/quote-catalog/products/{productId}");
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var product = JsonSerializer.Deserialize<QuoteProductDto>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jopt);

                if (product != null)
                    PopulateVariants(new List<QuoteProductDto> { product });
            }
        }
        catch (Exception ex) { txtProductName.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadCategoryProducts(int categoryId)
    {
        try
        {
            string url = $"/api/quote-catalog/products?categoryId={categoryId}";
            string json = await ApiClient.GetAsync(url);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var products = JsonSerializer.Deserialize<List<QuoteProductDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jopt) ?? new();

                PopulateVariants(products);
            }
        }
        catch (Exception ex) { txtProductName.Text = $"Errore: {ex.Message}"; }
    }

    private void PopulateVariants(List<QuoteProductDto> products)
    {

        var groups = new ObservableCollection<PickerProductGroup>();

        foreach (var p in products)
        {
            var variants = new ObservableCollection<PickerVariantItem>();
            foreach (var v in p.Variants)
            {
                var item = new PickerVariantItem
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    VariantId = v.Id,
                    VariantCode = v.Code,
                    VariantName = !string.IsNullOrWhiteSpace(v.Name) ? v.Name : p.Name,
                    CostPrice = v.CostPrice,
                    MarkupValue = v.MarkupValue > 0 ? v.MarkupValue : 1.300m,
                    Quantity = 1
                };
                variants.Add(item);

            }

            if (variants.Count > 0)
                groups.Add(new PickerProductGroup { ProductName = p.Name, Variants = variants });
        }

        icProductGroups.ItemsSource = groups;
        UpdateCartCount();
    }

    // ══════════════════════════════════════════════════
    // CART MANAGEMENT
    // ══════════════════════════════════════════════════

    private void Variant_CheckChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox cb || cb.DataContext is not PickerVariantItem item) return;

        if (item.IsSelected)
        {
            if (!_cart.Any(c => c.VariantId == item.VariantId))
                _cart.Add(item);
        }
        else
        {
            var existing = _cart.FirstOrDefault(c => c.VariantId == item.VariantId);
            if (existing != null) _cart.Remove(existing);
        }

        pnlCart.Visibility = _cart.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCartCount();
    }

    // ══════════════════════════════════════════════════
    // BUTTONS
    // ══════════════════════════════════════════════════

    private readonly ObservableCollection<PickerVariantItem> _cart = new();

    private void BtnRemoveFromCart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is PickerVariantItem item)
        {
            _cart.Remove(item);
            pnlCart.Visibility = _cart.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateCartCount();
        }
    }

    private void BtnConfirm_Click(object sender, RoutedEventArgs e)
    {
        if (_cart.Count == 0)
        {
            MessageBox.Show("Il carrello è vuoto. Aggiungi almeno una variante.", "Attenzione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        SelectedItems = _cart.ToList();
        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void UpdateCartCount()
    {
        txtSelectionCount.Text = _cart.Count > 0
            ? $"{_cart.Count} elemento/i nel carrello"
            : "Carrello vuoto";
        btnConfirm.Content = _cart.Count > 0 ? $"Conferma ({_cart.Count})" : "Conferma";
    }
}
