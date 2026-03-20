using System.Collections.ObjectModel;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ATEC.PM.Client.Services;
using ATEC.PM.Shared.DTOs;

namespace ATEC.PM.Client.Views;

public partial class CodexCompositionPage : Page
{
    private List<CodexListItem> _allItems = new();
    private List<CatalogItemListItem> _catalogItems = new();
    private int? _selectedParentId;
    private Point _dragStartPoint;
    private TreeViewItem? _lastHighlighted;
    private bool _catalogLoaded;
    private string _currentSource = "codex";

    private static readonly JsonSerializerOptions _jsonOpt = new() { PropertyNameCaseInsensitive = true };

    // Tipi composizione disponibili
    private static readonly List<CompositionType> _types = new()
    {
        new("501", "Gruppo meccanico",   new[] { "1", "2", "3", "4" }),
        new("601", "Assieme meccanico",  new[] { "5" }),
        new("701", "Layout meccanico",   new[] { "6" }),
    };

    // Mapping prefisso → label per la combo sotto-tipo
    private static readonly Dictionary<string, string> _prefixLabels = new()
    {
        { "1", "1xx — Commerciale" },
        { "2", "2xx — Elettrico" },
        { "3", "3xx — Pneumatico" },
        { "4", "4xx — Meccanico" },
        { "5", "5xx — Gruppo mecc." },
        { "6", "6xx — Assieme mecc." },
    };

    public CodexCompositionPage()
    {
        InitializeComponent();
        cmbType.ItemsSource = _types;
        Loaded += async (_, _) => await LoadAllItems();
    }

    // ── DATA LOADING ────────────────────────────────────────

    private async Task LoadAllItems()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/codex");
            var response = JsonSerializer.Deserialize<ApiResponse<List<CodexListItem>>>(json, _jsonOpt);
            if (response?.Success == true)
                _allItems = response.Data ?? new();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── SORGENTE COMBO ─────────────────────────────────────

    private void CmbSource_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbSource.SelectedItem is not ComboBoxItem sel) return;
        _currentSource = sel.Content?.ToString() == "Catalogo" ? "catalog" : "codex";

        if (_currentSource == "catalog" && !_catalogLoaded)
            _ = LoadCatalogItems();
        else
            RefreshBottomPanel();
    }

    private async Task LoadCatalogItems()
    {
        try
        {
            string json = await ApiClient.GetAsync("/api/catalog");
            var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.GetProperty("success").GetBoolean())
            {
                _catalogItems = JsonSerializer.Deserialize<List<CatalogItemListItem>>(
                    doc.RootElement.GetProperty("data").GetRawText(), _jsonOpt) ?? new();
                _catalogLoaded = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore caricamento catalogo: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        RefreshBottomPanel();
    }

    // ── COMBO BOX HANDLERS ──────────────────────────────────

    private void CmbType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (cmbType.SelectedItem is not CompositionType type) return;

        // 601/701: solo sorgente Codex, no catalogo
        bool allowCatalog = type.Prefix == "501";
        cmbSource.SelectedIndex = 0;
        cmbSource.IsEnabled = allowCatalog;
        _currentSource = "codex";
        _selectedParentId = null;

        // Popola combo sotto-tipo per griglia inferiore
        PopulateChildTypeCombo(type);

        // Aggiorna griglia superiore (compositi)
        RefreshTopPanel();

        // Aggiorna griglia inferiore
        RefreshBottomPanel();

        tvComposition.Items.Clear();
        txtTreeHeader.Text = "COMPOSIZIONE";
    }

    private void PopulateChildTypeCombo(CompositionType type)
    {
        cmbChildType.Items.Clear();
        cmbChildType.Items.Add(new ComboBoxItem { Content = "Tutti", Tag = "all", IsSelected = true });

        foreach (var prefix in type.AllowedChildPrefixes)
        {
            string label = _prefixLabels.TryGetValue(prefix, out var l) ? l : $"{prefix}xx";
            cmbChildType.Items.Add(new ComboBoxItem { Content = label, Tag = prefix });
        }

        cmbChildType.SelectedIndex = 0;
    }

    private void CmbChildType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshBottomPanel();
    }

    // ── TOP PANEL: Compositi ──────────────────────────────────

    private void TxtTopSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshTopPanel();
    }

    private void RefreshTopPanel()
    {
        if (cmbType.SelectedItem is not CompositionType type) return;

        string searchCode = txtTopSearchCode.Text.Trim().ToLower();
        string searchDescr = txtTopSearchDescr.Text.Trim().ToLower();

        var filtered = _allItems
            .Where(i => i.Codice.StartsWith(type.Prefix))
            .Where(i => string.IsNullOrEmpty(searchCode) || Match(i.Codice, searchCode))
            .Where(i => string.IsNullOrEmpty(searchDescr) || Match(i.Descr, searchDescr))
            .OrderBy(i => i.Codice)
            .ToList();

        dgComposites.ItemsSource = filtered;
        txtStatus.Text = $"{filtered.Count} compositi {type.Prefix} trovati";
    }

    private async void DgComposites_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (dgComposites.SelectedItem is not CodexListItem selected) return;

        _selectedParentId = selected.Id;
        txtTreeHeader.Text = $"COMPOSIZIONE — {selected.Codice}";
        await LoadTree(selected.Id);
    }

    // ── BOTTOM PANEL: Articoli disponibili ────────────────────

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshBottomPanel();
    }

    private void RefreshBottomPanel()
    {
        if (cmbType.SelectedItem is not CompositionType type) return;

        if (_currentSource == "catalog")
            FilterBottomPanelCatalog();
        else
            FilterBottomPanelCodex(type);
    }

    private void FilterBottomPanelCodex(CompositionType type)
    {
        string searchCodice = txtSearchCodice.Text.Trim().ToLower();
        string searchDescr = txtSearchDescr.Text.Trim().ToLower();

        // Determina prefissi filtrati
        string[] prefixes;
        if (cmbChildType.SelectedItem is ComboBoxItem sel && sel.Tag?.ToString() != "all")
            prefixes = new[] { sel.Tag!.ToString()! };
        else
            prefixes = type.AllowedChildPrefixes;

        var filtered = _allItems
            .Where(i => prefixes.Any(p => i.Codice.StartsWith(p)))
            .Where(i => string.IsNullOrEmpty(searchCodice) || Match(i.Codice, searchCodice))
            .Where(i => string.IsNullOrEmpty(searchDescr) || Match(i.Descr, searchDescr))
            .Select(i => new AvailableItem { Id = i.Id, Codice = i.Codice, Descr = i.Descr, Source = "codex" })
            .OrderBy(i => i.Codice)
            .ToList();

        dgAvailable.ItemsSource = filtered;
    }

    private void FilterBottomPanelCatalog()
    {
        string searchCodice = txtSearchCodice.Text.Trim().ToLower();
        string searchDescr = txtSearchDescr.Text.Trim().ToLower();

        var filtered = _catalogItems
            .Where(i => string.IsNullOrEmpty(searchCodice) || Match(i.Code, searchCodice))
            .Where(i => string.IsNullOrEmpty(searchDescr) || Match(i.Description, searchDescr))
            .Select(i => new AvailableItem { Id = i.Id, Codice = i.Code, Descr = i.Description, Source = "catalog" })
            .OrderBy(i => i.Codice)
            .ToList();

        dgAvailable.ItemsSource = filtered;
    }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _ = RefreshAll();
    }

    private async Task RefreshAll()
    {
        await LoadAllItems();
        if (cmbType.SelectedItem is CompositionType)
        {
            CmbType_SelectionChanged(cmbType, null!);
            if (_selectedParentId.HasValue)
                await LoadTree(_selectedParentId.Value);
        }
    }

    // ── TREE LOADING ────────────────────────────────────────

    private async Task LoadTree(int parentId)
    {
        tvComposition.Items.Clear();

        try
        {
            string json = await ApiClient.GetAsync($"/api/codex/compositions/tree/{parentId}");
            var response = JsonSerializer.Deserialize<ApiResponse<CompositionTreeNode>>(json, _jsonOpt);

            if (response?.Success == true && response.Data != null)
            {
                var rootNode = BuildTreeViewItem(response.Data, isRoot: true);
                rootNode.IsExpanded = true;
                tvComposition.Items.Add(rootNode);

                int count = CountNodes(response.Data) - 1;
                txtStatus.Text = $"{count} componenti nella composizione";
            }
        }
        catch (Exception ex)
        {
            txtStatus.Text = $"Errore: {ex.Message}";
        }
    }

    // Colori sfondo per tipo codice
    private static readonly Dictionary<string, Color> _nodeColors = new()
    {
        { "1", Color.FromRgb(0xDB, 0xED, 0xF8) },
        { "2", Color.FromRgb(0xE8, 0xF5, 0xE9) },
        { "3", Color.FromRgb(0xFF, 0xF3, 0xE0) },
        { "4", Color.FromRgb(0xF3, 0xE5, 0xF5) },
        { "5", Color.FromRgb(0xFD, 0xF6, 0xD6) },
        { "6", Color.FromRgb(0xE0, 0xF2, 0xF1) },
        { "7", Color.FromRgb(0xFC, 0xE4, 0xEC) },
    };

    private static Color GetNodeColor(string codice)
    {
        string prefix = codice.Length > 0 ? codice.Substring(0, 1) : "";
        return _nodeColors.TryGetValue(prefix, out var color) ? color : Color.FromRgb(0xF5, 0xF5, 0xF5);
    }

    private static string FormatCodice(string codice)
    {
        var raw = (codice ?? "").Replace(".", "");
        if (raw.Length > 3)
            return string.Concat(raw.AsSpan(0, raw.Length - 3), ".", raw.AsSpan(raw.Length - 3));
        return raw;
    }

    private TreeViewItem BuildTreeViewItem(CompositionTreeNode node, bool isRoot = false, bool isEditable = true)
    {
        string displayCodice = node.Source == "codex" ? FormatCodice(node.Codice) : node.Codice;
        var bgColor = GetNodeColor(node.Codice);
        double fontSize = isRoot ? 16 : 13;

        var panel = new StackPanel { Orientation = Orientation.Horizontal };

        string icon = node.Source == "catalog" ? "🛒" :
                       node.Codice.StartsWith("7") ? "📦" :
                       node.Codice.StartsWith("6") ? "🔧" :
                       node.Codice.StartsWith("5") ? "⚙" : "🔩";

        panel.Children.Add(new TextBlock
        {
            Text = icon + " ",
            FontSize = fontSize,
            VerticalAlignment = VerticalAlignment.Center
        });

        panel.Children.Add(new TextBlock
        {
            Text = displayCodice,
            FontWeight = isRoot ? FontWeights.Bold : FontWeights.SemiBold,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x26)),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $" — {node.Descr}",
            FontSize = fontSize,
            Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
            VerticalAlignment = VerticalAlignment.Center
        });

        if (!isRoot && isEditable && App.CurrentUser.IsAdmin)
        {
            var btnDelete = new Button
            {
                Content = "✕",
                Width = 22,
                Height = 22,
                Margin = new Thickness(8, 0, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x1A, 0xEF, 0x44, 0x44)),
                Foreground = new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                FontSize = 10,
                Tag = node.CompositionId,
                ToolTip = "Rimuovi dalla composizione"
            };
            btnDelete.Click += BtnRemoveNode_Click;
            panel.Children.Add(btnDelete);
        }

        var border = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 3, 6, 3),
            Margin = new Thickness(0, 1, 0, 1),
            Child = panel
        };

        var item = new TreeViewItem
        {
            Header = border,
            IsExpanded = true,
            Tag = node,
            AllowDrop = isEditable
        };

        if (isEditable)
        {
            item.DragOver += TreeViewItem_DragOver;
            item.Drop += TreeViewItem_Drop;
        }

        if (isRoot && node.Children.Count > 0)
        {
            // Raggruppa figli in due sezioni: Codex e Commerciali
            var codexChildren = node.Children
                .Where(c => c.Source == "codex")
                .OrderBy(c => c.Codice, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var catalogChildren = node.Children
                .Where(c => c.Source != "codex")
                .OrderBy(c => c.Codice, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (codexChildren.Count > 0)
            {
                var codexGroup = BuildGroupNode("🔩 Componenti Codex", $"{codexChildren.Count}",
                    Color.FromRgb(0xEE, 0xF2, 0xFF), Color.FromRgb(0x4F, 0x6E, 0xF7));
                foreach (var child in codexChildren)
                    codexGroup.Items.Add(BuildTreeViewItem(child, isEditable: true));
                item.Items.Add(codexGroup);
            }

            if (catalogChildren.Count > 0)
            {
                var catalogGroup = BuildGroupNode("🛒 Componenti commerciali", $"{catalogChildren.Count}",
                    Color.FromRgb(0xFF, 0xF8, 0xF0), Color.FromRgb(0xD9, 0x77, 0x06));
                foreach (var child in catalogChildren)
                    catalogGroup.Items.Add(BuildTreeViewItem(child, isEditable: true));
                item.Items.Add(catalogGroup);
            }
        }
        else
        {
            foreach (var child in node.Children.OrderBy(c => c.Codice, StringComparer.OrdinalIgnoreCase))
            {
                bool childEditable = isRoot;
                item.Items.Add(BuildTreeViewItem(child, isEditable: childEditable));
            }
        }

        return item;
    }

    private TreeViewItem BuildGroupNode(string title, string count, Color bgColor, Color fgColor)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fgColor),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"  ({count})",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var groupBorder = new Border
        {
            Background = new SolidColorBrush(bgColor),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 4, 0, 2),
            Child = panel
        };

        var groupItem = new TreeViewItem
        {
            Header = groupBorder,
            IsExpanded = true,
            AllowDrop = true
        };

        groupItem.DragOver += GroupNode_DragOver;
        groupItem.Drop += GroupNode_Drop;

        return groupItem;
    }

    private int CountNodes(CompositionTreeNode node)
    {
        return 1 + node.Children.Sum(c => CountNodes(c));
    }

    // ── GROUP NODE DRAG & DROP ────────────────────────────────

    private void GroupNode_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (!App.CurrentUser.IsAdmin || _selectedParentId == null) return;
        if (!e.Data.GetDataPresent(typeof(AvailableItem))) return;

        e.Effects = DragDropEffects.Copy;
    }

    private void GroupNode_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (!App.CurrentUser.IsAdmin || _selectedParentId == null) return;
        if (e.Data.GetData(typeof(AvailableItem)) is not AvailableItem droppedItem) return;

        _ = HandleDrop(_selectedParentId.Value, droppedItem);
    }

    // ── DRAG & DROP ─────────────────────────────────────────

    private void DgAvailable_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!App.CurrentUser.IsAdmin) return;
        if (_selectedParentId == null) return;
        if (dgAvailable.SelectedItem is not AvailableItem item) return;

        _ = HandleQuickAdd(_selectedParentId.Value, item);
    }

    private async Task HandleQuickAdd(int parentId, AvailableItem child)
    {
        try
        {
            var req = new AddCompositionRequest
            {
                ParentCodexId = parentId,
                ChildCodexId = child.Source == "codex" ? child.Id : null,
                ChildCatalogId = child.Source == "catalog" ? child.Id : null,
                Quantity = 1
            };
            string body = JsonSerializer.Serialize(req);
            string json = await ApiClient.PostAsync("/api/codex/compositions", body);
            var response = JsonSerializer.Deserialize<ApiResponse<int>>(json, _jsonOpt);

            if (response?.Success == true)
                await LoadTree(_selectedParentId!.Value);
            else
                MessageBox.Show(response?.Message ?? "Errore", "Attenzione",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DgAvailable_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
    }

    private void DgAvailable_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        if (!App.CurrentUser.IsAdmin) return;

        Point pos = e.GetPosition(null);
        Vector diff = _dragStartPoint - pos;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (dgAvailable.SelectedItem is AvailableItem item)
            {
                var data = new DataObject(typeof(AvailableItem), item);
                DragDrop.DoDragDrop(dgAvailable, data, DragDropEffects.Copy);
            }
        }
    }

    private void TvComposition_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;

        if (!App.CurrentUser.IsAdmin || _selectedParentId == null) return;
        if (!e.Data.GetDataPresent(typeof(AvailableItem))) return;

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void TvComposition_Drop(object sender, DragEventArgs e)
    {
        if (!App.CurrentUser.IsAdmin || _selectedParentId == null) return;
        if (e.Data.GetData(typeof(AvailableItem)) is not AvailableItem droppedItem) return;

        _ = HandleDrop(_selectedParentId.Value, droppedItem);
        e.Handled = true;
    }

    private void TreeViewItem_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;

        if (!App.CurrentUser.IsAdmin) return;
        if (!e.Data.GetDataPresent(typeof(AvailableItem))) return;
        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CompositionTreeNode targetNode) return;

        if (e.Data.GetData(typeof(AvailableItem)) is AvailableItem item)
        {
            string? error = item.Source == "catalog" ? null : ValidateDropLocal(targetNode.Codice, item.Codice);
            if (error == null)
            {
                e.Effects = DragDropEffects.Copy;
                ClearHighlight();
                tvi.Background = new SolidColorBrush(Color.FromRgb(0xE0, 0xE7, 0xFF));
                _lastHighlighted = tvi;
            }
        }
    }

    private void TreeViewItem_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        ClearHighlight();

        if (!App.CurrentUser.IsAdmin) return;
        if (sender is not TreeViewItem tvi) return;
        if (tvi.Tag is not CompositionTreeNode targetNode) return;
        if (e.Data.GetData(typeof(AvailableItem)) is not AvailableItem droppedItem) return;

        _ = HandleDrop(targetNode.CodexId, droppedItem);
    }

    private void ClearHighlight()
    {
        if (_lastHighlighted != null)
        {
            _lastHighlighted.Background = Brushes.Transparent;
            _lastHighlighted = null;
        }
    }

    private async Task HandleDrop(int parentId, AvailableItem child)
    {
        var dialog = new QuantityDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var req = new AddCompositionRequest
            {
                ParentCodexId = parentId,
                ChildCodexId = child.Source == "codex" ? child.Id : null,
                ChildCatalogId = child.Source == "catalog" ? child.Id : null,
                Quantity = dialog.Quantity
            };
            string body = JsonSerializer.Serialize(req);
            string json = await ApiClient.PostAsync("/api/codex/compositions", body);
            var response = JsonSerializer.Deserialize<ApiResponse<int>>(json, _jsonOpt);

            if (response?.Success == true)
            {
                await LoadTree(_selectedParentId!.Value);
            }
            else
            {
                MessageBox.Show(response?.Message ?? "Errore", "Attenzione",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── REMOVE NODE ─────────────────────────────────────────

    private async void BtnRemoveNode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        if (_selectedParentId == null) return;

        int compositionId = (int)btn.Tag;

        var result = MessageBox.Show(
            "Rimuovere questo elemento dalla composizione?",
            "Conferma rimozione", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            string delJson = await ApiClient.DeleteAsync($"/api/codex/compositions/{compositionId}");
            var delResponse = JsonSerializer.Deserialize<ApiResponse<bool>>(delJson, _jsonOpt);

            if (delResponse?.Success == true)
                await LoadTree(_selectedParentId.Value);
            else
                MessageBox.Show(delResponse?.Message ?? "Errore", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Errore: {ex.Message}", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── WILDCARD MATCH ─────────────────────────────────────

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

    // ── VALIDATION ──────────────────────────────────────────

    private static string? ValidateDropLocal(string targetCodice, string childCodice)
    {
        string targetPrefix = targetCodice.Substring(0, 1);
        string childPrefix = childCodice.Substring(0, 1);

        return targetPrefix switch
        {
            "5" => childPrefix is "1" or "2" or "3" or "4" ? null : "5xx accetta solo 1xx-4xx",
            "6" => childPrefix is "5" ? null : "6xx accetta solo 5xx",
            "7" => childPrefix is "6" ? null : "7xx accetta solo 6xx",
            _ => "Questo nodo non può contenere figli"
        };
    }

    // ── HELPER CLASSES ──────────────────────────────────────

    private class CompositionType
    {
        public string Prefix { get; }
        public string Description { get; }
        public string[] AllowedChildPrefixes { get; }
        public string Display => $"{Prefix} — {Description}";

        public CompositionType(string prefix, string description, string[] allowedChildPrefixes)
        {
            Prefix = prefix;
            Description = description;
            AllowedChildPrefixes = allowedChildPrefixes;
        }
    }

    /// <summary>Wrapper unificato per articoli Codex e Catalogo nel DataGrid sinistro.</summary>
    public class AvailableItem
    {
        public int Id { get; set; }
        public string Codice { get; set; } = "";
        public string Descr { get; set; } = "";
        public string Source { get; set; } = "codex";
    }
}
