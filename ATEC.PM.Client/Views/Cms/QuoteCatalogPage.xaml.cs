using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views.Quotes;

public partial class QuoteCatalogPage : Page
{
    private QuoteCatalogTreeDto _tree = new();
    private List<ProductListItem> _allProducts = new();
    private Dictionary<string, TextBox> _productFilterBoxes = new();
    private CancellationTokenSource? _filterCts;

    private int? _selectedGroupId;
    private int? _selectedCategoryId;
    private List<QuotePriceListDto> _priceLists = new();
    private static readonly JsonSerializerOptions _jopt = new() { PropertyNameCaseInsensitive = true };

    public QuoteCatalogPage()
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
                _priceLists = JsonSerializer.Deserialize<List<QuotePriceListDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jopt) ?? new();

                var items = new List<QuotePriceListDto> { new() { Id = 0, Name = "Tutti i listini" } };
                items.AddRange(_priceLists);
                cmbPriceList.ItemsSource = items;
                cmbPriceList.SelectedIndex = 0;
            }
        }
        catch { }
    }

    private void CmbPriceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _ = LoadTree();
    }

    // ══════════════════════════════════════════════════
    // TREE — Caricamento e costruzione
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
                _tree = JsonSerializer.Deserialize<QuoteCatalogTreeDto>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jopt) ?? new();
                BuildTree();
                txtTreeStatus.Text = $"{_tree.TotalGroups} gruppi · {_tree.TotalCategories} categorie · {_tree.TotalProducts} prodotti";
            }
        }
        catch (Exception ex) { txtTreeStatus.Text = $"Errore: {ex.Message}"; }
    }

    // Stato espansione salvato tra rebuild — set di chiavi "tipo|id" per tutti i livelli
    private HashSet<string> _expandedKeys = new();
    private string? _selectedKey;

    private void SaveTreeState()
    {
        _expandedKeys.Clear();
        _selectedKey = null;
        SaveExpandedRecursive(treeGroups.Items);

        if (treeGroups.SelectedItem is TreeViewItem sel && sel.Tag is ValueTuple<string, int, string> tag)
            _selectedKey = $"{tag.Item1}|{tag.Item2}";
    }

    private void SaveExpandedRecursive(ItemCollection items)
    {
        foreach (TreeViewItem node in items)
        {
            if (node.IsExpanded && node.Tag is ValueTuple<string, int, string> tag)
            {
                _expandedKeys.Add($"{tag.Item1}|{tag.Item2}");
                SaveExpandedRecursive(node.Items);
            }
        }
    }

    private void RestoreTreeState()
    {
        RestoreExpandedRecursive(treeGroups.Items);
    }

    private void RestoreExpandedRecursive(ItemCollection items)
    {
        foreach (TreeViewItem node in items)
        {
            if (node.Tag is ValueTuple<string, int, string> tag)
            {
                string key = $"{tag.Item1}|{tag.Item2}";
                if (_expandedKeys.Contains(key))
                {
                    node.IsExpanded = true;
                    RestoreExpandedRecursive(node.Items);
                }
                if (key == _selectedKey)
                    node.IsSelected = true;
            }
        }
    }

    private void BuildTree()
    {
        SaveTreeState();
        treeGroups.Items.Clear();

        bool showAll = !SelectedPriceListId.HasValue;

        if (showAll)
        {
            var byPriceList = _tree.Groups
                .GroupBy(g => new { Id = g.PriceListId ?? 0, Name = string.IsNullOrEmpty(g.PriceListName) ? "Senza listino" : g.PriceListName })
                .OrderBy(pl => pl.Key.Name, _naturalComparer);

            foreach (var plGroup in byPriceList)
            {
                int plProductCount = plGroup.Sum(g => g.ProductCount);
                var plNode = new TreeViewItem
                {
                    Header = BuildTreeHeader(plGroup.Key.Name, plProductCount, "#4F6EF7", FontWeights.Bold),
                    Tag = ("pricelist", plGroup.Key.Id, plGroup.Key.Name),
                    FontSize = 14,
                    IsExpanded = false
                };
                plNode.Expanded += AccordionNode_Expanded;

                foreach (var group in plGroup.OrderBy(g => g.SortOrder).ThenBy(g => g.Name, _naturalComparer))
                {
                    var groupNode = BuildGroupNode(group);
                    plNode.Items.Add(groupNode);
                }

                treeGroups.Items.Add(plNode);
            }
        }
        else
        {
            foreach (var group in _tree.Groups.OrderBy(g => g.SortOrder).ThenBy(g => g.Name, _naturalComparer))
            {
                treeGroups.Items.Add(BuildGroupNode(group));
            }
        }

        RestoreTreeState();
    }

    private void AccordionNode_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is not TreeViewItem expandedNode) return;
        // Chiudi i fratelli allo stesso livello
        var parent = expandedNode.Parent;
        ItemCollection siblings;
        if (parent is TreeViewItem parentItem)
            siblings = parentItem.Items;
        else if (parent is TreeView tv)
            siblings = tv.Items;
        else return;

        foreach (TreeViewItem sibling in siblings)
        {
            if (sibling != expandedNode && sibling.IsExpanded)
                sibling.IsExpanded = false;
        }
        e.Handled = true;
    }

    private TreeViewItem BuildGroupNode(QuoteGroupDto group)
    {
        var groupNode = new TreeViewItem
        {
            Header = BuildTreeHeader(group.Name, group.ProductCount, "#1A1D26", FontWeights.SemiBold),
            Tag = ("group", group.Id, group.Name),
            FontSize = 13,
            IsExpanded = false,
            ContextMenu = (ContextMenu)treeGroups.Resources["GroupContextMenu"]
        };
        groupNode.Expanded += AccordionNode_Expanded;

        foreach (var cat in group.Categories.OrderBy(c => c.Name, _naturalComparer))
        {
            var catNode = BuildCategoryNode(cat);
            if (catNode != null) groupNode.Items.Add(catNode);
        }

        return groupNode;
    }

    private TreeViewItem? BuildCategoryNode(QuoteCategoryDto cat)
    {
        bool hasChildren = cat.Children != null && cat.Children.Count > 0;
        bool hasProducts = cat.Products != null && cat.Products.Count > 0;
        bool hasSubItems = hasChildren || hasProducts;
        var catNode = new TreeViewItem
        {
            Header = BuildTreeHeader(cat.Name, cat.ProductCount, hasSubItems ? "#1A1D26" : "#374151", hasSubItems ? FontWeights.Medium : FontWeights.Normal),
            Tag = ("category", cat.Id, cat.Name),
            FontSize = 13,
            ContextMenu = (ContextMenu)treeGroups.Resources["CategoryContextMenu"]
        };

        if (hasChildren)
        {
            catNode.Expanded += AccordionNode_Expanded;
            foreach (var child in cat.Children.OrderBy(c => c.Name, _naturalComparer))
            {
                var childNode = BuildCategoryNode(child);
                if (childNode != null) catNode.Items.Add(childNode);
            }
        }

        // Aggiungi prodotti come foglie
        if (hasProducts)
        {
            foreach (var prod in cat.Products!.OrderBy(p => p.Name, _naturalComparer))
            {
                var prodNode = new TreeViewItem
                {
                    Header = BuildProductHeader(prod.Name),
                    Tag = ("product", prod.Id, prod.Name),
                    FontSize = 12
                };
                catNode.Items.Add(prodNode);
            }
        }

        return catNode;
    }

    private static StackPanel BuildProductHeader(string name)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            Foreground = Brush("#6B7280"),
            VerticalAlignment = VerticalAlignment.Center
        });
        return sp;
    }

    private static SolidColorBrush Brush(string hex) =>
        new((Color)ColorConverter.ConvertFromString(hex));

    /// <summary>Natural sort: "IRB 120" viene prima di "IRB 1200"</summary>
    private static readonly NaturalStringComparer _naturalComparer = new();

    private class NaturalStringComparer : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return -1;
            if (y == null) return 1;

            int ix = 0, iy = 0;
            while (ix < x.Length && iy < y.Length)
            {
                if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
                {
                    // Confronta blocchi numerici
                    int sx = ix, sy = iy;
                    while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                    while (iy < y.Length && char.IsDigit(y[iy])) iy++;
                    long nx = long.Parse(x[sx..ix]);
                    long ny = long.Parse(y[sy..iy]);
                    int cmp = nx.CompareTo(ny);
                    if (cmp != 0) return cmp;
                }
                else
                {
                    int cmp = char.ToLowerInvariant(x[ix]).CompareTo(char.ToLowerInvariant(y[iy]));
                    if (cmp != 0) return cmp;
                    ix++;
                    iy++;
                }
            }
            return x.Length.CompareTo(y.Length);
        }
    }

    private static StackPanel BuildTreeHeader(string text, int count, string color, FontWeight weight)
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

    // ══════════════════════════════════════════════════
    // TREE — Selezione e ricerca
    // ══════════════════════════════════════════════════

    private async void Tree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (treeGroups.SelectedItem is TreeViewItem tvi && tvi.Tag is (string type, int id, string name))
        {
            if (type == "group")
            {
                _selectedGroupId = id;
                _selectedCategoryId = null;
                txtSectionTitle.Text = name;
                btnNewProduct.Visibility = Visibility.Collapsed;
                await LoadProducts(groupId: id);
            }
            else if (type == "category")
            {
                _selectedGroupId = null;
                _selectedCategoryId = id;
                txtSectionTitle.Text = name;
                btnNewProduct.Visibility = Visibility.Visible;
                await LoadProducts(categoryId: id);
            }
            else if (type == "product")
            {
                // Mostra solo questo prodotto nella lista
                if (tvi.Parent is TreeViewItem parentNode && parentNode.Tag is ("category", int catId, string _))
                {
                    _selectedGroupId = null;
                    _selectedCategoryId = catId;
                    txtSectionTitle.Text = name;
                    btnNewProduct.Visibility = Visibility.Visible;
                    await LoadSingleProduct(id);
                }
            }
        }
    }

    private CancellationTokenSource? _treeFilterCts;

    private async void TxtTreeSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        string search = txtTreeSearch.Text.Trim().ToLower();
        txtTreeSearchPlaceholder.Visibility = string.IsNullOrEmpty(search)
            ? Visibility.Visible : Visibility.Collapsed;

        _treeFilterCts?.Cancel();
        _treeFilterCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(250, _treeFilterCts.Token);
        }
        catch (TaskCanceledException) { return; }

        string[] terms = string.IsNullOrEmpty(search) ? Array.Empty<string>() : search.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (TreeViewItem rootNode in treeGroups.Items)
        {
            bool rootVisible = FilterNodeRecursive(rootNode, terms);
            rootNode.Visibility = rootVisible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Filtra ricorsivamente: un nodo è visibile se matcha o se un figlio matcha.
    /// Se matcha, espande il percorso. Restituisce true se visibile.</summary>
    private static bool FilterNodeRecursive(TreeViewItem node, string[] terms)
    {
        // Se nessun termine, mostra tutto
        if (terms.Length == 0)
        {
            node.Visibility = Visibility.Visible;
            foreach (TreeViewItem child in node.Items)
                FilterNodeRecursive(child, terms);
            return true;
        }

        // Controlla se questo nodo matcha (tutti i termini devono essere presenti nel nome)
        string nodeName = "";
        if (node.Tag is ValueTuple<string, int, string> tag)
            nodeName = tag.Item3.ToLower();

        bool selfMatch = terms.All(t => nodeName.Contains(t));

        // Controlla ricorsivamente i figli
        bool anyChildMatch = false;
        foreach (TreeViewItem child in node.Items)
        {
            bool childVisible = FilterNodeRecursive(child, terms);
            if (childVisible) anyChildMatch = true;
        }

        bool visible = selfMatch || anyChildMatch;
        node.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        // Se un figlio matcha, espandi per mostrarlo
        if (anyChildMatch && !selfMatch)
            node.IsExpanded = true;

        return visible;
    }

    // ══════════════════════════════════════════════════
    // PRODUCTS — Caricamento e filtro
    // ══════════════════════════════════════════════════

    private async Task LoadSingleProduct(int productId)
    {
        txtProductStatus.Text = "Caricamento...";
        try
        {
            string json = await ApiClient.GetAsync($"/api/quote-catalog/products/{productId}");
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var product = JsonSerializer.Deserialize<QuoteProductDto>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    _jopt);
                if (product != null)
                {
                    _allProducts = new List<ProductListItem> { new ProductListItem(product) };
                    ApplyProductFilter();
                }
            }
        }
        catch (Exception ex) { txtProductStatus.Text = $"Errore: {ex.Message}"; }
    }

    private async Task LoadProducts(int? categoryId = null, int? groupId = null)
    {
        txtProductStatus.Text = "Caricamento prodotti...";
        try
        {
            string url = "/api/quote-catalog/products?";
            if (categoryId.HasValue) url += $"categoryId={categoryId}";
            else if (groupId.HasValue) url += $"groupId={groupId}";

            string json = await ApiClient.GetAsync(url);
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                var products = JsonSerializer.Deserialize<List<QuoteProductDto>>(
                    doc.RootElement.GetProperty("data").GetRawText(),
                    _jopt) ?? new();

                _allProducts = products.Select(p => new ProductListItem(p))
                    .OrderBy(p => p.Name, _naturalComparer).ToList();
                ApplyProductFilter();
            }
        }
        catch (Exception ex) { txtProductStatus.Text = $"Errore: {ex.Message}"; }
    }

    private void ProductFilter_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag != null)
            _productFilterBoxes[tb.Tag.ToString()!] = tb;
    }

    private async void ProductFilter_Changed(object sender, TextChangedEventArgs e)
    {
        _filterCts?.Cancel();
        _filterCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(300, _filterCts.Token);
            ApplyProductFilter();
        }
        catch (TaskCanceledException) { }
    }

    private string PF(string tag) =>
        _productFilterBoxes.GetValueOrDefault(tag)?.Text.Trim().ToLower() ?? "";

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

    private void ApplyProductFilter()
    {
        string fCode = PF("Code");
        string fName = PF("Name");

        var filtered = _allProducts.Where(p =>
            Match(p.Code, fCode) &&
            Match(p.Name, fName)
        ).ToList();

        dgProducts.ItemsSource = filtered;
        txtProductStatus.Text = $"{filtered.Count} prodotti su {_allProducts.Count}";
    }

    // ══════════════════════════════════════════════════
    // PRODUCTS — Selezione e azioni
    // ══════════════════════════════════════════════════

    private void DgProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void BtnExpandRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var row = FindParent<DataGridRow>(btn);
        if (row == null) return;

        bool expanding = row.DetailsVisibility != Visibility.Visible;
        row.DetailsVisibility = expanding ? Visibility.Visible : Visibility.Collapsed;
        btn.Content = expanding ? "▼" : "▶";
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void DgProducts_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Ignora doppio click sul pulsante expand
        if (FindParent<Button>(e.OriginalSource as DependencyObject) is Button btn
            && btn.Name == "btnExpand")
            return;

        BtnEditProduct_Click(sender, e);
    }

    // ══════════════════════════════════════════════════
    // GROUPS — CRUD
    // ══════════════════════════════════════════════════

    private void BtnNewGroup_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new QuoteGroupDialog { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = LoadTree();
    }

    private void BtnEditGroup_Click(object sender, RoutedEventArgs e)
    {
        if (treeGroups.SelectedItem is TreeViewItem tvi && tvi.Tag is ("group", int id, string _))
        {
            var group = _tree.Groups.FirstOrDefault(g => g.Id == id);
            if (group != null)
            {
                var dlg = new QuoteGroupDialog(group) { Owner = Window.GetWindow(this) };
                if (dlg.ShowDialog() == true) _ = LoadTree();
            }
        }
    }

    private async void BtnDeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (treeGroups.SelectedItem is TreeViewItem tvi && tvi.Tag is ("group", int id, string name))
        {
            if (MessageBox.Show($"Eliminare il gruppo '{name}' e tutte le sue categorie/prodotti?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await ApiClient.DeleteAsync($"/api/quote-catalog/groups/{id}");
                await LoadTree();
                dgProducts.ItemsSource = null;
                txtSectionTitle.Text = "Seleziona un gruppo o categoria";
            }
        }
    }

    // ══════════════════════════════════════════════════
    // CATEGORIES — CRUD
    // ══════════════════════════════════════════════════

    private void BtnNewCategory_Click(object sender, RoutedEventArgs e)
    {
        int? groupId = null;
        if (treeGroups.SelectedItem is TreeViewItem tvi)
        {
            if (tvi.Tag is ("group", int gid, string _)) groupId = gid;
            else if (tvi.Tag is ("category", int _, string _) && tvi.Parent is TreeViewItem parent
                     && parent.Tag is ("group", int pgid, string _)) groupId = pgid;
        }

        var dlg = new QuoteCategoryDialog(_tree.Groups, groupId) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true) _ = LoadTree();
    }

    private void BtnEditCategory_Click(object sender, RoutedEventArgs e)
    {
        if (treeGroups.SelectedItem is TreeViewItem tvi && tvi.Tag is ("category", int id, string _))
        {
            QuoteCategoryDto? cat = null;
            foreach (var g in _tree.Groups)
            {
                cat = g.Categories.FirstOrDefault(c => c.Id == id);
                if (cat != null) break;
            }
            if (cat != null)
            {
                var dlg = new QuoteCategoryDialog(_tree.Groups, cat) { Owner = Window.GetWindow(this) };
                if (dlg.ShowDialog() == true) _ = LoadTree();
            }
        }
    }

    private async void BtnDeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (treeGroups.SelectedItem is TreeViewItem tvi && tvi.Tag is ("category", int id, string name))
        {
            if (MessageBox.Show($"Eliminare la categoria '{name}' e tutti i suoi prodotti?",
                "Conferma", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await ApiClient.DeleteAsync($"/api/quote-catalog/categories/{id}");
                await LoadTree();
                dgProducts.ItemsSource = null;
            }
        }
    }

    private async void BtnAddSubCategory_Click(object sender, RoutedEventArgs e)
    {
        if (treeGroups.SelectedItem is not TreeViewItem tvi || tvi.Tag is not ("category", int parentId, string parentName))
            return;

        // Trova il groupId risalendo il tree
        int groupId = 0;
        TreeViewItem? node = tvi;
        while (node != null)
        {
            if (node.Tag is ("group", int gid, string _)) { groupId = gid; break; }
            node = node.Parent as TreeViewItem;
        }
        if (groupId == 0) return;

        string? subName = Microsoft.VisualBasic.Interaction.InputBox(
            $"Nome sotto-categoria di '{parentName}':", "Nuova sotto-categoria", "");
        if (string.IsNullOrWhiteSpace(subName)) return;

        string body = JsonSerializer.Serialize(new { GroupId = groupId, ParentId = parentId, Name = subName.Trim() }, _jopt);
        var json = await ApiClient.PostAsync("/api/quote-catalog/categories", body);
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("success").GetBoolean())
        {
            _selectedCategoryId = parentId; // mantieni focus sul parent
            await LoadTree();
        }
        else
            MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
    }

    // ══════════════════════════════════════════════════
    // DRAG & DROP — Sposta categorie
    // ══════════════════════════════════════════════════

    private Point _dragStartPoint;

    private void TreeGroups_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void TreeGroups_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var diff = _dragStartPoint - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (treeGroups.SelectedItem is TreeViewItem tvi)
        {
            if (tvi.Tag is ("category", int catId, string _))
            {
                DragDrop.DoDragDrop(tvi, catId, DragDropEffects.Move);
            }
            else if (tvi.Tag is ("product", int prodId, string _))
            {
                var data = new DataObject("ProductDrag", prodId);
                DragDrop.DoDragDrop(tvi, data, DragDropEffects.Move);
            }
        }
    }

    private void TreeGroups_Drop(object sender, DragEventArgs e)
    {
        // Drop di un prodotto dalla lista
        if (e.Data.GetDataPresent("ProductDrag"))
        {
            int productId = (int)e.Data.GetData("ProductDrag")!;
            var target = FindTreeViewItemUnderMouse(e.GetPosition(treeGroups));
            if (target?.Tag is ("category", int targetCatId, string _))
                _ = MoveProductAsync(productId, targetCatId);
            return;
        }

        // Drop di una categoria
        if (!e.Data.GetDataPresent(typeof(int))) return;
        int draggedCatId = (int)e.Data.GetData(typeof(int))!;

        var catTarget = FindTreeViewItemUnderMouse(e.GetPosition(treeGroups));
        if (catTarget == null) return;

        int? newParentId = null;
        int newGroupId = 0;

        if (catTarget.Tag is ("category", int targetCatId2, string _))
        {
            if (targetCatId2 == draggedCatId) return;
            newParentId = targetCatId2;
            TreeViewItem? n = catTarget;
            while (n != null)
            {
                if (n.Tag is ("group", int gid, string _)) { newGroupId = gid; break; }
                n = n.Parent as TreeViewItem;
            }
        }
        else if (catTarget.Tag is ("group", int gid2, string _))
        {
            newParentId = null;
            newGroupId = gid2;
        }
        else return;

        if (newGroupId == 0) return;
        _ = MoveCategoryAsync(draggedCatId, newParentId, newGroupId);
    }

    private async Task MoveProductAsync(int productId, int newCategoryId)
    {
        string body = JsonSerializer.Serialize(new { CategoryId = newCategoryId }, _jopt);
        var json = await ApiClient.PutAsync($"/api/quote-catalog/products/{productId}/move", body);
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("success").GetBoolean())
        {
            await LoadTree();
            await LoadProducts(categoryId: _selectedCategoryId, groupId: _selectedGroupId);
        }
        else
            MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
    }

    private async Task MoveCategoryAsync(int catId, int? newParentId, int newGroupId)
    {
        string body = JsonSerializer.Serialize(new { NewParentId = newParentId, NewGroupId = newGroupId }, _jopt);
        var json = await ApiClient.PutAsync($"/api/quote-catalog/categories/{catId}/move", body);
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.GetProperty("success").GetBoolean())
            await LoadTree();
        else
            MessageBox.Show(doc.RootElement.GetProperty("message").GetString() ?? "Errore");
    }

    private TreeViewItem? FindTreeViewItemUnderMouse(Point position)
    {
        var hit = treeGroups.InputHitTest(position) as DependencyObject;
        while (hit != null)
        {
            if (hit is TreeViewItem tvi) return tvi;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return null;
    }

    // ══════════════════════════════════════════════════
    // DRAG & DROP — Sposta prodotti dalla lista al tree
    // ══════════════════════════════════════════════════

    private Point _productDragStartPoint;

    private void DgProducts_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _productDragStartPoint = e.GetPosition(null);
    }

    private void DgProducts_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        var diff = _productDragStartPoint - e.GetPosition(null);
        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        if (dgProducts.SelectedItem is ProductListItem product)
        {
            var data = new DataObject("ProductDrag", product.Id);
            DragDrop.DoDragDrop(dgProducts, data, DragDropEffects.Move);
        }
    }

    // ══════════════════════════════════════════════════
    // PRODUCTS — CRUD
    // ══════════════════════════════════════════════════

    private void BtnNewProduct_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedCategoryId.HasValue)
        {
            MessageBox.Show("Seleziona prima una categoria.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new QuoteProductDialog(_selectedCategoryId.Value) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() == true)
        {
            _ = LoadProducts(categoryId: _selectedCategoryId);
            _ = LoadTree();
        }
    }

    private async void BtnEditProduct_Click(object sender, RoutedEventArgs e)
    {
        if (dgProducts.SelectedItem is ProductListItem item)
        {
            // Carica il prodotto completo per passarlo al dialog
            try
            {
                string json = await ApiClient.GetAsync($"/api/quote-catalog/products/{item.Id}");
                var doc = JsonDocument.Parse(json);
                if (doc.RootElement.GetProperty("success").GetBoolean())
                {
                    var product = JsonSerializer.Deserialize<QuoteProductDto>(
                        doc.RootElement.GetProperty("data").GetRawText(),
                        _jopt);
                    if (product != null)
                    {
                        var dlg = new QuoteProductDialog(product) { Owner = Window.GetWindow(this) };
                        if (dlg.ShowDialog() == true)
                        {
                            _ = LoadProducts(categoryId: _selectedCategoryId, groupId: _selectedGroupId);
                            _ = LoadTree();
                        }
                    }
                }
            }
            catch (Exception ex) { MessageBox.Show($"Errore: {ex.Message}"); }
        }
    }

    private async void BtnDuplicateProduct_Click(object sender, RoutedEventArgs e)
    {
        if (dgProducts.SelectedItem is ProductListItem item)
        {
            await ApiClient.PostAsync($"/api/quote-catalog/products/{item.Id}/duplicate", "{}");
            await LoadProducts(categoryId: _selectedCategoryId, groupId: _selectedGroupId);
            await LoadTree();
        }
    }

    private async void BtnDeleteProduct_Click(object sender, RoutedEventArgs e)
    {
        if (dgProducts.SelectedItem is ProductListItem item &&
            MessageBox.Show($"Eliminare '{item.Name}'?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
        {
            await ApiClient.DeleteAsync($"/api/quote-catalog/products/{item.Id}");
            await LoadProducts(categoryId: _selectedCategoryId, groupId: _selectedGroupId);
            await LoadTree();
        }
    }

    // ── Azioni inline per riga ──

    private void SelectProductById(int id)
    {
        if (dgProducts.ItemsSource is List<ProductListItem> items)
        {
            var item = items.FirstOrDefault(p => p.Id == id);
            if (item != null) dgProducts.SelectedItem = item;
        }
    }

    private void BtnEditProductRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            SelectProductById(id);
            BtnEditProduct_Click(sender, e);
        }
    }

    private void BtnDuplicateProductRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            SelectProductById(id);
            BtnDuplicateProduct_Click(sender, e);
        }
    }

    private async void BtnDeleteProductRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int id)
        {
            var item = (dgProducts.ItemsSource as List<ProductListItem>)?.FirstOrDefault(p => p.Id == id);
            string nome = item?.Name ?? $"#{id}";
            if (MessageBox.Show($"Eliminare '{nome}'?", "Conferma",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                await ApiClient.DeleteAsync($"/api/quote-catalog/products/{id}");
                await LoadProducts(categoryId: _selectedCategoryId, groupId: _selectedGroupId);
                await LoadTree();
            }
        }
    }

    private async void BtnRefreshTree_Click(object sender, RoutedEventArgs e)
    {
        await LoadTree();
    }

    // ══════════════════════════════════════════════════
    // IMPORT EXCEL
    // ══════════════════════════════════════════════════

    private async void BtnImportExcel_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importa catalogo da Excel",
            Filter = "File Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var importData = ParseExcelCatalog(dlg.FileName);
            int totalProd = importData.PriceLists.Sum(pl => pl.Groups.Sum(g => g.Categories.Sum(c => c.Products.Count)));
            int totalVar = importData.PriceLists.Sum(pl => pl.Groups.Sum(g => g.Categories.Sum(c => c.Products.Sum(p => p.Variants.Count))));

            var confirm = MessageBox.Show(
                $"Importare il catalogo?\n\n" +
                $"Listini: {importData.PriceLists.Count}\n" +
                $"Gruppi: {importData.PriceLists.Sum(pl => pl.Groups.Count)}\n" +
                $"Categorie: {importData.PriceLists.Sum(pl => pl.Groups.Sum(g => g.Categories.Count))}\n" +
                $"Prodotti: {totalProd}\n" +
                $"Varianti: {totalVar}",
                "Conferma importazione",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            string body = JsonSerializer.Serialize(importData);
            string json = await ApiClient.PostAsync("/api/quote-catalog/import", body);
            var doc = JsonDocument.Parse(json);
            bool success = doc.RootElement.GetProperty("success").GetBoolean();
            string msg = doc.RootElement.GetProperty("message").GetString() ?? "";

            if (success)
            {
                MessageBox.Show(msg, "Import completato", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadPriceLists();
                await LoadTree();
            }
            else
            {
                MessageBox.Show(msg, "Errore import", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static QuoteCatalogImportDto ParseExcelCatalog(string filePath)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook(filePath);
        var ws = wb.Worksheet(1);
        var rows = ws.RangeUsed()?.RowsUsed().Skip(1).ToList() ?? new();

        var result = new QuoteCatalogImportDto();
        QuoteCatalogImportListino? curListino = null;
        QuoteCatalogImportGroup? curGroup = null;
        QuoteCatalogImportCategory? curCat = null;
        QuoteCatalogImportProduct? curProd = null;

        foreach (var row in rows)
        {
            string cellA = row.Cell(1).GetString().Trim();
            string cellE = row.Cell(5).GetString().Trim();
            string cellG = row.Cell(7).GetString().Trim();
            string cellI = row.Cell(9).GetString().Trim();
            string cellN = row.Cell(14).GetString().Trim();

            if (!string.IsNullOrEmpty(cellA))
            {
                curListino = new QuoteCatalogImportListino
                {
                    Name = row.Cell(2).GetString().Trim(),
                    Currency = row.Cell(3).GetString().Trim(),
                    Locale = row.Cell(4).GetString().Trim()
                };
                if (string.IsNullOrEmpty(curListino.Currency)) curListino.Currency = "EUR";
                if (string.IsNullOrEmpty(curListino.Locale)) curListino.Locale = "it";
                result.PriceLists.Add(curListino);
                curGroup = null; curCat = null; curProd = null;
            }

            if (!string.IsNullOrEmpty(cellE) && curListino != null)
            {
                curGroup = new QuoteCatalogImportGroup { Name = row.Cell(6).GetString().Trim() };
                curListino.Groups.Add(curGroup);
                curCat = null; curProd = null;
            }

            if (!string.IsNullOrEmpty(cellG) && curGroup != null)
            {
                curCat = new QuoteCatalogImportCategory { Name = row.Cell(8).GetString().Trim() };
                curGroup.Categories.Add(curCat);
                curProd = null;
            }

            if (!string.IsNullOrEmpty(cellI) && curCat != null)
            {
                curProd = new QuoteCatalogImportProduct
                {
                    Code = row.Cell(10).GetString().Trim(),
                    Name = row.Cell(11).GetString().Trim(),
                    Position = row.Cell(12).GetString().Trim(),
                    Description = row.Cell(13).GetString().Trim()
                };
                curCat.Products.Add(curProd);
            }

            if (!string.IsNullOrEmpty(cellN) && curProd != null)
            {
                curProd.Variants.Add(new QuoteCatalogImportVariant
                {
                    Code = row.Cell(15).GetString().Trim(),
                    Name = row.Cell(16).GetString().Trim(),
                    Description = row.Cell(17).GetString().Trim(),
                    SellPrice = row.Cell(18).IsEmpty() ? 0 : (decimal)row.Cell(18).GetDouble(),
                    CostPrice = row.Cell(19).IsEmpty() ? 0 : (decimal)row.Cell(19).GetDouble(),
                    VatPct = row.Cell(20).IsEmpty() ? 22 : (decimal)row.Cell(20).GetDouble()
                });
            }
        }

        return result;
    }
}

// ══════════════════════════════════════════════════
// ProductListItem — ViewModel per la riga DataGrid
// ══════════════════════════════════════════════════

public class ProductListItem
{
    public int Id { get; set; }
    public string ItemType { get; set; } = "";
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string CategoryName { get; set; } = "";
    public string GroupName { get; set; } = "";
    public bool AutoInclude { get; set; }
    public int VariantCount { get; set; }
    public string PriceRange { get; set; } = "";
    public string CostRange { get; set; } = "";
    public string AutoIncludeLabel => AutoInclude ? "✓" : "";
    public List<VariantDisplayItem> Variants { get; set; } = new();

    public ProductListItem(QuoteProductDto p)
    {
        Id = p.Id;
        ItemType = p.ItemType;
        Code = p.Code;
        Name = p.Name;
        CategoryName = p.CategoryName;
        GroupName = p.GroupName;
        AutoInclude = p.AutoInclude;
        VariantCount = p.Variants.Count;

        Variants = p.Variants.Select(v => new VariantDisplayItem
        {
            Code = v.Code,
            Name = v.Name,
            CostPrice = v.CostPrice,
            SellPrice = v.SellPrice,
            DiscountPct = v.DiscountPct,
            VatPct = v.VatPct,
            Unit = v.Unit,
            DefaultQty = v.DefaultQty
        }).ToList();

        if (p.ItemType == "content" || p.Variants.Count == 0)
        {
            PriceRange = "—";
            CostRange = "—";
        }
        else if (p.Variants.Count == 1)
        {
            PriceRange = $"{p.Variants[0].SellPrice:N2}€";
            CostRange = $"{p.Variants[0].CostPrice:N2}€";
        }
        else
        {
            decimal minP = p.Variants.Min(v => v.SellPrice);
            decimal maxP = p.Variants.Max(v => v.SellPrice);
            PriceRange = minP == maxP ? $"{minP:N2}€" : $"{minP:N2}€ – {maxP:N2}€";

            decimal minC = p.Variants.Min(v => v.CostPrice);
            decimal maxC = p.Variants.Max(v => v.CostPrice);
            CostRange = minC == maxC ? $"{minC:N2}€" : $"{minC:N2}€ – {maxC:N2}€";
        }
    }
}

public class VariantDisplayItem
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal CostPrice { get; set; }
    public decimal SellPrice { get; set; }
    public decimal DiscountPct { get; set; }
    public decimal VatPct { get; set; }
    public string Unit { get; set; } = "nr.";
    public decimal DefaultQty { get; set; } = 1;
    public string SellPriceFormatted => $"{SellPrice:N2}€";
    public string CostPriceFormatted => $"{CostPrice:N2}€";
    public string DiscountFormatted => DiscountPct > 0 ? $"{DiscountPct:N1}%" : "—";
    public string VatFormatted => $"{VatPct:N0}%";
    public string QtyFormatted => $"{DefaultQty:G} {Unit}";
}
