using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Costing;

internal class PickerVariantItem : INotifyPropertyChanged
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = "";
    public int VariantId { get; set; }
    public string VariantCode { get; set; } = "";
    public string VariantName { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal MarkupValue { get; set; } = 1.300m;
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
    private List<PickerVariantItem> _currentItems = new();

    internal List<PickerVariantItem> SelectedItems { get; private set; } = new();

    // Expose selected data as a simple public DTO list for external consumers
    public List<(int ProductId, string ProductName, int VariantId, string VariantCode,
        string VariantName, decimal CostPrice, decimal SellPrice, decimal Quantity)> SelectedVariants
        => SelectedItems.Select(i => (i.ProductId, i.ProductName, i.VariantId,
            i.VariantCode, i.VariantName, i.CostPrice, i.SellPrice, i.Quantity)).ToList();

    public CatalogPickerDialog()
    {
        InitializeComponent();
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

    private void BuildTree(JsonElement data)
    {
        treeCatalog.Items.Clear();
        int groupCount = 0, catCount = 0;

        if (!data.TryGetProperty("groups", out var groups)) return;

        foreach (var group in groups.EnumerateArray())
        {
            groupCount++;
            int gId = group.GetProperty("id").GetInt32();
            string gName = group.GetProperty("name").GetString() ?? "";

            var groupNode = new TreeViewItem
            {
                Header = BuildTreeHeader(gName, FontWeights.SemiBold, "#1A1D26"),
                Tag = $"group|{gId}",
                FontSize = 13,
                IsExpanded = true
            };

            if (group.TryGetProperty("categories", out var cats))
            {
                foreach (var cat in cats.EnumerateArray())
                {
                    catCount++;
                    int cId = cat.GetProperty("id").GetInt32();
                    string cName = cat.GetProperty("name").GetString() ?? "";
                    int pCount = cat.TryGetProperty("productCount", out var pc) ? pc.GetInt32() : 0;

                    var sp = BuildTreeHeader(cName, FontWeights.Normal, "#374151");
                    if (pCount > 0)
                    {
                        sp.Children.Add(new TextBlock
                        {
                            Text = $" ({pCount})",
                            FontSize = 11,
                            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9CA3AF")),
                            VerticalAlignment = VerticalAlignment.Center
                        });
                    }

                    var catNode = new TreeViewItem
                    {
                        Header = sp,
                        Tag = $"category|{cId}|{cName}",
                        FontSize = 13
                    };
                    groupNode.Items.Add(catNode);
                }
            }

            treeCatalog.Items.Add(groupNode);
        }

        txtTreeStatus.Text = $"{groupCount} gruppi, {catCount} categorie";
    }

    private static StackPanel BuildTreeHeader(string text, FontWeight weight, string color)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = weight,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color))
        });
        return sp;
    }

    // ══════════════════════════════════════════════════
    // TREE SELECTION -> LOAD PRODUCTS
    // ══════════════════════════════════════════════════

    private async void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (treeCatalog.SelectedItem is not TreeViewItem tvi) return;
        string? tag = tvi.Tag as string;
        if (string.IsNullOrEmpty(tag)) return;

        if (tag.StartsWith("category|"))
        {
            var parts = tag.Split('|');
            if (parts.Length >= 3 && int.TryParse(parts[1], out int catId))
            {
                string catName = parts[2];
                txtProductName.Text = catName;
                await LoadProducts(catId);
            }
        }
    }

    private async Task LoadProducts(int categoryId)
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

                _currentItems = new List<PickerVariantItem>();
                foreach (var p in products)
                {
                    foreach (var v in p.Variants)
                    {
                        _currentItems.Add(new PickerVariantItem
                        {
                            ProductId = p.Id,
                            ProductName = p.Name,
                            VariantId = v.Id,
                            VariantCode = v.Code,
                            VariantName = !string.IsNullOrWhiteSpace(v.Name) ? v.Name : p.Name,
                            CostPrice = v.CostPrice,
                            SellPrice = v.SellPrice,
                            MarkupValue = v.MarkupValue > 0 ? v.MarkupValue : 1.300m,
                            Quantity = v.DefaultQty > 0 ? v.DefaultQty : 1
                        });
                    }
                }

                lstVariants.ItemsSource = _currentItems;
                UpdateSelectionCount();
            }
        }
        catch (Exception ex)
        {
            txtProductName.Text = $"Errore: {ex.Message}";
        }
    }

    // ══════════════════════════════════════════════════
    // SELECTION COUNT
    // ══════════════════════════════════════════════════

    private void Variant_CheckChanged(object sender, RoutedEventArgs e)
    {
        UpdateSelectionCount();
    }

    private void UpdateSelectionCount()
    {
        int count = _currentItems.Count(i => i.IsSelected);
        txtSelectionCount.Text = $"{count} variant{(count == 1 ? "e" : "i")} selezionat{(count == 1 ? "a" : "e")}";
        btnAdd.Content = count > 0 ? $"Aggiungi ({count})" : "Aggiungi";
    }

    // ══════════════════════════════════════════════════
    // BUTTONS
    // ══════════════════════════════════════════════════

    private void BtnAdd_Click(object sender, RoutedEventArgs e)
    {
        SelectedItems = _currentItems.Where(i => i.IsSelected).ToList();
        if (SelectedItems.Count == 0)
        {
            MessageBox.Show("Seleziona almeno una variante.", "Attenzione",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
